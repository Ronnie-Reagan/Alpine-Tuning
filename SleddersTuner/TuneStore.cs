using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AlpineTuning
{
    internal class TuneStore
    {
        private readonly PartCatalog _catalog;
        private readonly Dictionary<string, SledDefaults> _defaults = new Dictionary<string, SledDefaults>();
        private readonly Dictionary<string, TuneProfile> _profiles = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, string> _activeProfileIdsBySled = new Dictionary<string, string>();

        private static readonly string ConfigRoot =
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning");

        private static readonly string LegacyConfigRoot =
            Path.Combine(MelonEnvironment.UserDataDirectory, "SleddersTuner");

        private static string DefaultsDir => Path.Combine(ConfigRoot, "Defaults");
        private static string LegacyPresetsDir => Path.Combine(ConfigRoot, "Presets");
        private static string ProfilesDir => Path.Combine(ConfigRoot, "Profiles");
        private static string ActiveMapPath => Path.Combine(ConfigRoot, "active-profiles.json");

        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public TuneStore(PartCatalog catalog)
        {
            _catalog = catalog;
        }

        public IReadOnlyDictionary<string, SledDefaults> Defaults => _defaults;
        public IReadOnlyDictionary<string, TuneProfile> Profiles => _profiles;
        public int ActiveProfileMapCount => _activeProfileIdsBySled.Count;
        public string DiagnosticsSummary =>
            $"Store: defaults={_defaults.Count}, profiles={_profiles.Count}, activeMaps={_activeProfileIdsBySled.Count}, root={ConfigRoot}";

        public void Initialize()
        {
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(DefaultsDir);
            Directory.CreateDirectory(ProfilesDir);
            MaybeMigrateLegacyConfig();
            LoadDefaults();
            LoadProfiles();
            LoadActiveMap();
            PruneMissingActiveProfiles();
        }

        public SledDefaults GetDefaults(string sledKey)
        {
            _defaults.TryGetValue(sledKey, out var defaults);
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

            _defaults[defaults.sledKey] = defaults;
            SaveDefaults(defaults);
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
                .Where(p =>
                    string.Equals(p.targetSledKey, sledKey, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(vehicleId) &&
                     string.Equals(p.targetVehicleId, vehicleId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.updatedUnixTime)
                .ToList();
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
            if (!string.IsNullOrWhiteSpace(sledKey))
                _activeProfileIdsBySled.TryGetValue(sledKey, out profileId);

            if (string.IsNullOrWhiteSpace(profileId))
            {
                if (string.IsNullOrWhiteSpace(vehicleId) ||
                    !_activeProfileIdsBySled.TryGetValue(vehicleId, out profileId))
                {
                    return null;
                }
            }

            var profile = GetProfile(profileId);
            if (profile == null)
                return null;

            if (!string.IsNullOrWhiteSpace(vehicleId) &&
                string.Equals(profile.targetVehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }

            return string.Equals(profile.targetSledKey, sledKey, StringComparison.OrdinalIgnoreCase)
                ? profile
                : null;
        }

        public bool SetActiveProfile(string sledKey, string profileId)
        {
            if (!IsSafeIdentity(sledKey))
                return false;

            if (string.IsNullOrWhiteSpace(profileId))
                _activeProfileIdsBySled.Remove(sledKey);
            else if (!IsSafeProfileId(profileId))
                return false;
            else
                _activeProfileIdsBySled[sledKey] = profileId;

            return SaveActiveMap();
        }

        public TuneProfile CreateWorkingProfile(VehicleScriptableObject sled, string author)
        {
            string sledKey = AlpineTuningMod.GetSledKey(sled);
            var active = GetActiveProfileForSled(sledKey);
            if (active != null)
            {
                var clone = Clone(active);
                _catalog.EnsureProfileSelections(clone);
                return clone;
            }

            return _catalog.CreateDefaultProfile(sled, author);
        }

        public bool SaveProfile(TuneProfile profile, bool makeActive)
        {
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
            profile.checksum = null;
            if (!TryValidateProfileForCatalog(profile, _catalog, false, false, out var reason))
            {
                MelonLogger.Warning($"Could not save profile '{profile.name ?? profile.profileId}': {reason}");
                return false;
            }

            profile.checksum = ComputeChecksum(profile);

            string path = Path.Combine(ProfilesDir, SafeFileName(profile.profileId) + ".json");
            if (!WriteJsonAtomic(path, profile, $"profile {profile.profileId}"))
                return false;

            _profiles[profile.profileId] = Clone(profile);

            if (makeActive)
                return SetActiveProfile(profile.targetSledKey, profile.profileId);

            return true;
        }

        public void DeleteProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return;

            _profiles.Remove(profileId);
            string path = Path.Combine(ProfilesDir, SafeFileName(profileId) + ".json");
            if (IsPathInsideConfigRoot(path) && File.Exists(path))
                File.Delete(path);

            var activeKeys = _activeProfileIdsBySled
                .Where(kvp => kvp.Value == profileId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in activeKeys)
                _activeProfileIdsBySled.Remove(key);

            SaveActiveMap();
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
                ? "Shared Tune"
                : imported.name;

            return SaveProfile(imported, false) ? imported : null;
        }

        public void MigrateLegacyPresets(IEnumerable<VehicleScriptableObject> sleds, string author)
        {
            if (!Directory.Exists(LegacyPresetsDir))
                return;

            foreach (string file in Directory.GetFiles(LegacyPresetsDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var legacy = JsonConvert.DeserializeObject<LegacyTunePreset>(json);
                    if (legacy == null || string.IsNullOrWhiteSpace(legacy.SledKey))
                        continue;

                    bool alreadyMigrated = _profiles.Values.Any(p =>
                        p.targetSledKey == legacy.SledKey &&
                        p.name != null &&
                        p.name.Contains("Migrated"));

                    if (alreadyMigrated)
                        continue;

                    var sled = sleds.FirstOrDefault(s => AlpineTuningMod.GetSledKey(s) == legacy.SledKey);
                    if (sled == null)
                        continue;

                    var profile = _catalog.CreateLegacyProfile(
                        sled,
                        author,
                        legacy.EnginePartName,
                        legacy.TrackPartName,
                        legacy.HandlingPartName,
                        legacy.DonorSledKey);

                    SaveProfile(profile, true);
                    MelonLogger.Msg($"Migrated legacy Alpine preset for {legacy.SledKey}.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Legacy preset migration skipped for {file}: {ex.Message}");
                }
            }
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

            foreach (string file in Directory.GetFiles(DefaultsDir, "*.json"))
            {
                try
                {
                    var defaults = JsonConvert.DeserializeObject<SledDefaults>(File.ReadAllText(file));
                    if (!TryValidateDefaults(defaults, out var reason))
                    {
                        MelonLogger.Warning($"Skipped invalid defaults {file}: {reason}");
                        continue;
                    }

                    _defaults[defaults.sledKey] = defaults;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not load defaults {file}: {ex.Message}");
                }
            }
        }

        private void SaveDefaults(SledDefaults defaults)
        {
            try
            {
                Directory.CreateDirectory(DefaultsDir);
                string path = Path.Combine(DefaultsDir, SafeFileName(defaults.sledKey) + ".json");
                WriteJsonAtomic(path, defaults, $"defaults {defaults.sledKey}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save defaults for {defaults.sledKey}: {ex.Message}");
            }
        }

        private void LoadProfiles()
        {
            _profiles.Clear();
            if (!Directory.Exists(ProfilesDir))
                return;

            foreach (string file in Directory.GetFiles(ProfilesDir, "*.json"))
            {
                try
                {
                    var profile = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(file));
                    if (!TryValidateProfileForCatalog(profile, _catalog, false, false, out var reason))
                    {
                        MelonLogger.Warning($"Skipped invalid profile {file}: {reason}");
                        continue;
                    }

                    _catalog.EnsureProfileSelections(profile);
                    string normalizedChecksum = ComputeChecksum(profile);
                    bool repaired = !string.Equals(profile.checksum, normalizedChecksum, StringComparison.OrdinalIgnoreCase);
                    if (repaired)
                    {
                        profile.checksum = normalizedChecksum;
                    }

                    _profiles[profile.profileId] = profile;

                    if (repaired)
                        WriteJsonAtomic(file, profile, $"profile {profile.profileId} checksum repair");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not load profile {file}: {ex.Message}");
                }
            }
        }

        private void LoadActiveMap()
        {
            _activeProfileIdsBySled.Clear();
            if (!File.Exists(ActiveMapPath))
                return;

            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(ActiveMapPath));
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
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not load active profile map: {ex.Message}");
            }
        }

        private bool SaveActiveMap()
        {
            try
            {
                return WriteJsonAtomic(ActiveMapPath, _activeProfileIdsBySled, "active profile map");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save active profile map: {ex.Message}");
                return false;
            }
        }

        private void MaybeMigrateLegacyConfig()
        {
            try
            {
                if (!Directory.Exists(LegacyConfigRoot))
                    return;

                bool hasNewContent = HasMeaningfulConfigContent(ConfigRoot);
                if (hasNewContent)
                    return;

                DirectoryCopy(LegacyConfigRoot, ConfigRoot, true);
                MelonLogger.Msg("Pulled old SleddersTuner files into AlpineTuning.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Legacy config migration skipped: {ex.Message}");
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

            if (!string.IsNullOrWhiteSpace(profile.checksum) && !ChecksumMatches(profile))
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
            foreach (var selection in profile.selectedParts)
            {
                if (selection == null ||
                    !validCategories.Contains(selection.category) ||
                    string.IsNullOrWhiteSpace(selection.partId))
                {
                    reason = "selected part shape is invalid";
                    return false;
                }

                if (catalog != null && catalog.Find(selection.partId) == null && strictCatalog)
                {
                    reason = $"unknown part {selection.partId}";
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
                !IsFinite(defaults.weight))
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

            return true;
        }

        private void PruneMissingActiveProfiles()
        {
            var stale = _activeProfileIdsBySled
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value) || !_profiles.ContainsKey(pair.Value))
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
                MelonLogger.Warning($"Could not save {description}: {ex.Message}");
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

        private static bool HasMeaningfulConfigContent(string root)
        {
            if (!Directory.Exists(root))
                return false;

            foreach (string entry in Directory.EnumerateFileSystemEntries(root))
            {
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry) &&
                    (string.Equals(name, "Defaults", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(name, "Profiles", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(name, "Presets", StringComparison.OrdinalIgnoreCase)) &&
                    !Directory.EnumerateFileSystemEntries(entry).Any())
                {
                    continue;
                }

                return true;
            }

            return false;
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
            stats.lugHeight = FiniteOr(stats.lugHeight, 0f);
            stats.friction = FiniteOr(stats.friction, 0f);
            stats.weight = FiniteOr(stats.weight, 0f);
            stats.skiStance = FiniteOr(stats.skiStance, 0f);
            stats.skisXDistanceOffset = FiniteOr(stats.skisXDistanceOffset, 0f);
            stats.boostTargetPsi = Mathf.Clamp(FiniteOr(stats.boostTargetPsi, 0f), 0f, 60f);
            stats.boostLimitPsi = Mathf.Clamp(FiniteOr(stats.boostLimitPsi, 0f), 0f, 60f);
            stats.estimatedBoostPsi = Mathf.Clamp(FiniteOr(stats.estimatedBoostPsi, 0f), 0f, 60f);
            stats.altitudeCompensationPercent = Mathf.Clamp(FiniteOr(stats.altitudeCompensationPercent, 0f), 0f, 100f);
            stats.estimatedManifoldPressureKpa = Mathf.Clamp(FiniteOr(stats.estimatedManifoldPressureKpa, 0f), 0f, 600f);

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
    }
}
