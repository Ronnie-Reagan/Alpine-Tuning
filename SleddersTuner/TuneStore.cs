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

        public void Initialize()
        {
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(DefaultsDir);
            Directory.CreateDirectory(ProfilesDir);
            MaybeMigrateLegacyConfig();
            LoadDefaults();
            LoadProfiles();
            LoadActiveMap();
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
            return _profiles.Values
                .Where(p => string.Equals(p.targetSledKey, sledKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.updatedUnixTime)
                .ToList();
        }

        public TuneProfile GetActiveProfileForSled(string sledKey)
        {
            if (string.IsNullOrWhiteSpace(sledKey))
                return null;

            if (!_activeProfileIdsBySled.TryGetValue(sledKey, out var profileId))
                return null;

            return GetProfile(profileId);
        }

        public void SetActiveProfile(string sledKey, string profileId)
        {
            if (string.IsNullOrWhiteSpace(sledKey))
                return;

            if (string.IsNullOrWhiteSpace(profileId))
                _activeProfileIdsBySled.Remove(sledKey);
            else
                _activeProfileIdsBySled[sledKey] = profileId;

            SaveActiveMap();
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

        public void SaveProfile(TuneProfile profile, bool makeActive)
        {
            if (profile == null)
                return;

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
            profile.checksum = ComputeChecksum(profile);

            _profiles[profile.profileId] = Clone(profile);
            string path = Path.Combine(ProfilesDir, SafeFileName(profile.profileId) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(profile, _jsonSettings));

            if (makeActive)
                SetActiveProfile(profile.targetSledKey, profile.profileId);
        }

        public void DeleteProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return;

            _profiles.Remove(profileId);
            string path = Path.Combine(ProfilesDir, SafeFileName(profileId) + ".json");
            if (File.Exists(path))
                File.Delete(path);

            var activeKeys = _activeProfileIdsBySled
                .Where(kvp => kvp.Value == profileId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in activeKeys)
                _activeProfileIdsBySled.Remove(key);

            SaveActiveMap();
        }

        public void ImportSharedProfile(TuneProfile profile)
        {
            if (profile == null)
                return;

            if (string.IsNullOrWhiteSpace(profile.profileId))
                profile.profileId = Guid.NewGuid().ToString("N");

            profile.name = string.IsNullOrWhiteSpace(profile.name)
                ? "Shared Tune"
                : profile.name;

            SaveProfile(profile, false);
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
                    if (defaults != null && !string.IsNullOrWhiteSpace(defaults.sledKey))
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
                File.WriteAllText(path, JsonConvert.SerializeObject(defaults, _jsonSettings));
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
                    if (profile == null || string.IsNullOrWhiteSpace(profile.profileId))
                        continue;

                    _catalog.EnsureProfileSelections(profile);
                    _profiles[profile.profileId] = profile;
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
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        _activeProfileIdsBySled[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not load active profile map: {ex.Message}");
            }
        }

        private void SaveActiveMap()
        {
            try
            {
                File.WriteAllText(ActiveMapPath, JsonConvert.SerializeObject(_activeProfileIdsBySled, _jsonSettings));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not save active profile map: {ex.Message}");
            }
        }

        private void MaybeMigrateLegacyConfig()
        {
            try
            {
                if (!Directory.Exists(LegacyConfigRoot))
                    return;

                bool hasNewContent = Directory.EnumerateFileSystemEntries(ConfigRoot).Any();
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
                file.CopyTo(Path.Combine(destDir, file.Name), true);

            if (!copySubDirs)
                return;

            foreach (DirectoryInfo subdir in dir.GetDirectories())
                DirectoryCopy(subdir.FullName, Path.Combine(destDir, subdir.Name), true);
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
