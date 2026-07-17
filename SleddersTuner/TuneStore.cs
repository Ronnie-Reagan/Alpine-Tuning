using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AlpineTuning
{
    internal sealed class TuneHistoryEntry
    {
        public string historyId;
        public string sourceProfileId;
        public long archivedUnixTime;
        public TuneProfile profile;
    }

    internal sealed class LoadedCurrentSetupCandidate
    {
        public string sourcePath;
        public string canonicalPath;
        public string normalizedIdentity;
        public CurrentSetupRecord record;
        public TuneProfile originalProfile;
        public bool recoveredFromBackup;
        public bool originalChecksumValid;
        public bool originalPathCanonical;
        public bool repaired;
        public bool preserveOriginal;
        public string contentFingerprint;
    }

    internal class TuneStore
    {
        private readonly PartCatalog _catalog;
        private readonly Dictionary<string, SledDefaults> _defaults =
            new Dictionary<string, SledDefaults>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TuneProfile> _profiles =
            new Dictionary<string, TuneProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeProfileIdsBySled =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CurrentSetupRecord> _currentSetupsByIdentity =
            new Dictionary<string, CurrentSetupRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousSledKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AlpineUserSettings _settings = new AlpineUserSettings();

        private static readonly object TestStorageRootLock = new object();
        private static string _testStorageRoot;
        private static string _testLegacyStorageRoot;

        // TuneStore's dependency-free regression runner deliberately executes
        // without a bootstrapped Melon logger. Shadow the imported logger only
        // inside this class so expected recovery/rejection messages remain normal
        // in game but cannot abort an isolated test-storage scope.
        private static class MelonLogger
        {
            public static void Msg(string message)
            {
                Forward(() => global::MelonLoader.MelonLogger.Msg(message));
            }

            public static void Warning(string message)
            {
                Forward(() => global::MelonLoader.MelonLogger.Warning(message));
            }

            private static void Forward(Action write)
            {
                try
                {
                    write?.Invoke();
                }
                catch
                {
                    if (_testStorageRoot == null)
                        throw;
                }
            }
        }

        // Resolve production paths lazily. The dependency-free regression runner
        // can establish its explicit test root before MelonLoader has initialized
        // a game environment, while in-game callers still use Melon UserData.
        private static string ProductionConfigRoot =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning");

        private static string ProductionLegacyConfigRoot =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "SleddersTuner");

        private static string ConfigRoot => _testStorageRoot ?? ProductionConfigRoot;
        private static string LegacyConfigRoot => _testLegacyStorageRoot ?? ProductionLegacyConfigRoot;

        private static string DefaultsDir => Path.Combine(ConfigRoot, "Defaults");
        private static string LegacyPresetsDir => Path.Combine(ConfigRoot, "Presets");
        private static string ProfilesDir => Path.Combine(ConfigRoot, "Profiles");
        private static string ProfileHistoryDir => Path.Combine(ConfigRoot, "ProfileHistory");
        private static string ArchivedProfilesDir => Path.Combine(ConfigRoot, "ArchivedProfiles");
        private static string RecoveryDir => Path.Combine(ConfigRoot, "Recovery");
        private static string CurrentSetupsDir => Path.Combine(ConfigRoot, "CurrentSetups");
        private static string ActiveMapPath => Path.Combine(ConfigRoot, "active-profiles.json");
        private static string SettingsPath => Path.Combine(ConfigRoot, "user-settings.json");
        private static string LegacyImportMarkerPath => Path.Combine(ConfigRoot, "legacy-import-complete.json");

        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public TuneStore(PartCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>
        /// Redirects every TuneStore read/write into an isolated non-UserData
        /// directory for release regression tests. The override is process-wide,
        /// deliberately single-owner, and must be disposed before another test
        /// scope is opened. Production callers continue to use the unchanged
        /// MelonEnvironment.UserDataDirectory paths.
        /// </summary>
        internal static IDisposable UseTestStorageRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A test storage root is required.", nameof(root));

            string fullRoot = Path.GetFullPath(root);
            string liveUserData = null;
            try
            {
                string melonUserData = MelonEnvironment.UserDataDirectory;
                if (!string.IsNullOrWhiteSpace(melonUserData))
                    liveUserData = Path.GetFullPath(melonUserData);
            }
            catch
            {
                // Standalone release tests intentionally run before MelonLoader
                // has a game context. Once the explicit override is installed,
                // no TuneStore path can fall back to production UserData.
            }
            if (!string.IsNullOrWhiteSpace(liveUserData) && PathEqualsOrIsInside(fullRoot, liveUserData))
            {
                throw new InvalidOperationException(
                    "TuneStore tests may not use MelonLoader's live UserData directory.");
            }

            string pathRoot = Path.GetPathRoot(fullRoot);
            if (string.Equals(
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    pathRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("TuneStore tests may not use a filesystem root.");
            }

            lock (TestStorageRootLock)
            {
                if (_testStorageRoot != null)
                    throw new InvalidOperationException("A TuneStore test storage scope is already active.");

                _testStorageRoot = fullRoot;
                _testLegacyStorageRoot = Path.Combine(fullRoot, "LegacyInput");
                return new TestStorageRootScope(fullRoot);
            }
        }

        private static bool PathEqualsOrIsInside(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
                return false;

            string normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private sealed class TestStorageRootScope : IDisposable
        {
            private readonly string _root;
            private bool _disposed;

            public TestStorageRootScope(string root)
            {
                _root = root;
            }

            public void Dispose()
            {
                lock (TestStorageRootLock)
                {
                    if (_disposed)
                        return;

                    if (string.Equals(_testStorageRoot, _root, StringComparison.OrdinalIgnoreCase))
                    {
                        _testStorageRoot = null;
                        _testLegacyStorageRoot = null;
                    }

                    _disposed = true;
                }
            }
        }

        public IReadOnlyDictionary<string, SledDefaults> Defaults => _defaults;
        public IReadOnlyDictionary<string, TuneProfile> Profiles => _profiles;
        public AlpineUserSettings Settings => _settings;
        public int ActiveProfileMapCount => _activeProfileIdsBySled.Count;
        public int CurrentSetupCount => _currentSetupsByIdentity.Values.Distinct().Count();
        public string DiagnosticsSummary =>
            $"Store: defaults={_defaults.Values.Distinct().Count()}, profiles={_profiles.Count}, " +
            $"currentSetups={CurrentSetupCount}, activeMaps={_activeProfileIdsBySled.Count}";

        public void Initialize()
        {
            Directory.CreateDirectory(ConfigRoot);
            // Merge legacy files before loading the live indexes. Existing Alpine
            // files always win, while missing setup slots can still be recovered.
            MaybeMigrateLegacyConfig();
            Directory.CreateDirectory(DefaultsDir);
            Directory.CreateDirectory(ProfilesDir);
            Directory.CreateDirectory(ProfileHistoryDir);
            Directory.CreateDirectory(ArchivedProfilesDir);
            Directory.CreateDirectory(RecoveryDir);
            Directory.CreateDirectory(CurrentSetupsDir);
            ScrubStoredProfilePrivacy();
            LoadDefaults();
            LoadProfiles();
            LoadCurrentSetups();
            LoadActiveMap();
            LoadSettings();
            PruneMissingActiveProfiles();
        }

        public void RefreshKnownVehicleIdentities(IEnumerable<VehicleScriptableObject> sleds)
        {
            var uniqueNativeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _ambiguousSledKeys.Clear();

            var candidates = (sleds ?? Enumerable.Empty<VehicleScriptableObject>())
                .Where(sled => sled != null)
                .GroupBy(AlpineTuningMod.GetSledKey, StringComparer.OrdinalIgnoreCase);
            foreach (var group in candidates)
            {
                string[] stableIdentities = group
                    .Select(SledIdentity.StableIdentityKey)
                    .Where(IsSafeIdentity)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string[] nativeIds = group
                    .Select(sled => AlpineTuningMod.GetVehicleId(sled))
                    .Where(id => SledIdentity.HasNativeVehicleIdentity(group.Key, id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (stableIdentities.Length > 1 || nativeIds.Length > 1)
                {
                    _ambiguousSledKeys.Add(group.Key);
                    continue;
                }

                if (nativeIds.Length == 1)
                    uniqueNativeIds[group.Key] = nativeIds[0];
            }

            int profileMigrations = MigrateProfileVehicleIdentities(uniqueNativeIds);
            int currentSetupMigrations = MigrateCurrentSetupVehicleIdentities(uniqueNativeIds);
            bool activeMapChanged = MigrateActiveProfileIdentities(uniqueNativeIds);
            if (activeMapChanged)
                SaveActiveMap();

            if (profileMigrations > 0 || currentSetupMigrations > 0 || activeMapChanged)
            {
                MelonLogger.Msg(
                    $"Migrated Alpine native identities: profiles={profileMigrations}, " +
                    $"currentSetups={currentSetupMigrations}, activeMap={(activeMapChanged ? 1 : 0)}.");
            }

            if (_ambiguousSledKeys.Count > 0)
            {
                MelonLogger.Warning(
                    $"Quarantined legacy name-only setup fallback for {_ambiguousSledKeys.Count} " +
                    "ambiguous sled name(s); current native vehicle IDs remain isolated.");
            }
        }

        private int MigrateProfileVehicleIdentities(
            IReadOnlyDictionary<string, string> uniqueNativeIds)
        {
            int migratedCount = 0;
            foreach (TuneProfile original in _profiles.Values.ToList())
            {
                TuneProfile migrated = Clone(original);
                bool changed = false;

                if (migrated != null &&
                    IsSafeIdentity(migrated.targetSledKey) &&
                    uniqueNativeIds.TryGetValue(migrated.targetSledKey, out string targetVehicleId) &&
                    !SledIdentity.HasNativeVehicleIdentity(
                        migrated.targetSledKey,
                        migrated.targetVehicleId))
                {
                    migrated.targetVehicleId = targetVehicleId;
                    changed = true;
                }

                if (migrated != null &&
                    IsSafeIdentity(migrated.donorSledKey) &&
                    uniqueNativeIds.TryGetValue(migrated.donorSledKey, out string donorVehicleId) &&
                    !SledIdentity.HasNativeVehicleIdentity(
                        migrated.donorSledKey,
                        migrated.donorVehicleId))
                {
                    migrated.donorVehicleId = donorVehicleId;
                    changed = true;
                }

                if (!changed || migrated == null)
                    continue;

                migrated.checksum = null;
                migrated.checksum = ComputeChecksum(migrated);
                string path = Path.Combine(ProfilesDir, SafeFileName(migrated.profileId) + ".json");
                if (!WriteJsonAtomic(path, migrated, $"profile {migrated.profileId} identity migration"))
                    continue;

                _profiles[migrated.profileId] = migrated;
                migratedCount++;
            }

            return migratedCount;
        }

        private int MigrateCurrentSetupVehicleIdentities(
            IReadOnlyDictionary<string, string> uniqueNativeIds)
        {
            int migratedCount = 0;
            var migratedRecords = new List<CurrentSetupRecord>();
            foreach (CurrentSetupRecord original in _currentSetupsByIdentity.Values.Distinct().ToList())
            {
                if (original == null || original.profile == null)
                    continue;

                var migrated = new CurrentSetupRecord
                {
                    schemaVersion = original.schemaVersion,
                    sledKey = original.sledKey,
                    vehicleId = original.vehicleId,
                    displayName = original.displayName,
                    setupSlotId = original.setupSlotId,
                    setupSlotName = original.setupSlotName,
                    setupEdited = original.setupEdited,
                    updatedUnixTime = original.updatedUnixTime,
                    profile = Clone(original.profile)
                };
                bool changed = false;

                if (IsSafeIdentity(migrated.sledKey) &&
                    uniqueNativeIds.TryGetValue(migrated.sledKey, out string targetVehicleId) &&
                    !SledIdentity.HasNativeVehicleIdentity(migrated.sledKey, migrated.vehicleId))
                {
                    migrated.vehicleId = targetVehicleId;
                    migrated.profile.targetVehicleId = targetVehicleId;
                    changed = true;
                }

                if (IsSafeIdentity(migrated.profile.donorSledKey) &&
                    uniqueNativeIds.TryGetValue(migrated.profile.donorSledKey, out string donorVehicleId) &&
                    !SledIdentity.HasNativeVehicleIdentity(
                        migrated.profile.donorSledKey,
                        migrated.profile.donorVehicleId))
                {
                    migrated.profile.donorVehicleId = donorVehicleId;
                    changed = true;
                }

                CurrentSetupRecord selected = original;
                if (changed)
                {
                    string oldIdentity = IdentityKey(original.sledKey, original.vehicleId);
                    if (WriteCurrentSetup(migrated))
                    {
                        selected = migrated;
                        migratedCount++;
                        DeleteMigratedIdentityFile(CurrentSetupsDir, oldIdentity, IdentityKey(migrated.sledKey, migrated.vehicleId));
                    }
                }

                migratedRecords.Add(selected);
            }

            _currentSetupsByIdentity.Clear();
            foreach (CurrentSetupRecord record in migratedRecords)
                IndexCurrentSetup(record);
            return migratedCount;
        }

        private bool MigrateActiveProfileIdentities(
            IReadOnlyDictionary<string, string> uniqueNativeIds)
        {
            bool changed = false;
            foreach (var pair in _activeProfileIdsBySled.ToList())
            {
                if (!_profiles.TryGetValue(pair.Value, out TuneProfile profile) ||
                    profile == null ||
                    !IsSafeIdentity(profile.targetSledKey) ||
                    !uniqueNativeIds.TryGetValue(profile.targetSledKey, out string nativeVehicleId) ||
                    string.Equals(pair.Key, nativeVehicleId, StringComparison.OrdinalIgnoreCase) ||
                    SledIdentity.HasNativeVehicleIdentity(profile.targetSledKey, pair.Key))
                {
                    continue;
                }

                if (!_activeProfileIdsBySled.ContainsKey(nativeVehicleId))
                    _activeProfileIdsBySled[nativeVehicleId] = pair.Value;
                _activeProfileIdsBySled.Remove(pair.Key);
                changed = true;
            }

            return changed;
        }

        private static void DeleteMigratedIdentityFile(
            string directory,
            string oldIdentity,
            string newIdentity)
        {
            if (!IsSafeIdentity(oldIdentity) ||
                string.Equals(oldIdentity, newIdentity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string oldPath = Path.Combine(directory, SafeFileName(oldIdentity) + ".json");
            DeleteStoredFileAndBackup(oldPath, "migrated identity file");
        }

        private static void DeleteStoredFileAndBackup(string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsPathInsideConfigRoot(path))
                return;

            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (File.Exists(candidate))
                        File.Delete(candidate);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning(
                        $"Could not remove {description} {Path.GetFileName(candidate)}: {StorageError(ex)}");
                }
            }
        }

        public bool SaveSettings()
        {
            if (_settings == null)
                _settings = new AlpineUserSettings();

            _settings.Normalize();
            return WriteJsonAtomic(SettingsPath, _settings, "user settings");
        }

        public SledDefaults GetDefaults(string sledKey)
        {
            return GetDefaults(sledKey, null);
        }

        public SledDefaults GetDefaults(string sledKey, string vehicleId)
        {
            SledDefaults defaults = null;
            if (IsSafeIdentity(vehicleId))
                _defaults.TryGetValue(vehicleId, out defaults);
            if (defaults == null && IsSafeIdentity(sledKey))
            {
                _defaults.TryGetValue(sledKey, out defaults);
                if (defaults != null && SledIdentity.HasNativeVehicleIdentity(sledKey, vehicleId) &&
                    (_ambiguousSledKeys.Contains(sledKey) ||
                     (SledIdentity.HasNativeVehicleIdentity(defaults.sledKey, defaults.vehicleId) &&
                      !string.Equals(defaults.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))))
                {
                    defaults = null;
                }
            }
            return defaults;
        }

        public void PutDefaults(SledDefaults defaults)
        {
            if (defaults == null || string.IsNullOrWhiteSpace(defaults.sledKey))
                return;

            if (!TryValidateDefaults(defaults, out var reason))
            {
                MelonLogger.Warning($"Skipped saving invalid defaults for {defaults.sledKey}: {reason}");
                return;
            }

            if (SaveDefaults(defaults))
                IndexDefaults(defaults);
        }

        private void IndexDefaults(SledDefaults defaults)
        {
            if (defaults == null)
                return;

            if (IsSafeIdentity(defaults.vehicleId))
                _defaults[defaults.vehicleId] = defaults;
            if (IsSafeIdentity(defaults.sledKey))
                _defaults[defaults.sledKey] = defaults;
        }

        public TuneProfile GetProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return null;

            _profiles.TryGetValue(profileId, out var profile);
            return profile;
        }

        public List<TuneProfile> GetProfilesForSled(string sledKey)
        {
            return GetProfilesForSled(sledKey, null);
        }

        public List<TuneProfile> GetProfilesForSled(string sledKey, string vehicleId)
        {
            return _profiles.Values
                .Where(p => ProfileTargetsIdentity(p, sledKey, vehicleId))
                .OrderByDescending(p => p.updatedUnixTime)
                .ToList();
        }

        private bool ProfileTargetsIdentity(TuneProfile profile, string sledKey, string vehicleId)
        {
            if (profile == null)
                return false;

            if (IsSafeIdentity(vehicleId))
            {
                if (string.Equals(profile.targetVehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))
                    return true;

                // A different current numeric ItemIdentifier is authoritative.
                // GUID/name identities from pre-1.1.6 builds remain eligible for
                // guarded migration through their legacy sled key.
                if (SledIdentity.HasNativeVehicleIdentity(
                        profile.targetSledKey,
                        profile.targetVehicleId))
                {
                    return false;
                }

                if (IsSafeIdentity(sledKey) && _ambiguousSledKeys.Contains(sledKey))
                    return false;
            }

            return IsSafeIdentity(sledKey) &&
                   string.Equals(profile.targetSledKey, sledKey, StringComparison.OrdinalIgnoreCase);
        }

        public TuneProfile GetActiveProfileForSled(string sledKey)
        {
            return GetActiveProfileForSled(sledKey, null);
        }

        public TuneProfile GetActiveProfileForSled(string sledKey, string vehicleId)
        {
            if (string.IsNullOrWhiteSpace(sledKey) && string.IsNullOrWhiteSpace(vehicleId))
                return null;

            string profileId = null;
            if (!string.IsNullOrWhiteSpace(vehicleId))
                _activeProfileIdsBySled.TryGetValue(vehicleId, out profileId);

            if (string.IsNullOrWhiteSpace(profileId))
            {
                if (string.IsNullOrWhiteSpace(sledKey) ||
                    !_activeProfileIdsBySled.TryGetValue(sledKey, out profileId))
                {
                    return null;
                }
            }

            var profile = GetProfile(profileId);
            if (profile == null)
                return null;

            return ProfileTargetsIdentity(profile, sledKey, vehicleId) ? profile : null;
        }

        public bool SetActiveProfile(string sledKey, string profileId)
        {
            return SetActiveProfile(sledKey, null, profileId);
        }

        public bool SetActiveProfile(string sledKey, string vehicleId, string profileId)
        {
            string identity = IdentityKey(sledKey, vehicleId);
            if (!IsSafeIdentity(identity))
                return false;

            var previousMap = new Dictionary<string, string>(
                _activeProfileIdsBySled,
                StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(profileId))
                _activeProfileIdsBySled.Remove(identity);
            else if (!IsSafeProfileId(profileId))
                return false;
            else
            {
                if (!_profiles.TryGetValue(profileId, out var profile) ||
                    !ProfileTargetsIdentity(profile, sledKey, vehicleId))
                {
                    return false;
                }

                _activeProfileIdsBySled[identity] = profileId;
            }

            if (!string.IsNullOrWhiteSpace(vehicleId) &&
                !string.Equals(vehicleId, sledKey, StringComparison.OrdinalIgnoreCase))
            {
                _activeProfileIdsBySled.Remove(sledKey);
            }

            if (SaveActiveMap())
                return true;

            _activeProfileIdsBySled.Clear();
            foreach (var pair in previousMap)
                _activeProfileIdsBySled[pair.Key] = pair.Value;
            return false;
        }

        public TuneProfile CreateWorkingProfile(VehicleScriptableObject sled, string author)
        {
            string sledKey = AlpineTuningMod.GetSledKey(sled);
            string vehicleId = AlpineTuningMod.GetVehicleId(sled);
            var current = GetCurrentSetupForSled(sledKey, vehicleId);
            if (current != null)
                return current;

            var active = GetActiveProfileForSled(sledKey, vehicleId);
            if (active != null)
            {
                var clone = Clone(active);
                _catalog.EnsureProfileSelections(clone);
                MarkSetupMetadata(clone, active.profileId, active.name, false, true);
                return clone;
            }

            var profile = _catalog.CreateDefaultProfile(sled, author);
            MarkSetupMetadata(profile, profile.profileId, profile.name, false, true);
            return profile;
        }

        public CurrentSetupRecord GetCurrentSetupRecordForSled(string sledKey, string vehicleId)
        {
            return FindCurrentSetupRecord(sledKey, vehicleId);
        }

        public TuneProfile GetCurrentSetupForSled(string sledKey, string vehicleId)
        {
            var record = FindCurrentSetupRecord(sledKey, vehicleId);
            if (record == null || record.profile == null)
                return null;

            var clone = Clone(record.profile);
            if (clone == null)
                return null;

            _catalog.EnsureProfileSelections(clone);
            MarkSetupMetadata(
                clone,
                record.setupSlotId,
                record.setupSlotName,
                record.setupEdited,
                true);
            return clone;
        }

        public bool SetCurrentSetup(
            TuneProfile profile,
            string sledKey,
            string vehicleId,
            string displayName,
            string setupSlotId,
            string setupSlotName,
            bool setupEdited,
            bool writeNow)
        {
            if (profile == null || (!IsSafeIdentity(sledKey) && !IsSafeIdentity(vehicleId)))
                return false;

            _catalog.EnsureProfileSelections(profile);
            if (string.IsNullOrWhiteSpace(profile.profileId))
                profile.profileId = Guid.NewGuid().ToString("N");

            long now = NowUnix();
            if (profile.createdUnixTime <= 0)
                profile.createdUnixTime = now;
            profile.updatedUnixTime = now;
            profile.schemaVersion = AlpineConstants.SchemaVersion;
            profile.modVersion = AlpineConstants.ModVersion;
            profile.catalogVersion = AlpineConstants.CatalogVersion;
            NormalizeProfileAuthor(profile);
            profile.targetSledKey = sledKey;
            profile.targetVehicleId = vehicleId;
            MarkSetupMetadata(profile, setupSlotId, setupSlotName, setupEdited, true);
            profile.checksum = null;

            if (!TryValidateProfileForCatalog(profile, _catalog, false, false, out var reason))
            {
                MelonLogger.Warning($"Could not preserve current setup for '{sledKey ?? vehicleId}': {reason}");
                return false;
            }

            var record = new CurrentSetupRecord
            {
                sledKey = sledKey,
                vehicleId = vehicleId,
                // The display label is unused and may contain locally authored
                // asset text; derive visible setup text from the checksummed tune.
                displayName = null,
                setupSlotId = setupSlotId,
                setupSlotName = setupSlotName,
                setupEdited = setupEdited,
                updatedUnixTime = now,
                profile = Clone(profile)
            };

            if (writeNow)
            {
                if (!WriteCurrentSetup(record))
                    return false;
                IndexCurrentSetup(record);
                return true;
            }

            IndexCurrentSetup(record);
            return true;
        }

        public bool FlushCurrentSetup(string sledKey, string vehicleId)
        {
            var record = FindCurrentSetupRecord(sledKey, vehicleId);
            return record != null && WriteCurrentSetup(record);
        }

        public bool SaveProfile(TuneProfile profile, bool makeActive)
        {
            return SaveProfile(
                profile,
                makeActive,
                out _,
                out _);
        }

        public bool SaveProfile(
            TuneProfile profile,
            bool makeActive,
            out bool profileWritten,
            out bool madeActive)
        {
            profileWritten = false;
            madeActive = false;
            if (profile == null)
                return false;

            _catalog.EnsureProfileSelections(profile);
            if (string.IsNullOrWhiteSpace(profile.profileId))
                profile.profileId = Guid.NewGuid().ToString("N");

            long now = NowUnix();
            if (profile.createdUnixTime <= 0)
                profile.createdUnixTime = now;
            profile.updatedUnixTime = now;
            profile.schemaVersion = AlpineConstants.SchemaVersion;
            profile.modVersion = AlpineConstants.ModVersion;
            profile.catalogVersion = AlpineConstants.CatalogVersion;
            NormalizeProfileAuthor(profile);
            if (profile.usesAutomaticName || string.IsNullOrWhiteSpace(profile.name))
            {
                profile.name = BuildUniqueAutomaticProfileName(profile);
                profile.usesAutomaticName = true;
            }
            profile.isCurrentSetup = false;
            profile.setupEdited = false;
            profile.setupSlotId = profile.profileId;
            profile.setupSlotName = profile.name;
            profile.checksum = null;
            if (!TryValidateProfileForCatalog(profile, _catalog, false, false, out var reason))
            {
                MelonLogger.Warning($"Could not save profile '{profile.name ?? profile.profileId}': {reason}");
                return false;
            }

            profile.checksum = ComputeChecksum(profile);

            if (_profiles.TryGetValue(profile.profileId, out TuneProfile existing) && existing != null)
            {
                if (!ProfilesTargetSameSled(existing, profile))
                {
                    MelonLogger.Warning(
                        $"Refused to overwrite setup slot {profile.profileId} because it belongs to another sled.");
                    return false;
                }

                string oldFingerprint = ComputeContentFingerprint(existing);
                string newFingerprint = ComputeContentFingerprint(profile);
                if (!string.Equals(oldFingerprint, newFingerprint, StringComparison.OrdinalIgnoreCase) &&
                    !PreserveProfileSnapshot(existing, ProfileHistoryDir, "setup history"))
                {
                    MelonLogger.Warning(
                        $"Refused to overwrite setup slot {profile.profileId} because its previous revision could not be preserved.");
                    return false;
                }
            }

            string path = Path.Combine(ProfilesDir, SafeFileName(profile.profileId) + ".json");
            if (!WriteJsonAtomic(path, profile, $"profile {profile.profileId}"))
                return false;

            _profiles[profile.profileId] = Clone(profile);
            profileWritten = true;

            if (makeActive)
            {
                madeActive = SetActiveProfile(
                    profile.targetSledKey,
                    profile.targetVehicleId,
                    profile.profileId);
                return madeActive;
            }

            return true;
        }

        public bool DeleteProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return false;

            if (!_profiles.TryGetValue(profileId, out TuneProfile profile) || profile == null)
                return false;

            if (IsProfileInUse(profileId, out bool isCurrent, out bool isDefault))
            {
                string usage = isCurrent && isDefault
                    ? "current and default"
                    : isCurrent ? "current" : "default";
                MelonLogger.Warning(
                    $"Refused to remove setup slot {profileId} because it is the {usage} setup.");
                return false;
            }

            if (!PreserveProfileSnapshot(profile, ArchivedProfilesDir, "removed setup"))
            {
                MelonLogger.Warning(
                    $"Refused to remove setup slot {profileId} because its recovery copy could not be written.");
                return false;
            }

            string path = Path.Combine(ProfilesDir, SafeFileName(profileId) + ".json");
            try
            {
                if (!IsPathInsideConfigRoot(path))
                    return false;

                // An orphan .bak is treated as a recovery candidate on startup.
                // Remove it first so a successfully archived deletion cannot
                // silently resurrect on the next launch.
                string backupPath = path + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not remove setup slot {profileId}: {StorageError(ex)}");
                return false;
            }

            _profiles.Remove(profileId);

            var activeKeys = _activeProfileIdsBySled
                .Where(kvp => string.Equals(kvp.Value, profileId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in activeKeys)
                _activeProfileIdsBySled.Remove(key);

            if (!SaveActiveMap())
                MelonLogger.Warning($"Removed setup slot {profileId}, but its default mapping cleanup will retry next load.");

            return true;
        }

        public bool IsProfileCurrentForSled(string profileId, string sledKey, string vehicleId)
        {
            if (!IsSafeProfileId(profileId))
                return false;

            CurrentSetupRecord record = FindCurrentSetupRecord(sledKey, vehicleId);
            return CurrentSetupReferencesProfile(record, profileId);
        }

        public bool IsProfileDefaultForSled(string profileId, string sledKey, string vehicleId)
        {
            if (!IsSafeProfileId(profileId))
                return false;

            string mappedProfileId = null;
            if (IsSafeIdentity(vehicleId))
                _activeProfileIdsBySled.TryGetValue(vehicleId, out mappedProfileId);
            if (string.IsNullOrWhiteSpace(mappedProfileId) && IsSafeIdentity(sledKey))
                _activeProfileIdsBySled.TryGetValue(sledKey, out mappedProfileId);

            return string.Equals(mappedProfileId, profileId, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsProfileInUse(string profileId, out bool isCurrent, out bool isDefault)
        {
            isCurrent = false;
            isDefault = false;
            if (!IsSafeProfileId(profileId))
                return false;

            isCurrent = _currentSetupsByIdentity.Values
                .Where(record => record != null)
                .Distinct()
                .Any(record => CurrentSetupReferencesProfile(record, profileId));
            isDefault = _activeProfileIdsBySled.Values.Any(mappedProfileId =>
                string.Equals(mappedProfileId, profileId, StringComparison.OrdinalIgnoreCase));
            return isCurrent || isDefault;
        }

        private static bool CurrentSetupReferencesProfile(CurrentSetupRecord record, string profileId)
        {
            return record != null &&
                   (string.Equals(record.setupSlotId, profileId, StringComparison.OrdinalIgnoreCase) ||
                    (record.profile != null &&
                     string.Equals(record.profile.profileId, profileId, StringComparison.OrdinalIgnoreCase)));
        }

        public TuneProfile ImportSharedProfile(TuneProfile profile)
        {
            if (profile == null)
                return null;

            var imported = Clone(profile);
            string remoteProfileId = imported.profileId;
            imported.sourceProfileId = remoteProfileId;
            imported.importedUnixTime = NowUnix();
            imported.profileId = Guid.NewGuid().ToString("N");

            imported.name = string.IsNullOrWhiteSpace(imported.name)
                ? "Shared Setup"
                : imported.name;

            return SaveProfile(imported, false) ? imported : null;
        }

        public List<TuneProfile> GetArchivedProfilesForSled(string sledKey, string vehicleId)
        {
            var archived = new List<TuneProfile>();
            if (!Directory.Exists(ArchivedProfilesDir))
                return archived;

            var seenProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(ArchivedProfilesDir, "*.json", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var profile = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(file));
                    if (profile == null || !ProfileTargetsIdentity(profile, sledKey, vehicleId))
                        continue;
                    if (NormalizeProfileAuthor(profile))
                    {
                        profile.checksum = null;
                        profile.checksum = ComputeChecksum(profile);
                    }
                    if (!TryValidateProfileForCatalog(profile, _catalog, false, false, true, out _))
                        continue;
                    if (_profiles.TryGetValue(profile.profileId, out TuneProfile liveProfile) &&
                        string.Equals(
                            ComputeContentFingerprint(liveProfile),
                            ComputeContentFingerprint(profile),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!seenProfileIds.Add(profile.profileId))
                        continue;

                    _catalog.EnsureProfileSelections(profile);
                    archived.Add(profile);
                }
                catch
                {
                    // A damaged recovery entry must not prevent the rest of the
                    // library from being shown. It remains on disk for inspection.
                }
            }

            return archived
                .OrderByDescending(profile => profile.updatedUnixTime)
                .ToList();
        }

        public bool RestoreLatestArchivedProfile(string profileId, out TuneProfile restored)
        {
            restored = null;
            if (!IsSafeProfileId(profileId))
                return false;

            string directory = Path.Combine(ArchivedProfilesDir, SafeFileName(profileId));
            if (!IsPathInsideConfigRoot(directory) || !Directory.Exists(directory))
                return false;

            foreach (string file in Directory.GetFiles(directory, "*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var candidate = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(file));
                    if (NormalizeProfileAuthor(candidate))
                    {
                        candidate.checksum = null;
                        candidate.checksum = ComputeChecksum(candidate);
                    }
                    if (!TryValidateProfileForCatalog(candidate, _catalog, false, false, true, out _))
                        continue;

                    _catalog.EnsureProfileSelections(candidate);
                    if (_profiles.ContainsKey(candidate.profileId))
                    {
                        candidate.profileId = Guid.NewGuid().ToString("N");
                        candidate.name = string.IsNullOrWhiteSpace(candidate.name)
                            ? "Recovered Setup"
                            : candidate.name + " (Recovered)";
                        candidate.usesAutomaticName = false;
                    }

                    candidate.checksum = null;
                    if (!SaveProfile(candidate, false))
                        continue;

                    restored = candidate;
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not restore archived setup {profileId}: {StorageError(ex)}");
                }
            }

            return false;
        }

        public List<TuneHistoryEntry> GetProfileHistoryForSled(
            string sledKey,
            string vehicleId,
            int maximumEntries = 20)
        {
            var history = new List<TuneHistoryEntry>();
            if (!Directory.Exists(ProfileHistoryDir))
                return history;

            int limit = Math.Max(1, Math.Min(maximumEntries, 100));
            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(ProfileHistoryDir, "*.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not enumerate setup history: {StorageError(ex)}");
                return history;
            }

            foreach (string file in files)
            {
                if (history.Count >= limit)
                    break;

                try
                {
                    if (!TryDeserializeFile(file, out TuneProfile profile, out _) ||
                        !ProfileTargetsIdentity(profile, sledKey, vehicleId))
                    {
                        continue;
                    }

                    if (NormalizeProfileAuthor(profile))
                    {
                        profile.checksum = null;
                        profile.checksum = ComputeChecksum(profile);
                    }
                    if (!TryValidateProfileForCatalog(profile, _catalog, true, true, out _))
                        continue;

                    string sourceProfileId = Path.GetFileName(Path.GetDirectoryName(file));
                    string historyId = Path.GetFileNameWithoutExtension(file);
                    if (!IsSafeProfileId(sourceProfileId) || !IsSafeHistoryId(historyId) ||
                        !string.Equals(profile.profileId, sourceProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _catalog.EnsureProfileSelections(profile);
                    history.Add(new TuneHistoryEntry
                    {
                        historyId = historyId,
                        sourceProfileId = sourceProfileId,
                        archivedUnixTime = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds(),
                        profile = Clone(profile)
                    });
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Skipped unreadable setup history entry: {StorageError(ex)}");
                }
            }

            return history;
        }

        public bool RestoreProfileHistory(
            string sourceProfileId,
            string historyId,
            out TuneProfile restored)
        {
            restored = null;
            if (!IsSafeProfileId(sourceProfileId) || !IsSafeHistoryId(historyId))
                return false;

            string directory = Path.Combine(ProfileHistoryDir, SafeFileName(sourceProfileId));
            string path = Path.Combine(directory, historyId + ".json");
            if (!IsPathInsideConfigRoot(path) || !File.Exists(path))
                return false;

            try
            {
                if (!TryDeserializeFile(path, out TuneProfile candidate, out string readReason))
                {
                    MelonLogger.Warning($"Could not read setup history {historyId}: {readReason}");
                    return false;
                }

                if (NormalizeProfileAuthor(candidate))
                {
                    candidate.checksum = null;
                    candidate.checksum = ComputeChecksum(candidate);
                }

                if (!string.Equals(candidate.profileId, sourceProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Warning($"Could not restore setup history {historyId}: slot identity mismatch");
                    return false;
                }

                if (!TryValidateProfileForCatalog(candidate, _catalog, true, true, out string reason))
                {
                    MelonLogger.Warning($"Could not restore setup history {historyId}: {reason}");
                    return false;
                }

                _catalog.EnsureProfileSelections(candidate);
                candidate.sourceProfileId = sourceProfileId;
                candidate.profileId = Guid.NewGuid().ToString("N");
                candidate.name = BuildUniqueProfileName(
                    candidate,
                    (string.IsNullOrWhiteSpace(candidate.name) ? "Saved Setup" : candidate.name) + " (Recovered)");
                candidate.usesAutomaticName = false;
                candidate.createdUnixTime = 0;
                candidate.updatedUnixTime = 0;
                candidate.setupSlotId = null;
                candidate.setupSlotName = null;
                candidate.setupEdited = false;
                candidate.isCurrentSetup = false;
                candidate.checksum = null;

                if (!SaveProfile(candidate, false))
                    return false;

                restored = candidate;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not restore setup history {historyId}: {StorageError(ex)}");
                return false;
            }
        }

        public void MigrateLegacyPresets(IEnumerable<VehicleScriptableObject> sleds, string author)
        {
            if (!Directory.Exists(LegacyPresetsDir))
                return;

            foreach (string file in Directory.GetFiles(LegacyPresetsDir, "*.json")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var legacy = JsonConvert.DeserializeObject<LegacyTunePreset>(json);
                    if (legacy == null || string.IsNullOrWhiteSpace(legacy.SledKey))
                        continue;

                    var sled = sleds.FirstOrDefault(s => LegacySledKeyMatches(
                        AlpineTuningMod.GetSledKey(s),
                        legacy.SledKey));
                    if (sled == null)
                        continue;

                    var profile = _catalog.CreateLegacyProfile(
                        sled,
                        author,
                        legacy.EnginePartName,
                        legacy.TrackPartName,
                        legacy.HandlingPartName,
                        legacy.DonorSledKey);

                    string fingerprint = ComputeContentFingerprint(profile);
                    bool alreadyMigrated = _profiles.Values.Any(existing =>
                        ProfileTargetsIdentity(
                            existing,
                            AlpineTuningMod.GetSledKey(sled),
                            AlpineTuningMod.GetVehicleId(sled)) &&
                        string.Equals(
                            ComputeContentFingerprint(existing),
                            fingerprint,
                            StringComparison.OrdinalIgnoreCase));
                    if (!alreadyMigrated)
                        alreadyMigrated = ArchivedProfileFingerprintExists(fingerprint);
                    if (alreadyMigrated)
                        continue;

                    if (!string.IsNullOrWhiteSpace(fingerprint))
                    {
                        profile.sourceProfileId = "legacy-" +
                                                  fingerprint.Substring(0, Math.Min(32, fingerprint.Length));
                    }

                    if (SaveProfile(profile, true))
                        MelonLogger.Msg($"Migrated legacy Alpine preset for {legacy.SledKey}.");
                    else
                        MelonLogger.Warning($"Legacy Alpine preset was retained because migration failed: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Legacy preset migration skipped for {Path.GetFileName(file)}: {StorageError(ex)}");
                }
            }
        }

        internal static bool LegacySledKeyMatches(string currentSledKey, string legacySledKey)
        {
            return !string.IsNullOrWhiteSpace(currentSledKey) &&
                   !string.IsNullOrWhiteSpace(legacySledKey) &&
                   string.Equals(
                       currentSledKey,
                       legacySledKey,
                       StringComparison.OrdinalIgnoreCase);
        }

        private bool ArchivedProfileFingerprintExists(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || !Directory.Exists(ArchivedProfilesDir))
                return false;

            foreach (string file in Directory.GetFiles(ArchivedProfilesDir, "*.json", SearchOption.AllDirectories))
            {
                if (!TryDeserializeFile(file, out TuneProfile archived, out _))
                    continue;
                if (string.Equals(
                        ComputeContentFingerprint(archived),
                        fingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static TuneProfile Clone(TuneProfile profile)
        {
            if (profile == null)
                return null;

            return JsonConvert.DeserializeObject<TuneProfile>(
                JsonConvert.SerializeObject(profile));
        }

        public static string ComputeChecksum(TuneProfile profile)
        {
            if (profile == null)
                return null;

            var clone = Clone(profile);
            clone.checksum = null;
            string json = JsonConvert.SerializeObject(clone, Formatting.None);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Returns a stable identity for the actual setup choices. Display names,
        /// timestamps, derived stats and sharing metadata are intentionally omitted
        /// so a rename does not look like a new tune revision.
        /// </summary>
        public static string ComputeContentFingerprint(TuneProfile profile)
        {
            if (profile == null)
                return null;

            FineTuneSettings fine = profile.fineTune ?? new FineTuneSettings();
            var data = new ProfileContentFingerprint
            {
                targetIdentity = SledIdentity.StableIdentityKey(
                    profile.targetSledKey,
                    profile.targetVehicleId),
                donorIdentity = SledIdentity.StableIdentityKey(
                    profile.donorSledKey,
                    profile.donorVehicleId),
                headlightEnabled = profile.headlightEnabled,
                selectedParts = (profile.selectedParts ?? new List<PartSelection>())
                    .Where(selection => selection != null)
                    .OrderBy(selection => selection.category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(selection => selection.partId, StringComparer.OrdinalIgnoreCase)
                    .Select(selection => new FingerprintPartSelection
                    {
                        category = selection.category,
                        partId = selection.partId
                    })
                    .ToList(),
                fineTune = new FineTuneSettings
                {
                    powerTrimPercent = fine.powerTrimPercent,
                    tractionTrimPercent = fine.tractionTrimPercent,
                    weightTrimPercent = fine.weightTrimPercent,
                    clutchTrimPercent = fine.clutchTrimPercent,
                    centerOfMassYTrim = fine.centerOfMassYTrim,
                    centerOfMassZTrim = fine.centerOfMassZTrim,
                    skiStanceTrim = fine.skiStanceTrim
                }
            };

            string json = JsonConvert.SerializeObject(data, Formatting.None);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public int CountModifiedParts(TuneProfile profile)
        {
            return ModifiedPartNames(profile).Count;
        }

        public string BuildProfilePartSummary(TuneProfile profile, int maximumNames = 3)
        {
            List<string> names = ModifiedPartNames(profile);
            bool adjusted = HasFineTuneAdjustments(profile != null ? profile.fineTune : null);
            int total = names.Count + (adjusted ? 1 : 0);
            if (total == 0)
                return "Stock";

            int limit = Math.Max(1, maximumNames);
            var displayed = names.Take(limit).ToList();
            if (displayed.Count < limit && adjusted)
                displayed.Add("Adjusted");

            int hidden = total - displayed.Count;
            string summary = string.Join(" · ", displayed.ToArray());
            if (hidden > 0)
                summary += $" · +{hidden}";
            return summary;
        }

        public string BuildAutomaticProfileName(TuneProfile profile)
        {
            if (profile == null)
                return "Saved Setup";

            List<string> names = ModifiedPartNames(profile);
            bool adjusted = HasFineTuneAdjustments(profile.fineTune);
            if (names.Count == 0)
                return adjusted ? "Adjusted Stock" : "Stock Setup";

            var displayed = names.Take(2).ToList();
            int hidden = names.Count - displayed.Count + (adjusted ? 1 : 0);
            string value = string.Join(" + ", displayed.ToArray());
            if (hidden > 0)
                value += $" +{hidden}";

            value = value.Trim();
            if (value.Length > AlpineConstants.MaxProfileNameLength)
                value = value.Substring(0, AlpineConstants.MaxProfileNameLength).TrimEnd();
            return string.IsNullOrWhiteSpace(value) ? "Saved Setup" : value;
        }

        private string BuildUniqueAutomaticProfileName(TuneProfile profile)
        {
            return BuildUniqueProfileName(profile, BuildAutomaticProfileName(profile));
        }

        private string BuildUniqueProfileName(TuneProfile profile, string preferredName)
        {
            string baseName = string.IsNullOrWhiteSpace(preferredName)
                ? "Saved Setup"
                : preferredName.Trim();
            if (baseName.Length > AlpineConstants.MaxProfileNameLength)
                baseName = baseName.Substring(0, AlpineConstants.MaxProfileNameLength).TrimEnd();

            var used = new HashSet<string>(
                _profiles.Values
                    .Where(existing => existing != null &&
                                       !string.Equals(existing.profileId, profile.profileId, StringComparison.OrdinalIgnoreCase) &&
                                       ProfilesTargetSameSled(existing, profile) &&
                                       !string.IsNullOrWhiteSpace(existing.name))
                    .Select(existing => existing.name),
                StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseName))
                return baseName;

            for (int suffixNumber = 2; suffixNumber < 10000; suffixNumber++)
            {
                string suffix = $" ({suffixNumber})";
                int baseLength = Math.Max(1, AlpineConstants.MaxProfileNameLength - suffix.Length);
                string candidateBase = baseName.Length > baseLength
                    ? baseName.Substring(0, baseLength).TrimEnd()
                    : baseName;
                string candidate = candidateBase + suffix;
                if (!used.Contains(candidate))
                    return candidate;
            }

            return baseName;
        }

        private List<string> ModifiedPartNames(TuneProfile profile)
        {
            var names = new List<string>();
            if (profile == null || _catalog == null)
                return names;

            if (IsSafeIdentity(profile.donorSledKey) || IsSafeIdentity(profile.donorVehicleId))
            {
                names.Add(GetDefaults(profile.donorSledKey, profile.donorVehicleId) != null
                    ? "Engine Swap"
                    : "Unavailable Engine");
            }

            foreach (string category in PartCatalog.OrderedCategories)
            {
                string selectedId = profile.GetPartId(category);
                string defaultId = _catalog.DefaultPartId(category);
                if (string.IsNullOrWhiteSpace(selectedId) ||
                    string.Equals(selectedId, defaultId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TunePart part = _catalog.Find(selectedId);
                string name = part != null && !string.IsNullOrWhiteSpace(part.name)
                    ? part.name.Trim()
                    : _catalog.LabelForCategory(category);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            if (profile.headlightEnabled.HasValue)
                names.Add(profile.headlightEnabled.Value ? "Lights On" : "Lights Off");

            return names;
        }

        private static bool HasFineTuneAdjustments(FineTuneSettings fine)
        {
            if (fine == null)
                return false;

            const float epsilon = 0.00001f;
            return Mathf.Abs(fine.powerTrimPercent) > epsilon ||
                   Mathf.Abs(fine.tractionTrimPercent) > epsilon ||
                   Mathf.Abs(fine.weightTrimPercent) > epsilon ||
                   Mathf.Abs(fine.clutchTrimPercent) > epsilon ||
                   Mathf.Abs(fine.centerOfMassYTrim) > epsilon ||
                   Mathf.Abs(fine.centerOfMassZTrim) > epsilon ||
                   Mathf.Abs(fine.skiStanceTrim) > epsilon;
        }

        public static bool ChecksumMatches(TuneProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.checksum))
                return false;

            return string.Equals(profile.checksum, ComputeChecksum(profile), StringComparison.OrdinalIgnoreCase);
        }

        private void LoadDefaults()
        {
            _defaults.Clear();
            if (!Directory.Exists(DefaultsDir))
                return;

            foreach (string file in EnumerateJsonWithBackupCandidates(DefaultsDir))
            {
                try
                {
                    bool recoveredFromBackup = false;
                    bool primaryRead = TryDeserializeFile(
                        file,
                        out SledDefaults defaults,
                        out string primaryReadReason);
                    string primaryValidationReason = null;
                    bool primaryValid = primaryRead && TryValidateDefaults(defaults, out primaryValidationReason);
                    if (!primaryValid)
                    {
                        string backupPath = file + ".bak";
                        bool backupRead = TryDeserializeFile(
                            backupPath,
                            out SledDefaults backup,
                            out string backupReadReason);
                        string backupValidationReason = null;
                        bool backupValid = backupRead && TryValidateDefaults(backup, out backupValidationReason);
                        if (!backupValid)
                        {
                            string reason = primaryRead
                                ? primaryValidationReason
                                : primaryReadReason;
                            MelonLogger.Warning(
                                $"Skipped invalid defaults {Path.GetFileName(file)}: {reason ?? "no valid primary or backup"}");
                            continue;
                        }

                        defaults = backup;
                        recoveredFromBackup = true;
                        MelonLogger.Warning($"Recovered sled defaults from backup: {Path.GetFileName(file)}");
                    }

                    if (recoveredFromBackup &&
                        !RestoreRecoveredPrimary(
                            file,
                            defaults,
                            $"defaults {IdentityKey(defaults.sledKey, defaults.vehicleId)}"))
                    {
                        continue;
                    }

                    IndexDefaults(defaults);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not load defaults {Path.GetFileName(file)}: {StorageError(ex)}");
                }
            }
        }

        private bool SaveDefaults(SledDefaults defaults)
        {
            try
            {
                Directory.CreateDirectory(DefaultsDir);
                string key = IdentityKey(defaults.sledKey, defaults.vehicleId);
                string path = Path.Combine(DefaultsDir, SafeFileName(key) + ".json");
                bool written = WriteJsonAtomic(path, defaults, $"defaults {key}");
                if (!written)
                    return false;

                if (written && IsSafeIdentity(defaults.vehicleId) &&
                    IsSafeIdentity(defaults.sledKey) &&
                    !string.Equals(defaults.vehicleId, defaults.sledKey, StringComparison.OrdinalIgnoreCase))
                {
                    string legacyPath = Path.Combine(DefaultsDir, SafeFileName(defaults.sledKey) + ".json");
                    if (!string.Equals(path, legacyPath, StringComparison.OrdinalIgnoreCase))
                        DeleteStoredFileAndBackup(legacyPath, "migrated defaults");
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save defaults for {defaults.sledKey}: {StorageError(ex)}");
                return false;
            }
        }

        private bool TryReadProfileWithRecovery(
            string path,
            out TuneProfile profile,
            out bool recovered,
            out bool recoveredFromBackup,
            out string reason)
        {
            profile = null;
            recovered = false;
            recoveredFromBackup = false;
            reason = null;

            TuneProfile primary = null;
            string primaryReadReason;
            bool primaryRead = TryDeserializeFile(path, out primary, out primaryReadReason);
            if (primaryRead && IsLoadableProfileCandidate(primary, true, out reason))
            {
                profile = primary;
                return true;
            }

            string backupPath = path + ".bak";
            if (TryDeserializeFile(backupPath, out TuneProfile backup, out string backupReadReason) &&
                IsLoadableProfileCandidate(backup, true, out string backupValidationReason))
            {
                if (primaryRead &&
                    primary != null &&
                    IsSafeProfileId(primary.profileId) &&
                    !PreserveProfileSnapshot(primary, RecoveryDir, "rejected setup primary"))
                {
                    reason = "rejected primary could not be preserved before backup recovery";
                    return false;
                }

                profile = backup;
                recovered = true;
                recoveredFromBackup = true;
                reason = null;
                MelonLogger.Warning($"Recovered setup profile from backup: {Path.GetFileName(path)}");
                return true;
            }

            // Keep the previous permissive local-file behavior as a last resort,
            // but mark it as recovered so an untouched snapshot is retained before
            // its checksum is normalized. A parseable tune is preferable to making
            // it disappear from the rider's library.
            if (primaryRead && IsLoadableProfileCandidate(primary, false, out string salvageReason))
            {
                profile = primary;
                recovered = true;
                reason = null;
                MelonLogger.Warning($"Recovered setup profile with repairable metadata: {Path.GetFileName(path)}");
                return true;
            }

            reason = !string.IsNullOrWhiteSpace(primaryReadReason)
                ? primaryReadReason
                : (reason ?? "profile and backup are invalid");
            return false;
        }

        private bool TryReadCurrentSetupWithRecovery(
            string path,
            out CurrentSetupRecord record,
            out bool recovered,
            out bool recoveredFromBackup,
            out string reason)
        {
            record = null;
            recovered = false;
            recoveredFromBackup = false;
            reason = null;

            CurrentSetupRecord primary = null;
            string primaryReadReason;
            bool primaryRead = TryDeserializeFile(path, out primary, out primaryReadReason);
            if (primaryRead && IsLoadableCurrentSetupCandidate(primary, true, out reason))
            {
                record = primary;
                return true;
            }

            string backupPath = path + ".bak";
            if (TryDeserializeFile(backupPath, out CurrentSetupRecord backup, out string backupReadReason) &&
                IsLoadableCurrentSetupCandidate(backup, true, out string backupValidationReason))
            {
                if (primaryRead &&
                    primary?.profile != null &&
                    IsSafeProfileId(primary.profile.profileId) &&
                    !PreserveProfileSnapshot(
                        primary.profile,
                        RecoveryDir,
                        "rejected current setup primary"))
                {
                    reason = "rejected current setup primary could not be preserved before backup recovery";
                    return false;
                }

                record = backup;
                recovered = true;
                recoveredFromBackup = true;
                reason = null;
                MelonLogger.Warning($"Recovered current setup from backup: {Path.GetFileName(path)}");
                return true;
            }

            if (primaryRead && IsLoadableCurrentSetupCandidate(primary, false, out string salvageReason))
            {
                record = primary;
                recovered = true;
                reason = null;
                MelonLogger.Warning($"Recovered current setup with repairable metadata: {Path.GetFileName(path)}");
                return true;
            }

            reason = !string.IsNullOrWhiteSpace(primaryReadReason)
                ? primaryReadReason
                : (reason ?? "current setup and backup are invalid");
            return false;
        }

        private bool IsLoadableProfileCandidate(TuneProfile candidate, bool requireChecksumMatch, out string reason)
        {
            reason = null;
            if (candidate == null)
            {
                reason = "profile is empty";
                return false;
            }

            if (requireChecksumMatch &&
                (string.IsNullOrWhiteSpace(candidate.checksum) || !ChecksumMatches(candidate)))
            {
                reason = string.IsNullOrWhiteSpace(candidate.checksum)
                    ? "missing checksum"
                    : "checksum mismatch";
                return false;
            }

            TuneProfile validationCopy = Clone(candidate);
            return TryValidateProfileForCatalog(
                validationCopy,
                _catalog,
                false,
                false,
                true,
                out reason);
        }

        private bool IsLoadableCurrentSetupCandidate(
            CurrentSetupRecord candidate,
            bool requireChecksumMatch,
            out string reason)
        {
            reason = null;
            if (candidate == null || candidate.profile == null)
            {
                reason = "current setup is empty";
                return false;
            }

            return IsLoadableProfileCandidate(candidate.profile, requireChecksumMatch, out reason);
        }

        private static bool NormalizeCurrentSetupIdentity(CurrentSetupRecord record)
        {
            if (record?.profile == null)
                return false;

            string profileSledKey = IsSafeIdentity(record.profile.targetSledKey)
                ? record.profile.targetSledKey
                : null;
            string profileVehicleId = IsSafeIdentity(record.profile.targetVehicleId)
                ? record.profile.targetVehicleId
                : null;

            // The embedded profile is the checksummed source of truth. A legacy
            // wrapper vehicle ID may be retained only when its sled key already
            // agrees with a profile that predates native vehicle IDs.
            string normalizedVehicleId = profileVehicleId;
            if (normalizedVehicleId == null &&
                profileSledKey != null &&
                string.Equals(record.sledKey, profileSledKey, StringComparison.OrdinalIgnoreCase) &&
                IsSafeIdentity(record.vehicleId))
            {
                normalizedVehicleId = record.vehicleId;
            }

            bool changed =
                !string.Equals(record.sledKey, profileSledKey, StringComparison.Ordinal) ||
                !string.Equals(record.vehicleId, normalizedVehicleId, StringComparison.Ordinal);
            record.sledKey = profileSledKey;
            record.vehicleId = normalizedVehicleId;
            return changed;
        }

        private static bool TryDeserializeFile<T>(string path, out T value, out string reason)
        {
            value = default(T);
            reason = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                reason = "file is missing";
                return false;
            }

            try
            {
                value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                if (ReferenceEquals(value, null))
                {
                    reason = "file is empty";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = StorageError(ex);
                return false;
            }
        }

        private static IEnumerable<string> EnumerateJsonWithBackupCandidates(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Enumerable.Empty<string>();

            IEnumerable<string> primary = Directory.GetFiles(directory, "*.json");
            IEnumerable<string> backupOnly = Directory.GetFiles(directory, "*.json.bak")
                .Select(path => path.Substring(0, path.Length - ".bak".Length));
            return primary
                .Concat(backupOnly)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void LoadProfiles()
        {
            _profiles.Clear();
            if (!Directory.Exists(ProfilesDir))
                return;

            foreach (string file in EnumerateJsonWithBackupCandidates(ProfilesDir))
            {
                try
                {
                    if (!TryReadProfileWithRecovery(
                            file,
                            out TuneProfile profile,
                            out bool recovered,
                            out bool recoveredFromBackup,
                            out string loadReason))
                    {
                        MelonLogger.Warning($"Could not load profile {Path.GetFileName(file)}: {loadReason}");
                        continue;
                    }

                    TuneProfile originalProfile = Clone(profile);
                    string originalFingerprint = ComputeContentFingerprint(originalProfile);
                    bool privacyRepaired = NormalizeProfileAuthor(profile);
                    if (!TryValidateProfileForCatalog(profile, _catalog, false, false, true, out var reason))
                    {
                        MelonLogger.Warning($"Skipped invalid profile {Path.GetFileName(file)}: {reason}");
                        continue;
                    }

                    _catalog.EnsureProfileSelections(profile);
                    bool versionRepaired =
                        profile.schemaVersion != AlpineConstants.SchemaVersion ||
                        !string.Equals(profile.modVersion, AlpineConstants.ModVersion, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(profile.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase);
                    profile.schemaVersion = AlpineConstants.SchemaVersion;
                    profile.modVersion = AlpineConstants.ModVersion;
                    profile.catalogVersion = AlpineConstants.CatalogVersion;
                    bool catalogRepaired = !string.Equals(
                        originalFingerprint,
                        ComputeContentFingerprint(profile),
                        StringComparison.OrdinalIgnoreCase);
                    string normalizedChecksum = ComputeChecksum(profile);
                    bool repaired = recovered || privacyRepaired || catalogRepaired || versionRepaired ||
                                     !string.Equals(profile.checksum, normalizedChecksum, StringComparison.OrdinalIgnoreCase);
                    if (repaired)
                    {
                        profile.checksum = normalizedChecksum;
                    }

                    bool recoveryPreserved = true;
                    if (recovered)
                        recoveryPreserved &= PreserveProfileSnapshot(originalProfile, RecoveryDir, "recovered setup");
                    else if (catalogRepaired || versionRepaired)
                        recoveryPreserved &= PreserveProfileSnapshot(originalProfile, RecoveryDir, "pre-migration setup");

                    TuneProfile durableProfile = profile;
                    bool durable = true;
                    if (repaired)
                    {
                        if (!recoveryPreserved)
                        {
                            durable = false;
                        }
                        else if (recoveredFromBackup)
                        {
                            durable = RestoreRecoveredPrimary(
                                file,
                                profile,
                                $"profile {profile.profileId}");
                        }
                        else
                        {
                            durable = WriteJsonAtomic(
                                file,
                                profile,
                                $"profile {profile.profileId} checksum repair");
                        }

                        durable &= TryReadDurableProfile(file, profile.profileId, out durableProfile);
                    }

                    if (durable)
                        _profiles[durableProfile.profileId] = durableProfile;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not load profile {Path.GetFileName(file)}: {StorageError(ex)}");
                }
            }
        }

        private bool TryReadDurableProfile(
            string path,
            string expectedProfileId,
            out TuneProfile profile)
        {
            profile = null;
            if (!TryDeserializeFile(path, out TuneProfile durable, out _) ||
                durable == null ||
                !string.Equals(durable.profileId, expectedProfileId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(durable.checksum) ||
                !ChecksumMatches(durable) ||
                !IsLoadableProfileCandidate(durable, true, out _))
            {
                return false;
            }

            _catalog.EnsureProfileSelections(durable);
            profile = durable;
            return true;
        }

        private void LoadCurrentSetups()
        {
            _currentSetupsByIdentity.Clear();
            if (!Directory.Exists(CurrentSetupsDir))
                return;

            var candidates = new List<LoadedCurrentSetupCandidate>();
            foreach (string file in EnumerateJsonWithBackupCandidates(CurrentSetupsDir))
            {
                try
                {
                    if (!TryPrepareCurrentSetupCandidate(
                            file,
                            out LoadedCurrentSetupCandidate candidate,
                            out string loadReason))
                    {
                        MelonLogger.Warning($"Could not load current setup {Path.GetFileName(file)}: {loadReason}");
                        continue;
                    }

                    candidates.Add(candidate);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not load current setup {Path.GetFileName(file)}: {StorageError(ex)}");
                }
            }

            foreach (var group in candidates.GroupBy(
                candidate => candidate.normalizedIdentity,
                StringComparer.OrdinalIgnoreCase))
            {
                List<LoadedCurrentSetupCandidate> ranked = group
                    .OrderByDescending(candidate => candidate.originalChecksumValid)
                    .ThenByDescending(candidate => candidate.originalChecksumValid
                        ? candidate.originalProfile.updatedUnixTime
                        : long.MinValue)
                    .ThenByDescending(candidate => candidate.originalPathCanonical)
                    .ThenBy(candidate => candidate.contentFingerprint, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.sourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                LoadedCurrentSetupCandidate winner = ranked[0];
                List<LoadedCurrentSetupCandidate> losers = ranked.Skip(1).ToList();

                bool recoveryPreserved = true;
                if (winner.preserveOriginal)
                {
                    recoveryPreserved &= PreserveProfileSnapshot(
                        winner.originalProfile,
                        RecoveryDir,
                        "recovered current setup");
                }

                foreach (LoadedCurrentSetupCandidate loser in losers)
                {
                    recoveryPreserved &= PreserveProfileSnapshot(
                        loser.originalProfile,
                        RecoveryDir,
                        "superseded current setup");
                }

                // A previous canonical backup is another recoverable revision that
                // normal atomic replacement would rotate away. Preserve it before
                // a non-canonical winner replaces the canonical primary.
                if (!winner.originalPathCanonical && File.Exists(winner.canonicalPath + ".bak"))
                {
                    if (TryDeserializeFile(
                            winner.canonicalPath + ".bak",
                            out CurrentSetupRecord previousBackup,
                            out _) &&
                        previousBackup?.profile != null &&
                        IsLoadableProfileCandidate(previousBackup.profile, false, out _))
                    {
                        recoveryPreserved &= PreserveProfileSnapshot(
                            previousBackup.profile,
                            RecoveryDir,
                            "superseded current setup backup");
                    }
                }

                if (!recoveryPreserved)
                {
                    MelonLogger.Warning(
                        $"Current setup repair for {winner.normalizedIdentity} was deferred because recovery could not be preserved.");
                    continue;
                }

                bool requiresWrite = winner.repaired || !winner.originalPathCanonical;
                bool written = true;
                if (requiresWrite)
                {
                    written = winner.recoveredFromBackup && winner.originalPathCanonical
                        ? RestoreRecoveredPrimary(
                            winner.canonicalPath,
                            winner.record,
                            $"current setup {winner.normalizedIdentity}")
                        : WriteJsonAtomic(
                            winner.canonicalPath,
                            winner.record,
                            $"current setup {winner.normalizedIdentity} repair");
                }

                if (!written ||
                    !TryReadDurableCurrentSetup(
                        winner.canonicalPath,
                        winner.normalizedIdentity,
                        out CurrentSetupRecord durableWinner))
                {
                    MelonLogger.Warning(
                        $"Current setup repair for {winner.normalizedIdentity} did not pass durable readback.");
                    continue;
                }

                IndexCurrentSetup(durableWinner);

                foreach (LoadedCurrentSetupCandidate stale in ranked)
                {
                    if (!string.Equals(
                            stale.sourcePath,
                            winner.canonicalPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteStoredFileAndBackup(stale.sourcePath, "superseded current setup");
                    }
                }
            }
        }

        private bool TryPrepareCurrentSetupCandidate(
            string path,
            out LoadedCurrentSetupCandidate candidate,
            out string reason)
        {
            candidate = null;
            if (!TryReadCurrentSetupWithRecovery(
                    path,
                    out CurrentSetupRecord record,
                    out bool recovered,
                    out bool recoveredFromBackup,
                    out reason))
            {
                return false;
            }

            bool originalChecksumValid =
                !string.IsNullOrWhiteSpace(record.profile.checksum) &&
                ChecksumMatches(record.profile);
            TuneProfile originalProfile = Clone(record.profile);
            string originalFingerprint = ComputeContentFingerprint(originalProfile);
            bool privacyRepaired = NormalizeProfileAuthor(record.profile);
            bool identityRepaired = NormalizeCurrentSetupIdentity(record);

            if (!IsSafeIdentity(record.sledKey) && !IsSafeIdentity(record.vehicleId))
            {
                reason = "normalized current setup identity is invalid";
                return false;
            }

            if (!TryValidateProfileForCatalog(
                    record.profile,
                    _catalog,
                    false,
                    false,
                    true,
                    out reason))
            {
                return false;
            }

            _catalog.EnsureProfileSelections(record.profile);
            bool versionRepaired =
                record.profile.schemaVersion != AlpineConstants.SchemaVersion ||
                !string.Equals(record.profile.modVersion, AlpineConstants.ModVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.profile.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase);
            record.profile.schemaVersion = AlpineConstants.SchemaVersion;
            record.profile.modVersion = AlpineConstants.ModVersion;
            record.profile.catalogVersion = AlpineConstants.CatalogVersion;
            bool catalogRepaired = !string.Equals(
                originalFingerprint,
                ComputeContentFingerprint(record.profile),
                StringComparison.OrdinalIgnoreCase);
            bool metadataRepaired = NormalizeCurrentSetupMetadata(record);

            string normalizedChecksum = ComputeChecksum(record.profile);
            bool checksumRepaired = !string.Equals(
                record.profile.checksum,
                normalizedChecksum,
                StringComparison.OrdinalIgnoreCase);
            if (checksumRepaired)
                record.profile.checksum = normalizedChecksum;

            string normalizedIdentity = IdentityKey(record.sledKey, record.vehicleId);
            if (!IsSafeIdentity(normalizedIdentity))
            {
                reason = "normalized current setup storage identity is invalid";
                return false;
            }

            string canonicalPath = Path.Combine(
                CurrentSetupsDir,
                SafeFileName(normalizedIdentity) + ".json");
            candidate = new LoadedCurrentSetupCandidate
            {
                sourcePath = path,
                canonicalPath = canonicalPath,
                normalizedIdentity = normalizedIdentity,
                record = record,
                originalProfile = originalProfile,
                recoveredFromBackup = recoveredFromBackup,
                originalChecksumValid = originalChecksumValid,
                originalPathCanonical = string.Equals(path, canonicalPath, StringComparison.OrdinalIgnoreCase),
                repaired = recovered || privacyRepaired || identityRepaired || catalogRepaired ||
                           versionRepaired || metadataRepaired || checksumRepaired,
                preserveOriginal = recovered || catalogRepaired || versionRepaired,
                contentFingerprint = originalFingerprint ?? string.Empty
            };
            reason = null;
            return true;
        }

        private bool NormalizeCurrentSetupMetadata(CurrentSetupRecord record)
        {
            if (record?.profile == null)
                return false;

            string slotId = record.profile.profileId;
            string slotName = record.profile.name;
            bool setupEdited = record.setupEdited;
            if (_profiles.TryGetValue(record.profile.profileId, out TuneProfile savedProfile) &&
                savedProfile != null &&
                ProfilesTargetSameSled(savedProfile, record.profile))
            {
                slotName = savedProfile.name;
                setupEdited = !string.Equals(
                    ComputeContentFingerprint(savedProfile),
                    ComputeContentFingerprint(record.profile),
                    StringComparison.OrdinalIgnoreCase);
            }

            bool changed =
                record.schemaVersion != 1 ||
                record.displayName != null ||
                !string.Equals(record.setupSlotId, slotId, StringComparison.Ordinal) ||
                !string.Equals(record.setupSlotName, slotName, StringComparison.Ordinal) ||
                record.setupEdited != setupEdited ||
                record.updatedUnixTime != record.profile.updatedUnixTime;
            record.schemaVersion = 1;
            record.displayName = null;
            record.setupSlotId = slotId;
            record.setupSlotName = slotName;
            record.setupEdited = setupEdited;
            record.updatedUnixTime = record.profile.updatedUnixTime;
            MarkSetupMetadata(record.profile, slotId, slotName, setupEdited, true);
            return changed;
        }

        private bool TryReadDurableCurrentSetup(
            string path,
            string expectedIdentity,
            out CurrentSetupRecord record)
        {
            record = null;
            if (!TryDeserializeFile(path, out CurrentSetupRecord durable, out _) ||
                durable?.profile == null ||
                string.IsNullOrWhiteSpace(durable.profile.checksum) ||
                !ChecksumMatches(durable.profile) ||
                !IsLoadableProfileCandidate(durable.profile, true, out _))
            {
                return false;
            }

            NormalizeCurrentSetupIdentity(durable);
            if (!string.Equals(
                    IdentityKey(durable.sledKey, durable.vehicleId),
                    expectedIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _catalog.EnsureProfileSelections(durable.profile);
            NormalizeCurrentSetupMetadata(durable);
            record = durable;
            return true;
        }

        private bool WriteCurrentSetup(CurrentSetupRecord record)
        {
            if (record == null || record.profile == null)
                return false;

            try
            {
                Directory.CreateDirectory(CurrentSetupsDir);
                string key = IdentityKey(record.sledKey, record.vehicleId);
                if (!IsSafeIdentity(key))
                    return false;

                record.updatedUnixTime = NowUnix();
                record.profile.updatedUnixTime = record.updatedUnixTime;
                NormalizeProfileAuthor(record.profile);
                _catalog.EnsureProfileSelections(record.profile);
                NormalizeCurrentSetupMetadata(record);
                record.profile.checksum = null;
                record.profile.checksum = ComputeChecksum(record.profile);
                string path = Path.Combine(CurrentSetupsDir, SafeFileName(key) + ".json");
                bool written = WriteJsonAtomic(path, record, $"current setup {key}");
                if (!written)
                    return false;

                // Current builds use native numeric vehicle IDs. Remove the old
                // name-keyed record after the new identity file is safely written,
                // otherwise load order can resurrect stale setup state.
                if (IsSafeIdentity(record.vehicleId) &&
                    IsSafeIdentity(record.sledKey) &&
                    !string.Equals(record.vehicleId, record.sledKey, StringComparison.OrdinalIgnoreCase))
                {
                    string legacyPath = Path.Combine(CurrentSetupsDir, SafeFileName(record.sledKey) + ".json");
                    if (!string.Equals(path, legacyPath, StringComparison.OrdinalIgnoreCase))
                        DeleteStoredFileAndBackup(legacyPath, "migrated current setup");
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save current setup for {record.sledKey ?? record.vehicleId}: {StorageError(ex)}");
                return false;
            }
        }

        private void LoadActiveMap()
        {
            _activeProfileIdsBySled.Clear();
            if (!File.Exists(ActiveMapPath) && !File.Exists(ActiveMapPath + ".bak"))
                return;

            try
            {
                bool recovered = false;
                if (!TryDeserializeFile(
                        ActiveMapPath,
                        out Dictionary<string, string> map,
                        out string primaryReason))
                {
                    if (!TryDeserializeFile(
                            ActiveMapPath + ".bak",
                            out map,
                            out string backupReason))
                    {
                        MelonLogger.Warning($"Could not load active profile map: {primaryReason}");
                        return;
                    }

                    recovered = true;
                    MelonLogger.Warning("Recovered the default setup map from its backup.");
                }

                if (map == null)
                    return;

                foreach (var pair in map)
                {
                    if (!IsSafeIdentity(pair.Key) || !IsSafeProfileId(pair.Value))
                    {
                        MelonLogger.Warning("Skipped invalid active profile map entry.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        _activeProfileIdsBySled[pair.Key] = pair.Value;
                }

                if (recovered)
                    RestoreRecoveredPrimary(ActiveMapPath, _activeProfileIdsBySled, "active profile map");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not load active profile map: {StorageError(ex)}");
            }
        }

        private void LoadSettings()
        {
            _settings = new AlpineUserSettings();

            bool primaryExists = File.Exists(SettingsPath);
            bool backupExists = File.Exists(SettingsPath + ".bak");
            if (!primaryExists && !backupExists)
            {
                _settings.Normalize();
                SaveSettings();
                return;
            }

            try
            {
                bool recoveredFromBackup = false;
                if (!TryDeserializeFile(
                        SettingsPath,
                        out AlpineUserSettings loaded,
                        out string primaryReason))
                {
                    if (!TryDeserializeFile(
                            SettingsPath + ".bak",
                            out loaded,
                            out string backupReason))
                    {
                        MelonLogger.Warning($"Could not load Alpine user settings: {primaryReason}");
                        _settings.Normalize();
                        return;
                    }

                    recoveredFromBackup = true;
                    MelonLogger.Warning("Recovered Alpine user settings from backup.");
                }

                _settings = loaded;
                _settings.Normalize();
                if (recoveredFromBackup)
                    RestoreRecoveredPrimary(SettingsPath, _settings, "user settings");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not load Alpine user settings: {StorageError(ex)}");
                _settings = new AlpineUserSettings();
                _settings.Normalize();
            }
        }

        private void IndexCurrentSetup(CurrentSetupRecord record)
        {
            if (record == null)
                return;

            if (IsSafeIdentity(record.vehicleId))
                IndexCurrentSetupIdentity(record.vehicleId, record);

            if (IsSafeIdentity(record.sledKey))
                IndexCurrentSetupIdentity(record.sledKey, record);
        }

        private void IndexCurrentSetupIdentity(string identity, CurrentSetupRecord candidate)
        {
            if (!_currentSetupsByIdentity.TryGetValue(identity, out var existing) ||
                existing == null ||
                candidate.updatedUnixTime > existing.updatedUnixTime ||
                (candidate.updatedUnixTime == existing.updatedUnixTime &&
                 HasNativeVehicleIdentity(candidate) && !HasNativeVehicleIdentity(existing)))
            {
                _currentSetupsByIdentity[identity] = candidate;
            }
        }

        private static bool HasNativeVehicleIdentity(CurrentSetupRecord record)
        {
            return record != null &&
                   IsSafeIdentity(record.vehicleId) &&
                   !string.Equals(record.vehicleId, record.sledKey, StringComparison.OrdinalIgnoreCase);
        }

        private CurrentSetupRecord FindCurrentSetupRecord(string sledKey, string vehicleId)
        {
            if (IsSafeIdentity(vehicleId) &&
                _currentSetupsByIdentity.TryGetValue(vehicleId, out var byVehicleId))
            {
                return byVehicleId;
            }

            if (IsSafeIdentity(sledKey) &&
                _currentSetupsByIdentity.TryGetValue(sledKey, out var bySledKey))
            {
                if (SledIdentity.HasNativeVehicleIdentity(sledKey, vehicleId) &&
                    (_ambiguousSledKeys.Contains(sledKey) ||
                     (SledIdentity.HasNativeVehicleIdentity(bySledKey.sledKey, bySledKey.vehicleId) &&
                      !string.Equals(bySledKey.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))))
                {
                    return null;
                }

                return bySledKey;
            }

            return null;
        }

        private static string IdentityKey(string sledKey, string vehicleId)
        {
            return IsSafeIdentity(vehicleId) ? vehicleId : sledKey;
        }

        private static void MarkSetupMetadata(
            TuneProfile profile,
            string setupSlotId,
            string setupSlotName,
            bool setupEdited,
            bool isCurrentSetup)
        {
            if (profile == null)
                return;

            profile.setupSlotId = setupSlotId;
            profile.setupSlotName = setupSlotName;
            profile.setupEdited = setupEdited;
            profile.isCurrentSetup = isCurrentSetup;
        }

        private static bool NormalizeProfileAuthor(TuneProfile profile)
        {
            if (profile == null)
                return false;

            bool changed = false;
            if (!string.Equals(profile.author, AlpineConstants.DefaultProfileAuthor, StringComparison.Ordinal))
            {
                profile.author = AlpineConstants.DefaultProfileAuthor;
                changed = true;
            }

            if (profile.sourceSenderId != 0)
            {
                profile.sourceSenderId = 0;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(profile.sourceSenderName))
            {
                profile.sourceSenderName = null;
                changed = true;
            }

            return changed;
        }

        private void ScrubStoredProfilePrivacy()
        {
            int repaired = 0;
            repaired += ScrubProfileDirectoryPrivacy(ProfilesDir, false);
            repaired += ScrubProfileDirectoryPrivacy(ProfileHistoryDir, false);
            repaired += ScrubProfileDirectoryPrivacy(ArchivedProfilesDir, false);
            repaired += ScrubProfileDirectoryPrivacy(RecoveryDir, false);
            repaired += ScrubProfileDirectoryPrivacy(LegacyPresetsDir, false);
            repaired += ScrubProfileDirectoryPrivacy(CurrentSetupsDir, true);
            if (repaired > 0)
                MelonLogger.Msg($"Sanitized privacy metadata in {repaired} stored setup file(s).");
        }

        private int ScrubProfileDirectoryPrivacy(string directory, bool currentSetupRecords)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            int repaired = 0;
            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(directory, "*.json*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not enumerate stored setup privacy data: {StorageError(ex)}");
                return 0;
            }

            foreach (string file in files)
            {
                try
                {
                    if (currentSetupRecords)
                    {
                        if (!TryDeserializeFile(file, out CurrentSetupRecord record, out _) ||
                            record?.profile == null)
                        {
                            continue;
                        }

                        string currentRecordChecksum = record.profile.checksum;
                        bool currentRecordChecksumValid =
                            !string.IsNullOrWhiteSpace(currentRecordChecksum) &&
                            ChecksumMatches(record.profile);
                        if (!NormalizeProfileAuthor(record.profile))
                            continue;

                        if (currentRecordChecksumValid)
                        {
                            record.profile.checksum = null;
                            record.profile.checksum = ComputeChecksum(record.profile);
                        }
                        else
                        {
                            // Sanitizing privacy metadata must not convert a
                            // missing/mismatched checksum into trusted content.
                            record.profile.checksum = currentRecordChecksum;
                        }
                        if (RewriteSanitizedJson(file, record))
                            repaired++;
                        continue;
                    }

                    if (!TryDeserializeFile(file, out TuneProfile profile, out _) ||
                        profile == null ||
                        string.IsNullOrWhiteSpace(profile.profileId))
                    {
                        continue;
                    }

                    string originalChecksum = profile.checksum;
                    bool originalChecksumValid =
                        !string.IsNullOrWhiteSpace(originalChecksum) &&
                        ChecksumMatches(profile);
                    if (!NormalizeProfileAuthor(profile))
                        continue;

                    if (originalChecksumValid)
                    {
                        profile.checksum = null;
                        profile.checksum = ComputeChecksum(profile);
                    }
                    else
                    {
                        profile.checksum = originalChecksum;
                    }
                    if (RewriteSanitizedJson(file, profile))
                        repaired++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not sanitize stored setup privacy metadata: {StorageError(ex)}");
                }
            }

            return repaired;
        }

        private bool RewriteSanitizedJson<T>(string path, T value)
        {
            if (!IsPathInsideConfigRoot(path))
                return false;

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".privacy.tmp";
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);
            try
            {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(value, _jsonSettings), Encoding.UTF8);
                if (File.Exists(path))
                    File.Replace(tempPath, path, null, true);
                else
                    File.Move(tempPath, path);
                File.SetLastWriteTimeUtc(path, originalWriteTime);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not rewrite sanitized setup metadata: {StorageError(ex)}");
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
                return false;
            }
        }

        private static string StorageError(Exception exception)
        {
            if (exception == null)
                return "unknown error";

            // Exception messages frequently embed absolute profile paths. Release
            // logs need an actionable error class/code, not machine identity data.
            return $"{exception.GetType().Name} (0x{unchecked((uint)exception.HResult):X8})";
        }

        private static bool ProfilesTargetSameSled(TuneProfile left, TuneProfile right)
        {
            if (left == null || right == null)
                return false;

            if (IsSafeIdentity(left.targetVehicleId) &&
                IsSafeIdentity(right.targetVehicleId) &&
                string.Equals(left.targetVehicleId, right.targetVehicleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (SledIdentity.HasNativeVehicleIdentity(left.targetSledKey, left.targetVehicleId) ||
                SledIdentity.HasNativeVehicleIdentity(right.targetSledKey, right.targetVehicleId))
            {
                return false;
            }

            return IsSafeIdentity(left.targetSledKey) &&
                   string.Equals(left.targetSledKey, right.targetSledKey, StringComparison.OrdinalIgnoreCase);
        }

        private bool PreserveProfileSnapshot(TuneProfile profile, string root, string description)
        {
            if (profile == null || !IsSafeProfileId(profile.profileId))
                return false;

            TuneProfile snapshot = Clone(profile);
            if (snapshot == null)
                return false;

            NormalizeProfileAuthor(snapshot);
            snapshot.checksum = null;
            snapshot.checksum = ComputeChecksum(snapshot);

            string fingerprint = ComputeContentFingerprint(snapshot) ?? "setup";
            string shortFingerprint = fingerprint.Length > 12
                ? fingerprint.Substring(0, 12)
                : fingerprint;
            string fileName =
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" +
                shortFingerprint + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 6) + ".json";
            string directory = Path.Combine(root, SafeFileName(snapshot.profileId));
            string path = Path.Combine(directory, fileName);
            return WriteJsonAtomic(path, snapshot, $"{description} {snapshot.profileId}");
        }

        private bool SaveActiveMap()
        {
            try
            {
                return WriteJsonAtomic(ActiveMapPath, _activeProfileIdsBySled, "active profile map");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save active profile map: {StorageError(ex)}");
                return false;
            }
        }

        private void MaybeMigrateLegacyConfig()
        {
            try
            {
                if (!Directory.Exists(LegacyConfigRoot))
                    return;
                if (File.Exists(LegacyImportMarkerPath))
                    return;

                // Merge missing legacy files instead of treating either root as
                // all-or-nothing. DirectoryCopy never overwrites a current file,
                // so an older install can contribute forgotten setup slots without
                // replacing anything the rider has saved in AlpineTuning.
                DirectoryCopy(LegacyConfigRoot, ConfigRoot, true);
                WriteJsonAtomic(
                    LegacyImportMarkerPath,
                    new Dictionary<string, object>
                    {
                        ["schemaVersion"] = 1,
                        ["completedUnixTime"] = NowUnix()
                    },
                    "legacy import marker");
                MelonLogger.Msg("Checked old SleddersTuner files for recoverable Alpine setups.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Legacy config migration skipped: {StorageError(ex)}");
            }
        }

        private static void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                return;

            Directory.CreateDirectory(destDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string target = Path.Combine(destDir, file.Name);
                if (IsPathInsideConfigRoot(target) && !File.Exists(target))
                    file.CopyTo(target, false);
            }

            if (!copySubDirs)
                return;

            foreach (DirectoryInfo subdir in dir.GetDirectories())
            {
                string target = Path.Combine(destDir, subdir.Name);
                if (IsPathInsideConfigRoot(target))
                    DirectoryCopy(subdir.FullName, target, true);
            }
        }

        internal static bool TryValidateProfileForCatalog(
            TuneProfile profile,
            PartCatalog catalog,
            bool strictCatalog,
            bool requireChecksum,
            out string reason)
        {
            return TryValidateProfileForCatalog(
                profile,
                catalog,
                strictCatalog,
                requireChecksum,
                false,
                out reason);
        }

        private static bool TryValidateProfileForCatalog(
            TuneProfile profile,
            PartCatalog catalog,
            bool strictCatalog,
            bool requireChecksum,
            bool allowChecksumRepair,
            out string reason)
        {
            reason = null;

            if (profile == null)
            {
                reason = "profile is empty";
                return false;
            }

            if (!IsSafeProfileId(profile.profileId))
            {
                reason = "profile id is invalid";
                return false;
            }

            if (profile.schemaVersion <= 0 || profile.schemaVersion > AlpineConstants.SchemaVersion)
            {
                reason = $"unsupported schema {profile.schemaVersion}";
                return false;
            }

            if (strictCatalog && profile.schemaVersion != AlpineConstants.SchemaVersion)
            {
                reason = $"incompatible schema {profile.schemaVersion}";
                return false;
            }

            if (strictCatalog &&
                !string.Equals(profile.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"incompatible catalog {profile.catalogVersion ?? "(missing)"}";
                return false;
            }

            if (requireChecksum && string.IsNullOrWhiteSpace(profile.checksum))
            {
                reason = "missing checksum";
                return false;
            }

            if (!allowChecksumRepair &&
                !string.IsNullOrWhiteSpace(profile.checksum) &&
                !ChecksumMatches(profile))
            {
                reason = "checksum mismatch";
                return false;
            }

            if (!IsSafeIdentity(profile.targetSledKey) && !IsSafeIdentity(profile.targetVehicleId))
            {
                reason = "target sled identity is invalid";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(profile.donorSledKey) && !IsSafeIdentity(profile.donorSledKey))
            {
                reason = "donor sled identity is invalid";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(profile.donorVehicleId) && !IsSafeIdentity(profile.donorVehicleId))
            {
                reason = "donor vehicle identity is invalid";
                return false;
            }

            if (profile.name != null && profile.name.Length > AlpineConstants.MaxProfileNameLength)
            {
                reason = "profile name is too long";
                return false;
            }

            if (profile.author != null && profile.author.Length > AlpineConstants.MaxProfileNameLength)
            {
                reason = "author name is too long";
                return false;
            }

            if (profile.selectedParts == null)
            {
                if (strictCatalog)
                {
                    reason = "selected parts are missing";
                    return false;
                }

                profile.selectedParts = new List<PartSelection>();
            }

            if (profile.selectedParts.Count > PartCatalog.OrderedCategories.Length + 4)
            {
                reason = "selected part list is too large";
                return false;
            }

            var validCategories = new HashSet<string>(PartCatalog.OrderedCategories, StringComparer.OrdinalIgnoreCase);
            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in profile.selectedParts)
            {
                if (selection == null ||
                    !validCategories.Contains(selection.category) ||
                    string.IsNullOrWhiteSpace(selection.partId))
                {
                    if (strictCatalog)
                    {
                        reason = "selected part shape is invalid";
                        return false;
                    }

                    // Local schema-2 files remain repairable: normalization will
                    // drop malformed entries and reconstruct every category while
                    // a pre-repair snapshot retains the original document.
                    continue;
                }

                if (strictCatalog && !selectedCategories.Add(selection.category))
                {
                    reason = $"duplicate selected category {selection.category}";
                    return false;
                }

                TunePart selectedPart = catalog?.Find(selection.partId);
                if (selectedPart == null && strictCatalog)
                {
                    reason = $"unknown part {selection.partId}";
                    return false;
                }

                if (strictCatalog &&
                    selectedPart != null &&
                    !string.Equals(
                        selectedPart.category,
                        selection.category,
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"part {selection.partId} does not belong to {selection.category}";
                    return false;
                }
            }

            if (profile.fineTune == null)
                profile.fineTune = new FineTuneSettings();

            if (!IsFineTuneFinite(profile.fineTune))
            {
                reason = "fine tune values contain invalid numbers";
                return false;
            }

            if (strictCatalog && !FineTuneWithinBounds(profile.fineTune))
            {
                reason = "fine tune values are outside allowed range";
                return false;
            }

            if (!strictCatalog)
                AlpineTuneMath.ClampFineTune(profile.fineTune);

            if (profile.resolvedStats == null)
                profile.resolvedStats = new ResolvedStats();

            SanitizeResolvedStats(profile.resolvedStats);

            return true;
        }

        private static bool TryValidateDefaults(SledDefaults defaults, out string reason)
        {
            reason = null;

            if (defaults == null)
            {
                reason = "defaults are empty";
                return false;
            }

            if (!IsSafeIdentity(defaults.sledKey))
            {
                reason = "sled key is invalid";
                return false;
            }

            if (!IsFinite(defaults.horsePower) ||
                !IsFinite(defaults.powerFactor) ||
                !IsFinite(defaults.lugHeight) ||
                !IsFinite(defaults.friction) ||
                !IsFinite(defaults.weight) ||
                !IsFinite(defaults.skiStance) ||
                !IsFinite(defaults.skisXDistanceOffset) ||
                (defaults.hasMaxRpm && (!IsFinite(defaults.maxRpm) || defaults.maxRpm <= 0f)) ||
                (defaults.hasSnowmobileStats &&
                 (!IsFinite(defaults.statsPower) ||
                  !IsFinite(defaults.statsClimbing) ||
                  !IsFinite(defaults.statsAgility))))
            {
                reason = "baseline stats contain invalid numbers";
                return false;
            }

            if (defaults.centerOfMassOffset == null)
                defaults.centerOfMassOffset = new Vec3Data();

            if (defaults.driverCenterOfMassOffset == null)
                defaults.driverCenterOfMassOffset = new Vec3Data();

            if (defaults.controller == null)
                defaults.controller = new ControllerDefaults();

            if (defaults.nativePhysics == null)
                defaults.nativePhysics = new NativePhysicsDefaults();

            if (defaults.controller.stabilizerDamping == null)
                defaults.controller.stabilizerDamping = new Vec3Data();

            if (defaults.controller.trackSpeedDamping == null)
                defaults.controller.trackSpeedDamping = new Vec3Data();

            if (!IsVec3Finite(defaults.centerOfMassOffset) ||
                !IsVec3Finite(defaults.driverCenterOfMassOffset) ||
                !ControllerDefaultsFinite(defaults.controller) ||
                !NativePhysicsDefaultsFinite(defaults.nativePhysics))
            {
                reason = "baseline runtime values contain invalid numbers";
                return false;
            }

            return true;
        }

        private void PruneMissingActiveProfiles()
        {
            var stale = _activeProfileIdsBySled
                .Where(pair =>
                {
                    if (string.IsNullOrWhiteSpace(pair.Value) ||
                        !_profiles.TryGetValue(pair.Value, out var profile) ||
                        profile == null)
                    {
                        return true;
                    }

                    return !string.Equals(pair.Key, profile.targetVehicleId, StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(pair.Key, profile.targetSledKey, StringComparison.OrdinalIgnoreCase);
                })
                .Select(pair => pair.Key)
                .ToList();

            if (stale.Count == 0)
                return;

            foreach (string key in stale)
                _activeProfileIdsBySled.Remove(key);

            SaveActiveMap();
            MelonLogger.Msg($"Pruned {stale.Count} stale Alpine active profile reference(s).");
        }

        private bool WriteJsonAtomic<T>(string path, T value, string description)
        {
            if (!IsPathInsideConfigRoot(path))
            {
                MelonLogger.Warning($"Refused to write {description} outside AlpineTuning user data.");
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(value, _jsonSettings), Encoding.UTF8);

                if (File.Exists(path))
                {
                    string backupPath = path + ".bak";
                    try
                    {
                        File.Replace(tempPath, path, backupPath, true);
                    }
                    catch
                    {
                        File.Copy(path, backupPath, true);
                        File.Copy(tempPath, path, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save {description}: {StorageError(ex)}");
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                return false;
            }
        }

        private bool RestoreRecoveredPrimary<T>(string path, T value, string description)
        {
            if (!IsPathInsideConfigRoot(path))
            {
                MelonLogger.Warning($"Refused to restore {description} outside AlpineTuning user data.");
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
            try
            {
                string json = JsonConvert.SerializeObject(value, _jsonSettings);
                File.WriteAllText(tempPath, json, Encoding.UTF8);

                // Validate the exact bytes before replacing the damaged primary.
                // No backup destination is supplied: path.bak is the known-good
                // recovery source and must not be replaced by damaged primary data.
                T verified = JsonConvert.DeserializeObject<T>(File.ReadAllText(tempPath));
                if (ReferenceEquals(verified, null))
                    throw new JsonSerializationException("restored JSON did not deserialize");

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null, true);
                    }
                    catch
                    {
                        File.Copy(tempPath, path, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                if (!TryDeserializeFile(path, out T readBack, out _) || ReferenceEquals(readBack, null))
                    throw new IOException("restored primary did not pass readback");

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not restore {description}: {StorageError(ex)}");
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                return false;
            }
        }

        private static bool IsPathInsideConfigRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string root = Path.GetFullPath(ConfigRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeProfileId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > AlpineConstants.MaxProfileIdLength)
                return false;

            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            }

            return true;
        }

        private static bool IsSafeHistoryId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;

            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            }

            return true;
        }

        private static bool IsSafeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > AlpineConstants.MaxSledIdentityLength)
                return false;

            foreach (char c in value)
            {
                if (char.IsControl(c) || c == '/' || c == '\\')
                    return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsVec3Finite(Vec3Data value)
        {
            return value != null &&
                   IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool ControllerDefaultsFinite(ControllerDefaults value)
        {
            return value != null &&
                   IsFinite(value.throttleExponent) &&
                   IsFinite(value.rpmSensitivity) &&
                   IsFinite(value.rpmSensitivityDown) &&
                   IsFinite(value.clutchRpmMin) &&
                   IsFinite(value.clutchRpmMax) &&
                   IsFinite(value.minThrottleOnClutchEngagement) &&
                   IsVec3Finite(value.stabilizerDamping) &&
                   IsVec3Finite(value.trackSpeedDamping) &&
                   IsFinite(value.trackSpeedGyroMultiplier);
        }

        private static bool NativePhysicsDefaultsFinite(NativePhysicsDefaults value)
        {
            return value != null &&
                   IsFinite(value.powerEfficiency) &&
                   IsFinite(value.drivetrainMinSpeed) &&
                   IsFinite(value.drivetrainMaxSpeed1) &&
                   IsFinite(value.drivetrainMaxSpeed2) &&
                   IsFinite(value.trackMass) &&
                   IsFinite(value.brakeForce) &&
                   IsFinite(value.antiRollBar) &&
                   IsFinite(value.trackRigidityFront) &&
                   IsFinite(value.trackRigidityRear) &&
                   IsFinite(value.frontSpring) &&
                   IsFinite(value.frontDamper) &&
                   IsFinite(value.frontCompressionDamping) &&
                   IsFinite(value.frontReboundDamping) &&
                   IsFinite(value.rearSpring) &&
                   IsFinite(value.rearDamper) &&
                   IsFinite(value.rearCompressionDamping) &&
                   IsFinite(value.rearReboundDamping) &&
                   IsFinite(value.skisMaxAngle) &&
                   IsFinite(value.toeAngle) &&
                   IsFinite(value.leftCamberFactor) &&
                   IsFinite(value.rightCamberFactor) &&
                   IsFinite(value.skiGrip) &&
                   IsFinite(value.trackGrip);
        }

        private static bool IsFineTuneFinite(FineTuneSettings fine)
        {
            return fine != null &&
                   IsFinite(fine.powerTrimPercent) &&
                   IsFinite(fine.tractionTrimPercent) &&
                   IsFinite(fine.weightTrimPercent) &&
                   IsFinite(fine.clutchTrimPercent) &&
                   IsFinite(fine.centerOfMassYTrim) &&
                   IsFinite(fine.centerOfMassZTrim) &&
                   IsFinite(fine.skiStanceTrim);
        }

        private static bool FineTuneWithinBounds(FineTuneSettings fine)
        {
            return fine != null &&
                   fine.powerTrimPercent >= -10f && fine.powerTrimPercent <= 10f &&
                   fine.tractionTrimPercent >= -10f && fine.tractionTrimPercent <= 10f &&
                   fine.weightTrimPercent >= -8f && fine.weightTrimPercent <= 8f &&
                   fine.clutchTrimPercent >= -10f && fine.clutchTrimPercent <= 10f &&
                   fine.centerOfMassYTrim >= -0.08f && fine.centerOfMassYTrim <= 0.08f &&
                   fine.centerOfMassZTrim >= -0.12f && fine.centerOfMassZTrim <= 0.12f &&
                   fine.skiStanceTrim >= -0.08f && fine.skiStanceTrim <= 0.08f;
        }

        private static void SanitizeResolvedStats(ResolvedStats stats)
        {
            if (stats == null)
                return;

            stats.horsePower = FiniteOr(stats.horsePower, 0f);
            stats.powerFactor = FiniteOr(stats.powerFactor, 0f);
            stats.maxRpm = FiniteOr(stats.maxRpm, 0f);
            if (!stats.hasMaxRpm || stats.maxRpm <= 1000f)
            {
                stats.hasMaxRpm = false;
                stats.maxRpm = 0f;
            }
            else
            {
                stats.maxRpm = Mathf.Clamp(stats.maxRpm, 3000f, 14000f);
            }
            stats.lugHeight = FiniteOr(stats.lugHeight, 0f);
            stats.friction = FiniteOr(stats.friction, 0f);
            stats.weight = FiniteOr(stats.weight, 0f);
            stats.skiStance = FiniteOr(stats.skiStance, 0f);
            stats.skisXDistanceOffset = FiniteOr(stats.skisXDistanceOffset, 0f);

            if (stats.centerOfMassOffset == null)
                stats.centerOfMassOffset = new Vec3Data();

            if (stats.driverCenterOfMassOffset == null)
                stats.driverCenterOfMassOffset = new Vec3Data();

            stats.centerOfMassOffset.x = FiniteOr(stats.centerOfMassOffset.x, 0f);
            stats.centerOfMassOffset.y = FiniteOr(stats.centerOfMassOffset.y, 0f);
            stats.centerOfMassOffset.z = FiniteOr(stats.centerOfMassOffset.z, 0f);
            stats.driverCenterOfMassOffset.x = FiniteOr(stats.driverCenterOfMassOffset.x, 0f);
            stats.driverCenterOfMassOffset.y = FiniteOr(stats.driverCenterOfMassOffset.y, 0f);
            stats.driverCenterOfMassOffset.z = FiniteOr(stats.driverCenterOfMassOffset.z, 0f);
        }

        private static float FiniteOr(float value, float fallback)
        {
            return IsFinite(value) ? value : fallback;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim().Replace(' ', '_');
        }

        private static long NowUnix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private class LegacyTunePreset
        {
            public string SledKey { get; set; }
            public string EnginePartName { get; set; }
            public string TrackPartName { get; set; }
            public string HandlingPartName { get; set; }
            public string DonorSledKey { get; set; }
        }

        private sealed class ProfileContentFingerprint
        {
            public string targetIdentity;
            public string donorIdentity;
            public List<FingerprintPartSelection> selectedParts;
            public FineTuneSettings fineTune;
            public bool? headlightEnabled;
        }

        private sealed class FingerprintPartSelection
        {
            public string category;
            public string partId;
        }
    }
}
