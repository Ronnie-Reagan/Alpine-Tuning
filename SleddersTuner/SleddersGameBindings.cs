using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpineTuning
{
    internal static class SleddersGameBindings
    {
        public static readonly BindingFlags All =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>();
        private const int AssemblyFingerprintChunkBytes = 65536;

        private static bool _initialized;
        private static AlpineCompatibilityReport _compatibilityReport;
        private static FieldInfo _vehicleIdField;
        private static PropertyInfo _vehicleIdProperty;
        private static FieldInfo _vehicleEngineAudioTypeField;
        private static PropertyInfo _vehicleListSelectableVehiclesProp;
        private static FieldInfo _vehicleListVehiclesField;
        private static PropertyInfo _snowmobileVehicleProp;
        private static FieldInfo _snowmobileVehicleField;
        private static Type _controllerType;
        private static PropertyInfo _controllerInstanceProp;
        private static MethodInfo _reCreateSnowmobileMethod;
        private static MethodInfo _trySpawnPlayerMethod;
        private static Type _engineAudioControllerType;
        private static Type _engineAudioEnumType;
        private static MethodInfo _miSetEngineType;
        private static MethodInfo _miEngineInit;
        private static MethodInfo _miStopEngineSound;
        private static FieldInfo _fiCurrentEngineType;
        private static FieldInfo _fiEngineAudioIsLocal;
        private static FieldInfo _fiEngineAudioIsTurbo;
        private static bool _engineAudioReflectionResolved;
        private static bool _engineAudioReflectionReady;
        private static Type _snowmobileAccessoriesType;
        private static Type _headLightType;
        private static FieldInfo _headLightLightField;
        private static bool _headLightReflectionResolved;
        private static bool _headLightReflectionReady;
        private static Type _meshInterpretterType;
        private static Type _suspensionControllerType;
        private static Type _snowmobileControllerBaseType;
        private static Type _ski2Type;
        private static Type _skiHardSurfaceContactType;
        private static Type _trackHardSurfaceContactType;
        private static Type _hardSurfaceContactBaseType;
        private static bool _nativePhysicsReflectionResolved;
        private static bool _nativePhysicsReflectionReady;
        private static bool _nativeDrivetrainReflectionReady;
        private static bool _nativeSuspensionReflectionReady;
        private static bool _nativeBrakeReflectionReady;
        private static bool _nativeSteeringReflectionReady;
        private static bool _nativeGripReflectionReady;
        private static Type _netClientType;
        private static PropertyInfo _netClientInstanceProp;
        private static PropertyInfo _netClientLocalClientIdProp;
        private static FieldInfo _netClientNetInterfaceField;
        private static MethodInfo _netClientGetIdsMethod;
        private static MethodInfo _netClientGetNickMethod;
        private static Type _netServerType;
        private static PropertyInfo _netServerInstanceProp;
        private static FieldInfo _netServerInterfaceField;
        private static Type _netClientGameplayType;
        private static PropertyInfo _netClientGameplayInstanceProp;
        private static MethodInfo _netClientGameplayGetVehicleMethod;

        public static bool VehicleIdAvailable
        {
            get
            {
                Initialize();
                return _vehicleIdField != null || _vehicleIdProperty != null;
            }
        }

        public static bool VehicleListAvailable
        {
            get
            {
                Initialize();
                return _vehicleListSelectableVehiclesProp != null || _vehicleListVehiclesField != null;
            }
        }

        public static bool SnowmobileVehicleBindingAvailable
        {
            get
            {
                Initialize();
                return _snowmobileVehicleProp != null || _snowmobileVehicleField != null;
            }
        }

        public static bool ReloadBindingAvailable
        {
            get
            {
                Initialize();
                return _controllerType != null &&
                       _controllerInstanceProp != null &&
                       _reCreateSnowmobileMethod != null;
            }
        }

        public static bool EngineAudioAvailable
        {
            get { return ResolveEngineAudioReflection(); }
        }

        public static bool PeerDiscoveryAvailable
        {
            get
            {
                Initialize();
                ResolvePeerDiscoveryBindings();
                return _netClientType != null && _netClientInstanceProp != null;
            }
        }

        public static bool HeadlightRuntimeBindingAvailable
        {
            get { return ResolveHeadlightReflection(); }
        }

        public static bool NativePhysicsRuntimeBindingAvailable
        {
            get { return ResolveNativePhysicsReflection(); }
        }

        public static bool NativeDrivetrainRuntimeBindingAvailable
        {
            get { ResolveNativePhysicsReflection(); return _nativeDrivetrainReflectionReady; }
        }

        public static bool NativeSuspensionRuntimeBindingAvailable
        {
            get { ResolveNativePhysicsReflection(); return _nativeSuspensionReflectionReady; }
        }

        public static bool NativeBrakeRuntimeBindingAvailable
        {
            get { ResolveNativePhysicsReflection(); return _nativeBrakeReflectionReady; }
        }

        public static bool NativeSteeringRuntimeBindingAvailable
        {
            get { ResolveNativePhysicsReflection(); return _nativeSteeringReflectionReady; }
        }

        public static bool NativeSurfaceGripRuntimeBindingAvailable
        {
            get { ResolveNativePhysicsReflection(); return _nativeGripReflectionReady; }
        }

        public static string CapabilitySummary
        {
            get
            {
                var report = GetCompatibilityReport();
                return report != null ? report.SummaryLine : "Compatibility unknown";
            }
        }

        public static AlpineCompatibilityReport GetCompatibilityReport(bool refresh = false)
        {
            if (_compatibilityReport != null && !refresh)
                return _compatibilityReport;

            Initialize();
            ResolveEngineAudioReflection();
            ResolveHeadlightReflection();
            ResolvePeerDiscoveryBindings();
            ResolveAccessoryReflection();
            ResolveNativePhysicsReflection();

            var report = new AlpineCompatibilityReport();
            PopulateAssemblyFingerprint(report);

            AddCapability(
                report,
                "vehicleData",
                "Vehicle Data",
                (_vehicleIdField != null || _vehicleIdProperty != null) &&
                (_vehicleListSelectableVehiclesProp != null || _vehicleListVehiclesField != null),
                true,
                $"vehicleId={Status(_vehicleIdField != null || _vehicleIdProperty != null)} " +
                $"({NameOrNull((MemberInfo)_vehicleIdField ?? _vehicleIdProperty)}), " +
                $"list={Status(_vehicleListSelectableVehiclesProp != null || _vehicleListVehiclesField != null)}");

            AddCapability(
                report,
                "runtimeController",
                "Runtime Controller",
                _snowmobileVehicleProp != null || _snowmobileVehicleField != null,
                true,
                $"vehicle property={NameOrNull(_snowmobileVehicleProp)}, vehicle field={NameOrNull(_snowmobileVehicleField)}");

            bool runtimeControlsReady = RuntimeTuningControlsReady(out var runtimeControlsDetail);
            AddCapability(
                report,
                "runtimeTuning",
                "Runtime Tuning Controls",
                runtimeControlsReady,
                false,
                runtimeControlsDetail);

            AddCapability(
                report,
                "reload",
                "Ride Reload",
                _controllerType != null && _controllerInstanceProp != null && _reCreateSnowmobileMethod != null,
                false,
                $"controller={NameOrNull(_controllerType)}, instance={NameOrNull(_controllerInstanceProp)}, " +
                $"recreate={NameOrNull(_reCreateSnowmobileMethod)}");

            AddCapability(
                report,
                "nativeDrivetrain",
                "Native Drivetrain",
                _nativeDrivetrainReflectionReady,
                false,
                NativeDrivetrainCompatibilityDetail());

            AddCapability(
                report,
                "nativeSuspension",
                "Native Suspension",
                _nativeSuspensionReflectionReady,
                false,
                NativeSuspensionCompatibilityDetail());

            AddCapability(
                report,
                "nativeBrake",
                "Native Brake",
                _nativeBrakeReflectionReady,
                false,
                $"mesh={NameOrNull(_meshInterpretterType)}, breakForce={Status(CountFields(_meshInterpretterType, "breakForce") == 1)}");

            AddCapability(
                report,
                "nativeSteering",
                "Native Steering Geometry",
                _nativeSteeringReflectionReady,
                false,
                NativeSteeringCompatibilityDetail());

            AddCapability(
                report,
                "nativeSurfaceGrip",
                "Native Surface Grip",
                _nativeGripReflectionReady,
                false,
                NativeGripCompatibilityDetail());

            AddCapability(
                report,
                "nativeUi",
                "Native UI",
                typeof(VisualElement) != null && typeof(VehicleSelectionUiController) != null,
                false,
                "garage/pause UI is guarded and verified against live menu instances");

            AddCapability(
                report,
                "accessories",
                "Native Accessories",
                _snowmobileAccessoriesType != null,
                false,
                $"type={NameOrNull(_snowmobileAccessoriesType)}");

            AddCapability(
                report,
                "lights",
                "Runtime Lights",
                _headLightReflectionReady,
                false,
                $"type={NameOrNull(_headLightType)}, lightField={NameOrNull(_headLightLightField)}");

            AddCapability(
                report,
                "engineAudio",
                "Engine Audio",
                _engineAudioReflectionReady,
                false,
                $"controller={NameOrNull(_engineAudioControllerType)}, enum={NameOrNull(_engineAudioEnumType)}");

            AddCapability(
                report,
                "peerDiscovery",
                "Peer Discovery",
                _netClientType != null && _netClientInstanceProp != null,
                false,
                $"netClient={NameOrNull(_netClientType)}, instance={NameOrNull(_netClientInstanceProp)}");

            report.overallStatus = ResolveOverallCompatibility(report);
            _compatibilityReport = report;
            return report;
        }

        public static string EngineAudioStatus
        {
            get
            {
                ResolveEngineAudioReflection();
                if (!_engineAudioReflectionReady)
                    return "engineAudio=unavailable";

                return $"engineAudio={_engineAudioControllerType.FullName}/{_engineAudioEnumType.FullName}";
            }
        }

        private static void AddCapability(
            AlpineCompatibilityReport report,
            string id,
            string label,
            bool ready,
            bool required,
            string detail)
        {
            if (report == null)
                return;

            report.capabilities.Add(new AlpineCapabilityStatus
            {
                id = id,
                label = label,
                state = ready ? "ready" : (required ? "broken" : "degraded"),
                required = required,
                detail = detail
            });
        }

        private static string ResolveOverallCompatibility(AlpineCompatibilityReport report)
        {
            if (report == null || report.capabilities == null || report.capabilities.Count == 0)
                return "broken";

            bool hasBrokenRequired = report.capabilities.Any(c => c != null && c.required && !c.IsReady);
            if (hasBrokenRequired)
                return "broken";

            bool hasDegraded = report.capabilities.Any(c => c != null && !c.IsReady);
            return hasDegraded ? "degraded" : "ready";
        }

        private static void PopulateAssemblyFingerprint(AlpineCompatibilityReport report)
        {
            if (report == null)
                return;

            try
            {
                string path = typeof(SnowmobileController).Assembly.Location;
                report.assemblyFileName = string.IsNullOrWhiteSpace(path)
                    ? null
                    : Path.GetFileName(path);

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    report.assemblyLightHash = "missing";
                    return;
                }

                var file = new FileInfo(path);
                report.assemblyLengthBytes = file.Length;
                report.assemblyLastWriteUtc = file.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                report.assemblyLightHash = ComputeLightweightFileHash(file);
            }
            catch (Exception ex)
            {
                report.assemblyLightHash = "error:" + ex.GetType().Name;
            }
        }

        private static string ComputeLightweightFileHash(FileInfo file)
        {
            if (file == null || !file.Exists)
                return "missing";

            byte[] buffer = new byte[AssemblyFingerprintChunkBytes];
            using (var sha = SHA256.Create())
            using (var stream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] header = Encoding.UTF8.GetBytes(file.Length + "|" + file.LastWriteTimeUtc.Ticks);
                sha.TransformBlock(header, 0, header.Length, header, 0);

                long maxOffset = Math.Max(0L, stream.Length - AssemblyFingerprintChunkBytes);
                long middleOffset = Math.Max(0L, (stream.Length / 2L) - (AssemblyFingerprintChunkBytes / 2L));
                var offsets = new[] { 0L, middleOffset, maxOffset }.Distinct().OrderBy(v => v).ToArray();

                foreach (long offset in offsets)
                {
                    stream.Position = offset;
                    int wanted = (int)Math.Min(AssemblyFingerprintChunkBytes, stream.Length - offset);
                    int read = wanted > 0 ? stream.Read(buffer, 0, wanted) : 0;
                    if (read > 0)
                        sha.TransformBlock(buffer, 0, read, buffer, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string hex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                return hex.Substring(0, Math.Min(16, hex.Length));
            }
        }

        private static string Status(bool ready)
        {
            return ready ? "ready" : "missing";
        }

        private static string NameOrNull(Type type)
        {
            return type != null ? type.FullName : "NULL";
        }

        private static string NameOrNull(MemberInfo member)
        {
            return member != null ? member.Name : "NULL";
        }

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;

            _vehicleIdField = GetField(typeof(VehicleScriptableObject), "vehicleId");
            _vehicleIdProperty = GetProperty(typeof(VehicleScriptableObject), "Id");
            _vehicleEngineAudioTypeField = GetField(typeof(VehicleScriptableObject), "engineAudioType");
            _vehicleListSelectableVehiclesProp = GetProperty(typeof(VehicleListScriptableObject), "SelectableVehicles");
            _vehicleListVehiclesField = GetField(typeof(VehicleListScriptableObject), "vehicles");
            _snowmobileVehicleProp = GetProperty(typeof(SnowmobileController), "GKMNAIKNNMJ");
            _snowmobileVehicleField = GetField(typeof(SnowmobileController), "KJFNKMCOKLL");
            _controllerType = typeof(Controller);
            _controllerInstanceProp = GetProperty(_controllerType, "PKMPAOKMHCB");
            _reCreateSnowmobileMethod = GetMethod(_controllerType, "ReCreateSnowmobile", Type.EmptyTypes);
        }

        private static bool RuntimeTuningControlsReady(out string detail)
        {
            FieldInfo stabilizerField =
                GetField(typeof(SnowmobileController), "BFJKIBCBFHJ") ??
                GetField(typeof(SnowmobileController), "BFJKIBCBFH");
            Type stabilizerType = stabilizerField != null ? stabilizerField.FieldType : null;

            string[] controllerFields =
            {
                "throttleExponent",
                "rpmSensitivity",
                "rpmSensitivityDown",
                "clutchRpmMin",
                "clutchRpmMax",
                "minThrottleOnClutchEngagement"
            };
            string[] stabilizerFields =
            {
                "damping",
                "trackSpeedDamping",
                "trackSpeedGyroMultiplier"
            };

            int controllerReady = controllerFields.Count(name => GetField(typeof(SnowmobileController), name) != null);
            int stabilizerReady = stabilizerType != null
                ? stabilizerFields.Count(name => GetField(stabilizerType, name) != null)
                : 0;

            detail =
                $"controller fields={controllerReady}/{controllerFields.Length}, " +
                $"stabilizer={NameOrNull(stabilizerField)}, " +
                $"stabilizer fields={stabilizerReady}/{stabilizerFields.Length}";
            return controllerReady == controllerFields.Length &&
                   stabilizerField != null &&
                   stabilizerReady == stabilizerFields.Length;
        }

        private static bool ResolveNativePhysicsReflection()
        {
            if (_nativePhysicsReflectionResolved)
                return _nativePhysicsReflectionReady;

            _nativePhysicsReflectionResolved = true;
            try
            {
                Assembly gameAssembly = typeof(SnowmobileController).Assembly;
                _meshInterpretterType = gameAssembly.GetType("MeshInterpretter") ?? Type.GetType("MeshInterpretter, Assembly-CSharp");
                _suspensionControllerType = gameAssembly.GetType("SuspensionController") ?? Type.GetType("SuspensionController, Assembly-CSharp");
                _snowmobileControllerBaseType = gameAssembly.GetType("SnowmobileControllerBase") ?? typeof(SnowmobileControllerBase);
                _ski2Type = gameAssembly.GetType("Ski2") ?? typeof(Ski2);
                _skiHardSurfaceContactType = gameAssembly.GetType("SkiHardSurfaceContact");
                _trackHardSurfaceContactType = gameAssembly.GetType("TrackHardSurfaceContact");
                _hardSurfaceContactBaseType = gameAssembly.GetType("HardSurfaceContactBase");

                _nativeDrivetrainReflectionReady = ProbeNativeSubsystem(() =>
                    IsComponentType(_meshInterpretterType) &&
                    GetField(_meshInterpretterType, "powerEfficiency") != null &&
                    GetField(_meshInterpretterType, "drivetrainMinSpeed") != null &&
                    GetField(_meshInterpretterType, "drivetrainMaxSpeed1") != null &&
                    GetField(_meshInterpretterType, "drivetrainMaxSpeed2") != null &&
                    GetField(_meshInterpretterType, "trackMass") != null);

                _nativeBrakeReflectionReady = ProbeNativeSubsystem(() =>
                    IsComponentType(_meshInterpretterType) &&
                    GetField(_meshInterpretterType, "breakForce") != null);

                _nativeSuspensionReflectionReady = ProbeNativeSubsystem(() =>
                {
                    FieldInfo frontShockField = GetField(_suspensionControllerType, "frontSuspension");
                    FieldInfo rearShockField = GetField(_suspensionControllerType, "rearSuspension");
                    Type shockType = frontShockField != null
                        ? frontShockField.FieldType
                        : rearShockField?.FieldType;
                    FieldInfo softSettingsField = GetField(shockType, "soft");
                    FieldInfo hardSettingsField = GetField(shockType, "hard");
                    Type settingsType = softSettingsField != null
                        ? softSettingsField.FieldType
                        : hardSettingsField?.FieldType;
                    return IsComponentType(_suspensionControllerType) &&
                           frontShockField != null && rearShockField != null &&
                           GetField(_suspensionControllerType, "antiRollBarFactor") != null &&
                           GetField(_suspensionControllerType, "trackRigidityFront") != null &&
                           GetField(_suspensionControllerType, "trackRigidityRear") != null &&
                           softSettingsField != null && hardSettingsField != null &&
                           GetField(settingsType, "springFactor") != null &&
                           GetField(settingsType, "damperFactor") != null &&
                           GetField(settingsType, "compressionRatio") != null &&
                           GetField(settingsType, "compressionFastRatio") != null &&
                           GetField(settingsType, "reboundRatio") != null &&
                           GetField(settingsType, "reboundFastRatio") != null;
                });

                _nativeSteeringReflectionReady = ProbeNativeSubsystem(() =>
                    IsComponentType(_snowmobileControllerBaseType) &&
                    IsComponentType(_ski2Type) &&
                    GetField(_snowmobileControllerBaseType, "skisMaxAngle") != null &&
                    GetField(_snowmobileControllerBaseType, "toeAngle") != null &&
                    GetField(_snowmobileControllerBaseType, "leftSki") != null &&
                    GetField(_snowmobileControllerBaseType, "rightSki") != null &&
                    GetField(_ski2Type, "camberFactor") != null);

                _nativeGripReflectionReady = ProbeNativeSubsystem(() =>
                {
                    FieldInfo skiContactBaseField = GetField(_skiHardSurfaceContactType, "contactBase");
                    FieldInfo trackContactBaseField = GetField(_trackHardSurfaceContactType, "contactBase");
                    return IsComponentType(_skiHardSurfaceContactType) &&
                           IsComponentType(_trackHardSurfaceContactType) &&
                           IsComponentType(_hardSurfaceContactBaseType) &&
                           skiContactBaseField != null && trackContactBaseField != null &&
                           _hardSurfaceContactBaseType.IsAssignableFrom(skiContactBaseField.FieldType) &&
                           _hardSurfaceContactBaseType.IsAssignableFrom(trackContactBaseField.FieldType) &&
                           GetField(_hardSurfaceContactBaseType, "grip") != null;
                });

                // A missing optional subsystem must not disable unrelated native
                // tuning. Callers capture and apply only fields that are present.
                _nativePhysicsReflectionReady =
                    _nativeDrivetrainReflectionReady ||
                    _nativeSuspensionReflectionReady ||
                    _nativeBrakeReflectionReady ||
                    _nativeSteeringReflectionReady ||
                    _nativeGripReflectionReady;
            }
            catch
            {
                _meshInterpretterType = null;
                _suspensionControllerType = null;
                _snowmobileControllerBaseType = null;
                _ski2Type = null;
                _skiHardSurfaceContactType = null;
                _trackHardSurfaceContactType = null;
                _hardSurfaceContactBaseType = null;
                _nativePhysicsReflectionReady = false;
                _nativeDrivetrainReflectionReady = false;
                _nativeSuspensionReflectionReady = false;
                _nativeBrakeReflectionReady = false;
                _nativeSteeringReflectionReady = false;
                _nativeGripReflectionReady = false;
            }

            return _nativePhysicsReflectionReady;
        }

        private static bool ProbeNativeSubsystem(Func<bool> probe)
        {
            try
            {
                return probe != null && probe();
            }
            catch
            {
                return false;
            }
        }

        private static string NativeDrivetrainCompatibilityDetail()
        {
            int drivetrainFields = CountFields(
                _meshInterpretterType,
                "powerEfficiency",
                "drivetrainMinSpeed",
                "drivetrainMaxSpeed1",
                "drivetrainMaxSpeed2",
                "trackMass");
            return $"mesh={NameOrNull(_meshInterpretterType)} fields={drivetrainFields}/5";
        }

        private static string NativeSuspensionCompatibilityDetail()
        {
            int suspensionFields = CountFields(
                _suspensionControllerType,
                "frontSuspension",
                "rearSuspension",
                "antiRollBarFactor",
                "trackRigidityFront",
                "trackRigidityRear");

            FieldInfo shockField =
                GetField(_suspensionControllerType, "rearSuspension") ??
                GetField(_suspensionControllerType, "frontSuspension");
            Type shockType = shockField != null ? shockField.FieldType : null;
            FieldInfo settingsField = GetField(shockType, "soft") ?? GetField(shockType, "hard");
            Type settingsType = settingsField != null ? settingsField.FieldType : null;
            int shockSettingsFields = CountFields(
                settingsType,
                "springFactor",
                "damperFactor",
                "compressionRatio",
                "compressionFastRatio",
                "reboundRatio",
                "reboundFastRatio");

            return
                $"suspension={NameOrNull(_suspensionControllerType)} fields={suspensionFields}/5, " +
                $"shock settings={NameOrNull(settingsType)} fields={shockSettingsFields}/6";
        }

        private static string NativeSteeringCompatibilityDetail()
        {
            int baseFields = CountFields(
                _snowmobileControllerBaseType,
                "skisMaxAngle",
                "toeAngle",
                "leftSki",
                "rightSki");
            int skiFields = CountFields(_ski2Type, "camberFactor");
            return
                $"controllerBase={NameOrNull(_snowmobileControllerBaseType)} fields={baseFields}/4, " +
                $"ski={NameOrNull(_ski2Type)} fields={skiFields}/1";
        }

        private static string NativeGripCompatibilityDetail()
        {
            int skiWrapperFields = CountFields(_skiHardSurfaceContactType, "contactBase");
            int trackWrapperFields = CountFields(_trackHardSurfaceContactType, "contactBase");
            int gripFields = CountFields(_hardSurfaceContactBaseType, "grip");
            return
                $"base={NameOrNull(_hardSurfaceContactBaseType)} fields={gripFields}/1, " +
                $"ski={NameOrNull(_skiHardSurfaceContactType)} fields={skiWrapperFields}/1, " +
                $"track={NameOrNull(_trackHardSurfaceContactType)} fields={trackWrapperFields}/1";
        }

        private static bool IsComponentType(Type type)
        {
            return type != null && typeof(Component).IsAssignableFrom(type);
        }

        private static int CountFields(Type type, params string[] names)
        {
            return type == null || names == null
                ? 0
                : names.Count(name => GetField(type, name) != null);
        }

        public static string GetVehicleId(VehicleScriptableObject sled, string fallback)
        {
            Initialize();
            if (sled == null)
                return fallback;

            try
            {
                if (_vehicleIdField != null)
                {
                    var legacyValue = _vehicleIdField.GetValue(sled) as string;
                    if (!string.IsNullOrWhiteSpace(legacyValue))
                        return legacyValue;
                }

                // Current Sledders builds moved identity to
                // IdentifiableScriptableObject.Id (an ItemIdentifier value type).
                // Keep this reflection based so older game builds that still expose
                // the legacy string field remain supported by the same mod binary.
                object identifier = _vehicleIdProperty?.GetValue(sled, null);
                string value = identifier != null ? identifier.ToString() : null;
                return !string.IsNullOrWhiteSpace(value) && value != "0" ? value : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static IEnumerable<VehicleScriptableObject> GetSelectableVehicles(VehicleListScriptableObject list)
        {
            Initialize();
            if (list == null)
                return Enumerable.Empty<VehicleScriptableObject>();

            try
            {
                var vehicles = _vehicleListSelectableVehiclesProp?.GetValue(list) as VehicleScriptableObject[];
                if (vehicles == null)
                    vehicles = _vehicleListVehiclesField?.GetValue(list) as VehicleScriptableObject[];

                return vehicles != null
                    ? vehicles.Where(v => v != null)
                    : Enumerable.Empty<VehicleScriptableObject>();
            }
            catch
            {
                return Enumerable.Empty<VehicleScriptableObject>();
            }
        }

        public static SnowmobileController GetPauseController(PauseUIController pause)
        {
            // Sledders 1.1.6 no longer stores a SnowmobileController on PauseUI.
            // The caller deliberately falls back to Alpine's current runtime sled.
            return null;
        }

        public static VehicleScriptableObject TryGetVehicleSelectionSled(object menu)
        {
            string source;
            return TryGetVehicleSelectionSled(menu, out source);
        }

        public static VehicleScriptableObject TryGetVehicleSelectionSled(object menu, out string source)
        {
            source = null;
            if (menu == null)
                return null;

            try
            {
                // IHKCPAEBKID is the live selected-sled envelope. DICGGOJLMJP is
                // only the value captured when the garage opened and must never
                // become a tuning target when the live selection is temporarily null.
                object selection = GetFieldValue<object>(menu, "IHKCPAEBKID");

                if (TryExtractVehicleScriptableObject(selection, 1, new HashSet<object>(), out var selectedVehicle, out var selectedSource))
                {
                    source = "garage selection " + selectedSource;
                    return selectedVehicle;
                }

            }
            catch
            {
            }

            return null;
        }

        private static bool TryExtractVehicleScriptableObject(
            object target,
            int depth,
            HashSet<object> visited,
            out VehicleScriptableObject sled,
            out string source)
        {
            sled = null;
            source = null;

            if (target == null || depth < 0)
                return false;

            if (target is VehicleScriptableObject direct)
            {
                sled = direct;
                source = "direct";
                return true;
            }

            Type type = target.GetType();
            if (IsUnsafeReflectionWalkTarget(type))
                return false;

            if (!visited.Add(target))
                return false;

            if (TryGetFieldValue(target, "KJFNKMCOKLL", out VehicleScriptableObject knownField) && knownField != null)
            {
                sled = knownField;
                source = "vehicle field KJFNKMCOKLL";
                return true;
            }

            foreach (var field in type.GetFields(All).OrderByDescending(f => ScoreVehicleMemberName(f.Name)))
            {
                if (!typeof(VehicleScriptableObject).IsAssignableFrom(field.FieldType))
                    continue;

                try
                {
                    sled = field.GetValue(target) as VehicleScriptableObject;
                    if (sled != null)
                    {
                        source = "vehicle field " + field.Name;
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (var property in type.GetProperties(All).OrderByDescending(p => ScoreVehicleMemberName(p.Name)))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                    !typeof(VehicleScriptableObject).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                try
                {
                    sled = property.GetValue(target) as VehicleScriptableObject;
                    if (sled != null)
                    {
                        source = "vehicle property " + property.Name;
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (depth == 0)
                return false;

            foreach (var field in type.GetFields(All).OrderByDescending(f => ScoreVehicleMemberName(f.Name)))
            {
                if (!ShouldWalkMemberType(field.FieldType))
                    continue;

                try
                {
                    object value = field.GetValue(target);
                    if (TryExtractVehicleScriptableObject(value, depth - 1, visited, out sled, out source))
                    {
                        source = field.Name + " -> " + source;
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (var property in type.GetProperties(All).OrderByDescending(p => ScoreVehicleMemberName(p.Name)))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0 || !ShouldWalkMemberType(property.PropertyType))
                    continue;

                try
                {
                    object value = property.GetValue(target);
                    if (TryExtractVehicleScriptableObject(value, depth - 1, visited, out sled, out source))
                    {
                        source = property.Name + " -> " + source;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static int ScoreVehicleMemberName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            string lower = name.ToLowerInvariant();
            int score = 0;
            if (lower.Contains("selected")) score += 100;
            if (lower.Contains("current")) score += 80;
            if (lower.Contains("active")) score += 60;
            if (lower.Contains("vehicle")) score += 40;
            if (lower.Contains("sled") || lower.Contains("snowmobile")) score += 20;
            return score;
        }

        private static bool ShouldWalkMemberType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return false;

            if (typeof(VisualElement).IsAssignableFrom(type) ||
                typeof(GameObject).IsAssignableFrom(type) ||
                typeof(Transform).IsAssignableFrom(type))
            {
                return false;
            }

            if (typeof(ScriptableObject).IsAssignableFrom(type) &&
                !typeof(VehicleScriptableObject).IsAssignableFrom(type))
            {
                return false;
            }

            return type.Assembly == typeof(VehicleScriptableObject).Assembly ||
                   type.Assembly == typeof(VehicleSelectionUiController).Assembly;
        }

        private static bool IsUnsafeReflectionWalkTarget(Type type)
        {
            if (type == null)
                return true;

            return typeof(VisualElement).IsAssignableFrom(type) ||
                   typeof(GameObject).IsAssignableFrom(type) ||
                   typeof(Transform).IsAssignableFrom(type);
        }

        public static VehicleScriptableObject GetVehicleFromController(SnowmobileController controller)
        {
            Initialize();
            if (controller == null)
                return null;

            try
            {
                var so = _snowmobileVehicleProp?.GetValue(controller) as VehicleScriptableObject;
                if (so != null)
                    return so;

                return _snowmobileVehicleField?.GetValue(controller) as VehicleScriptableObject;
            }
            catch
            {
                return null;
            }
        }

        public static bool TrySpawnPlayer(Transform spawnTransform, bool reload, out string reason)
        {
            reason = null;
            Initialize();

            if (spawnTransform == null)
            {
                reason = "spawn transform is null";
                return false;
            }

            object controllerInstance = null;
            try
            {
                controllerInstance = _controllerInstanceProp?.GetValue(null);
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }

            if (controllerInstance == null)
            {
                reason = "Controller singleton not found";
                return false;
            }

            try
            {
                if (_trySpawnPlayerMethod == null || _trySpawnPlayerMethod.DeclaringType != controllerInstance.GetType())
                {
                    _trySpawnPlayerMethod = controllerInstance.GetType().GetMethod(
                        "TrySpawnPlayer",
                        All,
                        null,
                        new[] { typeof(Transform), typeof(bool) },
                        null);

                    if (_trySpawnPlayerMethod == null)
                    {
                        _trySpawnPlayerMethod = controllerInstance.GetType()
                            .GetMethods(All)
                            .FirstOrDefault(m =>
                                m.Name == "TrySpawnPlayer" &&
                                m.GetParameters().Length == 2 &&
                                m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(Transform)) &&
                                m.GetParameters()[1].ParameterType == typeof(bool));
                    }
                }

                if (_trySpawnPlayerMethod == null)
                {
                    reason = "TrySpawnPlayer overload not found";
                    return false;
                }

                _trySpawnPlayerMethod.Invoke(controllerInstance, new object[] { spawnTransform, reload });
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        public static bool TryReCreateSnowmobile(
            SnowmobileController expectedController,
            out string reason)
        {
            reason = null;
            Initialize();

            object controllerInstance;
            try
            {
                controllerInstance = _controllerInstanceProp?.GetValue(null);
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }

            if (controllerInstance == null)
            {
                reason = "Controller singleton not found";
                return false;
            }

            SnowmobileController currentController =
                GetPropertyValue<SnowmobileController>(controllerInstance, "JFIFAJLPMIE") ??
                GetFieldValue<SnowmobileController>(controllerInstance, "FPHGKGPJDPG");
            if (currentController == null)
            {
                reason = "Controller has no current local sled";
                return false;
            }

            if (expectedController != null && currentController != expectedController)
            {
                reason = "Controller current sled does not match Alpine's live sled";
                return false;
            }

            MethodInfo recreate = _reCreateSnowmobileMethod;
            if (recreate == null || recreate.DeclaringType != controllerInstance.GetType())
            {
                recreate = GetMethod(controllerInstance.GetType(), "ReCreateSnowmobile", Type.EmptyTypes);
                _reCreateSnowmobileMethod = recreate;
            }

            if (recreate == null)
            {
                reason = "Controller.ReCreateSnowmobile binding not found";
                return false;
            }

            try
            {
                recreate.Invoke(controllerInstance, null);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.InnerException?.GetType().Name ?? ex.GetType().Name;
                return false;
            }
        }

        public static object GetStabilizer(object controller)
        {
            return GetFieldValue<object>(controller, "BFJKIBCBFHJ") ??
                   GetFieldValue<object>(controller, "BFJKIBCBFH");
        }

        public static Component[] GetMeshInterpreters(Component root)
        {
            ResolveNativePhysicsReflection();
            return GetComponentsInChildren(root, _meshInterpretterType);
        }

        public static Component[] GetSuspensionControllers(Component root)
        {
            ResolveNativePhysicsReflection();
            return GetComponentsInChildren(root, _suspensionControllerType);
        }

        public static Component GetSnowmobileControllerBase(SnowmobileController controller)
        {
            if (controller == null)
                return null;

            ResolveNativePhysicsReflection();
            object direct = GetFieldValue<object>(controller, "controllerBase");
            if (direct is Component directComponent)
                return directComponent;

            return GetComponentsInChildren(controller, _snowmobileControllerBaseType).FirstOrDefault();
        }

        public static Component GetControllerBaseSki(Component controllerBase, bool left)
        {
            if (controllerBase == null)
                return null;

            object value = GetFieldValue<object>(controllerBase, left ? "leftSki" : "rightSki");
            return value as Component;
        }

        public static Component[] GetSkiHardSurfaceContactBases(Component root)
        {
            ResolveNativePhysicsReflection();
            return GetHardSurfaceContactBases(root, _skiHardSurfaceContactType);
        }

        public static Component[] GetTrackHardSurfaceContactBases(Component root)
        {
            ResolveNativePhysicsReflection();
            return GetHardSurfaceContactBases(root, _trackHardSurfaceContactType);
        }

        private static Component[] GetHardSurfaceContactBases(Component root, Type wrapperType)
        {
            if (root == null ||
                !IsComponentType(wrapperType) ||
                !IsComponentType(_hardSurfaceContactBaseType))
            {
                return Array.Empty<Component>();
            }

            var result = new List<Component>();
            var capturedIds = new HashSet<int>();
            foreach (Component wrapper in GetComponentsInChildren(root, wrapperType))
            {
                object linkedBase = GetFieldValue<object>(wrapper, "contactBase");
                if (!(linkedBase is Component contactBase) ||
                    !_hardSurfaceContactBaseType.IsInstanceOfType(contactBase))
                {
                    continue;
                }

                // contactBase is a per-object MonoBehaviour linked by the native
                // wrapper. Capture that instance, never a shared friction asset or
                // an unrelated component found by a broad hierarchy search.
                if (capturedIds.Add(contactBase.GetInstanceID()))
                    result.Add(contactBase);
            }

            return result.ToArray();
        }

        private static Component[] GetComponentsInChildren(Component root, Type componentType)
        {
            if (root == null || !IsComponentType(componentType))
                return Array.Empty<Component>();

            try
            {
                return root.GetComponentsInChildren(componentType, true)
                    .OfType<Component>()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<Component>();
            }
        }

        public static void CaptureFloat(object target, string fieldName, Action<float> capture)
        {
            if (TryGetFieldValue(target, fieldName, out float value))
                capture(value);
        }

        public static bool TryGetFieldValue<T>(object target, string fieldName, out T value)
        {
            value = default(T);
            if (target == null)
                return false;

            try
            {
                var field = GetField(target.GetType(), fieldName);
                if (field == null)
                    return false;

                object raw = field.GetValue(target);
                if (!(raw is T typed))
                    return false;

                value = typed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetNumericField(object target, string fieldName, out double value)
        {
            value = 0d;
            if (target == null)
                return false;

            try
            {
                FieldInfo field = GetField(target.GetType(), fieldName);
                if (field == null)
                    return false;

                object raw = field.GetValue(target);
                if (raw == null || raw is bool || raw is char || raw is Enum)
                    return false;

                value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                value = 0d;
                return false;
            }
        }

        public static T GetFieldValue<T>(object target, string fieldName)
        {
            if (TryGetFieldValue(target, fieldName, out T value))
                return value;

            return default(T);
        }

        public static bool TryGetPropertyValue<T>(object target, string propertyName, out T value)
        {
            value = default(T);
            if (target == null)
                return false;

            try
            {
                var property = GetProperty(target.GetType(), propertyName);
                if (property == null || !property.CanRead)
                    return false;

                object raw = property.GetValue(target);
                if (!(raw is T typed))
                    return false;

                value = typed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static T GetPropertyValue<T>(object target, string propertyName)
        {
            if (TryGetPropertyValue(target, propertyName, out T value))
                return value;

            return default(T);
        }

        public static bool SetFloatField(object target, string fieldName, float value)
        {
            return SetFieldValue(target, fieldName, value);
        }

        public static bool SetFieldValue(object target, string fieldName, object value)
        {
            if (target == null)
                return false;

            try
            {
                var field = GetField(target.GetType(), fieldName);
                if (field == null)
                    return false;

                field.SetValue(target, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetNumericField(object target, string fieldName, double value)
        {
            if (target == null || double.IsNaN(value) || double.IsInfinity(value))
                return false;

            try
            {
                FieldInfo field = GetField(target.GetType(), fieldName);
                if (field == null || field.IsInitOnly || field.IsLiteral)
                    return false;

                object converted = Convert.ChangeType(
                    value,
                    field.FieldType,
                    System.Globalization.CultureInfo.InvariantCulture);
                field.SetValue(target, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Component[] GetSnowmobileAccessories(SnowmobileController controller)
        {
            return GetSnowmobileAccessories(controller as Component);
        }

        public static Component[] GetSnowmobileAccessories(Component root)
        {
            if (root == null)
                return Array.Empty<Component>();

            try
            {
                if (!ResolveAccessoryReflection())
                    return Array.Empty<Component>();

                return root.GetComponentsInChildren(_snowmobileAccessoriesType, true);
            }
            catch
            {
                return Array.Empty<Component>();
            }
        }

        private static bool ResolveAccessoryReflection()
        {
            if (_snowmobileAccessoriesType != null)
                return true;

            try
            {
                _snowmobileAccessoriesType = Type.GetType("SnowmobileAccessories, Assembly-CSharp");
            }
            catch
            {
                _snowmobileAccessoriesType = null;
            }

            return _snowmobileAccessoriesType != null;
        }

        public static Light[] GetHeadlightLights(SnowmobileController controller)
        {
            return GetHeadlightLights(controller as Component);
        }

        public static Light[] GetHeadlightLights(Component root)
        {
            if (root == null || !ResolveHeadlightReflection())
                return Array.Empty<Light>();

            try
            {
                var result = new List<Light>();
                var seen = new HashSet<int>();
                var components = root.GetComponentsInChildren(_headLightType, true);
                if (components == null)
                    return Array.Empty<Light>();

                foreach (var component in components)
                {
                    if (component == null)
                        continue;

                    Light light = null;
                    if (_headLightLightField != null)
                        light = _headLightLightField.GetValue(component) as Light;

                    if (light == null)
                        light = ((Component)component).GetComponentInChildren<Light>(true);

                    if (light == null || !seen.Add(light.GetInstanceID()))
                        continue;

                    result.Add(light);
                }

                return result.ToArray();
            }
            catch
            {
                return Array.Empty<Light>();
            }
        }

        public static void SetGameObjectListActive(object owner, string fieldName, bool active)
        {
            var enumerable = GetFieldValue<System.Collections.IEnumerable>(owner, fieldName);
            if (enumerable == null)
                return;

            foreach (object item in enumerable)
            {
                if (item is GameObject go)
                    go.SetActive(active);
            }
        }

        public static Component FindEngineAudioController(SnowmobileController controller)
        {
            return FindEngineAudioController(controller as Component);
        }

        public static Component FindEngineAudioController(Component root)
        {
            if (root == null || !ResolveEngineAudioReflection())
                return null;

            try
            {
                return root
                    .GetComponentsInChildren(_engineAudioControllerType, true)
                    .FirstOrDefault() as Component;
            }
            catch
            {
                return null;
            }
        }

        public static ulong GetControllerOwnerId(SnowmobileController controller)
        {
            if (controller == null)
                return 0;

            if (TryGetPropertyValue(controller, "LIMNFNLEGDJ", out ulong direct))
                return direct;

            if (TryGetPropertyValue(controller, "LIMNFNLEGDJ", out object boxed) && boxed is ulong boxedId)
                return boxedId;

            if (TryGetFieldValue(controller, "LIMNFNLEGDJ", out ulong fieldId))
                return fieldId;

            return 0;
        }

        public static ulong GetRemoteControllerOwnerId(SnowmobileControllerRemote remote)
        {
            if (remote == null)
                return 0;

            if (TryGetPropertyValue(remote, "LIMNFNLEGDJ", out ulong direct))
                return direct;

            if (TryGetPropertyValue(remote, "LIMNFNLEGDJ", out object boxed) && boxed is ulong boxedId)
                return boxedId;

            if (TryGetFieldValue(remote, "LIMNFNLEGDJ", out ulong fieldId))
                return fieldId;

            return 0;
        }

        public static bool TryFindRemoteSnowmobileController(ulong senderId, out SnowmobileController controller)
        {
            controller = null;
            if (senderId == 0)
                return false;

            try
            {
                var controllers = Resources.FindObjectsOfTypeAll<SnowmobileController>();
                if (controllers == null)
                    return false;

                foreach (var candidate in controllers)
                {
                    if (candidate == null || candidate == AlpineTuningMod.ActiveController)
                        continue;

                    if (GetControllerOwnerId(candidate) == senderId)
                    {
                        controller = candidate;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool TryFindRemoteSnowmobileRoot(ulong senderId, out Component root, out string reason)
        {
            root = null;
            reason = null;

            if (senderId == 0)
            {
                reason = "sender missing";
                return false;
            }

            try
            {
                var remotes = Resources.FindObjectsOfTypeAll<SnowmobileControllerRemote>();
                if (remotes != null)
                {
                    foreach (var remote in remotes)
                    {
                        if (remote == null || GetRemoteControllerOwnerId(remote) != senderId)
                            continue;

                        var baseController =
                            GetPropertyValue<SnowmobileControllerBase>(remote, "FHCHCEDHPOP") ??
                            GetFieldValue<SnowmobileControllerBase>(remote, "MDFGNAFIJEA");

                        root = baseController != null ? (Component)baseController : remote;
                        return true;
                    }
                }

                if (TryFindRemoteSnowmobileController(senderId, out var localStyleController))
                {
                    root = localStyleController;
                    return true;
                }
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }

            reason = "remote sled instance not found";
            return false;
        }

        public static bool TryGetRemoteNetworkVehicle(ulong senderId, out VehicleScriptableObject vehicle)
        {
            vehicle = null;
            if (senderId == 0)
                return false;

            try
            {
                ResolveNetClientGameplayBindings();
                object gameplay = _netClientGameplayInstanceProp?.GetValue(null);
                if (gameplay != null)
                {
                    if (_netClientGameplayGetVehicleMethod == null ||
                        _netClientGameplayGetVehicleMethod.DeclaringType != gameplay.GetType())
                    {
                        _netClientGameplayGetVehicleMethod = GetMethod(
                            gameplay.GetType(),
                            "GetVehicle",
                            new[] { typeof(ulong) });
                    }

                    vehicle = _netClientGameplayGetVehicleMethod?.Invoke(
                        gameplay,
                        new object[] { senderId }) as VehicleScriptableObject;

                    if (vehicle != null)
                        return true;
                }

                if (TryFindRemoteSnowmobileController(senderId, out var controller))
                {
                    vehicle = GetVehicleFromController(controller);
                    return vehicle != null;
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool TryReadActiveEngineAudioToken(
            SnowmobileController controller,
            out string enumTypeName,
            out string enumName,
            out int enumRawValue)
        {
            enumTypeName = null;
            enumName = null;
            enumRawValue = 0;

            if (!ResolveEngineAudioReflection() || _fiCurrentEngineType == null)
                return false;

            Component audioController = FindEngineAudioController(controller);
            if (audioController == null)
                return false;

            try
            {
                object value = _fiCurrentEngineType.GetValue(audioController);
                return TryReadEngineAudioValue(value, out enumTypeName, out enumName, out enumRawValue);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadEngineAudioTokenFromVehicle(
            VehicleScriptableObject sled,
            out string enumTypeName,
            out string enumName,
            out int enumRawValue)
        {
            enumTypeName = null;
            enumName = null;
            enumRawValue = 0;

            if (sled == null || !ResolveEngineAudioReflection() || _engineAudioEnumType == null)
                return false;

            try
            {
                if (_vehicleEngineAudioTypeField != null &&
                    _vehicleEngineAudioTypeField.FieldType == _engineAudioEnumType)
                {
                    object directValue = _vehicleEngineAudioTypeField.GetValue(sled);
                    if (TryReadEngineAudioValue(directValue, out enumTypeName, out enumName, out enumRawValue))
                        return true;
                }

                foreach (var field in sled.GetType().GetFields(All))
                {
                    if (field.FieldType != _engineAudioEnumType)
                        continue;

                    object value = field.GetValue(sled);
                    if (TryReadEngineAudioValue(value, out enumTypeName, out enumName, out enumRawValue))
                        return true;
                }

                foreach (var prop in sled.GetType().GetProperties(All))
                {
                    if (prop.PropertyType != _engineAudioEnumType || !prop.CanRead)
                        continue;

                    object value = prop.GetValue(sled);
                    if (TryReadEngineAudioValue(value, out enumTypeName, out enumName, out enumRawValue))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool TryApplyEngineAudioTokenToVehicle(
            VehicleScriptableObject sled,
            string enumTypeName,
            string enumName,
            int enumRawValue,
            out string reason)
        {
            reason = null;

            if (sled == null)
            {
                reason = "vehicle scriptable object missing";
                return false;
            }

            if (!ResolveEngineAudioReflection())
            {
                reason = "engine audio binding unavailable";
                return false;
            }

            if (_vehicleEngineAudioTypeField == null)
            {
                reason = "VehicleScriptableObject.engineAudioType unavailable";
                return false;
            }

            if (!TryResolveEngineAudioValue(enumTypeName, enumName, enumRawValue, out var desiredValue, out reason))
                return false;

            try
            {
                if (desiredValue.GetType() != _vehicleEngineAudioTypeField.FieldType)
                    desiredValue = Enum.ToObject(_vehicleEngineAudioTypeField.FieldType, Convert.ToInt32(desiredValue));

                _vehicleEngineAudioTypeField.SetValue(sled, desiredValue);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        public static bool TryApplyEngineAudioToken(
            Component audioController,
            string enumTypeName,
            string enumName,
            int enumRawValue,
            out string reason)
        {
            reason = null;

            if (audioController == null)
            {
                reason = "audio controller not found";
                return false;
            }

            if (!ResolveEngineAudioReflection())
            {
                reason = "engine audio binding unavailable";
                return false;
            }

            try
            {
                if (!TryResolveEngineAudioValue(enumTypeName, enumName, enumRawValue, out var desiredValue, out reason))
                    return false;

                bool isLocal = ReadBoolField(audioController, _fiEngineAudioIsLocal, true);
                bool isTurbo = ReadBoolField(audioController, _fiEngineAudioIsTurbo, false);

                if (_miStopEngineSound != null && _miStopEngineSound.GetParameters().Length == 0)
                    _miStopEngineSound.Invoke(audioController, Array.Empty<object>());

                if (_miSetEngineType != null)
                    _miSetEngineType.Invoke(audioController, new[] { desiredValue });

                if (_miEngineInit != null && _miEngineInit.GetParameters().Length == 3)
                    _miEngineInit.Invoke(audioController, new object[] { isLocal, desiredValue, isTurbo });

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        private static bool TryReadEngineAudioValue(
            object value,
            out string enumTypeName,
            out string enumName,
            out int enumRawValue)
        {
            enumTypeName = null;
            enumName = null;
            enumRawValue = 0;

            if (value == null)
                return false;

            try
            {
                Type valueType = value.GetType();
                if (!valueType.IsEnum)
                    return false;

                enumTypeName = valueType.AssemblyQualifiedName;
                enumName = Enum.GetName(valueType, value);
                enumRawValue = Convert.ToInt32(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveEngineAudioValue(
            string enumTypeName,
            string enumName,
            int enumRawValue,
            out object desiredValue,
            out string reason)
        {
            desiredValue = null;
            reason = null;

            Type enumType = _engineAudioEnumType;
            if (enumType == null && !string.IsNullOrWhiteSpace(enumTypeName))
                enumType = Type.GetType(enumTypeName, false);

            if (enumType == null || !enumType.IsEnum)
            {
                reason = "engine audio enum unavailable";
                return false;
            }

            try
            {
                desiredValue = !string.IsNullOrWhiteSpace(enumName) &&
                               Enum.IsDefined(enumType, enumName)
                    ? Enum.Parse(enumType, enumName)
                    : Enum.ToObject(enumType, enumRawValue);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        private static bool ReadBoolField(object target, FieldInfo field, bool fallback)
        {
            if (target == null || field == null)
                return fallback;

            try
            {
                object value = field.GetValue(target);
                return value is bool b ? b : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static VisualElement FindVisualRoot(object controller)
        {
            if (controller == null)
                return null;

            VisualElement preferred = GetFieldValue<VisualElement>(
                controller,
                AlpineNativeUiConfig.VehicleRootFieldName);

            if (preferred != null)
                return preferred;

            foreach (FieldInfo field in controller.GetType().GetFields(All))
            {
                if (!typeof(VisualElement).IsAssignableFrom(field.FieldType))
                    continue;

                var element = field.GetValue(controller) as VisualElement;
                if (element != null)
                    return element;
            }

            return null;
        }

        public static IEnumerable<ulong> DiscoverPeerIds(ulong localSteamId)
        {
            return DiscoverPeers(localSteamId, false)
                .Where(p => p != null)
                .Select(p => p.hasInternalClientId ? p.sleddersClientId : p.steamId)
                .Where(id => id != 0)
                .Distinct()
                .ToArray();
        }

        public static AlpineDiscoveredPeer[] DiscoverPeers(ulong localSteamId, bool log)
        {
            var peers = new Dictionary<ulong, AlpineDiscoveredPeer>();

            try
            {
                object netClient = GetNetClientInstance(false);
                if (netClient == null)
                    return peers.Values.ToArray();

                MethodInfo getIds = ResolveNetClientGetIdsMethod(netClient);
                if (getIds == null)
                    return peers.Values.ToArray();

                object raw = null;
                try
                {
                    raw = getIds.Invoke(netClient, Array.Empty<object>());
                }
                catch
                {
                    return peers.Values.ToArray();
                }

                var result = raw as ulong[];
                if (result == null)
                    return peers.Values.ToArray();

                ulong localSleddersId = GetLocalSleddersClientId(netClient);

                foreach (ulong id in result)
                {
                    if (id == 0 || (localSleddersId != 0 && id == localSleddersId) || (localSleddersId == 0 && id == localSteamId))
                        continue;

                    bool steam64 = LooksLikeSteam64(id);
                    ulong key = steam64 ? id : id + 0x1000000000000000UL;
                    if (!peers.TryGetValue(key, out var peer) || peer == null)
                    {
                        peer = new AlpineDiscoveredPeer
                        {
                            source = "NetClient.GetAllClientIdsIncludingLocalPlayer",
                            name = GetNetClientNickname(id)
                        };
                        peers[key] = peer;
                    }

                    if (steam64)
                    {
                        peer.steamId = id;
                        peer.hasSteamId = true;
                    }
                    else
                    {
                        peer.sleddersClientId = id;
                        peer.hasInternalClientId = true;
                    }
                }
            }
            catch
            {
            }

            return peers.Values.ToArray();
        }

        public static ulong GetLocalSleddersClientId()
        {
            try
            {
                object netClient = GetNetClientInstance(false);
                return GetLocalSleddersClientId(netClient);
            }
            catch
            {
                return 0;
            }
        }

        public static string GetNetClientNickname(ulong clientId)
        {
            if (clientId == 0)
                return null;

            try
            {
                object netClient = GetNetClientInstance(false);
                if (netClient == null)
                    return null;

                if (_netClientGetNickMethod == null || _netClientGetNickMethod.DeclaringType != netClient.GetType())
                {
                    _netClientGetNickMethod = GetMethod(
                        netClient.GetType(),
                        "GetNickOrFallback",
                        new[] { typeof(ulong) });
                }

                return _netClientGetNickMethod?.Invoke(netClient, new object[] { clientId }) as string;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetNetClientInterface(out object netInterface, out string reason)
        {
            netInterface = null;
            reason = null;

            try
            {
                object netClient = GetNetClientInstance(false);
                if (netClient == null)
                {
                    reason = "NetClient instance missing";
                    return false;
                }

                if (_netClientNetInterfaceField == null || _netClientNetInterfaceField.DeclaringType != netClient.GetType())
                    _netClientNetInterfaceField = GetField(netClient.GetType(), "netInterface");

                netInterface = _netClientNetInterfaceField?.GetValue(netClient);
                if (netInterface == null)
                {
                    reason = "NetClient.netInterface null";
                    return false;
                }

                reason = netInterface.GetType().FullName;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        public static bool TryGetNetServer(out object netServer, out string reason)
        {
            netServer = null;
            reason = null;

            try
            {
                netServer = GetNetServerInstance(false);
                if (netServer == null)
                {
                    reason = "NetServer instance missing";
                    return false;
                }

                reason = netServer.GetType().FullName;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        public static bool TryGetNetServerInterface(out object netInterface, out string reason)
        {
            netInterface = null;
            reason = null;

            try
            {
                object netServer = GetNetServerInstance(false);
                if (netServer == null)
                {
                    reason = "NetServer instance missing";
                    return false;
                }

                if (_netServerInterfaceField == null || _netServerInterfaceField.DeclaringType != netServer.GetType())
                    _netServerInterfaceField = GetField(netServer.GetType(), "PLLFEEJPKKO");

                netInterface = _netServerInterfaceField?.GetValue(netServer);
                if (netInterface == null)
                {
                    reason = "NetServer.PLLFEEJPKKO null";
                    return false;
                }

                reason = netInterface.GetType().FullName;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        private static object GetNetClientInstance(bool log)
        {
            try
            {
                Initialize();
                ResolvePeerDiscoveryBindings();

                object netClient = _netClientInstanceProp?.GetValue(null);
                return netClient;
            }
            catch
            {
                return null;
            }
        }

        private static object GetNetServerInstance(bool log)
        {
            try
            {
                ResolveNetServerBindings();

                object netServer = _netServerInstanceProp?.GetValue(null);
                if (netServer == null)
                    netServer = FindNetServerObjectFallback();
                return netServer;
            }
            catch
            {
                return null;
            }
        }

        private static object FindNetServerObjectFallback()
        {
            try
            {
                ResolveNetServerBindings();
                if (_netServerType == null || !typeof(UnityEngine.Object).IsAssignableFrom(_netServerType))
                    return null;

                var instances = Resources.FindObjectsOfTypeAll(_netServerType);
                if (instances == null || instances.Length == 0)
                    return null;

                foreach (var instance in instances)
                {
                    if (instance == null)
                        continue;

                    var component = instance as Component;
                    if (component == null || component.gameObject != null)
                        return instance;
                }
            }
            catch
            {
            }

            return null;
        }

        private static MethodInfo ResolveNetClientGetIdsMethod(object netClient)
        {
            if (netClient == null)
                return null;

            if (_netClientGetIdsMethod == null || _netClientGetIdsMethod.DeclaringType != netClient.GetType())
            {
                _netClientGetIdsMethod = GetMethod(
                    netClient.GetType(),
                    "GetAllClientIdsIncludingLocalPlayer",
                    Type.EmptyTypes);
            }

            return _netClientGetIdsMethod;
        }

        private static ulong GetLocalSleddersClientId(object netClient)
        {
            if (netClient == null)
                return 0;

            try
            {
                if (_netClientLocalClientIdProp == null ||
                    _netClientLocalClientIdProp.DeclaringType != netClient.GetType())
                {
                    _netClientLocalClientIdProp =
                        GetProperty(netClient.GetType(), "LocalClientId") ??
                        GetProperty(netClient.GetType(), "IPDIALFDOEM");
                }

                object value = _netClientLocalClientIdProp?.GetValue(netClient);
                if (value is ulong direct)
                    return direct;

                if (value != null && ulong.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }
            catch
            {
            }

            return 0;
        }

        private static bool ResolveEngineAudioReflection()
        {
            if (_engineAudioReflectionResolved)
                return _engineAudioReflectionReady;

            _engineAudioReflectionResolved = true;

            try
            {
                var gameAsm = typeof(SnowmobileController).Assembly;
                _engineAudioControllerType =
                    gameAsm.GetType("EngineSoundControllerWwise") ??
                    gameAsm.GetType("EngineSoundControllerFmod") ??
                    gameAsm.GetType("EngineSFXController") ??
                    Type.GetType("EngineSoundControllerWwise, Assembly-CSharp") ??
                    Type.GetType("EngineSoundControllerFmod, Assembly-CSharp");

                if (_engineAudioControllerType == null)
                {
                    _engineAudioReflectionReady = false;
                    return false;
                }

                _miSetEngineType = _engineAudioControllerType
                    .GetMethods(All)
                    .FirstOrDefault(m => m.Name == "SetEngineType" && m.GetParameters().Length == 1);

                _fiCurrentEngineType = GetField(_engineAudioControllerType, "GILHLLEEAEH");
                _fiEngineAudioIsLocal = GetField(_engineAudioControllerType, "HLAIDKIJEPN");
                _fiEngineAudioIsTurbo = GetField(_engineAudioControllerType, "KNDFNJBKCLO");
                _miStopEngineSound = FindMethodByNameAndParamCount(_engineAudioControllerType, "StopEngineSound", 0);

                if (_miSetEngineType != null)
                    _engineAudioEnumType = _miSetEngineType.GetParameters()[0].ParameterType;

                if (_engineAudioEnumType == null && _fiCurrentEngineType != null)
                    _engineAudioEnumType = _fiCurrentEngineType.FieldType;

                if (_engineAudioEnumType == null)
                {
                    Initialize();
                    if (_vehicleEngineAudioTypeField != null)
                        _engineAudioEnumType = _vehicleEngineAudioTypeField.FieldType;
                }

                _miEngineInit = _engineAudioControllerType
                    .GetMethods(All)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Init")
                            return false;

                        var parameters = m.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(bool) &&
                               parameters[1].ParameterType == _engineAudioEnumType &&
                               parameters[2].ParameterType == typeof(bool);
                    });

                _engineAudioReflectionReady =
                    _engineAudioControllerType != null &&
                    _engineAudioEnumType != null &&
                    _miSetEngineType != null &&
                    _miEngineInit != null;

                if (_engineAudioReflectionReady)
                    MelonLogger.Msg($"Engine audio reflection ready: {_engineAudioControllerType.FullName} / {_engineAudioEnumType.FullName}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Engine audio reflection failed: {ex.GetType().Name}");
                _engineAudioReflectionReady = false;
            }

            return _engineAudioReflectionReady;
        }

        private static bool ResolveHeadlightReflection()
        {
            if (_headLightReflectionResolved)
                return _headLightReflectionReady;

            _headLightReflectionResolved = true;

            try
            {
                var gameAsm = typeof(SnowmobileController).Assembly;
                _headLightType =
                    gameAsm.GetType("HeadLight") ??
                    Type.GetType("HeadLight, Assembly-CSharp");

                if (_headLightType == null)
                {
                    _headLightReflectionReady = false;
                    return false;
                }

                _headLightLightField = GetField(_headLightType, "FNLLAFPMEDC") ??
                                      _headLightType.GetFields(All)
                                          .FirstOrDefault(f => typeof(Light).IsAssignableFrom(f.FieldType));

                _headLightReflectionReady = _headLightLightField != null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Headlight reflection failed: {ex.GetType().Name}");
                _headLightReflectionReady = false;
            }

            return _headLightReflectionReady;
        }

        private static void ResolvePeerDiscoveryBindings()
        {
            if (_netClientType != null)
                return;

            _netClientType = Type.GetType("NetClient, Assembly-CSharp");
            _netClientInstanceProp =
                GetProperty(_netClientType, "PKMPAOKMHCB") ??
                GetProperty(_netClientType, "Instance");
        }

        private static void ResolveNetServerBindings()
        {
            if (_netServerType != null)
                return;

            _netServerType = Type.GetType("NetServer, Assembly-CSharp");
            _netServerInstanceProp =
                GetProperty(_netServerType, "PKMPAOKMHCB") ??
                GetProperty(_netServerType, "Instance");
        }

        private static void ResolveNetClientGameplayBindings()
        {
            if (_netClientGameplayType != null)
                return;

            _netClientGameplayType = Type.GetType("NetClientGameplayController, Assembly-CSharp");
            _netClientGameplayInstanceProp = GetProperty(_netClientGameplayType, "PKMPAOKMHCB");
        }

        private static MethodInfo FindMethodByNameAndParamCount(Type type, string name, int count)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current
                    .GetMethods(All | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(candidate =>
                        candidate.Name == name && candidate.GetParameters().Length == count);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static FieldInfo GetField(Type type, string name)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
                return null;

            string key = type.AssemblyQualifiedName + "::field::" + name;
            if (FieldCache.TryGetValue(key, out var field))
                return field;

            for (Type current = type; current != null && field == null; current = current.BaseType)
                field = current.GetField(name, All | BindingFlags.DeclaredOnly);
            FieldCache[key] = field;
            return field;
        }

        private static PropertyInfo GetProperty(Type type, string name)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
                return null;

            string key = type.AssemblyQualifiedName + "::prop::" + name;
            if (PropertyCache.TryGetValue(key, out var prop))
                return prop;

            for (Type current = type; current != null && prop == null; current = current.BaseType)
                prop = current.GetProperty(name, All | BindingFlags.DeclaredOnly);
            PropertyCache[key] = prop;
            return prop;
        }

        private static MethodInfo GetMethod(Type type, string name, Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
                return null;

            string parameters = parameterTypes != null
                ? string.Join(",", parameterTypes.Select(t => t.FullName).ToArray())
                : string.Empty;

            string key = type.AssemblyQualifiedName + "::method::" + name + "::" + parameters;
            if (MethodCache.TryGetValue(key, out var method))
                return method;

            for (Type current = type; current != null && method == null; current = current.BaseType)
            {
                if (parameterTypes != null)
                {
                    method = current.GetMethod(
                        name,
                        All | BindingFlags.DeclaredOnly,
                        null,
                        parameterTypes,
                        null);
                }
                else
                {
                    method = current
                        .GetMethods(All | BindingFlags.DeclaredOnly)
                        .FirstOrDefault(candidate => candidate.Name == name);
                }
            }

            MethodCache[key] = method;
            return method;
        }
        private static bool LooksLikeSteam64(ulong value)
        {
            // Public Steam individual account IDs are normally in this broad range.
            return value >= 76561190000000000UL && value <= 76561210000000000UL;
        }

    }
}
