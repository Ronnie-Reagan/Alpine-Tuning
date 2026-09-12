using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AlpineTuning
{
    [Serializable]
    internal sealed class NitrousRuntimeState
    {
        public float chargeSeconds;
        public float capacitySeconds;
        public bool initialized;
    }

    [Serializable]
    internal sealed class NitrousStateFile
    {
        public int schemaVersion = 1;
        public Dictionary<string, NitrousRuntimeState> sleds =
            new Dictionary<string, NitrousRuntimeState>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A resource-backed power modifier. Native power is changed only for the
    /// duration of UpdateSimulation and is restored by both postfix and finalizer.
    /// Charge is independent from the recreated controller graph.
    /// </summary>
    internal sealed class AlpineNitrousSystem
    {
        private const float SaveIntervalSeconds = 1.5f;
        private const KeyCode DefaultKeyboardKey = KeyCode.LeftShift;
        private readonly AlpineTuningMod _mod;
        private readonly AlpineControllerInput _controllerInput = new AlpineControllerInput();
        private readonly Dictionary<int, PowerSnapshot> _powerSnapshots = new Dictionary<int, PowerSnapshot>();
        private readonly Dictionary<int, StationRefillState> _stationStates = new Dictionary<int, StationRefillState>();
        private NitrousStateFile _state = new NitrousStateFile();
        private SnowmobileController _controller;
        private VehicleScriptableObject _sled;
        private NitrousRuntimeState _record;
        private float _capacity;
        private float _boostPercent = 100f;
        private float _nextSave;
        private bool _dirty;
        private bool _spraying;
        private bool _emptyNotified;
        private bool _thirdNotified;
        private bool _lowNotified;
        private bool _unavailableLatched;
        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;

        private struct PowerSnapshot
        {
            public object mesh;
            public float power;
        }

        private sealed class StationRefillState
        {
            public float pressedAt;
            public float nextTick;
            public bool started;
        }

        internal AlpineNitrousSystem(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        internal float ChargeRatio => _capacity > 0f && _record != null
            ? Mathf.Clamp01(_record.chargeSeconds / _capacity)
            : 0f;
        internal bool HasInstalledKit => _capacity > 0f;
        internal float ChargeSeconds => _record != null ? Mathf.Max(0f, _record.chargeSeconds) : 0f;
        internal float CapacitySeconds => Mathf.Max(0f, _capacity);

        private static string StatePath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning", "nitrous-state.json");

        internal void Initialize()
        {
            LoadState();
            MelonLogger.Msg("Alpine nitrous subsystem initialized.");
        }

        internal void OnControllerInitialized(SnowmobileController controller, VehicleScriptableObject sled)
        {
            RestoreAllPower();
            _controller = controller;
            _sled = sled;
            _capacity = 0f;
            _spraying = false;
            BindRecord();
        }

        internal void OnProfileApplied(
            SnowmobileController controller,
            VehicleScriptableObject sled,
            TuneComputation computation,
            TuneProfile profile)
        {
            RestoreAllPower();
            _controller = controller;
            _sled = sled;
            _capacity = computation != null && computation.mergedEffect != null
                ? Mathf.Max(0f, computation.mergedEffect.nitrousCapacitySeconds)
                : 0f;
            _boostPercent = profile != null && profile.fineTune != null
                ? Mathf.Clamp(profile.fineTune.nitrousBoostPercent, 25f, 200f)
                : 100f;
            BindRecord();

            if (_record == null)
                return;
            if (!_record.initialized && _capacity > 0f)
            {
                _record.chargeSeconds = _capacity;
                _record.initialized = true;
            }
            _record.capacitySeconds = _capacity;
            _record.chargeSeconds = Mathf.Clamp(_record.chargeSeconds, 0f, _capacity);
            float installedRatio = _capacity > 0f ? _record.chargeSeconds / _capacity : 0f;
            _emptyNotified = installedRatio <= 0.001f;
            _thirdNotified = installedRatio <= 0.33f;
            _lowNotified = installedRatio <= 0.10f;
            _dirty = true;
        }

        internal void Update()
        {
            UpdateFieldRefill();
            if (_dirty && Time.unscaledTime >= _nextSave)
                SaveState(false);
        }

        internal void BeforeSimulation(SnowmobileController controller)
        {
            if (controller == null || controller != _controller ||
                !_mod.Settings.alpineTuningEnabled || !_mod.IsNitrousRuntimeAllowed())
            {
                _spraying = false;
                return;
            }

            bool requested = ActivationRequested(controller);
            if (!requested)
                _unavailableLatched = false;
            if (_capacity <= 0f || _record == null)
            {
                _spraying = false;
                if (requested && !_unavailableLatched)
                {
                    _unavailableLatched = true;
                    Notify("Nitrous unavailable - install an engine nitrous kit");
                }
                return;
            }
            if (_record.chargeSeconds <= 0f)
            {
                _spraying = false;
                if (requested && !_unavailableLatched)
                {
                    _unavailableLatched = true;
                    Notify("Nitrous unavailable - bottle empty");
                }
                return;
            }

            if (!IsEngineOn(controller) || !requested)
            {
                _spraying = false;
                return;
            }

            object controllerBase = SleddersGameBindings.GetFieldValue<object>(controller, "controllerBase");
            object mesh = controllerBase != null
                ? SleddersGameBindings.GetFieldValue<object>(controllerBase, "meshInterpretter")
                : null;
            if (mesh == null || !TryReadFloat(mesh, "power", out float nativePower))
                return;

            int id = controller.GetInstanceID();
            if (_powerSnapshots.ContainsKey(id))
                RestorePower(controller);
            _powerSnapshots[id] = new PowerSnapshot { mesh = mesh, power = nativePower };
            SleddersGameBindings.SetFieldValue(mesh, "power", nativePower * ComputePowerMultiplier(_boostPercent));

            float consumptionScale = ComputeConsumptionScale(_boostPercent);
            _record.chargeSeconds = Mathf.Max(0f,
                _record.chargeSeconds - Mathf.Max(0.0001f, Time.fixedDeltaTime) * consumptionScale);
            _spraying = true;
            _dirty = true;
            float ratio = _capacity > 0f ? _record.chargeSeconds / _capacity : 0f;
            if (ratio <= 0.33f && !_thirdNotified)
            {
                _thirdNotified = true;
                Notify("Nitrous charge at 33%");
            }
            if (ratio <= 0.10f && !_lowNotified)
            {
                _lowNotified = true;
                Notify("Nitrous charge low - 10%");
            }
            if (_record.chargeSeconds <= 0.001f && !_emptyNotified)
            {
                _emptyNotified = true;
                Notify("Nitrous bottle empty");
            }
        }

        internal void AfterSimulation(SnowmobileController controller)
        {
            RestorePower(controller);
        }

        internal void SuspendRuntime()
        {
            RestoreAllPower();
            _spraying = false;
            SaveState(true);
        }

        internal void Shutdown()
        {
            SuspendRuntime();
            _controller = null;
            _sled = null;
            _record = null;
        }

        internal void Refill(float normalizedAmount)
        {
            if (_record == null || _capacity <= 0f)
                return;
            float previous = _record.chargeSeconds;
            _record.chargeSeconds = Mathf.Clamp(
                _record.chargeSeconds + _capacity * Mathf.Max(0f, normalizedAmount), 0f, _capacity);
            if (_record.chargeSeconds > previous)
            {
                _emptyNotified = false;
                float ratio = _record.chargeSeconds / Mathf.Max(0.001f, _capacity);
                if (ratio > 0.33f) _thirdNotified = false;
                if (ratio > 0.10f) _lowNotified = false;
                _dirty = true;
                if (_record.chargeSeconds >= _capacity - 0.001f)
                    Notify("Nitrous refill complete");
            }
        }

        private void UpdateFieldRefill()
        {
            if (!Input.GetKeyDown(KeyCode.N) || _controller == null || _record == null || _capacity <= 0f ||
                !_mod.Settings.alpineTuningEnabled || AlpineNativeUi.HasAttachedMenus)
                return;

            Rigidbody body = _controller.GetComponentsInChildren<Rigidbody>(true)
                .Where(candidate => candidate != null && !candidate.isKinematic)
                .OrderByDescending(candidate => candidate.mass)
                .FirstOrDefault();
            if (body == null || body.linearVelocity.sqrMagnitude > 0.35f * 0.35f || IsEngineOn(_controller))
            {
                Notify("Park and switch off the engine before refilling nitrous");
                return;
            }
            if (_record.chargeSeconds >= _capacity - 0.001f)
            {
                Notify("Nitrous bottle is already full");
                return;
            }

            Refill(1f);
        }

        internal bool HandleFuelStationInput(FuelStation station, object input)
        {
            if (station == null || _controller == null || _capacity <= 0f || _record == null ||
                !_mod.Settings.alpineTuningEnabled || _mod.Settings.refillTarget == AlpineRefillTarget.Fuel)
                return true;
            if (Vector3.Distance(station.transform.position, _controller.transform.position) > 15f)
                return true;

            bool pressed = ReadNativeRefuelPressed(input);
            int id = station.GetInstanceID();
            if (!_stationStates.TryGetValue(id, out StationRefillState state))
            {
                state = new StationRefillState();
                _stationStates[id] = state;
            }
            if (!pressed)
            {
                if (state.started) PostStationEvent(station, "refuelStopEvent");
                state.pressedAt = 0f;
                state.nextTick = 0f;
                state.started = false;
                return _mod.Settings.refillTarget != AlpineRefillTarget.Nitrous;
            }

            if (state.pressedAt <= 0f) state.pressedAt = Time.unscaledTime;
            float hold = Mathf.Max(0f, ReadStationFloat(station, "refuelInputHoldTime", 0.5f));
            if (Time.unscaledTime - state.pressedAt < hold)
                return _mod.Settings.refillTarget != AlpineRefillTarget.Nitrous;
            if (!state.started)
            {
                state.started = true;
                PostStationEvent(station, "refuelStartEvent");
                StopEngine(_controller);
            }
            if (Time.unscaledTime >= state.nextTick && _record.chargeSeconds < _capacity - 0.001f)
            {
                float cooldown = Mathf.Max(0.05f, ReadStationFloat(station, "refuelTickCooldown", 0.2f));
                state.nextTick = Time.unscaledTime + cooldown;
                Refill(cooldown / 3f);
                PostStationEvent(station, "refuelTickEvent");
            }
            return _mod.Settings.refillTarget != AlpineRefillTarget.Nitrous;
        }

        internal void DrawOverlay()
        {
            if (_capacity <= 0f || _record == null)
                return;
            EnsureStyles();
            DrawStationPrompt();
            if (!_mod.Settings.showNitrousOverlay)
                return;
            float ratio = _capacity > 0f ? Mathf.Clamp01(_record.chargeSeconds / _capacity) : 0f;
            Rect box = new Rect(Screen.width - 252f, Screen.height - 142f, 232f, 90f);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 12f, box.y + 8f, 208f, 22f),
                _spraying ? "NITROUS — SPRAYING" : "NITROUS", _headerStyle);
            GUI.Label(new Rect(box.x + 12f, box.y + 32f, 208f, 20f),
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F0}%  •  {1:F1}s / {2:F0}s", ratio * 100f, _record.chargeSeconds, _capacity),
                _textStyle);
            GUI.Label(new Rect(box.x + 12f, box.y + 53f, 208f, 20f),
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "+{0:F0}% • {1}", _boostPercent,
                    _mod.Settings.nitrousActivationMode == AlpineNitrousActivationMode.Hold
                        ? "Hold to spray" : "Automatic WOT"), _textStyle);
        }

        internal static float ComputePowerMultiplier(float boostPercent)
        {
            return 1f + Mathf.Clamp(boostPercent, 25f, 200f) / 100f;
        }

        internal static float ComputeConsumptionScale(float boostPercent)
        {
            return Mathf.Clamp(boostPercent, 25f, 200f) / 100f;
        }

        private void DrawStationPrompt()
        {
            FuelStation station = null;
            try { station = FuelStation.FindNearestActivated(_controller.transform.position); } catch { }
            if (station == null || Vector3.Distance(station.transform.position, _controller.transform.position) > 15f)
                return;
            string target = _mod.Settings.refillTarget == AlpineRefillTarget.Both
                ? "Fuel + Nitrous"
                : _mod.Settings.refillTarget.ToString();
            string status = _record.chargeSeconds >= _capacity - 0.001f
                ? "Nitrous full"
                : (_stationStates.TryGetValue(station.GetInstanceID(), out StationRefillState refill) && refill.started
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Refilling nitrous {0:F0}% - {1}", ChargeRatio * 100f, target)
                    : "Hold native refuel input - " + target);
            GUI.Box(new Rect(Screen.width * 0.5f - 145f, Screen.height - 92f, 290f, 32f), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f - 133f, Screen.height - 86f, 266f, 22f), status, _textStyle);
        }

        private bool ActivationRequested(SnowmobileController controller)
        {
            if (_mod.Settings.nitrousActivationMode == AlpineNitrousActivationMode.AutomaticWot)
                return TryReadThrottle(controller, out float throttle) &&
                       throttle >= _mod.Settings.nitrousWotThreshold;

            KeyCode key = DefaultKeyboardKey;
            if (!string.IsNullOrWhiteSpace(_mod.Settings.nitrousKeyboardKey) &&
                !Enum.TryParse(_mod.Settings.nitrousKeyboardKey, true, out key))
                key = DefaultKeyboardKey;
            bool keyboard = Input.GetKey(key);
            return keyboard || _controllerInput.BindingHeld(_mod.Settings.nitrousControllerButton);
        }

        private static bool TryReadThrottle(SnowmobileController controller, out float throttle)
        {
            throttle = 0f;
            object input = SleddersGameBindings.GetFieldValue<object>(controller, "GJKCDNOBELI");
            return input != null && TryReadFloat(input, "AINANLMJJDH", out throttle);
        }

        private static bool ReadNativeRefuelPressed(object input)
        {
            object current = input;
            string[] unwrap = { "DMAPDEINCBB", "GMLFBKNHDLB" };
            foreach (string name in unwrap)
            {
                if (current == null) return false;
                try
                {
                    MethodInfo method = current.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method != null && method.GetParameters().Length == 0) current = method.Invoke(current, null);
                }
                catch { return false; }
            }
            if (current == null) return false;
            try
            {
                PropertyInfo pressed = current.GetType().GetProperty("IsPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pressed != null && pressed.PropertyType == typeof(bool)) return (bool)pressed.GetValue(current, null);
                MethodInfo method = current.GetType().GetMethod("IsPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                return method != null && method.ReturnType == typeof(bool) && (bool)method.Invoke(current, null);
            }
            catch { return false; }
        }

        private static float ReadStationFloat(FuelStation station, string name, float fallback)
        {
            return SleddersGameBindings.TryGetFieldValue(station, name, out float value) ? value : fallback;
        }

        private static void StopEngine(SnowmobileController controller)
        {
            try
            {
                MethodInfo direct = controller.GetType().GetMethod("SetEngineOnOff", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (direct != null)
                {
                    direct.Invoke(controller, new object[] { false });
                    return;
                }
                MethodInfo setter = controller.GetType().GetMethod("set_IsEngineOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setter != null) setter.Invoke(controller, new object[] { false });
                else SleddersGameBindings.SetFieldValue(controller, "isEngineOn", false);
            }
            catch { }
        }

        private static void PostStationEvent(FuelStation station, string fieldName)
        {
            try
            {
                object audioEvent = SleddersGameBindings.GetFieldValue<object>(station, fieldName);
                MethodInfo post = audioEvent?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method => method.Name == "Post" && method.GetParameters().Length == 1 &&
                                              method.GetParameters()[0].ParameterType == typeof(GameObject));
                post?.Invoke(audioEvent, new object[] { station.gameObject });
            }
            catch { }
        }

        private static bool IsEngineOn(SnowmobileController controller)
        {
            if (controller == null)
                return false;
            try
            {
                MethodInfo method = controller.GetType().GetMethod(
                    "get_IsEngineOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null && method.ReturnType == typeof(bool))
                    return (bool)method.Invoke(controller, null);
                // Sledders 1.1.6 exposes this as a field, not a property. The old
                // property-only lookup left every nitrous request inactive.
                return SleddersGameBindings.TryGetFieldValue(controller, "isEngineOn", out bool engineOn) &&
                       engineOn;
            }
            catch { return false; }
        }

        private static bool TryReadFloat(object target, string fieldName, out float value)
        {
            value = 0f;
            if (!SleddersGameBindings.TryGetFieldValue(target, fieldName, out object raw) || raw == null)
                return false;
            try { value = Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture); return true; }
            catch { return false; }
        }

        private void RestorePower(SnowmobileController controller)
        {
            if (controller == null)
                return;
            int id = controller.GetInstanceID();
            if (!_powerSnapshots.TryGetValue(id, out PowerSnapshot snapshot))
                return;
            if (snapshot.mesh != null)
                SleddersGameBindings.SetFieldValue(snapshot.mesh, "power", snapshot.power);
            _powerSnapshots.Remove(id);
        }

        private void RestoreAllPower()
        {
            foreach (PowerSnapshot snapshot in _powerSnapshots.Values)
            {
                if (snapshot.mesh != null)
                    SleddersGameBindings.SetFieldValue(snapshot.mesh, "power", snapshot.power);
            }
            _powerSnapshots.Clear();
        }

        private void BindRecord()
        {
            _record = null;
            if (_sled == null)
                return;
            string key = SledIdentity.StableIdentityKey(_sled);
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (!_state.sleds.TryGetValue(key, out _record) || _record == null)
            {
                _record = new NitrousRuntimeState();
                _state.sleds[key] = _record;
            }
        }

        private void LoadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return;
                NitrousStateFile loaded = JsonConvert.DeserializeObject<NitrousStateFile>(File.ReadAllText(StatePath));
                if (loaded != null && loaded.schemaVersion == 1 && loaded.sleds != null)
                    _state = loaded;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Nitrous state was reset: {ex.GetType().Name}");
                _state = new NitrousStateFile();
            }
        }

        private void SaveState(bool force)
        {
            if (!force && !_dirty)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                string temporary = StatePath + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(_state, Formatting.Indented));
                if (File.Exists(StatePath))
                    File.Replace(temporary, StatePath, null);
                else
                    File.Move(temporary, StatePath);
                _dirty = false;
                _nextSave = Time.unscaledTime + SaveIntervalSeconds;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Nitrous state save skipped: {ex.GetType().Name}");
            }
        }

        private static void Notify(string text)
        {
            try
            {
                object instance = typeof(NotificationUIController).GetProperty(
                    "Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                MethodInfo notify = instance?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "Notify" && m.GetParameters().Length >= 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(string));
                if (notify == null)
                    return;
                ParameterInfo[] parameters = notify.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = text;
                for (int i = 1; i < args.Length; i++)
                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue :
                        (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null);
                notify.Invoke(instance, args);
            }
            catch { }
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null)
                return;
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.9f, 1f) }
            };
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
        }
    }
}
