using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private static bool _initialized;
        private static FieldInfo _vehicleIdField;
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
        private static bool _engineAudioReflectionResolved;
        private static bool _engineAudioReflectionReady;
        private static Type _snowmobileAccessoriesType;
        private static Type _headLightType;
        private static FieldInfo _headLightLightField;
        private static bool _headLightReflectionResolved;
        private static bool _headLightReflectionReady;
        private static Type _netClientType;
        private static PropertyInfo _netClientInstanceProp;
        private static MethodInfo _netClientGetIdsMethod;

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
                Initialize();
                ResolveEngineAudioReflection();
                ResolveHeadlightReflection();
                ResolvePeerDiscoveryBindings();
                return "Reflection capabilities: " +
                       $"vehicleId={VehicleIdAvailable}, " +
                       $"vehicleList={VehicleListAvailable}, " +
                       $"vehicleController={SnowmobileVehicleBindingAvailable}, " +
                       $"reload={ReloadBindingAvailable}, " +
                       $"engineAudio={_engineAudioReflectionReady}, " +
                       $"headlights={_headLightReflectionReady}, " +
                       $"peerDiscovery={PeerDiscoveryAvailable}, " +
                       "nativeUi=guarded";
            }
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

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;

            _vehicleIdField = GetField(typeof(VehicleScriptableObject), "vehicleId");
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
            object selection = GetFieldValue<object>(menu, "DICGGOJLMJP") ?? GetFieldValue<object>(menu, "IHKCPAEBKID");
            if (selection == null)
                return null;

            return GetFieldValue<VehicleScriptableObject>(selection, "KJFNKMCOKLL");
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
            if (controller == null)
                return Array.Empty<Component>();

            try
            {
                if (_snowmobileAccessoriesType == null)
                    _snowmobileAccessoriesType = Type.GetType("SnowmobileAccessories, Assembly-CSharp");

                if (_snowmobileAccessoriesType == null)
                    return Array.Empty<Component>();

                return controller.GetComponentsInChildren(_snowmobileAccessoriesType, true);
            }
            catch
            {
                return Array.Empty<Component>();
            }
        }

        public static Light[] GetHeadlightLights(SnowmobileController controller)
        {
            if (controller == null || !ResolveHeadlightReflection())
                return Array.Empty<Light>();

            try
            {
                var result = new List<Light>();
                var seen = new HashSet<int>();
                var components = controller.GetComponentsInChildren(_headLightType, true);
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
            if (controller == null || !ResolveEngineAudioReflection())
                return null;

            try
            {
                return controller
                    .GetComponentsInChildren(_engineAudioControllerType, true)
                    .FirstOrDefault() as Component;
            }
            catch
            {
                return null;
            }
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
                if (value == null)
                    return false;

                enumTypeName = value.GetType().AssemblyQualifiedName;
                enumName = Enum.GetName(value.GetType(), value);
                enumRawValue = Convert.ToInt32(value);
                return true;
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
                foreach (var field in sled.GetType().GetFields(All))
                {
                    if (field.FieldType != _engineAudioEnumType)
                        continue;

                    object value = field.GetValue(sled);
                    if (value == null)
                        continue;

                    enumTypeName = value.GetType().AssemblyQualifiedName;
                    enumName = Enum.GetName(value.GetType(), value);
                    enumRawValue = Convert.ToInt32(value);
                    return true;
                }

                foreach (var prop in sled.GetType().GetProperties(All))
                {
                    if (prop.PropertyType != _engineAudioEnumType || !prop.CanRead)
                        continue;

                    object value = prop.GetValue(sled);
                    if (value == null)
                        continue;

                    enumTypeName = value.GetType().AssemblyQualifiedName;
                    enumName = Enum.GetName(value.GetType(), value);
                    enumRawValue = Convert.ToInt32(value);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
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
                Type enumType = Type.GetType(enumTypeName) ?? _engineAudioEnumType;
                if (enumType == null)
                {
                    reason = "engine audio enum unavailable";
                    return false;
                }

                object desiredValue = !string.IsNullOrWhiteSpace(enumName) &&
                                      Enum.IsDefined(enumType, enumName)
                    ? Enum.Parse(enumType, enumName)
                    : Enum.ToObject(enumType, enumRawValue);

                bool canHardRestart =
                    _miStopEngineSound != null &&
                    _miStopEngineSound.GetParameters().Length == 0 &&
                    _miEngineInit != null &&
                    _miEngineInit.GetParameters().Length == 0;

                if (canHardRestart)
                {
                    _miStopEngineSound.Invoke(audioController, Array.Empty<object>());
                    _miSetEngineType.Invoke(audioController, new[] { desiredValue });
                    _miEngineInit.Invoke(audioController, Array.Empty<object>());
                }
                else
                {
                    _miSetEngineType.Invoke(audioController, new[] { desiredValue });
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
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

        public static IEnumerable<ulong> DiscoverPeerIds(ulong local)
        {
            var ids = new HashSet<ulong>();

            try
            {
                ResolvePeerDiscoveryBindings();
                object netClient = _netClientInstanceProp?.GetValue(null);
                if (netClient == null)
                    return ids;

                if (_netClientGetIdsMethod == null || _netClientGetIdsMethod.DeclaringType != netClient.GetType())
                {
                    _netClientGetIdsMethod = GetMethod(
                        netClient.GetType(),
                        "GetAllClientIdsIncludingLocalPlayer",
                        Type.EmptyTypes);
                }

                var result = _netClientGetIdsMethod?.Invoke(netClient, Array.Empty<object>()) as ulong[];
                if (result == null)
                    return ids;

                foreach (ulong id in result)
                {
                    if (id != 0 && id != local)
                        ids.Add(id);
                }
            }
            catch
            {
            }

            return ids;
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
                _miEngineInit = FindMethodByNameAndParamCount(_engineAudioControllerType, "Init", 0);
                _miStopEngineSound = FindMethodByNameAndParamCount(_engineAudioControllerType, "StopEngineSound", 0);

                if (_miSetEngineType != null)
                    _engineAudioEnumType = _miSetEngineType.GetParameters()[0].ParameterType;

                if (_engineAudioEnumType == null && _fiCurrentEngineType != null)
                    _engineAudioEnumType = _fiCurrentEngineType.FieldType;

                _engineAudioReflectionReady =
                    _engineAudioControllerType != null &&
                    _engineAudioEnumType != null &&
                    _miSetEngineType != null;

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
            _netClientInstanceProp = GetProperty(_netClientType, "PKMPAOKMHCB");
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
    }
}
