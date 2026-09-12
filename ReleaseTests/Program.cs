using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AlpineTuning.ReleaseTests
{
    public sealed class NativeTransportRegressionDomain : MarshalByRefObject
    {
        public string Run(string coreAssembly, string gameAssembly, string modAssembly)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = new AssemblyName(args.Name).Name;
                if (name == "UnityEngine.CoreModule")
                    return Assembly.LoadFrom(coreAssembly);
                if (name == "Alpine Tuning")
                    return Assembly.LoadFrom(modAssembly);
                string managed = Path.GetDirectoryName(gameAssembly);
                string game = Directory.GetParent(Directory.GetParent(managed).FullName).FullName;
                foreach (string directory in new[] { managed, Path.Combine(game, "MelonLoader", "net35") })
                {
                    string candidate = Path.Combine(directory, name + ".dll");
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }
                return null;
            };
            try
            {
                Assembly.LoadFrom(coreAssembly);
                Assembly.LoadFrom(gameAssembly);
                Assembly.LoadFrom(modAssembly);
                Program.RunNativeTransportRegression();
                return null;
            }
            catch (Exception ex)
            {
                return "native-transport-" + ex;
            }
        }
    }

    internal static class Program
    {
        private const string PublicVersion = "2026.09.12";
        private const string AssemblyVersion = "2026.9.12.0";
        private const string CatalogVersion = "2026.09.backlog-v5";
        private const int ExpectedGarageIconCount = 182;

        private static readonly string[] RequiredGarageIconKeys =
        {
            "action.continue", "action.discard", "action.save", "action.settings",
            "action.setups", "action.unavailable", "action.current-draft", "action.recovery",
            "root.engine", "root.drivetrain", "root.suspension", "root.track",
            "root.steering", "root.lighting", "root.fuel",
            "settings.display", "settings.hotkey", "settings.metric", "settings.imperial",
            "settings.enabled", "settings.disabled", "settings.keyboard",
            "settings.controller", "settings.clear", "settings.confirm-clear",
            "type.engine-core", "type.pistons", "type.crankshaft", "type.intake-exhaust",
            "type.turbo", "type.clutch-calibration", "type.clutch-weights", "type.gearing",
            "type.brake-calibration", "type.suspension", "type.chassis",
            "type.limiter-strap", "type.rear-shock", "type.rear-spring", "type.track",
            "type.skis", "type.steering-geometry", "type.headlight-color",
            "type.headlight-output", "type.headlight-beam", "type.headlight-aim",
            "type.headlight-delete",
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
            ".github/FUNDING.yml",
            ".gitignore",
            "README.md",
            "license.txt",
            "build-release.bat",
            "SleddersTuner/SleddersTuner.csproj",
            "SleddersTuner/AlpineNativeUi.cs",
            "SleddersTuner/AlpineControllerInput.cs",
            "SleddersTuner/AlpineFuelSystem.cs",
            "SleddersTuner/AlpineHeadTrackingSystem.cs",
            "SleddersTuner/AlpineHeadlightDeleteProjection.cs",
            "SleddersTuner/AlpineNitrousSystem.cs",
            "SleddersTuner/AlpineExperimentalSystems.cs",
            "SleddersTuner/AlpineTrackGraft.cs",
            "SleddersTuner/AlpinePeerSharing.cs",
            "SleddersTuner/AlpineRemoteReplication.cs",
            "SleddersTuner/AlpineSleddersTransport.cs",
            "SleddersTuner/AlpineSledForgeSystem.cs",
            "SleddersTuner/AlpineTuneMath.cs",
            "SleddersTuner/AlpineVisualPartSystem.cs",
            "SleddersTuner/VisualProjectionCoordinator.cs",
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
            "ReleaseTests/Fixtures/tune-legacy.json",
            "scripts/Audit-SleddersAssembly.ps1",
            "docs/native-feature-audit.generated.json"
        };

        private static readonly HashSet<string> IntentionalPublicIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "donreagan",
                "Ronnie-Reagan"
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

        private sealed class MockTrackingProvider : IAlpineHeadTrackingProvider
        {
            public MockTrackingProvider(string name, bool available)
            {
                Name = name;
                IsAvailable = available;
            }

            public string Name { get; }
            public bool IsAvailable { get; }
            public bool Disposed { get; private set; }
            public AlpineHeadPose NextPose { get; set; }
            public bool TryRead(out AlpineHeadPose pose) { pose = NextPose; return IsAvailable; }
            public void Dispose() { Disposed = true; }
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
            Run("experimental head tracking coordinator", TestHeadTrackingCoordinator);
            Run("backlog v5 runtime contracts", TestBacklogV5RuntimeContracts);
            Run("native transport packet isolation", TestNativeTransportIsolation);
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
            string unexpectedFile = entries.FirstOrDefault(path => !IsAllowedPublicPath(path));
            Require(unexpectedFile == null, "unexpected-public-file:" + unexpectedFile);

            foreach (string path in RequiredPublicFiles)
                Require(entries.Contains(path, StringComparer.OrdinalIgnoreCase), "required-file-not-published:" + path);

            string ignore = ReadRepoText(".gitignore");
            string firstRule = ignore.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !line.StartsWith("#", StringComparison.Ordinal));
            Require(firstRule == "*", "gitignore-not-allowlist");
            Require(ignore.Contains("!/SleddersTuner/SleddersTuner.csproj") &&
                    ignore.Contains("!/SleddersTuner/AlpineNativeUi.cs") &&
                    ignore.Contains("!/SleddersTuner/AlpineFuelSystem.cs") &&
                    ignore.Contains("!/SleddersTuner/Assets/GarageIcons/*.png") &&
                    ignore.Contains("!/ReleaseTests/ReleaseTests.csproj") &&
                    ignore.Contains("!/ReleaseTests/Program.cs") &&
                    ignore.IndexOf("!/SleddersTuner.slnx", StringComparison.OrdinalIgnoreCase) < 0 &&
                    ignore.IndexOf("!/SleddersTuner/*.cs", StringComparison.OrdinalIgnoreCase) < 0 &&
                    ignore.IndexOf("!/ReleaseTests/*.cs", StringComparison.OrdinalIgnoreCase) < 0,
                    "gitignore-compilation-input-contract");

            // Check Git's actual rules with an empty index: tracked files can otherwise
            // conceal broken exceptions, including missing parent-directory exceptions.
            string probeRoot = Path.Combine(_tuneTestRoot, "gitignore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probeRoot);
            File.WriteAllText(Path.Combine(probeRoot, ".gitignore"), ignore, new UTF8Encoding(false));
            int exitCode;
            RunGitProbe(probeRoot, "init --quiet", null, out exitCode);
            Require(exitCode == 0, "gitignore-probe-init");
            string[] excluded =
            {
                "local/private.json", "tmp/build.log", "tools/private.ps1",
                "SleddersTuner.slnx", "ReleaseTests/releaseTests.zip",
                "SleddersTuner/bin/x64/Release/Alpine Tuning.dll",
                "SleddersTuner/obj/build.pdb", "ReleaseTests/bin/runner.exe",
                "SleddersTuner/Unreviewed.cs", "ReleaseTests/Unreviewed.cs",
                "scripts/private.ps1", "docs/private.md", ".github/workflows/unreviewed.yml"
            };
            string output = RunGitProbe(probeRoot, "-c core.quotePath=false check-ignore --no-index --stdin",
                string.Join("\n", entries.Concat(excluded)) + "\n", out exitCode);
            Require(exitCode == 1 || exitCode == 0, "gitignore-probe-failed");
            var ignored = new HashSet<string>(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in entries)
                Require(!ignored.Contains(path), "public-file-ignored:" + path);
            foreach (string path in excluded)
                Require(ignored.Contains(path), "private-file-not-ignored:" + path);
        }

        private static string RunGitProbe(string directory, string arguments, string input, out int exitCode)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (input != null)
                    process.StandardInput.Write(input);
                process.StandardInput.Close();
                process.WaitForExit();
                error.GetAwaiter().GetResult();
                exitCode = process.ExitCode;
                return output.GetAwaiter().GetResult();
            }
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

            Require(Regex.IsMatch(models, "SchemaVersion\\s*=\\s*5\\s*;"), "schema-version");
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
            Require(readme.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0, "readme-multiplayer");
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
            string fuel = ReadRepoText("SleddersTuner/AlpineFuelSystem.cs");
            string catalog = ReadRepoText("SleddersTuner/PartCatalog.cs");
            string controllerInput = ReadRepoText("SleddersTuner/AlpineControllerInput.cs");
            string tracking = ReadRepoText("SleddersTuner/AlpineHeadTrackingSystem.cs");
            string visuals = ReadRepoText("SleddersTuner/AlpineVisualPartSystem.cs");
            string trackGraft = ReadRepoText("SleddersTuner/AlpineTrackGraft.cs");

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
            Require(ui.IndexOf("((Button)nativeTuningButton).clicked += toggleSurface;", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("nativeTuningButton.clicked +=", StringComparison.Ordinal) < 0,
                "controller-tertiary-single-click-channel");
            Require(ui.IndexOf("public static void UpdateGarageTuningShortcut()", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("GarageTertiaryPressed()", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("KeyCode.JoystickButton3", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("GetButtonDown", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("AlpineNativeUi.UpdateGarageTuningShortcut();", StringComparison.Ordinal) >= 0,
                "controller-tertiary-global-shortcut");
            Require(ui.IndexOf("\"fuel-overflow-prompt\"", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("\"exit-prompt\"", StringComparison.Ordinal) >= 0,
                "prompt-transient-focus-state");
            Require(main.IndexOf("public override void OnGUI()", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("FuelSystem?.DrawOverlay();", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("internal void DrawOverlay()", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("OUT OF FUEL", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("REFUEL FROM RESERVE", StringComparison.Ordinal) >= 0,
                "fuel-overlay-and-rescue-fallback");
            Require(fuel.IndexOf("KeyCode.JoystickButton2", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("new object[] { \"Secondary\" }", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("KeyCode.R", StringComparison.Ordinal) >= 0,
                "reserve-refuel-controller-input");
            Require(fuel.IndexOf("FindInstanceMethodInHierarchy", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("\"get_Fuel\"", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("\"SetFuel\"", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("\"get_FuelCapacity\"", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("FUEL BINDING UNAVAILABLE", StringComparison.Ordinal) >= 0,
                "fuel-live-method-binding-and-visible-failure");
            Require(main.IndexOf("AccessTools.TypeByName(\"FLIADIKAFHD\")", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("AccessTools.Field(drivetrainStateType, \"PAADEMIBEJN\")", StringComparison.Ordinal) >= 0,
                "fuel-native-signed-power-owner");
            Require(catalog.IndexOf("requiresCosmeticBackpack = true", StringComparison.Ordinal) < 0 &&
                    fuel.IndexOf("Sledders currently has no wearable backpack cosmetic", StringComparison.Ordinal) >= 0,
                "reserve-fuel-no-cosmetic-gate");
            Require(models.IndexOf("public bool showFuelOverlay;", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("if (_mod.Settings.showFuelOverlay)", StringComparison.Ordinal) >= 0 &&
                    fuel.IndexOf("REFUEL FROM RESERVE", StringComparison.Ordinal) >= 0,
                "optional-fuel-readout-keeps-rescue");
            Require(main.IndexOf("FuelSystem?.SuspendRuntime();", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("HeadTracking?.Suspend();", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("VisualParts?.RestoreTrackVisual();", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("Sharing?.Shutdown();", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("if (!Settings.alpineTuningEnabled)", StringComparison.Ordinal) >= 0,
                "true-runtime-shutdown");
            Require(controllerInput.IndexOf("InputSystem.devices", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("PhysicalControllers", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("ButtonControl", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("wasPressedThisFrame", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("gamepad.buttonEast", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("inputsystem:v1:", StringComparison.Ordinal) >= 0 &&
                    controllerInput.IndexOf("Rewired.ReInput", StringComparison.Ordinal) < 0 &&
                    project.IndexOf("Unity.InputSystem.dll", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    main.IndexOf("_controllerInput.TryCapture", StringComparison.Ordinal) >= 0 &&
                    main.IndexOf("AlpineNativeUi.IsGarageTuningOpen", StringComparison.Ordinal) >= 0,
                "input-system-headlight-binding");
            Require(ui.IndexOf("RegisterCallback<NavigationMoveEvent>", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("visibleTiles[visibleTiles.Count - 1]", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("CenterNativeGarageTile(controller, rail, destination)", StringComparison.Ordinal) >= 0,
                "controller-rail-wrap");
            Require(visuals.IndexOf("TryDetectLength", StringComparison.Ordinal) >= 0 &&
                    visuals.IndexOf("ResolvePlatformFamilyMetadata", StringComparison.Ordinal) >= 0 &&
                    visuals.IndexOf("lynx-radien", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("TrackAssemblyDescriptor", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("EvaluateCompatibility", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("Addressables.InstantiateAsync", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("ApplyArcticVariant", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("InstallContactMesh", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("TrySetRearAxelController", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("ScaledFallback", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("RestoreSnapshot", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("RequestGaragePreview", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("BeginCompatibilityScan", StringComparison.Ordinal) >= 0 &&
                    trackGraft.IndexOf("PumpCompatibilityScan", StringComparison.Ordinal) >= 0 &&
                    visuals.IndexOf("_compatibilityScanPending", StringComparison.Ordinal) >= 0 &&
                    project.IndexOf("AlpineTrackGraft.cs", StringComparison.Ordinal) >= 0,
                "surgical-track-graft-compatibility-and-rollback");
            Require(!Regex.IsMatch(visuals + trackGraft,
                        @"\b(assetReference|prefabName|trackPositionOverride|trackScaleOverride|traxTransform)\s*=(?!=)",
                        RegexOptions.CultureInvariant),
                "track-graft-does-not-rewrite-vehicle-definition");
            Require(ui.IndexOf("No alternate compatible track length found.",
                        StringComparison.Ordinal) >= 0,
                "track-graft-empty-platform-message");
            Require(tracking.IndexOf("FT_SharedMem", StringComparison.Ordinal) >= 0 &&
                    tracking.IndexOf("NP_StartDataTransmission", StringComparison.Ordinal) >= 0 &&
                    tracking.IndexOf("RestoreCameraPose();", StringComparison.Ordinal) >= 0 &&
                    tracking.IndexOf("AlpineNativeUi.HasAttachedMenus", StringComparison.Ordinal) >= 0 &&
                    tracking.IndexOf("Time.timeScale <= 0.0001f", StringComparison.Ordinal) >= 0,
                "six-axis-tracking-adapters");
            Require(ui.IndexOf("\"action.setups\"", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("tertiaryLabel = \"Setups\"", StringComparison.Ordinal) >= 0 &&
                    ui.IndexOf("AddGarageNavigationTile(rail, tileButtons, \"Setups\"", StringComparison.Ordinal) < 0,
                "setups-context-action-not-root-tile");
            Require(Regex.IsMatch(ui,
                    @"captured\.name\s*\?\?\s*""\(unnamed setup\)""[\s\S]{0,220}isPreviewSelected,",
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

            var sourceTrack = new TrackCompatibilitySignature
            {
                seamCenter = UnityEngine.Vector3.zero,
                forward = UnityEngine.Vector3.forward,
                up = UnityEngine.Vector3.up,
                tunnelWidth = 0.40f,
                trackCenter = new UnityEngine.Vector3(0f, -0.25f, -0.8f),
                rearBounds = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(0f, -0.2f, -0.9f),
                    new UnityEngine.Vector3(0.5f, 0.5f, 2f)),
                trackLength = 3.7f,
                animatedTrack = true,
                contactMesh = true,
                rearGraph = true
            };
            var compatibleDonor = new TrackCompatibilitySignature
            {
                seamCenter = new UnityEngine.Vector3(0.02f, 0f, 0f),
                forward = UnityEngine.Vector3.forward,
                up = UnityEngine.Vector3.up,
                tunnelWidth = 0.42f,
                trackCenter = new UnityEngine.Vector3(0.02f, -0.25f, -0.8f),
                rearBounds = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(0.02f, -0.2f, -1f),
                    new UnityEngine.Vector3(0.5f, 0.5f, 2.2f)),
                trackLength = 3.9f,
                animatedTrack = true,
                contactMesh = true,
                rearGraph = true
            };
            Require(AlpineTrackGraft.EvaluateCompatibility(sourceTrack, compatibleDonor).compatible,
                "track-graft-compatible-signature");
            compatibleDonor.tunnelWidth = 0.48f;
            Require(!AlpineTrackGraft.EvaluateCompatibility(sourceTrack, compatibleDonor).compatible,
                "track-graft-width-threshold");
            compatibleDonor.tunnelWidth = 0.42f;
            compatibleDonor.contactMesh = false;
            Require(!AlpineTrackGraft.EvaluateCompatibility(sourceTrack, compatibleDonor).compatible,
                "track-graft-requires-contact-graph");
            compatibleDonor.contactMesh = true;
            float sixDegrees = 6f * (float)Math.PI / 180f;
            compatibleDonor.forward = new UnityEngine.Vector3(
                (float)Math.Sin(sixDegrees), 0f, (float)Math.Cos(sixDegrees));
            Require(!AlpineTrackGraft.EvaluateCompatibility(sourceTrack, compatibleDonor).compatible,
                "track-graft-axis-threshold");
            compatibleDonor.forward = UnityEngine.Vector3.forward;
            compatibleDonor.seamCenter = new UnityEngine.Vector3(0.21f, 0f, 0f);
            Require(!AlpineTrackGraft.EvaluateCompatibility(sourceTrack, compatibleDonor).compatible,
                "track-graft-translation-threshold");

            Require(AlpineTrackGraft.TryResolveFallbackScale(4f, 5f,
                        out float fallbackRatio, out _) &&
                    Approximately(fallbackRatio, 1.25d, 1e-7),
                "track-fallback-scale-ratio");
            Require(!AlpineTrackGraft.TryResolveFallbackScale(4f, 6.7f, out _, out _),
                "track-fallback-scale-clamp");
            AlpineTrackGraft.CalculateFrontAnchoredAxis(
                1f, 2f, 1.25f, 4f, out float scaledAxis, out float shiftedAxis);
            Require(Approximately(scaledAxis, 1.25d, 1e-7) &&
                    Approximately(shiftedAxis + 5f * 0.5f, 2f + 4f * 0.5f, 1e-7),
                "track-fallback-fixed-front-anchor");
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
                fuelCapacity = 40f,
                fuelConsumption = 20f,
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
            foreach (float target in new[] { 6000f, 6500f, 7000f })
            {
                AlpineTuneMath.ResolvedClutchRange racing = AlpineTuneMath.ResolveClutchRange(
                    new ControllerDefaults
                    {
                        hasClutchRpmMin = true,
                        clutchRpmMin = 4000f,
                        hasClutchRpmMax = true,
                        clutchRpmMax = 6500f
                    },
                    new PartEffect { clutchEngagementTargetRpm = target },
                    new FineTuneSettings());
                Require(Approximately(racing.Minimum, target, 0d), "racing-clutch-exact-target");
                Require(racing.Maximum >= racing.Minimum + 100f, "racing-clutch-lock-ordering");
            }
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

        private static void TestHeadTrackingCoordinator()
        {
            var trackIr = new MockTrackingProvider("TrackIR", true);
            int openTrackFactoryCalls = 0;
            IAlpineHeadTrackingProvider selected = AlpineHeadTrackingSystem.SelectAvailableProvider(
                AlpineHeadTrackingSource.Auto,
                () => trackIr,
                () => { openTrackFactoryCalls++; return new MockTrackingProvider("OpenTrack", true); });
            Require(ReferenceEquals(selected, trackIr) && openTrackFactoryCalls == 0,
                "tracking-auto-priority");
            selected.Dispose();

            var unavailableTrackIr = new MockTrackingProvider("TrackIR", false);
            var openTrack = new MockTrackingProvider("OpenTrack", true);
            selected = AlpineHeadTrackingSystem.SelectAvailableProvider(
                AlpineHeadTrackingSource.Auto,
                () => unavailableTrackIr,
                () => openTrack);
            Require(unavailableTrackIr.Disposed && ReferenceEquals(selected, openTrack),
                "tracking-auto-fallback");
            selected.Dispose();

            int trackIrFactoryCalls = 0;
            selected = AlpineHeadTrackingSystem.SelectAvailableProvider(
                AlpineHeadTrackingSource.OpenTrack,
                () => { trackIrFactoryCalls++; return new MockTrackingProvider("TrackIR", true); },
                () => new MockTrackingProvider("OpenTrack", false));
            Require(selected == null && trackIrFactoryCalls == 0, "tracking-explicit-source");
            Require(AlpineHeadTrackingSystem.SelectAvailableProvider(
                    AlpineHeadTrackingSource.TrackIr,
                    () => throw new DllNotFoundException(),
                    () => new MockTrackingProvider("OpenTrack", true)) == null,
                "tracking-missing-library-safe");

            AlpineHeadPose clamped = AlpineHeadTrackingSystem.ClampRelativePose(
                new AlpineHeadPose
                {
                    positionMeters = new UnityEngine.Vector3(1f, -1f, 0.5f),
                    rotationDegrees = new UnityEngine.Vector3(90f, -100f, 50f)
                },
                new AlpineHeadPose());
            Require(Approximately(clamped.positionMeters.x, 0.20d, 1e-6) &&
                    Approximately(clamped.positionMeters.y, -0.20d, 1e-6) &&
                    Approximately(clamped.positionMeters.z, 0.20d, 1e-6),
                "tracking-position-clamp");
            Require(Approximately(clamped.rotationDegrees.x, 45d, 1e-6) &&
                    Approximately(clamped.rotationDegrees.y, -60d, 1e-6) &&
                    Approximately(clamped.rotationDegrees.z, 30d, 1e-6),
                "tracking-rotation-clamp");

            var filter = new AlpineHeadTrackingPoseFilter();
            var trackingSample = new MockTrackingProvider("Mock", true)
            {
                NextPose = new AlpineHeadPose
                {
                    positionMeters = new UnityEngine.Vector3(0.05f, 0f, 0f),
                    rotationDegrees = new UnityEngine.Vector3(0f, 5f, 0f)
                }
            };
            Require(trackingSample.TryRead(out AlpineHeadPose firstPose), "tracking-mock-first-pose");
            AlpineHeadPose firstFiltered = filter.Update(firstPose, 1f);
            Require(filter.HasPose && Approximately(firstFiltered.positionMeters.x, 0d, 1e-6),
                "tracking-first-pose-centers");
            trackingSample.NextPose = new AlpineHeadPose
            {
                positionMeters = new UnityEngine.Vector3(0.15f, 0f, 0f),
                rotationDegrees = new UnityEngine.Vector3(0f, 15f, 0f)
            };
            trackingSample.TryRead(out AlpineHeadPose movedPose);
            AlpineHeadPose moved = filter.Update(movedPose, 1f);
            Require(Approximately(moved.positionMeters.x, 0.10d, 1e-6) &&
                    Approximately(moved.rotationDegrees.y, 10d, 1e-6),
                "tracking-relative-pose");
            filter.Recenter();
            AlpineHeadPose recentered = filter.Update(movedPose, 1f);
            Require(filter.HasPose && Approximately(recentered.positionMeters.x, 0d, 1e-6) &&
                    Approximately(recentered.rotationDegrees.y, 0d, 1e-6),
                "tracking-recenter");

            var cameraPose = new AlpineCameraPoseState();
            var nativePosition = new UnityEngine.Vector3(1f, 2f, 3f);
            cameraPose.Capture(nativePosition, UnityEngine.Quaternion.identity);
            UnityEngine.Vector3 appliedPosition = cameraPose.ApplyPosition(
                new UnityEngine.Vector3(0.1f, -0.1f, 0.2f));
            Require(Approximately(appliedPosition.x, 1.1d, 1e-6) && cameraPose.IsCaptured,
                "tracking-camera-additive-pose");
            Require(cameraPose.TryRestore(out UnityEngine.Vector3 restoredPosition,
                        out UnityEngine.Quaternion restoredRotation) &&
                    Approximately(restoredPosition.x, nativePosition.x, 1e-6) &&
                    Approximately(restoredPosition.y, nativePosition.y, 1e-6) &&
                    Approximately(restoredPosition.z, nativePosition.z, 1e-6) &&
                    Approximately(restoredRotation.w, 1d, 1e-6) &&
                    !cameraPose.IsCaptured,
                "tracking-camera-restoration");
        }

        private static void TestBacklogV5RuntimeContracts()
        {
            var catalog = new PartCatalog();
            TunePart compact = catalog.Find("nitrous.compact.5lb");
            TunePart race = catalog.Find("nitrous.race.10lb");
            TunePart drag = catalog.Find("nitrous.drag.20lb");
            Require(compact != null && race != null && drag != null &&
                    Approximately(compact.effect.nitrousCapacitySeconds, 6d, 0d) &&
                    Approximately(race.effect.nitrousCapacitySeconds, 12d, 0d) &&
                    Approximately(drag.effect.nitrousCapacitySeconds, 24d, 0d) &&
                    Approximately(compact.effect.weightOffset, 5d, 0d) &&
                    Approximately(race.effect.weightOffset, 8d, 0d) &&
                    Approximately(drag.effect.weightOffset, 14d, 0d),
                "nitrous-kit-contracts");
            Require(Approximately(AlpineNitrousSystem.ComputePowerMultiplier(25f), 1.25d, 1e-6) &&
                    Approximately(AlpineNitrousSystem.ComputePowerMultiplier(100f), 2d, 1e-6) &&
                    Approximately(AlpineNitrousSystem.ComputePowerMultiplier(200f), 3d, 1e-6) &&
                    Approximately(AlpineNitrousSystem.ComputeConsumptionScale(200f), 2d, 1e-6),
                "nitrous-boost-consumption-contracts");

            var fine = new FineTuneSettings { nitrousBoostPercent = 0f };
            AlpineTuneMath.ClampFineTune(fine);
            Require(Approximately(fine.nitrousBoostPercent, 100d, 0d), "nitrous-schema4-default");
            fine.nitrousBoostPercent = 900f;
            AlpineTuneMath.ClampFineTune(fine);
            Require(Approximately(fine.nitrousBoostPercent, 200d, 0d), "nitrous-upper-bound");

            var settings = new AlpineUserSettings
            {
                nitrousWotThreshold = 2f,
                experimentalPropMassKg = 5000f,
                headTracking = new HeadTrackingSettings
                {
                    smoothingResponse = 100f,
                    motionCurve = 0.1f,
                    translationClampMeters = new Vec3Data(5f, 5f, 5f)
                }
            };
            settings.Normalize();
            Require(Approximately(settings.nitrousWotThreshold, 1d, 0d) &&
                    Approximately(settings.experimentalPropMassKg, 1000d, 0d) &&
                    Approximately(settings.headTracking.smoothingResponse, 40d, 0d) &&
                    Approximately(settings.headTracking.motionCurve, 0.5d, 0d) &&
                    Approximately(settings.headTracking.translationClampMeters.x, 0.5d, 0d) &&
                    !settings.experimentalTrackCompatibility && !settings.experimentalHiddenVehicles &&
                    !settings.experimentalPropVehicles && !settings.experimentalWalking,
                "schema5-settings-normalization");

            var buildSpec = new SledBuildSpec
            {
                propScale = new Vec3Data(99f, -2f, 0f),
                selections = new List<SledForgePartSelection>
                {
                    new SledForgePartSelection { slot = SledForgeSlot.Hood, donorSledKey = "donor-a" },
                    new SledForgePartSelection { slot = SledForgeSlot.Hood, donorSledKey = "donor-b" }
                }
            };
            buildSpec.Normalize();
            Require(buildSpec.selections.Count == 1 && buildSpec.selections[0].donorSledKey == "donor-a" &&
                    Approximately(buildSpec.propScale.x, 5d, 1e-6) && Approximately(buildSpec.propScale.y, 0.1d, 1e-6),
                "sled-forge-build-spec-normalization");

            var calibrated = new HeadTrackingSettings
            {
                translationSensitivity = new Vec3Data(2f, 1f, 1f),
                translationDeadzoneMeters = new Vec3Data(0.01f, 0f, 0f),
                translationClampMeters = new Vec3Data(0.20f, 0.20f, 0.20f),
                rotationSensitivity = new Vec3Data(1f, 2f, 1f),
                rotationDeadzoneDegrees = new Vec3Data(0f, 1f, 0f),
                rotationClampDegrees = new Vec3Data(45f, 60f, 30f),
                invertTranslationX = true,
                invertYaw = true
            };
            AlpineHeadPose transformed = AlpineHeadTrackingSystem.TransformRelativePose(
                new AlpineHeadPose
                {
                    positionMeters = new UnityEngine.Vector3(0.06f, 0f, 0f),
                    rotationDegrees = new UnityEngine.Vector3(0f, 11f, 0f)
                }, new AlpineHeadPose(), calibrated);
            Require(transformed.positionMeters.x < -0.09f && transformed.positionMeters.x > -0.11f &&
                    transformed.rotationDegrees.y < -19f && transformed.rotationDegrees.y > -21f,
                "tracking-axis-tuning");

            Type assembly = typeof(AlpineTuneMath).Assembly.GetType("AlpineTuning.VisualProjectionCoordinator", false);
            Require(assembly != null &&
                    typeof(AlpineTuneMath).Assembly.GetType("AlpineTuning.VisualProjectionContext", false) != null &&
                    typeof(AlpineTuneMath).Assembly.GetType("AlpineTuning.TrackAssemblyRecipe", false) != null &&
                    typeof(AlpineTuneMath).Assembly.GetType("AlpineTuning.ProjectionSnapshot", false) != null,
                "visual-projection-models");

            string main = ReadRepoText("SleddersTuner/ModMain.cs");
            string input = ReadRepoText("SleddersTuner/AlpineControllerInput.cs");
            string headlight = ReadRepoText("SleddersTuner/AlpineHeadlightDeleteProjection.cs");
            string experimental = ReadRepoText("SleddersTuner/AlpineExperimentalSystems.cs");
            string nativeUi = ReadRepoText("SleddersTuner/AlpineNativeUi.cs");
            string fuel = ReadRepoText("SleddersTuner/AlpineFuelSystem.cs");
            string nitrous = ReadRepoText("SleddersTuner/AlpineNitrousSystem.cs");
            string forge = ReadRepoText("SleddersTuner/AlpineSledForgeSystem.cs");
            string sharing = ReadRepoText("SleddersTuner/AlpinePeerSharing.cs");
            string transport = ReadRepoText("SleddersTuner/AlpineSleddersTransport.cs");
            string bindings = ReadRepoText("SleddersTuner/SleddersGameBindings.cs");
            Require(main.Contains("public static Exception Finalizer") &&
                    main.Contains("PatchNitrousStation") && main.Contains("PatchRespawnableRespawn") &&
                    main.Contains("PatchGamePositionChange") && main.Contains("TargetMethods()") &&
                    !main.Contains("[HarmonyPatch(typeof(Controller), \"OnRespawned\")]"),
                "runtime-lifecycle-finalizer-contracts");
            Require(input.Contains("BindingHeld") && input.Contains("BindableButtons") &&
                    input.Contains("PhysicalControllers") && input.Contains("buttonEast"),
                "physical-controller-adapter-contracts");
            Require(headlight.Contains("_Cull") && headlight.Contains("_CullMode") &&
                    headlight.Contains("_RenderFace") && headlight.Contains("0.85f") &&
                    headlight.Contains("0.4f"), "headlight-double-sided-metal-contracts");
            Require(experimental.Contains("RestoreHiddenVehicles") &&
                    experimental.Contains("RestorePropProjection") && experimental.Contains("CanRemount") &&
                    experimental.Contains("CharacterController"), "experimental-rollback-contracts");
            Require(fuel.Contains("model:") && fuel.Contains("LegacyRideIdentity") &&
                    fuel.Contains("Fuel belongs to the named sled model"),
                "model-level-fuel-persistence-contract");
            Require(nitrous.Contains("DefaultKeyboardKey") && nitrous.Contains("isEngineOn") &&
                    nitrous.Contains("property-only lookup left every nitrous request inactive"),
                "nitrous-native-engine-state-contract");
            Require(nitrous.Contains("UpdateFieldRefill") && nitrous.Contains("KeyCode.N") &&
                    nitrous.Contains("Park and switch off the engine"),
                "nitrous-stationless-refill-contract");
            Require(experimental.Contains("baseBoundsMaxExtent < 80f") &&
                    experimental.Contains("GetComponentsInChildren<Collider>") &&
                    nativeUi.Contains("PROP PAGE") && nativeUi.Contains("CompactPropName"),
                "expanded-paged-prop-catalog-contract");
            Require(experimental.Contains("BindSledPropMotionGroups") && forge.Contains("SlotUsesNativePhysics") &&
                    forge.Contains("DrawShowcaseTags") && forge.Contains("Restore(Projection"),
                "sled-forge-articulated-rollback-contract");
            Require(sharing.Contains("BuildSyncProtocolVersion") && sharing.Contains("PrepareBuildMessage") &&
                    sharing.Contains("incompatible game build"),
                "networked-build-capability-contract");
            Require(bindings.Contains("bool TryGetNetClient(out object netClient") &&
                    transport.Contains("SendJsonFromClient") &&
                    transport.Contains("client-to-host") &&
                    transport.Contains("if (serverSide && message != null && transportSenderId != 0)"),
                "internal-client-relay-and-canonical-sender-contract");
            Require(nativeUi.Contains("string.Equals(category, \"performance\"") &&
                    nativeUi.Contains("\"Engine Swap\", DonorDisplayName") &&
                    nativeUi.Contains("Setups & Stock Resets") &&
                    nativeUi.Contains("HEAD TRACKING\", \"Provider, six-axis calibration") &&
                    nativeUi.Contains("fine.nitrousBoostPercent != 100f"),
                "five-domain-transfer-contracts");

            Type nativeUiType = typeof(AlpineTuneMath).Assembly.GetType("AlpineTuning.AlpineNativeUi", true);
            MethodInfo categoriesForSection = nativeUiType.GetMethod(
                "PartCategoriesForGarageSection", BindingFlags.Static | BindingFlags.NonPublic);
            var transferredCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string section in new[] { "performance", "chassis-handling", "lighting", "utility" })
            {
                object result = categoriesForSection.Invoke(null, new object[] { section });
                foreach (string category in (System.Collections.IEnumerable)result)
                    transferredCategories.Add(category);
            }
            Require(new HashSet<string>(PartCatalog.OrderedCategories, StringComparer.OrdinalIgnoreCase)
                    .SetEquals(transferredCategories),
                "five-domain-complete-part-transfer");
        }

        private static void TestNativeTransportIsolation()
        {
            string directory = Path.Combine(_tuneTestRoot, "native-transport-runtime");
            Directory.CreateDirectory(directory);
            string core = Path.Combine(directory, "UnityEngine.CoreModule.dll");
            // Unity's memory intrinsics cannot run in the CLR test runner. Only
            // this isolated test copy receives equivalent managed operations;
            // the game's dispatcher, reader, and release mod remain original.
            using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(
                Path.Combine(Path.GetDirectoryName(_gameAssembly), "UnityEngine.CoreModule.dll")))
            {
                var memory = assembly.MainModule.GetType("Unity.Collections.LowLevel.Unsafe.UnsafeUtility");
                foreach (string name in new[] { "MemCpy", "MemClear" })
                {
                    var method = memory.Methods.Single(candidate => candidate.Name == name);
                    method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL;
                    method.Body = new Mono.Cecil.Cil.MethodBody(method);
                    var il = method.Body.GetILProcessor();
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                    if (name == "MemCpy")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Cpblk);
                    }
                    else
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initblk);
                    }
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                }
                var time = assembly.MainModule.GetType("UnityEngine.Time").Methods
                    .Single(method => method.Name == "get_unscaledTime");
                time.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL;
                time.Body = new Mono.Cecil.Cil.MethodBody(time);
                time.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, 0f);
                time.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
                foreach (var log in assembly.MainModule.GetType("UnityEngine.Debug").Methods
                    .Where(method => (method.Name == "LogError" || method.Name == "LogException") &&
                        method.Parameters.Count == 1))
                {
                    log.Body = new Mono.Cecil.Cil.MethodBody(log);
                    log.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
                }
                assembly.Write(core);
            }

            var domain = AppDomain.CreateDomain("Alpine transport isolation", null,
                new AppDomainSetup { ApplicationBase = Path.GetDirectoryName(typeof(Program).Assembly.Location) });
            try
            {
                var runner = (NativeTransportRegressionDomain)domain.CreateInstanceFromAndUnwrap(
                    typeof(Program).Assembly.Location, typeof(NativeTransportRegressionDomain).FullName);
                string failure = runner.Run(core, _gameAssembly, _releaseAssembly);
                Require(failure == null, failure ?? "native-transport-isolation");
            }
            finally
            {
                AppDomain.Unload(domain);
                File.Delete(core);
                Directory.Delete(directory);
            }
        }

        internal static void RunNativeTransportRegression()
        {
            const ulong sender = 123;
            var native = new NativePacketDispatcher();
            byte[] legacy = MakeInternalPacket(Encoding.UTF8.GetBytes("{}"));
            // This is the actual dispatcher, with harmless sentinels replacing
            // game effects. Native ping 80 reads one byte, exposing id 50 and
            // checkpoint-race kind 1 in the old unnegotiated ALP2 header.
            native.Dispatch(sender, legacy);
            Require(native.PingCount == 1 && native.GameRulesCount == 1 && native.LastGameType == 1,
                "native-dispatcher-reproduces-alp2-game-rules-leak");

            native = new NativePacketDispatcher();
            for (int i = 0; i < 512; i++)
                native.Dispatch(sender, new[] { AlpineConstants.SleddersInternalMessageId });
            Require(native.PingCount == 0 && native.GameRulesCount == 0,
                "repeated-unhandled-capability-probes-never-reach-native-commands");

            foreach (bool serverSide in new[] { false, true })
            {
                int delivered = 0;
                string received = null;
                ulong receivedSender = 0;
                var transport = new AlpineSleddersTransport((peer, json, side) =>
                {
                    delivered++;
                    received = json;
                    receivedSender = peer;
                });
                var dispatcher = new NativePacketDispatcher();
                MethodInfo callback = typeof(AlpineSleddersTransport).GetMethod(
                    serverSide ? "OnServerMessage" : "OnClientMessage", BindingFlags.Instance | BindingFlags.NonPublic);
                dispatcher.LIOJOEAICOP(AlpineConstants.SleddersInternalMessageId,
                    (HDIGLPKCIDC.BJIFGFGGNFP)Delegate.CreateDelegate(typeof(HDIGLPKCIDC.BJIFGFGGNFP), transport, callback));

                // An unsolicited complete packet must neither establish
                // capability nor leak its body to the native command loop.
                dispatcher.Dispatch(sender, legacy);
                Require(delivered == 0 && dispatcher.GameRulesCount == 0 && dispatcher.PingCount == 0,
                    "unconfirmed-internal-frame-is-contained-" + serverSide);
                if (serverSide)
                    ((HashSet<ulong>)typeof(AlpineSleddersTransport).GetField("_capableClients",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transport)).Add(sender);
                else
                    typeof(AlpineSleddersTransport).GetField("_hostCapabilityConfirmed",
                        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(transport, true);

                byte[] valid = MakeInternalPacket(Encoding.UTF8.GetBytes("{\"senderSleddersClientId\":999}"));
                var invalid = new List<byte[]>();
                for (int length = 2; length < 22; length++)
                    invalid.Add(new[] { AlpineConstants.SleddersInternalMessageId }
                        .Concat(Enumerable.Repeat((byte)50, length - 1)).ToArray());
                byte[] wrongMagic = (byte[])valid.Clone();
                wrongMagic[1] ^= 1;
                invalid.Add(wrongMagic);
                foreach (int offset in new[] { 5, 12, 14, 18 })
                {
                    byte[] packet = (byte[])valid.Clone();
                    packet[offset] = 255; // Invalid kind, count, total, or payload length.
                    invalid.Add(packet);
                }
                invalid.Add(valid.Concat(new byte[] { 50, 1, 80, 0 }).ToArray());
                foreach (byte[] packet in invalid)
                {
                    dispatcher.Dispatch(sender, packet);
                    Require(dispatcher.GameRulesCount == 0 && dispatcher.PingCount == 0,
                        "rejected-internal-body-never-reaches-native-command-" + serverSide);
                }
                Require(delivered == 0, "malformed-internal-frames-not-delivered-" + serverSide);

                dispatcher.Dispatch(sender, valid);
                Require(delivered == 1 && receivedSender == (serverSide ? sender : 999) &&
                        received.Contains("senderSleddersClientId"), "valid-internal-frame-delivery-" + serverSide);
                byte[] jsonBytes = Encoding.UTF8.GetBytes("{\"senderSleddersClientId\":999}");
                int split = jsonBytes.Length / 2;
                dispatcher.Dispatch(sender, MakeInternalPacket(jsonBytes.Skip(split).ToArray(), 2, 7, 1, 2, jsonBytes.Length));
                Require(delivered == 1, "incomplete-chunk-waits-" + serverSide);
                dispatcher.Dispatch(sender, MakeInternalPacket(jsonBytes.Take(split).ToArray(), 2, 7, 0, 2, jsonBytes.Length));
                Require(delivered == 2 && dispatcher.GameRulesCount == 0 && dispatcher.PingCount == 0,
                    "out-of-order-chunks-deliver-without-native-commands-" + serverSide);

                transport.Shutdown();
                Require(!transport.CanSend, "shutdown-blocks-internal-sends-" + serverSide);
                dispatcher.Dispatch(sender, legacy);
                dispatcher.Dispatch(sender, new[] { AlpineConstants.SleddersInternalMessageId });
                Require(delivered == 2 && dispatcher.GameRulesCount == 0 && dispatcher.PingCount == 0,
                    "shutdown-keeps-pending-packets-contained-" + serverSide);

                dispatcher.Dispatch(sender, new byte[] { 80, 7, 50, 1 });
                Require(dispatcher.PingCount == 1 && dispatcher.GameRulesCount == 1 && dispatcher.LastGameType == 1,
                    "intentional-native-ping-and-game-rules-still-dispatch-" + serverSide);
            }
        }

        private static byte[] MakeInternalPacket(byte[] payload, byte kind = 1, uint sequence = 1,
            ushort index = 0, ushort count = 1, int total = 0)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(AlpineConstants.SleddersInternalMessageId);
                writer.Write(0x324C5041u);
                writer.Write(kind);
                writer.Write(sequence);
                writer.Write(index);
                writer.Write(count);
                writer.Write(total == 0 ? payload.Length : total);
                writer.Write(payload.Length);
                writer.Write(payload);
                return stream.ToArray();
            }
        }

        private sealed class NativePacketDispatcher : HDIGLPKCIDC
        {
            internal int PingCount;
            internal int GameRulesCount;
            internal byte LastGameType;

            internal NativePacketDispatcher()
            {
                var unknown = (HashSet<byte>)typeof(HDIGLPKCIDC).GetField("OBJPEOCEMPN",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
                unknown.Add(AlpineConstants.SleddersInternalMessageId);
                unknown.Add(65);
                LIOJOEAICOP(80, (ulong peer, ref Unity.Collections.DataStreamReader reader) =>
                {
                    PingCount++;
                    reader.ReadByte();
                });
                LIOJOEAICOP(50, (ulong peer, ref Unity.Collections.DataStreamReader reader) =>
                {
                    GameRulesCount++;
                    LastGameType = reader.ReadByte();
                    reader.SeekSet(reader.Length);
                });
            }

            internal void Dispatch(ulong sender, byte[] packet)
            {
                IntPtr buffer = Marshal.AllocHGlobal(packet.Length);
                try
                {
                    Marshal.Copy(packet, 0, buffer, packet.Length);
                    object boxed = default(Unity.Collections.DataStreamReader);
                    Type type = boxed.GetType();
                    type.GetField("m_BufferPtr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxed, buffer);
                    type.GetField("m_Length", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxed, packet.Length);
                    var reader = (Unity.Collections.DataStreamReader)boxed;
                    CCILIGIBDPH(sender, ref reader);
                    Require(reader.GetBytesRead() == packet.Length, "native-dispatch-consumes-complete-datagram");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
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
            foreach (string fieldName in new[]
            {
                "assetReference", "prefabName", "lengthName", "lenghtIndex",
                "trackPositionOverride", "trackScaleOverride", "traxTransform", "group", "category"
            })
                RequireNativeField(vehicle, fieldName, "native-chassis-metadata-field");
            Require(FindNativeField(vehicle, "assetReference").FieldType.FullName ==
                    "UnityEngine.AddressableAssets.AssetReferenceGameObject",
                "native-chassis-addressable-type");
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
                "drivetrainMaxSpeed2", "trackMass", "breakForce", "power"
            })
                RequireNativeFloatField(mesh, fieldName, "native-drivetrain-field");
            RequireNativeField(mesh, "trackMesh", "native-track-contact-mesh-field");

            Type controllerBase = RequireNativeType(game, "SnowmobileControllerBase");
            foreach (string fieldName in new[] { "skisMaxAngle", "toeAngle" })
                RequireNativeFloatField(controllerBase, fieldName, "native-steering-field");
            RequireNativeField(controllerBase, "leftSki", "native-left-ski-field");
            RequireNativeField(controllerBase, "rightSki", "native-right-ski-field");
            RequireNativeField(controllerBase, "track", "native-track-contact-root-field");
            RequireNativeField(controllerBase, "meshInterpretter", "native-mesh-interpreter-field");

            Type netClient = RequireNativeType(game, "NetClient");
            Type netServer = RequireNativeType(game, "NetServer");
            Type delivery = RequireNativeType(game, "BIMHPJPECDH");
            Require(delivery.IsEnum && Enum.GetNames(delivery).Contains("ReliableFragmentedSequenced"),
                "native-internal-delivery-contract");
            foreach (Type transportEndpoint in new[] { netClient, netServer })
            {
                MethodInfo writerFactory = transportEndpoint.GetMethod(
                    "MDNBFANMMHH", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                MethodInfo sender = transportEndpoint.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                    {
                        if (method.Name != "PIDJHAOLBJM")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 && parameters[0].ParameterType == typeof(ulong) &&
                               parameters[1].ParameterType == delivery &&
                               parameters[2].ParameterType.FullName == "Unity.Collections.DataStreamWriter";
                    });
                Require(writerFactory != null && writerFactory.ReturnType.FullName == "Unity.Collections.DataStreamWriter" &&
                        sender != null,
                    "native-internal-" + transportEndpoint.Name.ToLowerInvariant() + "-send-contract");
            }
            Type ski = RequireNativeType(game, "Ski2");
            RequireNativeFloatField(ski, "camberFactor", "native-camber-field");

            Type structure = typeof(SnowmobileStructure);
            foreach (string fieldName in new[]
            {
                "trackRenderer", "trackMeshes", "otherTrackObjects", "traxGroup", "traxBody",
                "tunnelParts", "railParts"
            })
                RequireNativeField(structure, fieldName, "native-rear-assembly-field");
            MethodInfo structureInit = structure.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic)
                .SingleOrDefault(method => method.Name == "Init");
            Require(structureInit != null, "native-structure-init-signature");

            Type rearAxel = RequireNativeType(game, "RearAxelController");
            Type suspension = RequireNativeType(game, "SuspensionController");
            Require(rearAxel != null && suspension.GetFields(BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic).Length > 0,
                "native-suspension-rear-axel-cache");

            Type preview = RequireNativeType(game, "SnowmobilePreviewHelper");
            Require(preview.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(method => method.Name == "RefreshSnowmobilePreview"),
                "native-preview-refresh-signature");
            Require(FindNativeField(preview, "vehiclePreviewRoot") != null,
                "native-preview-root-field");

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

            Type station = typeof(FuelStation);
            foreach (string fieldName in new[]
            {
                "refuelInputHoldTime", "refuelTickCooldown", "refuelTickFuelLiters"
            })
                RequireNativeFloatField(station, fieldName, "native-station-timing-field");
            foreach (string fieldName in new[] { "refuelStartEvent", "refuelTickEvent", "refuelStopEvent" })
                RequireNativeField(station, fieldName, "native-station-audio-field");
            Require(station.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(method => method.Name == "UpdateRefuelInput" && method.ReturnType == typeof(bool)),
                "native-station-input-signature");
            Require(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(method => method.Name == "SetEngineOnOff" && method.GetParameters().Length == 1 &&
                                   method.GetParameters()[0].ParameterType == typeof(bool)),
                "native-engine-shutoff-signature");
        }

        private static Type RequireNativeType(Assembly assembly, string name)
        {
            Type type;
            try
            {
                type = assembly != null ? assembly.GetType(name, false, false) : null;
            }
            catch (Exception ex)
            {
                throw new ReleaseTestException("native-type-load-" + name + "-" + ex.GetType().Name);
            }
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
            Require(aliases.Count >= 8, "garage-icon-alias-count");
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

            Require(!PartCatalog.OrderedCategories.Contains("accessories") &&
                    !catalog.Parts.Any(part => string.Equals(part.category, "accessories", StringComparison.OrdinalIgnoreCase)),
                "native-cosmetics-not-an-alpine-category");
            Require(catalog.Find("engine.stage1")?.requiresReload == true,
                "spawn-effect-reload-derived");
            Require(catalog.Find("clutch.trail")?.requiresReload == false,
                "runtime-controller-effect-no-reload");
            foreach (float target in new[] { 6000f, 6500f, 7000f })
            {
                TunePart racing = catalog.Find("clutch.race." + target.ToString("0", CultureInfo.InvariantCulture));
                Require(racing != null && !racing.requiresReload &&
                        Approximately(racing.effect.clutchEngagementTargetRpm, target, 0d),
                    "catalog-racing-clutch-target");
            }
            Require(Approximately(catalog.Find("chassis.runningboards.light")?.effect.weightOffset ?? 0f, -10d, 0d) &&
                    Approximately(catalog.Find("chassis.cooling.light")?.effect.weightOffset ?? 0f, -3d, 0d),
                "catalog-chassis-weight-savings");
            TunePart headlightDelete = catalog.Find("light.delete.carbon");
            Require(headlightDelete?.effect.headlightDelete == true &&
                    Approximately(headlightDelete.effect.weightOffset, -2d, 0d),
                "catalog-headlight-delete");
            TunePart detectedTrack = catalog.RegisterDetectedTrackLength(
                "fixture.154", "154\" Fixture Track", 154f, 1.05f, 1.03f, 1.08f);
            Require(detectedTrack != null && detectedTrack.category == PartCatalog.Track &&
                    detectedTrack.effect.visualTrackVariantId == "fixture.154" &&
                    Approximately(detectedTrack.effect.nativeTrackMassMultiplier, 1.05d, 1e-7) &&
                    Approximately(detectedTrack.effect.frictionMultiplier, 1.03d, 1e-7) &&
                    Approximately(detectedTrack.effect.nativeTrackGripMultiplier, 1.08d, 1e-7),
                "catalog-detected-track-profile");
            var legacyTrackProfile = new TuneProfile
            {
                selectedParts = new List<PartSelection>
                {
                    new PartSelection
                    {
                        category = PartCatalog.Track,
                        partId = "track.length.old-vehicle-id.154"
                    }
                }
            };
            catalog.EnsureProfileSelections(legacyTrackProfile);
            Require(legacyTrackProfile.GetPartId(PartCatalog.Track) == detectedTrack.id,
                "catalog-detected-track-length-migration");
            TunePart preservedRmkTrack = catalog.RegisterDetectedTrackLength(
                "rmk.155", "155\" Polaris Matryx Native Chassis", 155f, 1f, 1f, 1f);
            Require(preservedRmkTrack?.id == "track.length.rmk.155",
                "catalog-rmk-track-id-preserved");
            TunePart metricTrack = catalog.RegisterDetectedTrackLength(
                "lynx-radien.3900", "3900 mm (153.5\") Lynx Radien Native Chassis",
                3900f / 25.4f, 1f, 1f, 1f);
            var legacyMetricTrackProfile = new TuneProfile
            {
                selectedParts = new List<PartSelection>
                {
                    new PartSelection
                    {
                        category = PartCatalog.Track,
                        partId = "track.length.old-lynx-donor.3900"
                    }
                }
            };
            catalog.EnsureProfileSelections(legacyMetricTrackProfile);
            Require(legacyMetricTrackProfile.GetPartId(PartCatalog.Track) == metricTrack.id,
                "catalog-metric-track-length-migration");
            var scopedMigrationCatalog = new PartCatalog();
            scopedMigrationCatalog.RegisterDetectedTrackLength(
                "rmk.146", "146\" Polaris Matryx", 146f, 1f, 1f, 1f);
            scopedMigrationCatalog.RegisterDetectedTrackLength(
                "g5.146", "146\" Ski-Doo G5", 146f, 1f, 1f, 1f);
            var ambiguousTrackProfile = new TuneProfile
            {
                selectedParts = new List<PartSelection>
                {
                    new PartSelection
                    {
                        category = PartCatalog.Track,
                        partId = "track.length.old-donor.146"
                    }
                }
            };
            scopedMigrationCatalog.EnsureProfileSelections(ambiguousTrackProfile);
            Require(ambiguousTrackProfile.GetPartId(PartCatalog.Track) ==
                        "track.length.old-donor.146",
                "catalog-ambiguous-track-migration-preserved");
            scopedMigrationCatalog.ClearDetectedTrackLengths();
            TunePart compatibleG5Track = scopedMigrationCatalog.RegisterDetectedTrackLength(
                "g5.146", "146\" Ski-Doo G5", 146f, 1f, 1f, 1f);
            scopedMigrationCatalog.EnsureProfileSelections(ambiguousTrackProfile);
            Require(ambiguousTrackProfile.GetPartId(PartCatalog.Track) == compatibleG5Track.id,
                "catalog-compatible-platform-track-migration");
            Require(AlpineControllerInput.FormatBinding("rewired|fixture-pad|12|Left%20Paddle") == "Left Paddle",
                "controller-binding-display");
            Require(AlpineControllerInput.TryMigrateRewiredBinding(
                        "rewired|fixture-pad|2|Square", out string migratedSquare) &&
                    migratedSquare == "inputsystem:v1:<Gamepad>/buttonWest",
                "controller-binding-rewired-migration");
            Require(!AlpineControllerInput.TryMigrateRewiredBinding(
                        "rewired|fixture-pad|1|Circle", out _),
                "controller-binding-cancel-not-migrated");
            Require(AlpineControllerInput.IsInputSystemBinding(
                        "inputsystem:v1:<Gamepad>/rightShoulder"),
                "controller-binding-input-system-format");
            var platformAliases = new[]
            {
                new[] { "rmk", "Indy VR1 137.prefab" },
                new[] { "rmk", "Switchback Assault 146.prefab" },
                new[] { "rmk", "RMK Khaos 155.prefab" },
                new[] { "rmk", "PRO RMK 165.prefab" },
                new[] { "g5", "MXZ X-RS 136.prefab" },
                new[] { "g5", "Backcountry X-RS 146.prefab" },
                new[] { "g5", "Freeride 146.prefab" },
                new[] { "g5", "Summit 154.prefab" },
                new[] { "g5", "Summit 165.prefab" },
                new[] { "g5", "G5 Future 154.prefab" },
                new[] { "arctic-cat", "ArticCat 146.prefab" },
                new[] { "arctic-cat", "ArticCat 154.prefab" },
                new[] { "arctic-cat", "Arctic Cat 165.prefab" },
                new[] { "arctic-cat", "Pollux Future 154.prefab" },
                new[] { "lynx-radien", "Lynx Rave 3500.prefab" },
                new[] { "lynx-radien", "Shredder RE 3700.prefab" },
                new[] { "lynx-radien", "Shredder RE 3900.prefab" },
                new[] { "lynx-radien", "Shredder DS 4100.prefab" },
                new[] { "lynx-radien", "Brutal RE 3900.prefab" }
            };
            Require(platformAliases.All(alias =>
                    AlpineVisualPartSystem.ResolvePlatformFamilyMetadata(
                        null, alias[1], null, null)?.key == alias[0]),
                "native-chassis-platform-aliases");
            Require(AlpineVisualPartSystem.ResolveFamilyMetadata(
                        "Polaris Mountain", "RMK Khaos 155.prefab", "Khaos", "Khaos") == "rmk",
                "native-chassis-rmk-profile-key-preserved");
            Require(AlpineVisualPartSystem.ResolveFamilyMetadata(
                        null, "Future Trail 137.prefab", null, null) == "future-trail" &&
                    AlpineVisualPartSystem.ResolveFamilyMetadata(
                        null, "Future Trail 165.prefab", null, null) == "future-trail" &&
                    AlpineVisualPartSystem.ResolveFamilyMetadata(
                        null, "Future Mountain 155.prefab", null, null) == "future-mountain",
                "native-chassis-unknown-platform-isolated");
            Require(AlpineVisualPartSystem.TryDetectLengthMetadata(
                        "155", "RMK Khaos 165.prefab", "Khaos", "Khaos", out float nativeLength) &&
                    Approximately(nativeLength, 155d, 0d),
                "native-chassis-length-metadata-precedence");
            foreach (int length in new[] { 100, 136, 137, 146, 154, 155, 165, 200 })
            {
                Require(AlpineVisualPartSystem.TryDetectLengthMetadata(
                            length.ToString(CultureInfo.InvariantCulture),
                            "Engine 850 " + length.ToString(CultureInfo.InvariantCulture) + ".prefab",
                            null, null, "g5", out AlpineVisualPartSystem.NativeTrackLength imperial) &&
                        imperial.unit == AlpineVisualPartSystem.NativeTrackLengthUnit.Inches &&
                        Approximately(imperial.nativeValue, length, 0d) &&
                        Approximately(imperial.canonicalInches, length, 0d),
                    "native-chassis-imperial-length");
            }
            foreach (int lengthMm in new[] { 3500, 3700, 3900, 4100 })
            {
                Require(AlpineVisualPartSystem.TryDetectLengthMetadata(
                            lengthMm.ToString(CultureInfo.InvariantCulture),
                            "Shredder 850 " + lengthMm.ToString(CultureInfo.InvariantCulture) + ".prefab",
                            null, null, "lynx-radien", out AlpineVisualPartSystem.NativeTrackLength metric) &&
                        metric.unit == AlpineVisualPartSystem.NativeTrackLengthUnit.Millimeters &&
                        Approximately(metric.nativeValue, lengthMm, 0d) &&
                        Approximately(metric.canonicalInches, lengthMm / 25.4d, 1e-4),
                    "native-chassis-metric-length");
            }
            Require(!AlpineVisualPartSystem.TryDetectLengthMetadata(
                        null, "Engine 650 850 900.prefab", null, null, "g5", out _),
                "native-chassis-engine-sizes-rejected");
            Require(!AlpineVisualPartSystem.TryDetectLengthMetadata(
                        null, "Unknown 3900.prefab", null, null, "unknown", out _),
                "native-chassis-metric-platform-gated");
            int sameLineRank = AlpineVisualPartSystem.RankDonorMetadata(
                "summit", "summit", "mountain", "other", false, "Summit 154 Mod 850.prefab");
            int sameGroupRank = AlpineVisualPartSystem.RankDonorMetadata(
                "summit", "freeride", "mountain", "mountain", true, "Freeride 154 Base.prefab");
            Require(sameLineRank > sameGroupRank, "native-chassis-same-line-rank-priority");
            Require(AlpineVisualPartSystem.RankDonorMetadata(
                        "articcat", "articcat", "cat", "cat", true, "ArticCat 154.prefab") >
                    AlpineVisualPartSystem.RankDonorMetadata(
                        "articcat", "articcat", "cat", "cat", true, "ArticCat 154 Mod 850.prefab"),
                "native-chassis-base-over-mod-ranking");

            Type nativeUiType = typeof(AlpineTuneMath).Assembly.GetType(
                "AlpineTuning.AlpineNativeUi", false, false);
            MethodInfo sectionCategories = nativeUiType?.GetMethod(
                "PartCategoriesForGarageSection", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo partTypeIcon = nativeUiType?.GetMethod(
                "GaragePartTypeIconKey", BindingFlags.Static | BindingFlags.NonPublic);
            Require(sectionCategories != null && partTypeIcon != null,
                "garage-category-routing-helpers");
            var routedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string section in new[] { "performance", "chassis-handling", "lighting", "utility" })
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
                Require(fileKeys.Contains(ResolveGarageIconKey(key, aliases)),
                    "garage-runtime-icon-missing:" + key + "->" + ResolveGarageIconKey(key, aliases));

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
            // Kept only for backwards-compatible package identity; accessory
            // selection is no longer a public Alpine category or runtime feature.
            expectedFileKeys.UnionWith(new[]
            {
                "part.accessory.stock", "part.accessory.race_trim", "part.accessory.utility"
            });
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
            value = value?.Trim();

            if (string.IsNullOrWhiteSpace(value) ||
                value.Length < 3 ||
                IntentionalPublicIdentifiers.Contains(value))
            {
                return;
            }

            markers.Add(new PrivacyMarker
            {
                Value = value,
                WholeWord = wholeWord,
                ContextOnly = contextOnly
            });
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
