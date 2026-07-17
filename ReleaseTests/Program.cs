using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AlpineTuning.ReleaseTests
{
    internal static class Program
    {
        private const string PublicVersion = "2026.07.17";
        private const string AssemblyVersion = "2026.7.17.0";
        private const string CatalogVersion = "2026.07.v2";
        private const int ExpectedGarageIconCount = 162;

        private static readonly string[] RequiredGarageIconKeys =
        {
            "action.continue", "action.discard", "action.save", "action.settings",
            "action.setups", "action.unavailable",
            "root.engine", "root.drivetrain", "root.suspension", "root.track",
            "root.steering", "root.lighting",
            "settings.display", "settings.hotkey", "settings.metric", "settings.imperial",
            "settings.enabled", "settings.disabled", "settings.keyboard",
            "settings.controller", "settings.clear", "settings.confirm-clear",
            "type.engine-core", "type.pistons", "type.crankshaft", "type.intake-exhaust",
            "type.turbo", "type.clutch-calibration", "type.clutch-weights", "type.gearing",
            "type.brake-calibration", "type.suspension", "type.chassis",
            "type.limiter-strap", "type.rear-shock", "type.rear-spring", "type.track",
            "type.skis", "type.steering-geometry", "type.headlight-color",
            "type.headlight-output", "type.headlight-beam", "type.headlight-aim",
            "type.engine-swap",
            "engine.stock-native", "engine.unavailable", "engine.generic-na",
            "engine.generic-turbo"
        };

        private static readonly int[] RequiredBrandIconSizes =
        {
            16, 24, 32, 48, 64, 128, 256
        };

        private static readonly string[] RequiredPublicFiles =
        {
            ".gitignore",
            "README.md",
            "license.txt",
            "build-release.bat",
            "SleddersTuner/SleddersTuner.csproj",
            "SleddersTuner/AlpineNativeUi.cs",
            "SleddersTuner/AlpinePeerSharing.cs",
            "SleddersTuner/AlpineRemoteReplication.cs",
            "SleddersTuner/AlpineSleddersTransport.cs",
            "SleddersTuner/AlpineTuneMath.cs",
            "SleddersTuner/GarageIconResources.cs",
            "SleddersTuner/ModMain.cs",
            "SleddersTuner/PartCatalog.cs",
            "SleddersTuner/Properties/AssemblyInfo.cs",
            "SleddersTuner/SleddersGameBindings.cs",
            "SleddersTuner/SledIdentity.cs",
            "SleddersTuner/TrackSpecResolver.cs",
            "SleddersTuner/TuneModels.cs",
            "SleddersTuner/TuneStore.cs",
            "SleddersTuner/UnitConversion.cs",
            "SleddersTuner/Assets/Brand/alpine-tuning.ico",
            "SleddersTuner/Assets/Brand/alpine-tuning-badge.png",
            "ReleaseTests/ReleaseTests.csproj",
            "ReleaseTests/Program.cs",
            "ReleaseTests/TuneStoreRegression.cs",
            "ReleaseTests/Fixtures/numerical-cases.json",
            "ReleaseTests/Fixtures/tune-stock.json",
            "ReleaseTests/Fixtures/tune-modified.json",
            "ReleaseTests/Fixtures/tune-legacy.json"
        };

        private static int _passed;
        private static int _failed;
        private static string _repoRoot;
        private static string _releaseAssembly;
        private static string _gameAssembly;
        private static string _inventoryFile;
        private static string _scanRoot;
        private static string _tuneTestRoot;

        private sealed class ReleaseTestException : Exception
        {
            public ReleaseTestException(string code) : base(code)
            {
            }
        }

        private sealed class PrivacyMarker
        {
            public string Value;
            public bool WholeWord;
            public bool ContextOnly;
        }

        private static int Main(string[] args)
        {
            if (!TryReadArguments(args))
            {
                Console.Error.WriteLine("Release tests require repo, assembly, game-assembly, inventory, scan-root, and tune-test-root arguments.");
                return 2;
            }

            ConfigureAssemblyResolution();

            Run("public inventory", TestPublicInventory);
            Run("version contracts", TestVersionContracts);
            Run("documentation contracts", TestDocumentationContracts);
            Run("source release contracts", TestSourceReleaseContracts);
            Run("unit and stance conversions", TestConversions);
            Run("tuning physics regression", TestTuningPhysics);
            Run("native field multiplier regression", TestNativeFieldScaling);
            Run("internal subsystem and identity contracts", TestInternalContracts);
            Run("native drive numerical model", TestNativeDriveModel);
            Run("estimated curve numerical model", TestEstimatedCurveMath);
            Run("TuneStore fixtures and recovery", () => TuneStoreRegression.Run(_repoRoot, _tuneTestRoot));
            Run("native assembly contracts", TestNativeAssemblyContracts);
            Run("release assembly metadata", TestReleaseAssemblyMetadata);
            Run("embedded garage resources", TestEmbeddedGarageResources);
            Run("asset dimensions and alpha", TestAssets);
            Run("privacy scan", TestPrivacy);

            Console.WriteLine("Release regression tests: {0} passed, {1} failed.", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static bool TryReadArguments(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < args.Length; i += 2)
                values[args[i]] = args[i + 1];

            if (!values.TryGetValue("--repo", out _repoRoot) ||
                !values.TryGetValue("--assembly", out _releaseAssembly) ||
                !values.TryGetValue("--game-assembly", out _gameAssembly) ||
                !values.TryGetValue("--inventory", out _inventoryFile) ||
                !values.TryGetValue("--scan-root", out _scanRoot) ||
                !values.TryGetValue("--tune-test-root", out _tuneTestRoot))
                return false;

            _repoRoot = Path.GetFullPath(_repoRoot);
            _releaseAssembly = Path.GetFullPath(_releaseAssembly);
            _gameAssembly = Path.GetFullPath(_gameAssembly);
            _inventoryFile = Path.GetFullPath(_inventoryFile);
            _scanRoot = Path.GetFullPath(_scanRoot);
            _tuneTestRoot = Path.GetFullPath(_tuneTestRoot);
            return Directory.Exists(_repoRoot) &&
                   File.Exists(_releaseAssembly) &&
                   File.Exists(_gameAssembly) &&
                   File.Exists(_inventoryFile) &&
                   Directory.Exists(_scanRoot);
        }

        private static void ConfigureAssemblyResolution()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var requested = new AssemblyName(eventArgs.Name);
                Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.GetName().Name,
                        requested.Name,
                        StringComparison.OrdinalIgnoreCase));
                if (loaded != null)
                    return loaded;

                if (string.Equals(requested.Name, "Alpine Tuning", StringComparison.OrdinalIgnoreCase))
                    return Assembly.LoadFrom(_releaseAssembly);

                string managed = Path.GetDirectoryName(_gameAssembly);
                string sleddersData = Directory.GetParent(managed)?.FullName;
                string game = sleddersData != null ? Directory.GetParent(sleddersData)?.FullName : null;
                string[] directories =
                {
                    managed,
                    game != null ? Path.Combine(game, "MelonLoader", "net35") : null
                };
                foreach (string directory in directories.Where(Directory.Exists))
                {
                    string candidate = Path.Combine(directory, requested.Name + ".dll");
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }
                return null;
            };
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS: {0}", name);
            }
            catch (ReleaseTestException ex)
            {
                _failed++;
                Console.Error.WriteLine("FAIL: {0} [{1}]", name, ex.Message);
            }
            catch (Exception ex)
            {
                _failed++;
                MethodBase site = ex.TargetSite;
                string location = site != null
                    ? (site.DeclaringType != null ? site.DeclaringType.FullName + "." : string.Empty) + site.Name
                    : "unknown-method";
                Console.Error.WriteLine("FAIL: {0} [{1} at {2}]", name, ex.GetType().Name, location);
            }
        }

        internal static void Require(bool condition, string code)
        {
            if (!condition)
                throw new ReleaseTestException(code);
        }

        private static string RepoFile(string relativePath)
        {
            return Path.Combine(_repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ReadRepoText(string relativePath)
        {
            string path = RepoFile(relativePath);
            Require(File.Exists(path), "missing-public-file");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void TestPublicInventory()
        {
            string[] entries = File.ReadAllLines(_inventoryFile)
                .Select(NormalizeRelativePath)
                .Where(path => path.Length > 0 && File.Exists(RepoFile(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Require(entries.Length >= 15, "inventory-too-small");
            Require(entries.All(IsAllowedPublicPath), "unexpected-public-file");

            foreach (string path in RequiredPublicFiles)
                Require(entries.Contains(path, StringComparer.OrdinalIgnoreCase), "required-file-not-published");

            string ignore = ReadRepoText(".gitignore");
            string firstRule = ignore.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !line.StartsWith("#", StringComparison.Ordinal));
            Require(firstRule == "*", "gitignore-not-allowlist");
            Require(ignore.Contains("!/SleddersTuner/SleddersTuner.csproj") &&
                    ignore.Contains("!/SleddersTuner/AlpineNativeUi.cs") &&
                    ignore.Contains("!/SleddersTuner/Assets/GarageIcons/*.png") &&
                    ignore.Contains("!/ReleaseTests/ReleaseTests.csproj") &&
                    ignore.Contains("!/ReleaseTests/Program.cs") &&
                    ignore.IndexOf("!/SleddersTuner.slnx", StringComparison.OrdinalIgnoreCase) < 0 &&
                    ignore.IndexOf("!/SleddersTuner/*.cs", StringComparison.OrdinalIgnoreCase) < 0 &&
                    ignore.IndexOf("!/ReleaseTests/*.cs", StringComparison.OrdinalIgnoreCase) < 0,
                    "gitignore-compilation-input-contract");
        }

        private static string NormalizeRelativePath(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        }

        private static bool IsAllowedPublicPath(string path)
        {
            path = NormalizeRelativePath(path);
            if (path.IndexOf("../", StringComparison.Ordinal) >= 0)
                return false;

            if (RequiredPublicFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                return true;

            const string iconPrefix = "SleddersTuner/Assets/GarageIcons/";
            if (!path.StartsWith(iconPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string iconName = path.Substring(iconPrefix.Length);
            return iconName.Length > 4 &&
                   iconName.IndexOf('/') < 0 &&
                   iconName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private static void TestVersionContracts()
        {
            string models = ReadRepoText("SleddersTuner/TuneModels.cs");
            string assemblyInfo = ReadRepoText("SleddersTuner/Properties/AssemblyInfo.cs");
            string build = ReadRepoText("build-release.bat");

            Require(Regex.IsMatch(models, "SchemaVersion\\s*=\\s*2\\s*;"), "schema-version");
            Require(models.Contains("ModVersion = \"" + PublicVersion + "\""), "mod-version");
            Require(models.Contains("CatalogVersion = \"" + CatalogVersion + "\""), "catalog-version");
            Require(assemblyInfo.Contains("AssemblyVersion(\"" + AssemblyVersion + "\")"), "assembly-version-source");
            Require(assemblyInfo.Contains("AssemblyFileVersion(\"" + AssemblyVersion + "\")"), "file-version-source");
            Require(assemblyInfo.Contains("AssemblyInformationalVersion(\"" + PublicVersion + "\")"), "informational-version-source");
            Require(build.Contains("PUBLIC_VERSION=" + PublicVersion) &&
                    build.Contains("ASSEMBLY_VERSION=" + AssemblyVersion), "build-script-version");
        }

        private static void TestDocumentationContracts()
        {
            string readme = ReadRepoText("README.md");
            Require(readme.Contains("Current public version: **" + PublicVersion + "**"), "readme-version");
            Require(readme.IndexOf("DYNO", StringComparison.OrdinalIgnoreCase) >= 0, "readme-dyno");
            Require(readme.IndexOf("GAME MODEL", StringComparison.OrdinalIgnoreCase) >= 0, "readme-game-model");
            Require(readme.IndexOf("ESTIMATED", StringComparison.OrdinalIgnoreCase) >= 0, "readme-estimate-disclosure");
            Require(readme.IndexOf("Brake Calibration", StringComparison.OrdinalIgnoreCase) >= 0, "readme-brake");
            Require(readme.IndexOf("Steering Geometry", StringComparison.OrdinalIgnoreCase) >= 0, "readme-steering-geometry");
            Require(readme.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) < 0, "readme-paused-multiplayer");
            Require(!Regex.IsMatch(readme, @"(?i)(press|shortcut|key)\s+(the\s+)?`?D`?\b|\[D\]"), "readme-d-shortcut");
        }

        private static void TestSourceReleaseContracts()
        {
            string ui = ReadRepoText("SleddersTuner/AlpineNativeUi.cs");
            string bindings = ReadRepoText("SleddersTuner/SleddersGameBindings.cs");
            string models = ReadRepoText("SleddersTuner/TuneModels.cs");
            string store = ReadRepoText("SleddersTuner/TuneStore.cs");
            string assemblyInfo = ReadRepoText("SleddersTuner/Properties/AssemblyInfo.cs");
            string build = ReadRepoText("build-release.bat");
            string project = ReadRepoText("SleddersTuner/SleddersTuner.csproj");
            string main = ReadRepoText("SleddersTuner/ModMain.cs");
            string math = ReadRepoText("SleddersTuner/AlpineTuneMath.cs");

            Require(!Regex.IsMatch(ui, @"KeyCode\s*\.\s*D\b|DYNO\s*\[D\]", RegexOptions.IgnoreCase), "source-d-shortcut");
            Require(ui.IndexOf("AttachInlineFallback", StringComparison.Ordinal) < 0 &&
                    ui.IndexOf("CreateTuningSurface", StringComparison.Ordinal) < 0 &&
                    ui.IndexOf("PauseInline", StringComparison.Ordinal) < 0 &&
                    ui.IndexOf("AttachToPause", StringComparison.Ordinal) < 0 &&
                    main.IndexOf("PatchPauseOpen", StringComparison.Ordinal) < 0 &&
                    main.IndexOf("PatchPauseMenuClose", StringComparison.Ordinal) < 0,
                "inline-fallback");
            Require(ui.IndexOf("Drive Response", StringComparison.OrdinalIgnoreCase) < 0, "fabricated-drive-response");
            Require(ui.IndexOf("Confirm Load", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("loadWouldDiscardDraft", StringComparison.Ordinal) >= 0, "dirty-load-confirmation");
            Require(bindings.IndexOf("LogNetClientSteamIdScan", StringComparison.Ordinal) < 0, "steam-scanner-binding");
            Require(models.IndexOf("diagnosticSteamIdScanEnabled", StringComparison.Ordinal) < 0, "steam-scanner-setting");
            Require(models.IndexOf("boostTargetPsi", StringComparison.Ordinal) < 0 &&
                    models.IndexOf("estimatedManifoldPressure", StringComparison.Ordinal) < 0 &&
                    models.IndexOf("EngineSimulationInput", StringComparison.Ordinal) < 0 &&
                    math.IndexOf("ComputePressureRatio", StringComparison.Ordinal) < 0,
                "fabricated-environment-pressure-surface");
            Require(store.IndexOf("UseTestStorageRoot", StringComparison.Ordinal) >= 0, "test-storage-hook");
            Require(assemblyInfo.IndexOf("InternalsVisibleTo(\"AlpineTuning.ReleaseTests\")", StringComparison.Ordinal) >= 0, "test-friend-assembly");

            string combined = ui + "\n" + main + "\n" + math;
            Require(combined.IndexOf("GAME MODEL", StringComparison.OrdinalIgnoreCase) >= 0, "game-model-label");
            Require(combined.IndexOf("ESTIMATED ENGINE", StringComparison.OrdinalIgnoreCase) >= 0, "estimated-engine-label");
            Require(combined.IndexOf("782.7273", StringComparison.Ordinal) >= 0, "native-power-constant");
            Require(combined.IndexOf("9549.2966", StringComparison.Ordinal) >= 0, "metric-torque-constant");
            Require(combined.IndexOf("5252.113", StringComparison.Ordinal) >= 0, "imperial-torque-constant");
            Require(ui.IndexOf("Estimated curve unavailable: engine family is unknown.", StringComparison.Ordinal) >= 0,
                "unknown-engine-estimate-suppression");
            Require(Regex.IsMatch(ui,
                    @"public\s+void\s+Close\(\)\s*\{\s*CancelHeadlightCaptureIfActive\(\);",
                    RegexOptions.CultureInvariant) &&
                    Regex.Matches(ui, @"CancelHeadlightCaptureIfActive\(").Count >= 5,
                "binding-capture-close-lifecycle");
            Require(ui.IndexOf("AlpineRoot-Setups", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("tertiaryLabel = \"Setups\"", StringComparison.Ordinal) < 0,
                "setups-root-tile-not-context-action");
            Require(Regex.IsMatch(ui,
                    @"captured\.name\s*\?\?\s*\"\(unnamed setup\)\"[\s\S]{0,220}isPreviewSelected,",
                    RegexOptions.CultureInvariant),
                "setup-preview-checkmark");
            Require(Regex.IsMatch(ui,
                    @"startFraction\s*=\s*Mathf\.Min\(clutchStart\s*/\s*redline,\s*peakFraction\)",
                    RegexOptions.CultureInvariant),
                "high-clutch-peak-inclusion-policy");
            Require(main.IndexOf("PreviewProfilesWithSharedEnvironment", StringComparison.Ordinal) >= 0,
                "shared-environment-comparison");
            Require(Regex.IsMatch(main,
                    @"RestoreCapturedNativePhysicsDefaults\(\);[\s\S]{0,1800}TryReCreateSnowmobile\(",
                    RegexOptions.CultureInvariant),
                "restore-before-native-rebuild");
            Require(bindings.IndexOf("GetHardSurfaceContactBases", StringComparison.Ordinal) >= 0 &&
                    bindings.IndexOf("GetFieldValue<object>(wrapper, \"contactBase\")", StringComparison.Ordinal) >= 0,
                "nested-contact-grip-capture");
            Require(main.IndexOf("SafeRatio(computation.stats.horsePower, defaults.horsePower)", StringComparison.Ordinal) >= 0,
                "recipient-factory-power-bar");

            Require(build.IndexOf("%ProgramData%", StringComparison.OrdinalIgnoreCase) >= 0, "neutral-stage");
            Require(build.IndexOf(".alpine-stage-owner", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    build.IndexOf("STAGE_OWNED", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    build.IndexOf("[IO.Directory]::Move($temporary,$stage)", StringComparison.Ordinal) >= 0,
                "owned-stage-reservation");
            Require(build.IndexOf("diff --cached --check", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    build.IndexOf("untracked-public-files.txt", StringComparison.OrdinalIgnoreCase) >= 0,
                "complete-whitespace-gate");
            Require(build.IndexOf("SHA256", StringComparison.OrdinalIgnoreCase) >= 0, "hash-check");
            Require(build.IndexOf(":transactional_deploy", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    build.IndexOf("Rollback hash mismatch", StringComparison.OrdinalIgnoreCase) >= 0,
                "transactional-deployment");
            Require(build.IndexOf(":delete_file_checked", StringComparison.OrdinalIgnoreCase) >= 0,
                "stale-symbol-cleanup");
            Require(build.IndexOf("ReleaseTests", StringComparison.OrdinalIgnoreCase) >= 0, "tests-not-wired");
            Require(build.IndexOf(".binlog", StringComparison.OrdinalIgnoreCase) >= 0, "binlog-gate");
            Require(project.IndexOf("<DebugSymbols>false</DebugSymbols>", StringComparison.OrdinalIgnoreCase) >= 0, "release-symbol-setting");
            Require(project.IndexOf("DeployToSledders", StringComparison.OrdinalIgnoreCase) < 0, "unchecked-project-deploy-target");
        }

        private static void TestConversions()
        {
            string fixture = ReadRepoText("ReleaseTests/Fixtures/numerical-cases.json");
            float stanceMm = FixtureFloat(fixture, "factoryStanceMillimeters");
            float stanceInches = FixtureFloat(fixture, "factoryStanceInches");
            float trimMeters = FixtureFloat(fixture, "stanceTrimMeters");
            float trimMm = FixtureFloat(fixture, "stanceTrimMillimeters");

            Require(Math.Abs(UnitConversion.MillimetersToInches(stanceMm) - stanceInches) < 0.02f, "stance-inch-conversion");
            Require(Math.Abs(trimMeters * 1000f - trimMm) < 0.001f, "positive-stance-trim");
            Require(Math.Abs(-trimMeters * 1000f + trimMm) < 0.001f, "negative-stance-trim");
            Require(Math.Abs(UnitConversion.InchesToMillimeters(stanceInches) - 939.8f) < 0.2f, "stance-mm-roundtrip");
        }

        private static void TestTuningPhysics()
        {
            SledDefaults defaults = SyntheticDefaults();
            var effect = new PartEffect();

            ResolvedStats positive = AlpineTuneMath.ComputeStats(
                defaults,
                defaults,
                effect,
                new FineTuneSettings { skiStanceTrim = 0.05f });
            ResolvedStats negative = AlpineTuneMath.ComputeStats(
                defaults,
                defaults,
                effect,
                new FineTuneSettings { skiStanceTrim = -0.05f });

            Require(Approximately(positive.skiStance, 989.8d, 0.001d), "stance-positive-mm-application");
            Require(Approximately(negative.skiStance, 889.8d, 0.001d), "stance-negative-mm-application");
            Require(Approximately(positive.skisXDistanceOffset, defaults.skisXDistanceOffset + 0.05d, 0.00001d),
                "ski-offset-remains-metres");
            Require(AlpineTuningMod.NativeSpawnValuesDiffer(defaults, positive), "stance-only-rebuild");

            var bars = new SnowmobileStats();
            var donorComputation = new TuneComputation
            {
                baseDefaults = defaults,
                engineDefaults = new SledDefaults { horsePower = 200f },
                stats = AlpineTuneMath.ComputeStats(defaults, defaults, effect, new FineTuneSettings())
            };
            donorComputation.stats.horsePower = 200f;
            AlpineTuningMod.ApplySnowmobileStatBars(bars, donorComputation);
            Require(Approximately(bars.power, 90d, 0.001d), "donor-power-vs-recipient-factory");
            Require(bars.power > 0f && bars.power <= 100f &&
                    bars.climbing > 0f && bars.climbing <= 100f &&
                    bars.agility > 0f && bars.agility <= 100f,
                "native-stat-domain");

            donorComputation.stats.horsePower = defaults.horsePower;
            donorComputation.stats.lugHeight = defaults.lugHeight;
            donorComputation.stats.friction = defaults.friction;
            donorComputation.stats.weight = defaults.weight;
            AlpineTuningMod.ApplySnowmobileStatBars(bars, donorComputation);
            Require(Approximately(bars.power, defaults.statsPower, 0d) &&
                    Approximately(bars.climbing, defaults.statsClimbing, 0d) &&
                    Approximately(bars.agility, defaults.statsAgility, 0d),
                "native-stat-exact-reset");

            ResolvedStats factoryPreview = AlpineTuneMath.ComputeStats(
                defaults, defaults, null, new PartEffect(), new FineTuneSettings());
            ResolvedStats currentPreview = AlpineTuneMath.ComputeStats(
                defaults, defaults, null,
                new PartEffect { horsePowerMultiplier = 1.10f },
                new FineTuneSettings());
            Require(Approximately(
                    currentPreview.horsePower / factoryPreview.horsePower,
                    1.10d,
                    0.0001d),
                "shared-configured-preview-input");

            var turboDonor = SyntheticDefaults();
            turboDonor.horsePower = 180f;
            turboDonor.isTurboOn = true;
            turboDonor.engineText = "Synthetic Turbo Donor";
            ResolvedStats donorStats = AlpineTuneMath.ComputeStats(
                defaults,
                turboDonor,
                new PartEffect(),
                new FineTuneSettings());
            Require(donorStats.isTurboOn, "donor-native-turbo-state");
            Require(string.Equals(donorStats.engineText, turboDonor.engineText, StringComparison.Ordinal),
                "donor-engine-text");

            var naturallyAspiratedDonor = SyntheticDefaults();
            naturallyAspiratedDonor.isTurboOn = false;
            defaults.isTurboOn = true;
            ResolvedStats naturallyAspiratedSwap = AlpineTuneMath.ComputeStats(
                defaults,
                naturallyAspiratedDonor,
                new PartEffect(),
                new FineTuneSettings());
            Require(!naturallyAspiratedSwap.isTurboOn, "donor-native-na-state");
            defaults.isTurboOn = false;

            defaults.hasMaxRpm = true;
            defaults.maxRpm = 8500f;
            var maxRpmDonor = SyntheticDefaults();
            maxRpmDonor.hasMaxRpm = true;
            maxRpmDonor.maxRpm = 9200f;
            ResolvedStats donorMaxRpm = AlpineTuneMath.ComputeStats(
                defaults, maxRpmDonor, new PartEffect(), new FineTuneSettings());
            Require(donorMaxRpm.hasMaxRpm && Approximately(donorMaxRpm.maxRpm, 9200d, 0d),
                "donor-max-rpm");
            Require(AlpineTuningMod.NativeSpawnValuesDiffer(defaults, donorMaxRpm),
                "max-rpm-only-rebuild");

            var missingMaxRpmDonor = SyntheticDefaults();
            missingMaxRpmDonor.hasMaxRpm = false;
            missingMaxRpmDonor.maxRpm = 0f;
            ResolvedStats fallbackMaxRpm = AlpineTuneMath.ComputeStats(
                defaults, missingMaxRpmDonor, new PartEffect(), new FineTuneSettings());
            Require(fallbackMaxRpm.hasMaxRpm && Approximately(fallbackMaxRpm.maxRpm, 8500d, 0d),
                "recipient-max-rpm-fallback");
            Require(!AlpineTuningMod.NativeSpawnValuesDiffer(defaults, fallbackMaxRpm),
                "matching-max-rpm-no-rebuild");

            SledDefaults noMaxRpmDefaults = SyntheticDefaults();
            ResolvedStats noMaxRpm = AlpineTuneMath.ComputeStats(
                noMaxRpmDefaults, noMaxRpmDefaults, new PartEffect(), new FineTuneSettings());
            Require(!noMaxRpm.hasMaxRpm && Approximately(noMaxRpm.maxRpm, 0d, 0d),
                "missing-max-rpm-remains-unavailable");
            noMaxRpm.hasMaxRpm = true;
            noMaxRpm.maxRpm = 8500f;
            Require(AlpineTuningMod.NativeSpawnValuesDiffer(noMaxRpmDefaults, noMaxRpm),
                "max-rpm-availability-rebuild");
        }

        private static void TestNativeFieldScaling()
        {
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(10d, 0.5f, AlpineTuningMod.NativePhysicsValueKind.BrakeForce),
                    8d,
                    0.00001d),
                "brake-lower-clamp");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(10d, 2f, AlpineTuningMod.NativePhysicsValueKind.BrakeForce),
                    12d,
                    0.00001d),
                "brake-upper-clamp");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(30d, 2f, AlpineTuningMod.NativePhysicsValueKind.SkisMaxAngle),
                    33d,
                    0.00001d),
                "steering-angle-clamp");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(-4d, 1.25f, AlpineTuningMod.NativePhysicsValueKind.ToeAngle),
                    -5d,
                    0.00001d),
                "toe-preserves-sign");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(0d, 1.25f, AlpineTuningMod.NativePhysicsValueKind.ToeAngle),
                    0d,
                    0d),
                "zero-toe-remains-zero");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(2d, 0.5f, AlpineTuningMod.NativePhysicsValueKind.LeftCamberFactor),
                    1.6d,
                    0.00001d),
                "camber-lower-clamp");
            Require(Approximately(
                    AlpineTuningMod.ScaleNativePhysicsValue(0.8d, 2f, AlpineTuningMod.NativePhysicsValueKind.SkiGrip),
                    1.08d,
                    0.00001d),
                "grip-upper-clamp");

            double leftBaseline = 0.8d;
            double rightBaseline = 1.1d;
            double leftFirst = AlpineTuningMod.ScaleNativePhysicsValue(
                leftBaseline, 1.1f, AlpineTuningMod.NativePhysicsValueKind.SkiGrip);
            double rightFirst = AlpineTuningMod.ScaleNativePhysicsValue(
                rightBaseline, 1.1f, AlpineTuningMod.NativePhysicsValueKind.SkiGrip);
            double leftReapplied = AlpineTuningMod.ScaleNativePhysicsValue(
                leftBaseline, 1.1f, AlpineTuningMod.NativePhysicsValueKind.SkiGrip);
            Require(Approximately(leftFirst, leftReapplied, 0d), "grip-reapply-from-baseline");
            Require(!Approximately(leftFirst, rightFirst, 0.00001d), "asymmetric-grip-defaults-preserved");
        }

        private static void TestInternalContracts()
        {
            var drivetrainKinds = new[]
            {
                AlpineTuningMod.NativePhysicsValueKind.PowerEfficiency,
                AlpineTuningMod.NativePhysicsValueKind.DrivetrainSpeed,
                AlpineTuningMod.NativePhysicsValueKind.TrackMass
            };
            var suspensionKinds = new[]
            {
                AlpineTuningMod.NativePhysicsValueKind.AntiRollBar,
                AlpineTuningMod.NativePhysicsValueKind.TrackRigidityFront,
                AlpineTuningMod.NativePhysicsValueKind.TrackRigidityRear,
                AlpineTuningMod.NativePhysicsValueKind.FrontSpring,
                AlpineTuningMod.NativePhysicsValueKind.FrontDamper,
                AlpineTuningMod.NativePhysicsValueKind.FrontCompressionDamping,
                AlpineTuningMod.NativePhysicsValueKind.FrontReboundDamping,
                AlpineTuningMod.NativePhysicsValueKind.RearSpring,
                AlpineTuningMod.NativePhysicsValueKind.RearDamper,
                AlpineTuningMod.NativePhysicsValueKind.RearCompressionDamping,
                AlpineTuningMod.NativePhysicsValueKind.RearReboundDamping
            };
            var steeringKinds = new[]
            {
                AlpineTuningMod.NativePhysicsValueKind.SkisMaxAngle,
                AlpineTuningMod.NativePhysicsValueKind.ToeAngle,
                AlpineTuningMod.NativePhysicsValueKind.LeftCamberFactor,
                AlpineTuningMod.NativePhysicsValueKind.RightCamberFactor
            };
            var explicitlyClassifiedKinds = new HashSet<AlpineTuningMod.NativePhysicsValueKind>(
                drivetrainKinds.Concat(suspensionKinds).Concat(steeringKinds)
                    .Concat(new[]
                    {
                        AlpineTuningMod.NativePhysicsValueKind.BrakeForce,
                        AlpineTuningMod.NativePhysicsValueKind.SkiGrip,
                        AlpineTuningMod.NativePhysicsValueKind.TrackGrip
                    }));
            Require(explicitlyClassifiedKinds.SetEquals(
                    Enum.GetValues(typeof(AlpineTuningMod.NativePhysicsValueKind))
                        .Cast<AlpineTuningMod.NativePhysicsValueKind>()),
                "native-subsystem-classification-complete");

            Require(drivetrainKinds.All(kind => AlpineTuningMod.NativePhysicsSubsystemFor(kind) ==
                    AlpineTuningMod.NativePhysicsSubsystem.Drivetrain), "native-subsystem-drivetrain");
            Require(AlpineTuningMod.NativePhysicsSubsystemFor(
                    AlpineTuningMod.NativePhysicsValueKind.BrakeForce) ==
                    AlpineTuningMod.NativePhysicsSubsystem.Brake, "native-subsystem-brake");
            Require(suspensionKinds.All(kind => AlpineTuningMod.NativePhysicsSubsystemFor(kind) ==
                    AlpineTuningMod.NativePhysicsSubsystem.Suspension), "native-subsystem-suspension");
            Require(steeringKinds.All(kind => AlpineTuningMod.NativePhysicsSubsystemFor(kind) ==
                    AlpineTuningMod.NativePhysicsSubsystem.Steering), "native-subsystem-steering");
            Require(AlpineTuningMod.NativePhysicsSubsystemFor(
                    AlpineTuningMod.NativePhysicsValueKind.SkiGrip) ==
                    AlpineTuningMod.NativePhysicsSubsystem.SkiGrip, "native-subsystem-ski-grip");
            Require(AlpineTuningMod.NativePhysicsSubsystemFor(
                    AlpineTuningMod.NativePhysicsValueKind.TrackGrip) ==
                    AlpineTuningMod.NativePhysicsSubsystem.TrackGrip, "native-subsystem-track-grip");

            Require(AlpineTuningMod.NormalizeSledKey(null) == "UNKNOWN" &&
                    AlpineTuningMod.NormalizeSledKey("   ") == "UNKNOWN", "sled-key-empty");
            Require(AlpineTuningMod.NormalizeSledKey("  Trail Sled  ") == "Trail_Sled",
                "sled-key-ordinary-space");

            string slashKey = AlpineTuningMod.NormalizeSledKey("Trail/Sled");
            Require(slashKey == AlpineTuningMod.NormalizeSledKey("Trail/Sled") &&
                    slashKey.IndexOf('/') < 0 && slashKey.Length <= 96 &&
                    slashKey.StartsWith("Trail_Sled_", StringComparison.Ordinal), "sled-key-slash");
            string controlKey = AlpineTuningMod.NormalizeSledKey("Trail\u0001Sled");
            Require(controlKey.All(character => !char.IsControl(character)) && controlKey.Length <= 96,
                "sled-key-control");
            string longKey = AlpineTuningMod.NormalizeSledKey(new string('A', 140));
            Require(longKey.Length == 96 && longKey == AlpineTuningMod.NormalizeSledKey(new string('A', 140)),
                "sled-key-long");
            Require(AlpineTuningMod.NormalizeSledKey("Mötör雪") == "Mötör雪",
                "sled-key-unicode");
        }

        private static SledDefaults SyntheticDefaults()
        {
            return new SledDefaults
            {
                sledKey = "synthetic_sled",
                vehicleId = "1001",
                horsePower = 100f,
                powerFactor = 1f,
                lugHeight = 50f,
                friction = 1.2f,
                weight = 250f,
                skiStance = 939.8f,
                skisXDistanceOffset = 0.4f,
                statsPower = 50f,
                statsClimbing = 55f,
                statsAgility = 60f,
                hasSnowmobileStats = true,
                centerOfMassOffset = new Vec3Data(),
                driverCenterOfMassOffset = new Vec3Data()
            };
        }

        private static void TestNativeDriveModel()
        {
            string fixture = ReadRepoText("ReleaseTests/Fixtures/numerical-cases.json");
            float hp = FixtureFloat(fixture, "driveHorsepower");
            float efficiency = FixtureFloat(fixture, "driveEfficiency");
            float minimumSpeed = FixtureFloat(fixture, "driveMinimumSpeed");
            float taperStart = FixtureFloat(fixture, "driveTaperStart");
            float taperEnd = FixtureFloat(fixture, "driveTaperEnd");
            float conversion = FixtureFloat(fixture, "nativePowerConversion");

            double basePower = hp * conversion * efficiency;
            float flat = AlpineTuneMath.NativeDeliveredTrackPower(
                hp, efficiency, 1f, 20f, taperStart, taperEnd);
            Require(Approximately(flat, basePower, 2e-6), "drive-flat-power");
            Require(Approximately(AlpineTuneMath.NativeDeliveredTrackPower(
                    hp, efficiency, 1f, taperStart, taperStart, taperEnd), basePower, 2e-6),
                "drive-taper-start");
            Require(Approximately(AlpineTuneMath.NativeDeliveredTrackPower(
                    hp, efficiency, 1f, taperEnd, taperStart, taperEnd), 0d, 1e-7),
                "drive-taper-end");
            Require(Approximately(AlpineTuneMath.NativeDeliveredTrackPower(
                    hp, efficiency, 1f, taperEnd + 50f, taperStart, taperEnd), 0d, 1e-7),
                "drive-after-taper");
            float reverse = AlpineTuneMath.NativeDeliveredTrackPower(
                hp, efficiency, 1f, -20f, taperStart, taperEnd);
            Require(Approximately(reverse, flat, 1e-7), "drive-negative-speed-symmetry");
            Require(Approximately(AlpineTuneMath.NativeTrackForce(flat, 0.1f, minimumSpeed),
                    flat / minimumSpeed, 2e-6), "drive-minimum-speed");
            Require(Approximately(AlpineTuneMath.NativeTrackForce(flat, 20f, minimumSpeed),
                    flat / 20f, 2e-6), "drive-force");
            Require(Approximately(AlpineTuneMath.NativeTrackForce(flat, -20f, minimumSpeed),
                    flat / 20f, 2e-6), "drive-force-negative-speed-symmetry");
            Require(AlpineTuneMath.NativeDeliveredTrackPower(
                    hp, efficiency, 1f, 20f, taperEnd, taperStart) == 0f,
                "drive-invalid-taper-rejected");
        }

        private static void TestEstimatedCurveMath()
        {
            string fixture = ReadRepoText("ReleaseTests/Fixtures/numerical-cases.json");
            double metricConstant = FixtureDouble(fixture, "torqueConversionMetric");
            double imperialConstant = FixtureDouble(fixture, "torqueConversionImperial");
            double horsepower = FixtureDouble(fixture, "driveHorsepower");
            double rpm = 7000d;
            double kilowatts = horsepower * UnitConversion.KilowattsPerHorsepower;
            double torqueNm = kilowatts * metricConstant / rpm;
            double torqueLbFt = horsepower * imperialConstant / rpm;

            Require(IsFinitePositive(torqueNm), "metric-torque-finite");
            Require(IsFinitePositive(torqueLbFt), "imperial-torque-finite");
            Require(Math.Abs(torqueLbFt - torqueNm * UnitConversion.PoundFeetPerNewtonMeter) < 0.02d, "torque-unit-agreement");

            string[] engineNames = { "Patriot 850", "E-TEC 850", "ACE 900", "ACE 900" };
            bool[] turboStates = { false, true, false, true };
            AlpineTuneMath.EstimatedEngineArchetype[] expectedArchetypes =
            {
                AlpineTuneMath.EstimatedEngineArchetype.TwoStrokeNaturallyAspirated,
                AlpineTuneMath.EstimatedEngineArchetype.TwoStrokeTurbo,
                AlpineTuneMath.EstimatedEngineArchetype.FourStrokeNaturallyAspirated,
                AlpineTuneMath.EstimatedEngineArchetype.FourStrokeTurbo
            };
            for (int index = 0; index < engineNames.Length; index++)
            {
                Require(AlpineTuneMath.TryGetEstimatedEngineCurve(
                        engineNames[index], turboStates[index], out AlpineTuneMath.EstimatedEngineArchetype archetype,
                        out UnityEngine.Vector2[] anchors), "curve-known-family");
                Require(archetype == expectedArchetypes[index] && anchors != null && anchors.Length == 4,
                    "curve-archetype");
                Require(anchors.Select(anchor => anchor.x).SequenceEqual(
                        anchors.Select(anchor => anchor.x).OrderBy(value => value)), "curve-anchor-order");
                Require(anchors.All(anchor => IsFinitePositive(anchor.y) &&
                                              !float.IsNaN(anchor.x) && !float.IsInfinity(anchor.x)),
                    "curve-anchor-finite");

                UnityEngine.Vector2 peak = anchors
                    .OrderByDescending(anchor => anchor.y)
                    .ThenBy(anchor => anchor.x)
                    .First();
                float peakFraction = AlpineTuneMath.InterpolateEstimatedEngineCurve(anchors, peak.x);
                Require(Approximately(peakFraction, 1d, 1e-7), "curve-peak");
                Require(Approximately(horsepower * peakFraction, horsepower, 1e-7),
                    "curve-configured-peak-output");

                for (int sample = 0; sample <= 100; sample++)
                {
                    float value = AlpineTuneMath.InterpolateEstimatedEngineCurve(anchors, sample / 100f);
                    Require(!float.IsNaN(value) && !float.IsInfinity(value) && value > 0f,
                        "curve-sample-finite");
                    double sampleRpm = Math.Max(1d, sample / 100d * 8500d);
                    double sampleHorsepower = horsepower * value;
                    double sampleNewtonMeters =
                        UnitConversion.HorsepowerToKilowatts((float)sampleHorsepower) *
                        metricConstant / sampleRpm;
                    double samplePoundFeet = sampleHorsepower * imperialConstant / sampleRpm;
                    Require(IsFinitePositive(sampleNewtonMeters) && IsFinitePositive(samplePoundFeet) &&
                            Math.Abs(samplePoundFeet -
                                sampleNewtonMeters * UnitConversion.PoundFeetPerNewtonMeter) < 0.03d,
                        "curve-torque-sample-finite");
                }
            }

            Require(!AlpineTuneMath.TryGetEstimatedEngineCurve(
                    "Unknown Engine Family", false, out AlpineTuneMath.EstimatedEngineArchetype unknown,
                    out UnityEngine.Vector2[] unknownAnchors) &&
                    unknown == AlpineTuneMath.EstimatedEngineArchetype.Unknown && unknownAnchors == null,
                "curve-unknown-family-rejected");

            var resolved = new ResolvedStats { hasMaxRpm = true, maxRpm = 9100f };
            Require(Approximately(AlpineTuneMath.ResolveEstimatedRedline(resolved), 9100d, 1e-7),
                "curve-resolved-redline");
            Require(Approximately(AlpineTuneMath.ResolveEstimatedRedline(new ResolvedStats()), 8500d, 1e-7),
                "curve-fallback-redline");
            Require(Approximately(AlpineTuneMath.ResolveEstimatedCurveStartRpm(
                    8500f, null, null, null), 3825d, 1e-7),
                "curve-default-start-rpm");
            var recipientController = new ControllerDefaults
            {
                hasClutchRpmMin = true,
                clutchRpmMin = 4000f
            };
            float recipientStart = AlpineTuneMath.ResolveEstimatedCurveStartRpm(
                8500f,
                recipientController,
                new PartEffect { clutchRpmMinOffset = 100f },
                new FineTuneSettings { clutchTrimPercent = 10f });
            Require(Approximately(recipientStart, 4510d, 1e-7), "curve-recipient-clutch-start-rpm");

            Require(AlpineTuneMath.TryGetEstimatedEngineCurve(
                    "ACE 900", true, out AlpineTuneMath.EstimatedEngineArchetype highStartArchetype,
                    out UnityEngine.Vector2[] highStartAnchors) &&
                    highStartArchetype == AlpineTuneMath.EstimatedEngineArchetype.FourStrokeTurbo,
                "curve-high-start-archetype");
            var highStartController = new ControllerDefaults
            {
                hasClutchRpmMin = true,
                clutchRpmMin = 9500f
            };
            float redline = 8500f;
            float clutchStart = AlpineTuneMath.ResolveEstimatedCurveStartRpm(
                redline, highStartController, new PartEffect(), new FineTuneSettings());
            float peakPosition = highStartAnchors.OrderByDescending(anchor => anchor.y).First().x;
            float plottedStart = Math.Min(clutchStart / redline, peakPosition);
            Require(clutchStart / redline > peakPosition &&
                    Approximately(AlpineTuneMath.InterpolateEstimatedEngineCurve(
                        highStartAnchors, plottedStart), 1d, 1e-7),
                "curve-high-clutch-start-preserves-peak");

            var invertedFactoryClutch = new ControllerDefaults
            {
                hasClutchRpmMin = true,
                clutchRpmMin = 5000f,
                hasClutchRpmMax = true,
                clutchRpmMax = 4900f
            };
            AlpineTuneMath.ResolvedClutchRange unchangedClutch = AlpineTuneMath.ResolveClutchRange(
                invertedFactoryClutch, new PartEffect(), new FineTuneSettings());
            Require(Approximately(unchangedClutch.Minimum, 5000d, 0d) &&
                    Approximately(unchangedClutch.Maximum, 4900d, 0d),
                "factory-clutch-values-reset-exactly");
            AlpineTuneMath.ResolvedClutchRange modifiedClutch = AlpineTuneMath.ResolveClutchRange(
                invertedFactoryClutch,
                new PartEffect { clutchRpmMinOffset = 1f },
                new FineTuneSettings());
            Require(modifiedClutch.Maximum >= modifiedClutch.Minimum + 100f,
                "modified-clutch-safe-ordering");
            Require(Approximately(AlpineTuneMath.ResolveRpmSensitivity(
                    0f, new PartEffect()), 0d, 0d) &&
                    Approximately(AlpineTuneMath.ResolveRpmSensitivityDown(
                    0f, new PartEffect()), 0d, 0d),
                "factory-rpm-zero-reset-exactly");
            Require(Approximately(AlpineTuneMath.ResolveRpmSensitivity(
                    2f,
                    new PartEffect
                    {
                        rpmSensitivityMultiplier = 1.2f,
                        turboRpmResponseMultiplier = 1.1f
                    }), 2.64d, 0.00001d),
                "combined-rpm-response");
        }

        private static void TestNativeAssemblyContracts()
        {
            Assembly game = typeof(VehicleScriptableObject).Assembly;
            Require(string.Equals(game.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal),
                "native-assembly-name");
            Require(AssemblyName.GetAssemblyName(_gameAssembly).FullName == game.GetName().FullName,
                "native-assembly-identity");

            Type vehicle = typeof(VehicleScriptableObject);
            foreach (string fieldName in new[]
            {
                "horsePower", "maxRpm", "skiStance", "lugHeight",
                "coefficientOfFriction", "weight", "skisXDistanceOffset"
            })
                RequireNativeFloatField(vehicle, fieldName, "native-vehicle-float-field");
            RequireNativeField(vehicle, "snowmobileStats", "native-snowmobile-stats-field");
            foreach (string fieldName in new[] { "power", "climbing", "agility" })
                RequireNativeFloatField(typeof(SnowmobileStats), fieldName, "native-stat-field");

            Type controller = typeof(SnowmobileController);
            foreach (string fieldName in new[]
            {
                "throttleExponent", "rpmSensitivity", "rpmSensitivityDown",
                "clutchRpmMin", "clutchRpmMax", "minThrottleOnClutchEngagement"
            })
                RequireNativeFloatField(controller, fieldName, "native-controller-field");
            Type mesh = RequireNativeType(game, "MeshInterpretter");
            foreach (string fieldName in new[]
            {
                "powerEfficiency", "drivetrainMinSpeed", "drivetrainMaxSpeed1",
                "drivetrainMaxSpeed2", "trackMass", "breakForce"
            })
                RequireNativeFloatField(mesh, fieldName, "native-drivetrain-field");

            Type controllerBase = RequireNativeType(game, "SnowmobileControllerBase");
            foreach (string fieldName in new[] { "skisMaxAngle", "toeAngle" })
                RequireNativeFloatField(controllerBase, fieldName, "native-steering-field");
            RequireNativeField(controllerBase, "leftSki", "native-left-ski-field");
            RequireNativeField(controllerBase, "rightSki", "native-right-ski-field");
            Type ski = RequireNativeType(game, "Ski2");
            RequireNativeFloatField(ski, "camberFactor", "native-camber-field");

            Type hardSurface = RequireNativeType(game, "HardSurfaceContactBase");
            Type skiContact = RequireNativeType(game, "SkiHardSurfaceContact");
            Type trackContact = RequireNativeType(game, "TrackHardSurfaceContact");
            FieldInfo skiContactBase = RequireNativeField(
                skiContact, "contactBase", "native-ski-contact-base-field");
            FieldInfo trackContactBase = RequireNativeField(
                trackContact, "contactBase", "native-track-contact-base-field");
            Require(hardSurface.IsAssignableFrom(skiContactBase.FieldType) &&
                    hardSurface.IsAssignableFrom(trackContactBase.FieldType),
                "native-contact-base-types");
            RequireNativeFloatField(hardSurface, "grip", "native-contact-grip-field");
        }

        private static Type RequireNativeType(Assembly assembly, string name)
        {
            Type type = assembly != null ? assembly.GetType(name, false, false) : null;
            Require(type != null, "native-type-missing");
            return type;
        }

        private static FieldInfo RequireNativeField(Type type, string name, string code)
        {
            FieldInfo field = FindNativeField(type, name);
            Require(field != null && !field.IsStatic, code);
            return field;
        }

        private static void RequireNativeFloatField(Type type, string name, string code)
        {
            FieldInfo field = RequireNativeField(type, name, code);
            Require(field.FieldType == typeof(float), code);
        }

        private static FieldInfo FindNativeField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static void TestReleaseAssemblyMetadata()
        {
            AssemblyName name = AssemblyName.GetAssemblyName(_releaseAssembly);
            Require(name.Name == "Alpine Tuning", "assembly-name");
            Require(name.Version != null && name.Version.ToString() == AssemblyVersion, "compiled-assembly-version");

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(_releaseAssembly);
            Require(info.FileVersion == AssemblyVersion, "compiled-file-version");
            Require(info.ProductVersion == PublicVersion, "compiled-informational-version");
            Require(info.ProductName == "Alpine Tuning", "compiled-product-name");

            ValidateAmd64PortableExecutable(_releaseAssembly);
        }

        private static void ValidateAmd64PortableExecutable(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Require(bytes.Length >= 0x40 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z', "pe-dos-header");
            int peOffset = ReadInt32LittleEndian(bytes, 0x3c, "pe-header-offset");
            Require(peOffset >= 0x40 && peOffset <= bytes.Length - 26, "pe-header-bounds");
            Require(bytes[peOffset] == (byte)'P' && bytes[peOffset + 1] == (byte)'E' &&
                    bytes[peOffset + 2] == 0 && bytes[peOffset + 3] == 0, "pe-signature");

            ushort machine = ReadUInt16LittleEndian(bytes, peOffset + 4, "pe-machine-bounds");
            ushort optionalHeaderSize = ReadUInt16LittleEndian(bytes, peOffset + 20, "pe-optional-size-bounds");
            ushort characteristics = ReadUInt16LittleEndian(bytes, peOffset + 22, "pe-characteristics-bounds");
            Require(machine == 0x8664, "pe-not-amd64");
            Require(optionalHeaderSize >= 2 && peOffset + 24L + optionalHeaderSize <= bytes.Length, "pe-optional-header-bounds");
            Require(ReadUInt16LittleEndian(bytes, peOffset + 24, "pe-magic-bounds") == 0x020b, "pe-not-pe32-plus");
            Require((characteristics & 0x2000) != 0, "pe-not-dll");
        }

        private static void TestEmbeddedGarageResources()
        {
            string iconDir = RepoFile("SleddersTuner/Assets/GarageIcons");
            Require(Directory.Exists(iconDir), "garage-icon-directory");
            Require(Directory.GetDirectories(iconDir, "*", SearchOption.TopDirectoryOnly).Length == 0,
                "garage-icon-nested-directory");
            string[] allFiles = Directory.GetFiles(iconDir, "*", SearchOption.TopDirectoryOnly);
            Require(allFiles.All(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)),
                "garage-icon-non-png");
            string[] iconFiles = allFiles
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            Require(iconFiles.Length == ExpectedGarageIconCount, "garage-icon-count");

            string[] iconNames = iconFiles.Select(Path.GetFileName).ToArray();
            Require(iconNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == iconNames.Length,
                "garage-icon-case-collision");
            Require(iconNames.All(name => name == name.ToLowerInvariant() &&
                                          Regex.IsMatch(name, "^[a-z0-9][a-z0-9._-]*\\.png$")),
                "garage-icon-name-contract");
            var fileKeys = new HashSet<string>(
                iconNames.Select(Path.GetFileNameWithoutExtension),
                StringComparer.OrdinalIgnoreCase);

            Assembly assembly = Assembly.ReflectionOnlyLoadFrom(_releaseAssembly);
            string[] resources = assembly.GetManifestResourceNames();
            string[] embeddedIcons = resources
                .Where(name => name.StartsWith("AlpineTuning.GarageIcons.", StringComparison.Ordinal) &&
                               name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] expectedResources = iconNames
                .Select(name => "AlpineTuning.GarageIcons." + name)
                .ToArray();
            Require(new HashSet<string>(embeddedIcons, StringComparer.Ordinal)
                    .SetEquals(expectedResources), "embedded-icon-manifest");
            Require(resources.Contains("AlpineTuning.Brand.Mark.png"), "brand-resource");

            Dictionary<string, string> aliases = ReadGarageIconAliases();
            Require(aliases.Count >= 16, "garage-icon-alias-count");
            foreach (KeyValuePair<string, string> alias in aliases)
            {
                Require(!string.Equals(alias.Key, alias.Value, StringComparison.OrdinalIgnoreCase),
                    "garage-icon-self-alias");
                Require(fileKeys.Contains(ResolveGarageIconKey(alias.Value, aliases)),
                    "garage-icon-alias-target");
            }

            var runtimeKeys = new HashSet<string>(RequiredGarageIconKeys, StringComparer.OrdinalIgnoreCase);
            var catalog = new PartCatalog();
            Require(catalog.Parts.Select(part => part.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                    catalog.Parts.Count, "garage-part-id-collision");
            foreach (TunePart part in catalog.Parts)
            {
                Require(part != null && !string.IsNullOrWhiteSpace(part.id) &&
                        Regex.IsMatch(part.id, "^[a-z0-9][a-z0-9._-]*$"), "garage-part-icon-id");
                runtimeKeys.Add("part." + part.id);
            }

            TunePart[] accessoryParts = catalog.PartsForCategory(PartCatalog.Accessories).ToArray();
            Require(accessoryParts.Length == 3 && accessoryParts.All(part =>
                    part.effect != null && Approximately(part.effect.weightOffset, 0d, 0d) &&
                    !part.requiresReload),
                "accessory-cosmetic-runtime-only");
            Require(catalog.Find("engine.stage1")?.requiresReload == true,
                "spawn-effect-reload-derived");
            Require(catalog.Find("clutch.trail")?.requiresReload == false,
                "runtime-controller-effect-no-reload");

            Type nativeUiType = typeof(AlpineTuneMath).Assembly.GetType(
                "AlpineTuning.AlpineNativeUi", false, false);
            MethodInfo sectionCategories = nativeUiType?.GetMethod(
                "PartCategoriesForGarageSection", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo partTypeIcon = nativeUiType?.GetMethod(
                "GaragePartTypeIconKey", BindingFlags.Static | BindingFlags.NonPublic);
            Require(sectionCategories != null && partTypeIcon != null,
                "garage-category-routing-helpers");
            var routedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string section in new[] { "engine", "drivetrain", "track", "steering", "suspension", "lighting" })
            {
                var categories = ((IEnumerable<string>)sectionCategories.Invoke(null, new object[] { section })).ToArray();
                Require(categories.Length > 0, "garage-root-category-empty");
                routedCategories.UnionWith(categories);
            }
            Require(routedCategories.SetEquals(PartCatalog.OrderedCategories),
                "garage-category-routing-complete");
            Require(PartCatalog.OrderedCategories.All(category =>
                    !string.IsNullOrWhiteSpace(partTypeIcon.Invoke(null, new object[] { category }) as string)),
                "garage-category-icon-routing-complete");

            foreach (string key in runtimeKeys)
                Require(fileKeys.Contains(ResolveGarageIconKey(key, aliases)), "garage-runtime-icon-missing");

            string[] nativeEngineKeys = fileKeys
                .Where(key => key.StartsWith("engine.native-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Require(nativeEngineKeys.Length > 0 && nativeEngineKeys.All(key =>
                    Regex.IsMatch(key, "^engine\\.native-[a-z0-9][a-z0-9.-]*$")),
                "garage-native-engine-icon-key");

            Type nativeUi = typeof(AlpineTuneMath).Assembly.GetType(
                "AlpineTuning.AlpineNativeUi", false, false);
            MethodInfo nativeEngineIconKey = nativeUi?.GetMethod(
                "EngineNativeIconKey", BindingFlags.Static | BindingFlags.NonPublic);
            Require(nativeEngineIconKey != null && nativeEngineIconKey.ReturnType == typeof(string),
                "native-engine-icon-key-helper");
            foreach (string expectedKey in nativeEngineKeys)
            {
                Match match = Regex.Match(expectedKey,
                    "^engine\\.native-(?<slug>[a-z0-9.-]+)-(?<hp>-?[0-9]+)-" +
                    "(?<powerFactor>-?[0-9]+)-(?<turbo>[tn])-(?<audio>-?[0-9]+)$");
                Require(match.Success, "native-engine-icon-key-shape");
                var defaults = SyntheticDefaults();
                defaults.engineText = match.Groups["slug"].Value.Replace('-', ' ');
                defaults.horsePower = int.Parse(
                    match.Groups["hp"].Value, CultureInfo.InvariantCulture);
                defaults.powerFactor = int.Parse(
                    match.Groups["powerFactor"].Value, CultureInfo.InvariantCulture) / 1000f;
                defaults.isTurboOn = match.Groups["turbo"].Value == "t";
                defaults.engineAudioEnumType = "Synthetic.EngineAudio";
                defaults.engineAudioEnumName = "Known";
                defaults.engineAudioEnumRawValue = int.Parse(
                    match.Groups["audio"].Value, CultureInfo.InvariantCulture);
                string actualKey = nativeEngineIconKey.Invoke(null, new object[] { defaults }) as string;
                Require(string.Equals(actualKey, expectedKey, StringComparison.Ordinal) &&
                        resources.Contains("AlpineTuning.GarageIcons." + actualKey + ".png"),
                    "native-engine-icon-resource-resolution");
            }
            Require(nativeEngineIconKey.Invoke(null, new object[] { SyntheticDefaults() }) == null,
                "native-engine-icon-requires-audio-token");

            var expectedFileKeys = new HashSet<string>(
                runtimeKeys.Select(key => ResolveGarageIconKey(key, aliases)),
                StringComparer.OrdinalIgnoreCase);
            expectedFileKeys.UnionWith(nativeEngineKeys);
            Require(fileKeys.SetEquals(expectedFileKeys), "garage-icon-file-manifest");
        }

        private static Dictionary<string, string> ReadGarageIconAliases()
        {
            string source = ReadRepoText("SleddersTuner/GarageIconResources.cs");
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(source,
                "\\{\\s*\\\"(?<alias>[a-z0-9._-]+)\\\"\\s*,\\s*\\\"(?<target>[a-z0-9._-]+)\\\"\\s*\\}"))
            {
                string alias = match.Groups["alias"].Value;
                string target = match.Groups["target"].Value;
                Require(!aliases.ContainsKey(alias), "garage-icon-duplicate-alias");
                aliases.Add(alias, target);
            }
            return aliases;
        }

        private static string ResolveGarageIconKey(string key, IDictionary<string, string> aliases)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string resolved = key;
            while (aliases.TryGetValue(resolved, out string target))
            {
                Require(visited.Add(resolved), "garage-icon-alias-cycle");
                resolved = target;
            }
            return resolved;
        }

        private static void TestAssets()
        {
            string iconDir = RepoFile("SleddersTuner/Assets/GarageIcons");
            foreach (string path in Directory.GetFiles(iconDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                PngInfo info = ValidatePngMetadata(path);
                Require(info.Width == 400 && info.Height == 320, "garage-icon-png-dimensions");
                using (var bitmap = new Bitmap(path))
                {
                    Require(bitmap.Width == 400 && bitmap.Height == 320, "garage-icon-dimensions");

                    int transparent = 0;
                    int visible = 0;
                    int minX = bitmap.Width;
                    int minY = bitmap.Height;
                    int maxX = -1;
                    int maxY = -1;
                    for (int y = 0; y < bitmap.Height; y += 2)
                    {
                        for (int x = 0; x < bitmap.Width; x += 2)
                        {
                            byte alpha = bitmap.GetPixel(x, y).A;
                            if (alpha == 0)
                            {
                                transparent++;
                                continue;
                            }

                            if (alpha > 8)
                            {
                                visible++;
                                minX = Math.Min(minX, x);
                                maxX = Math.Max(maxX, x);
                                minY = Math.Min(minY, y);
                                maxY = Math.Max(maxY, y);
                            }
                        }
                    }

                    Require(transparent > 0, "garage-icon-alpha");
                    Require(visible >= 500, "garage-icon-visible-coverage");
                    Require(maxX - minX + 1 >= 40 && maxY - minY + 1 >= 80, "garage-icon-bounds");
                }
            }

            string badge = RepoFile("SleddersTuner/Assets/Brand/alpine-tuning-badge.png");
            string icon = RepoFile("SleddersTuner/Assets/Brand/alpine-tuning.ico");
            Require(File.Exists(badge) && File.Exists(icon), "brand-assets");
            PngInfo badgeInfo = ValidatePngMetadata(badge);
            Require(badgeInfo.Width == 512 && badgeInfo.Height == 512 &&
                    badgeInfo.BitDepth == 8 && badgeInfo.ColorType == 6, "brand-badge-png-contract");
            using (var bitmap = new Bitmap(badge))
                Require(bitmap.Width == 512 && bitmap.Height == 512, "brand-badge-dimensions");
            ValidateBrandIcon(icon);
        }

        private sealed class PngInfo
        {
            public int Width;
            public int Height;
            public byte BitDepth;
            public byte ColorType;
        }

        private static PngInfo ValidatePngMetadata(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return ValidatePngMetadata(bytes, 0, bytes.Length);
        }

        private static PngInfo ValidatePngMetadata(byte[] bytes, int start, int count)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Require(bytes != null && start >= 0 && count >= 20 && start + (long)count <= bytes.Length,
                "png-bounds");
            Require(signature.Where((value, index) => bytes[start + index] != value).Count() == 0,
                "png-signature");

            int offset = start + 8;
            int end = start + count;
            bool sawHeader = false;
            bool sawData = false;
            bool sawEnd = false;
            var info = new PngInfo();
            while (offset + 12 <= end)
            {
                uint length = ReadUInt32BigEndian(bytes, offset, "png-chunk-size-bounds");
                Require(length <= int.MaxValue && offset + 12L + length <= end, "png-chunk-length");
                string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                Require(type.All(character => (character >= 'A' && character <= 'Z') ||
                                              (character >= 'a' && character <= 'z')), "png-chunk-type");
                uint expectedCrc = ReadUInt32BigEndian(bytes, offset + 8 + checked((int)length), "png-crc-bounds");
                Require(ComputeCrc32(bytes, offset + 4, checked((int)length + 4)) == expectedCrc,
                    "png-crc");
                if (!sawHeader)
                {
                    Require(type == "IHDR" && length == 13, "png-header-order");
                    uint width = ReadUInt32BigEndian(bytes, offset + 8, "png-width-bounds");
                    uint height = ReadUInt32BigEndian(bytes, offset + 12, "png-height-bounds");
                    Require(width > 0 && width <= int.MaxValue && height > 0 && height <= int.MaxValue,
                        "png-dimensions");
                    info.Width = (int)width;
                    info.Height = (int)height;
                    info.BitDepth = bytes[offset + 16];
                    info.ColorType = bytes[offset + 17];
                    Require(info.BitDepth == 8 && (info.ColorType == 3 || info.ColorType == 6),
                        "png-pixel-format");
                    Require(bytes[offset + 18] == 0 && bytes[offset + 19] == 0 &&
                            bytes[offset + 20] == 0, "png-encoding-method");
                    sawHeader = true;
                }

                Require(type != "tEXt" && type != "zTXt" && type != "iTXt" &&
                        type != "eXIf" && type != "iCCP" && type != "tIME", "png-identity-metadata");
                if (type == "IDAT")
                    sawData = true;
                if (type == "IEND")
                    Require(length == 0, "png-end-length");
                offset += checked((int)length + 12);
                if (type == "IEND")
                {
                    sawEnd = true;
                    break;
                }
            }

            Require(sawHeader && sawData && sawEnd && offset == end, "png-structure");
            return info;
        }

        private static void ValidateBrandIcon(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Require(bytes.Length > 6, "brand-icon-size");
            Require(ReadUInt16LittleEndian(bytes, 0, "ico-header-bounds") == 0 &&
                    ReadUInt16LittleEndian(bytes, 2, "ico-type-bounds") == 1, "ico-header");
            ushort entryCount = ReadUInt16LittleEndian(bytes, 4, "ico-count-bounds");
            Require(entryCount == RequiredBrandIconSizes.Length && 6L + entryCount * 16L <= bytes.Length,
                "ico-entry-count");

            int directoryEnd = 6 + entryCount * 16;
            int expectedOffset = directoryEnd;
            var dimensions = new HashSet<int>();
            for (int index = 0; index < entryCount; index++)
            {
                int entry = 6 + index * 16;
                int width = bytes[entry] == 0 ? 256 : bytes[entry];
                int height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
                byte colorCount = bytes[entry + 2];
                byte reserved = bytes[entry + 3];
                ushort planes = ReadUInt16LittleEndian(bytes, entry + 4, "ico-planes-bounds");
                ushort bitDepth = ReadUInt16LittleEndian(bytes, entry + 6, "ico-depth-bounds");
                uint imageSize = ReadUInt32LittleEndian(bytes, entry + 8, "ico-size-bounds");
                uint imageOffset = ReadUInt32LittleEndian(bytes, entry + 12, "ico-offset-bounds");

                Require(width == height && RequiredBrandIconSizes.Contains(width) && dimensions.Add(width),
                    "ico-dimensions");
                Require(colorCount == 0 && reserved == 0 && planes <= 1 && bitDepth == 32,
                    "ico-entry-metadata");
                Require(imageSize >= 20 && imageSize <= int.MaxValue && imageOffset == expectedOffset &&
                        imageOffset + (long)imageSize <= bytes.Length, "ico-entry-bounds");

                PngInfo embedded = ValidatePngMetadata(bytes, checked((int)imageOffset), checked((int)imageSize));
                Require(embedded.Width == width && embedded.Height == height &&
                        embedded.BitDepth == 8 && embedded.ColorType == 6, "ico-image-contract");
                expectedOffset = checked((int)(imageOffset + imageSize));
            }

            Require(dimensions.SetEquals(RequiredBrandIconSizes) && expectedOffset == bytes.Length,
                "ico-payload-layout");
        }

        private static void TestPrivacy()
        {
            List<PrivacyMarker> markers = DiscoverPrivacyMarkers();
            Require(markers.Count >= 1, "privacy-markers-unavailable");

            IEnumerable<string> publicFiles = File.ReadAllLines(_inventoryFile)
                .Select(NormalizeRelativePath)
                .Where(path => path.Length > 0 && File.Exists(RepoFile(path)))
                .Select(RepoFile);

            foreach (string file in publicFiles.Concat(Directory.GetFiles(_scanRoot, "*", SearchOption.AllDirectories)).Distinct(StringComparer.OrdinalIgnoreCase))
                Require(!ContainsPrivacyMarker(file, markers), "private-data-detected");
        }

        private static bool ContainsQuotedValue(string text, string value)
        {
            string escaped = Regex.Escape(value);
            return Regex.IsMatch(text, "[\\\"']\\s*" + escaped + "\\s*[\\\"']", RegexOptions.IgnoreCase);
        }

        private static List<PrivacyMarker> DiscoverPrivacyMarkers()
        {
            var markers = new List<PrivacyMarker>();
            string specialProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddMarker(markers, specialProfile, false);
            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            AddMarker(markers, profile, false);
            AddMarker(markers, (profile ?? string.Empty).Replace('\\', '/'), false);
            AddMarker(markers, (profile ?? string.Empty).Replace("\\", "\\\\"), false);
            if (!string.IsNullOrWhiteSpace(specialProfile))
            {
                try
                {
                    AddMarker(markers, new Uri(specialProfile).AbsoluteUri, false);
                }
                catch
                {
                    // Other normalized profile-path variants remain available.
                }
            }

            string userName = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(userName))
                AddMarker(markers, userName, userName.Length >= 5, userName.Length < 5);
            AddMarker(markers, Environment.MachineName, true);
            string domain = Environment.UserDomainName;
            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(userName))
                AddMarker(markers, domain + "\\" + userName, false);

            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    AddMarker(markers, identity.User != null ? identity.User.Value : null, false);
                    AddMarker(markers, identity.Name, false);
                }
            }
            catch
            {
                // The profile-path markers still provide a useful privacy gate.
            }

            foreach (string loginUsersPath in FindSteamLoginFiles())
            {
                if (!File.Exists(loginUsersPath))
                    continue;
                string text = File.ReadAllText(loginUsersPath);
                foreach (Match match in Regex.Matches(text, "\\\"(?<id>[0-9]{17})\\\""))
                    AddMarker(markers, match.Groups["id"].Value, false);
                foreach (Match match in Regex.Matches(text,
                    "\\\"(?:PersonaName|AccountName)\\\"\\s+\\\"(?<name>[^\\\"]+)\\\"",
                    RegexOptions.IgnoreCase))
                {
                    string accountName = match.Groups["name"].Value.Trim();
                    AddMarker(markers, accountName, accountName.Length >= 5, accountName.Length < 5);
                }
            }

            return markers
                .Where(marker => !string.IsNullOrWhiteSpace(marker.Value))
                .GroupBy(marker => marker.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static IEnumerable<string> FindSteamLoginFiles()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                AddPath(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
                AddPath(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
            }
            catch
            {
                // Registry access is optional; standard install roots follow.
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            AddPath(roots, Path.Combine(programFilesX86, "Steam"));
            return roots.Select(root => Path.Combine(root, "config", "loginusers.vdf"));
        }

        private static void AddPath(ISet<string> paths, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                paths.Add(value.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void AddMarker(
            ICollection<PrivacyMarker> markers,
            string value,
            bool wholeWord,
            bool contextOnly = false)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Length >= 3)
            {
                markers.Add(new PrivacyMarker
                {
                    Value = value,
                    WholeWord = wholeWord,
                    ContextOnly = contextOnly
                });
            }
        }

        private static bool ContainsPrivacyMarker(string file, IEnumerable<PrivacyMarker> markers)
        {
            byte[] bytes = File.ReadAllBytes(file);
            string utf8 = Encoding.UTF8.GetString(bytes);
            string unicode = Encoding.Unicode.GetString(bytes);
            string unicodeOdd = bytes.Length > 1
                ? Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1)
                : string.Empty;
            foreach (PrivacyMarker marker in markers)
            {
                if (marker.ContextOnly)
                {
                    if (ContainsPrivacyContext(utf8, marker.Value) ||
                        ContainsPrivacyContext(unicode, marker.Value) ||
                        ContainsPrivacyContext(unicodeOdd, marker.Value) ||
                        ContainsWholeWord(unicode, marker.Value) ||
                        ContainsWholeWord(unicodeOdd, marker.Value))
                        return true;
                }
                else if (marker.WholeWord)
                {
                    if (ContainsWholeWord(utf8, marker.Value) ||
                        ContainsWholeWord(unicode, marker.Value) ||
                        ContainsWholeWord(unicodeOdd, marker.Value))
                        return true;
                }
                else if (utf8.IndexOf(marker.Value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         unicode.IndexOf(marker.Value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         unicodeOdd.IndexOf(marker.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsPrivacyContext(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
                return false;

            string escaped = Regex.Escape(value);
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            return ContainsQuotedValue(text, value) ||
                   Regex.IsMatch(text, "(?:^|[\\\\/])" + escaped + "(?:[\\\\/]|$)", options) ||
                   Regex.IsMatch(text,
                       "\\b(?:user(?:name)?|account(?:name)?|persona(?:name)?|author|owner|profile(?:name)?)\\b" +
                       "\\s*[:=]\\s*[\\\"']?" + escaped + "\\b",
                       options);
        }

        private static bool ContainsWholeWord(string text, string value)
        {
            int start = 0;
            while ((start = text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + value.Length;
                bool leftBoundary = start == 0 || !IsWordCharacter(text[start - 1]);
                bool rightBoundary = end == text.Length || !IsWordCharacter(text[end]);
                if (leftBoundary && rightBoundary)
                    return true;
                start++;
            }
            return false;
        }

        private static bool IsWordCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset, string code)
        {
            Require(bytes != null && offset >= 0 && offset + 2L <= bytes.Length, code);
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static uint ReadUInt32LittleEndian(byte[] bytes, int offset, string code)
        {
            Require(bytes != null && offset >= 0 && offset + 4L <= bytes.Length, code);
            return (uint)(bytes[offset] |
                          (bytes[offset + 1] << 8) |
                          (bytes[offset + 2] << 16) |
                          (bytes[offset + 3] << 24));
        }

        private static int ReadInt32LittleEndian(byte[] bytes, int offset, string code)
        {
            return unchecked((int)ReadUInt32LittleEndian(bytes, offset, code));
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset, string code)
        {
            Require(bytes != null && offset >= 0 && offset + 4L <= bytes.Length, code);
            return ((uint)bytes[offset] << 24) |
                   ((uint)bytes[offset + 1] << 16) |
                   ((uint)bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }

        private static uint ComputeCrc32(byte[] bytes, int offset, int count)
        {
            Require(bytes != null && offset >= 0 && count >= 0 && offset + (long)count <= bytes.Length,
                "crc-bounds");
            uint crc = 0xffffffffu;
            for (int index = offset; index < offset + count; index++)
            {
                crc ^= bytes[index];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1u) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xffffffffu;
        }

        private static float FixtureFloat(string json, string key)
        {
            return (float)FixtureDouble(json, key);
        }

        private static double FixtureDouble(string json, string key)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)");
            Require(match.Success, "fixture-value");
            return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }

        private static bool Approximately(double left, double right, double relativeTolerance)
        {
            double scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= scale * relativeTolerance;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
