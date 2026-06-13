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
        private static FieldInfo _vehicleEngineAudioTypeField;
        private static PropertyInfo _vehicleListSelectableVehiclesProp;
        private static FieldInfo _vehicleListVehiclesField;
        private static PropertyInfo _snowmobileVehicleProp;
        private static FieldInfo _snowmobileVehicleField;
        private static Type _controllerType;
        private static PropertyInfo _controllerInstanceProp;
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
                return _vehicleIdField != null;
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
                return _controllerType != null && _controllerInstanceProp != null;
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

            var report = new AlpineCompatibilityReport();
            PopulateAssemblyFingerprint(report);

            AddCapability(
                report,
                "vehicleData",
                "Vehicle Data",
                _vehicleIdField != null && (_vehicleListSelectableVehiclesProp != null || _vehicleListVehiclesField != null),
                true,
                $"vehicleId={Status(_vehicleIdField != null)}, list={Status(_vehicleListSelectableVehiclesProp != null || _vehicleListVehiclesField != null)}");

            AddCapability(
                report,
                "runtimeController",
                "Runtime Controller",
                _snowmobileVehicleProp != null || _snowmobileVehicleField != null,
                true,
                $"vehicle property={NameOrNull(_snowmobileVehicleProp)}, vehicle field={NameOrNull(_snowmobileVehicleField)}");

            AddCapability(
                report,
                "reload",
                "Ride Reload",
                _controllerType != null && _controllerInstanceProp != null,
                false,
                $"controller={NameOrNull(_controllerType)}, instance={NameOrNull(_controllerInstanceProp)}");

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
                report.assemblyPath = path;

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
            _vehicleEngineAudioTypeField = GetField(typeof(VehicleScriptableObject), "engineAudioType");
            _vehicleListSelectableVehiclesProp = GetProperty(typeof(VehicleListScriptableObject), "SelectableVehicles");
            _vehicleListVehiclesField = GetField(typeof(VehicleListScriptableObject), "vehicles");
            _snowmobileVehicleProp = GetProperty(typeof(SnowmobileController), "GKMNAIKNNMJ");
            _snowmobileVehicleField = GetField(typeof(SnowmobileController), "KJFNKMCOKLL");
            _controllerType = typeof(Controller);
            _controllerInstanceProp = GetProperty(_controllerType, "PKMPAOKMHCB");
        }

        public static string GetVehicleId(VehicleScriptableObject sled, string fallback)
        {
            Initialize();
            if (sled == null || _vehicleIdField == null)
                return fallback;

            try
            {
                var value = _vehicleIdField.GetValue(sled) as string;
                return !string.IsNullOrWhiteSpace(value) ? value : fallback;
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
            return GetFieldValue<SnowmobileController>(pause, "CHJANEKOEDG");
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
                object selection =
                    GetFieldValue<object>(menu, "IHKCPAEBKID") ??
                    GetFieldValue<object>(menu, "DICGGOJLMJP");

                if (TryExtractVehicleScriptableObject(selection, 1, new HashSet<object>(), out var selectedVehicle, out var selectedSource))
                {
                    source = "garage selection " + selectedSource;
                    return selectedVehicle;
                }

                if (TryExtractVehicleScriptableObject(menu, 2, new HashSet<object>(), out var menuVehicle, out var menuSource))
                {
                    source = "garage controller " + menuSource;
                    return menuVehicle;
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
                reason = ex.Message;
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
                reason = ex.Message;
                return false;
            }
        }

        public static object GetStabilizer(object controller)
        {
            return GetFieldValue<object>(controller, "BFJKIBCBFHJ") ??
                   GetFieldValue<object>(controller, "BFJKIBCBFH");
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
                reason = ex.Message;
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
                reason = ex.Message;
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
                reason = ex.Message;
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
                reason = ex.Message;
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

        public static bool TryRegisterNativeTab(
            object nativeTabManager,
            VisualElement tabPanel,
            Button tabButton,
            int insertIndex,
            Action selected,
            out int nativeIndex)
        {
            nativeIndex = -1;

            if (nativeTabManager == null || tabPanel == null || tabButton == null)
                return false;

            var tabPanels = GetFieldValue<List<VisualElement>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabPanelsFieldName);

            var tabButtons = GetFieldValue<List<Button>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabButtonsFieldName);

            if (tabPanels == null || tabButtons == null || tabPanels.Count != tabButtons.Count)
                return false;

            nativeIndex = Mathf.Clamp(insertIndex, 0, tabPanels.Count);

            tabPanels.Insert(nativeIndex, tabPanel);
            tabButtons.Insert(nativeIndex, tabButton);

            var callbacks = GetFieldValue<Dictionary<int, Action>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabCallbacksFieldName);

            if (callbacks != null)
                callbacks[nativeIndex] = selected;

            return true;
        }

        public static void SelectNativeTab(object nativeTabManager, int index)
        {
            if (nativeTabManager == null || index < 0)
                return;

            MethodInfo select = GetMethod(
                nativeTabManager.GetType(),
                AlpineNativeUiConfig.NativeSelectTabMethodName,
                new[] { typeof(int) });

            select?.Invoke(nativeTabManager, new object[] { index });
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

            if (log)
            {
                MelonLogger.Msg("========== ALPINE PEER DISCOVERY DIAG ==========");
                MelonLogger.Msg($"[AlpinePeerDiag] localSteamId={localSteamId}");
                MelonLogger.Msg($"[AlpinePeerDiag] localSleddersClientId={GetLocalSleddersClientId()}");
            }

            try
            {
                object netClient = GetNetClientInstance(log);
                if (netClient == null)
                {
                    if (log)
                    {
                        MelonLogger.Warning("[AlpinePeerDiag] No NetClient singleton instance. You are probably not fully in a multiplayer session yet, or the singleton property name changed.");
                        MelonLogger.Msg("========== ALPINE PEER DISCOVERY END ==========");
                    }

                    return peers.Values.ToArray();
                }

                MethodInfo getIds = ResolveNetClientGetIdsMethod(netClient);
                if (getIds == null)
                {
                    if (log)
                    {
                        MelonLogger.Warning("[AlpinePeerDiag] NetClient.GetAllClientIdsIncludingLocalPlayer method not found.");
                        LogLikelyIdMethods(netClient.GetType());
                        MelonLogger.Msg("========== ALPINE PEER DISCOVERY END ==========");
                    }

                    return peers.Values.ToArray();
                }

                object raw = null;
                try
                {
                    raw = getIds.Invoke(netClient, Array.Empty<object>());
                }
                catch (Exception ex)
                {
                    if (log)
                        MelonLogger.Warning($"[AlpinePeerDiag] getIdsMethod invoke failed: {ex.GetType().Name}: {ex.Message}");
                    return peers.Values.ToArray();
                }

                var result = raw as ulong[];
                if (result == null)
                {
                    if (log)
                        MelonLogger.Warning("[AlpinePeerDiag] GetAllClientIdsIncludingLocalPlayer did not return ulong[].");
                    return peers.Values.ToArray();
                }

                ulong localSleddersId = GetLocalSleddersClientId(netClient);
                if (log)
                {
                    MelonLogger.Msg($"[AlpinePeerDiag] rawClientIds=[{string.Join(", ", result.Select(x => x.ToString()).ToArray())}]");
                    MelonLogger.Msg("[AlpinePeerDiag] rawClientIds are Sledders internal client IDs unless they pass Steam64 range validation.");
                }

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
            catch (Exception ex)
            {
                if (log)
                {
                    MelonLogger.Warning($"[AlpinePeerDiag] DiscoverPeers crashed: {ex.GetType().Name}: {ex.Message}");
                    MelonLogger.Warning(ex.ToString());
                }
            }

            if (log)
            {
                foreach (var peer in peers.Values)
                {
                    MelonLogger.Msg(
                        $"[AlpinePeerDiag] discoveredPeer sleddersClientId={(peer.hasInternalClientId ? peer.sleddersClientId.ToString() : "none")}, " +
                        $"steamId={(peer.hasSteamId ? peer.steamId.ToString() : "none")}, " +
                        $"name={peer.name ?? "NULL"}, source={peer.source ?? "NULL"}");
                }

                MelonLogger.Msg($"[AlpinePeerDiag] filteredRemoteCount={peers.Count}");
                MelonLogger.Msg("========== ALPINE PEER DISCOVERY END ==========");
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
                reason = ex.GetType().Name + ": " + ex.Message;
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
                reason = ex.GetType().Name + ": " + ex.Message;
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
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static object GetNetClientInstance(bool log)
        {
            try
            {
                Initialize();
                ResolvePeerDiscoveryBindings();

                if (log)
                {
                    MelonLogger.Msg($"[AlpinePeerDiag] PeerDiscoveryAvailable={PeerDiscoveryAvailable}");
                    MelonLogger.Msg($"[AlpinePeerDiag] _netClientType={(_netClientType != null ? _netClientType.FullName : "NULL")}");
                    MelonLogger.Msg($"[AlpinePeerDiag] _netClientInstanceProp={(_netClientInstanceProp != null ? _netClientInstanceProp.Name : "NULL")}");
                }

                object netClient = _netClientInstanceProp?.GetValue(null);

                if (log)
                    MelonLogger.Msg($"[AlpinePeerDiag] netClientInstance={(netClient != null ? netClient.GetType().FullName : "NULL")}");

                return netClient;
            }
            catch (Exception ex)
            {
                if (log)
                    MelonLogger.Warning($"[AlpinePeerDiag] NetClient singleton read failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static object GetNetServerInstance(bool log)
        {
            try
            {
                ResolveNetServerBindings();

                if (log)
                {
                    MelonLogger.Msg($"[AlpinePeerDiag] _netServerType={(_netServerType != null ? _netServerType.FullName : "NULL")}");
                    MelonLogger.Msg($"[AlpinePeerDiag] _netServerInstanceProp={(_netServerInstanceProp != null ? _netServerInstanceProp.Name : "NULL")}");
                }

                object netServer = _netServerInstanceProp?.GetValue(null);
                if (netServer == null)
                    netServer = FindNetServerObjectFallback();

                if (log)
                    MelonLogger.Msg($"[AlpinePeerDiag] netServerInstance={(netServer != null ? netServer.GetType().FullName : "NULL")}");

                return netServer;
            }
            catch (Exception ex)
            {
                if (log)
                    MelonLogger.Warning($"[AlpinePeerDiag] NetServer singleton read failed: {ex.GetType().Name}: {ex.Message}");
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

        private static void LogLikelyIdMethods(Type type)
        {
            if (type == null)
                return;

            try
            {
                var methods = type.GetMethods(All)
                    .Where(m =>
                        m.GetParameters().Length == 0 &&
                        (
                            m.Name.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0
                        ))
                    .Take(40)
                    .ToArray();

                MelonLogger.Msg($"[AlpinePeerDiag] Candidate no-arg ID/client/player methods on {type.FullName}: {methods.Length}");

                foreach (var method in methods)
                {
                    MelonLogger.Msg(
                        $"[AlpinePeerDiag] candidateMethod return={method.ReturnType.FullName} name={method.Name}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AlpinePeerDiag] Candidate method scan failed: {ex.Message}");
            }
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
                MelonLogger.Warning($"Engine audio reflection failed: {ex.Message}");
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
                MelonLogger.Warning($"Headlight reflection failed: {ex.Message}");
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
            return type != null
                ? type.GetMethods(All).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == count)
                : null;
        }

        private static FieldInfo GetField(Type type, string name)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
                return null;

            string key = type.AssemblyQualifiedName + "::field::" + name;
            if (FieldCache.TryGetValue(key, out var field))
                return field;

            field = type.GetField(name, All);
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

            prop = type.GetProperty(name, All);
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

            method = parameterTypes != null
                ? type.GetMethod(name, All, null, parameterTypes, null)
                : type.GetMethod(name, All);

            MethodCache[key] = method;
            return method;
        }
        private static void ScanObjectForSteamIds(object obj, string path, int depth, HashSet<object> visited)
        {
            if (obj == null)
                return;

            if (depth > 3)
                return;

            Type type = obj.GetType();

            if (IsUnsafeSteamIdScanType(type))
                return;

            if (!type.IsValueType)
            {
                try
                {
                    if (!visited.Add(obj))
                        return;
                }
                catch
                {
                    return;
                }
            }

            LogIfSteamIdLike(obj, path, type);

            foreach (var field in type.GetFields(All))
            {
                object value = null;
                try
                {
                    value = field.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                string childPath = $"{path}.{field.Name}";
                LogIfSteamIdLike(value, childPath, field.FieldType);

                if (value == null)
                    continue;

                ScanEnumerableForSteamIds(value, childPath, depth, visited);

                if (ShouldDeepScanSteamIdObject(field.FieldType))
                    ScanObjectForSteamIds(value, childPath, depth + 1, visited);
            }

            foreach (var prop in type.GetProperties(All))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0)
                    continue;

                object value = null;
                try
                {
                    value = prop.GetValue(obj, null);
                }
                catch
                {
                    continue;
                }

                string childPath = $"{path}.{prop.Name}";
                LogIfSteamIdLike(value, childPath, prop.PropertyType);

                if (value == null)
                    continue;

                ScanEnumerableForSteamIds(value, childPath, depth, visited);

                if (ShouldDeepScanSteamIdObject(prop.PropertyType))
                    ScanObjectForSteamIds(value, childPath, depth + 1, visited);
            }

            foreach (var method in type.GetMethods(All))
            {
                if (method.GetParameters().Length != 0)
                    continue;

                string lower = method.Name.ToLowerInvariant();
                if (!lower.Contains("steam") &&
                    !lower.Contains("id") &&
                    !lower.Contains("player") &&
                    !lower.Contains("client") &&
                    !lower.Contains("user") &&
                    !lower.Contains("connection"))
                {
                    continue;
                }

                object value = null;
                try
                {
                    value = method.Invoke(obj, Array.Empty<object>());
                }
                catch
                {
                    continue;
                }

                string methodPath = $"{path}.{method.Name}()";
                LogIfSteamIdLike(value, methodPath, method.ReturnType);
                ScanEnumerableForSteamIds(value, methodPath, depth, visited);

                if (value != null && ShouldDeepScanSteamIdObject(method.ReturnType))
                    ScanObjectForSteamIds(value, methodPath, depth + 1, visited);
            }
        }

        private static void ScanEnumerableForSteamIds(object value, string path, int depth, HashSet<object> visited)
        {
            if (value == null || value is string)
                return;

            if (!(value is System.Collections.IEnumerable enumerable))
                return;

            int index = 0;

            try
            {
                foreach (object item in enumerable)
                {
                    if (index >= 64)
                    {
                        MelonLogger.Msg($"[AlpineSteamIdScan] {path}: enumerable truncated at 64 items.");
                        break;
                    }

                    string itemPath = $"{path}[{index}]";
                    LogIfSteamIdLike(item, itemPath, item != null ? item.GetType() : typeof(object));

                    if (item != null)
                        ScanObjectForSteamIds(item, itemPath, depth + 1, visited);

                    index++;
                }
            }
            catch
            {
            }
        }

        private static void LogIfSteamIdLike(object value, string path, Type declaredType)
        {
            if (value == null)
                return;

            try
            {
                if (value is ulong u)
                {
                    if (LooksLikeSteam64(u))
                        MelonLogger.Msg($"[AlpineSteamIdScan] STEAM64 ulong {path} = {u}");
                    else if (u > 0)
                        MelonLogger.Msg($"[AlpineSteamIdScan] nonSteam ulong {path} = {u}");
                    return;
                }

                if (value is long l)
                {
                    if (l > 0 && LooksLikeSteam64((ulong)l))
                        MelonLogger.Msg($"[AlpineSteamIdScan] STEAM64 long {path} = {l}");
                    else if (l > 0)
                        MelonLogger.Msg($"[AlpineSteamIdScan] nonSteam long {path} = {l}");
                    return;
                }

                if (value is uint ui)
                {
                    if (ui > 0)
                        MelonLogger.Msg($"[AlpineSteamIdScan] uint {path} = {ui}");
                    return;
                }

                if (value is int i)
                {
                    if (i > 0)
                        MelonLogger.Msg($"[AlpineSteamIdScan] int {path} = {i}");
                    return;
                }

                string typeName = value.GetType().FullName ?? "";

                if (typeName.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MelonLogger.Msg($"[AlpineSteamIdScan] interesting object {path}: declared={declaredType?.FullName ?? "NULL"}, runtime={typeName}, value={value}");
                }
            }
            catch
            {
            }
        }

        private static bool LooksLikeSteam64(ulong value)
        {
            // Public Steam individual account IDs are normally in this broad range.
            return value >= 76561190000000000UL && value <= 76561210000000000UL;
        }

        private static bool ShouldDeepScanSteamIdObject(Type type)
        {
            if (type == null)
                return false;

            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return false;

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return false;

            string fullName = type.FullName ?? "";

            return
                type.Assembly == typeof(SnowmobileController).Assembly ||
                fullName.IndexOf("Net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnsafeSteamIdScanType(Type type)
        {
            if (type == null)
                return true;

            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                return true;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return true;

            string fullName = type.FullName ?? "";

            return
                fullName.StartsWith("System.Reflection", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LogNetClientSteamIdScan(bool diagnosticScanEnabled)
        {
            if (!diagnosticScanEnabled)
            {
                MelonLogger.Warning("[AlpineSteamIdScan] blocked; enable the Steam ID diagnostic scanner in Alpine Settings first.");
                return false;
            }

            LogNetClientSteamIdScanUnsafe();
            return true;
        }

        private static void LogNetClientSteamIdScanUnsafe()
        {
            MelonLogger.Msg("========== ALPINE NETCLIENT STEAMID SCAN ==========");

            try
            {
                Initialize();
                ResolvePeerDiscoveryBindings();

                MelonLogger.Msg($"[AlpineSteamIdScan] _netClientType={(_netClientType != null ? _netClientType.FullName : "NULL")}");
                MelonLogger.Msg($"[AlpineSteamIdScan] _netClientInstanceProp={(_netClientInstanceProp != null ? _netClientInstanceProp.Name : "NULL")}");

                object netClient = null;
                try
                {
                    netClient = _netClientInstanceProp?.GetValue(null);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AlpineSteamIdScan] NetClient singleton read failed: {ex.GetType().Name}: {ex.Message}");
                }

                if (netClient == null)
                {
                    MelonLogger.Warning("[AlpineSteamIdScan] NetClient instance is NULL.");
                    MelonLogger.Msg("========== ALPINE NETCLIENT STEAMID SCAN END ==========");
                    return;
                }

                Type type = netClient.GetType();
                MelonLogger.Msg($"[AlpineSteamIdScan] netClientInstance={type.FullName}");

                ScanObjectForSteamIds(netClient, "NetClient", 0, new HashSet<object>());

                MelonLogger.Msg("========== ALPINE NETCLIENT STEAMID SCAN END ==========");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AlpineSteamIdScan] scan failed: {ex}");
            }
        }
    }
}
