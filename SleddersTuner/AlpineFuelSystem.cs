using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpineTuning
{
    [Serializable]
    internal sealed class AlpineFuelStateRecord
    {
        public float tankLiters = -1f;
        public float lastTankCapacityLiters;
        public float backpackLiters;
        public float backpackCapacityLiters;
    }

    [Serializable]
    internal sealed class AlpineFuelStateFile
    {
        public int schemaVersion = 1;
        public Dictionary<string, AlpineFuelStateRecord> sleds =
            new Dictionary<string, AlpineFuelStateRecord>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Alpine's thin compatibility layer around Sledders' native fuel system.
    /// It deliberately keeps FuelManager, stations, native HUD state and native
    /// out-of-fuel behavior intact; only incorrect/omitted simulation behavior and
    /// Alpine-specific persistence/reserve fuel are layered on top.
    /// </summary>
    internal sealed class AlpineFuelSystem
    {
        internal const float GasolineDensityKgPerLiter = 0.74f;
        private const float ConsumptionWindowSeconds = 2.5f;
        private const float StationarySpeedMetersPerSecond = 0.35f;
        private const float SaveIntervalSeconds = 2f;

        private readonly AlpineTuningMod _mod;
        private AlpineFuelStateFile _state = new AlpineFuelStateFile();
        private readonly Dictionary<int, float> _fuelBeforeSimulation = new Dictionary<int, float>();
        private readonly Dictionary<string, float> _pendingTankLiters =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FuelSample> _samples = new List<FuelSample>();

        private SnowmobileController _controller;
        private VehicleScriptableObject _sled;
        private SledDefaults _baseDefaults;
        private ResolvedStats _stats;
        private PartEffect _effect;
        private Rigidbody _mainBody;
        private float _runtimeBaselineMass;
        private Vector3 _runtimeBaselineCenterOfMass;
        private bool _hasRuntimeBaseline;
        private float _lastAppliedDynamicMass = float.NaN;
        private Vector3 _lastAppliedDynamicCenterOfMass;

        private Component _fuelManager;
        private float _nextFuelManagerScan;
        private float _nextHudScan;
        private float _nextPersistenceSave;
        private float _nextSampleTime;
        private float _lastObservedCapacity = -1f;
        private bool _dirty;

        private Label _consumptionLabel;
        private ControlIndicatorButton _stationaryRefuelButton;
        private ControlIndicatorButton _rescueRefuelButton;

        private struct FuelSample
        {
            public float time;
            public float liters;
            public Vector3 position;
        }

        internal AlpineFuelSystem(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        private static string StatePath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning", "fuel-state.json");

        internal void Initialize()
        {
            if (_mod.Settings.persistentFuelLevelsEnabled)
                LoadState();
            else
                _state = new AlpineFuelStateFile();
        }

        internal void Shutdown()
        {
            SaveState(true);
            RemoveHudAdditions();
            RestoreRuntimePayloadMass();
            _controller = null;
            _sled = null;
        }

        internal void OnControllerInitializing(SnowmobileController controller, VehicleScriptableObject sled)
        {
            if (controller == null || sled == null)
                return;

            // ReCreateSnowmobile may retain the old controller while rebuilding its
            // child graph. Capture liters before any new capacity is copied in.
            if (_mod.Settings.alpineTuningEnabled &&
                AlpineTuningMod.ActiveController != null &&
                AlpineTuningMod.ActiveSO != null &&
                SameSled(AlpineTuningMod.ActiveSO, sled))
            {
                float liters;
                if (TryGetTankLiters(AlpineTuningMod.ActiveController, AlpineTuningMod.ActiveSO, out liters))
                    _pendingTankLiters[Identity(sled)] = Mathf.Max(0f, liters);
            }
        }

        internal void PrepareProfileInstall(
            VehicleScriptableObject sled,
            TuneComputation computation)
        {
            if (sled == null || computation == null || computation.stats == null)
                return;

            string key = Identity(sled);
            AlpineFuelStateRecord record = GetOrCreateRecord(key);

            float previousBackpackCapacity = Mathf.Max(0f, record.backpackCapacityLiters);
            float nextBackpackCapacity = Mathf.Max(0f, computation.stats.backpackFuelCapacityLiters);
            if (nextBackpackCapacity <= 0.001f)
            {
                record.backpackLiters = 0f;
                record.backpackCapacityLiters = 0f;
            }
            else
            {
                if (previousBackpackCapacity <= 0.001f)
                    record.backpackLiters = nextBackpackCapacity;
                else
                    record.backpackLiters = Mathf.Min(Mathf.Max(0f, record.backpackLiters), nextBackpackCapacity);
                record.backpackCapacityLiters = nextBackpackCapacity;
            }

            if (SameSled(sled, AlpineTuningMod.ActiveSO) && AlpineTuningMod.ActiveController != null)
            {
                float liters;
                if (TryGetTankLiters(AlpineTuningMod.ActiveController, sled, out liters))
                    _pendingTankLiters[key] = liters;
            }

            _dirty = true;
        }

        internal void OnProfileApplied(
            SnowmobileController controller,
            VehicleScriptableObject sled,
            TuneComputation computation)
        {
            if (controller == null || sled == null || computation == null || computation.stats == null)
                return;

            _controller = controller;
            _sled = sled;
            _baseDefaults = computation.baseDefaults;
            _stats = computation.stats;
            _effect = computation.mergedEffect;
            ApplyRuntimePayloadMassAndCog(true);
        }

        internal void OnControllerInitialized(SnowmobileController controller, VehicleScriptableObject sled)
        {
            RestoreRuntimePayloadMass();
            _controller = controller;
            _sled = sled;
            _baseDefaults = null;
            _stats = null;
            _effect = null;
            _samples.Clear();
            _lastObservedCapacity = -1f;
            _nextSampleTime = 0f;
            _mainBody = FindMainBody(controller);
            _hasRuntimeBaseline = _mainBody != null;
            if (_hasRuntimeBaseline)
            {
                _runtimeBaselineMass = _mainBody.mass;
                _runtimeBaselineCenterOfMass = _mainBody.centerOfMass;
                _lastAppliedDynamicMass = _runtimeBaselineMass;
                _lastAppliedDynamicCenterOfMass = _runtimeBaselineCenterOfMass;
            }

            if (controller == null || sled == null || !_mod.Settings.alpineTuningEnabled)
                return;

            string key = Identity(sled);
            float capacity = GetFuelCapacity(controller, sled);
            float litersToRestore = -1f;
            if (_pendingTankLiters.TryGetValue(key, out float pending))
            {
                litersToRestore = pending;
                _pendingTankLiters.Remove(key);
            }
            else if (_mod.Settings.persistentFuelLevelsEnabled &&
                     _state.sleds.TryGetValue(key, out AlpineFuelStateRecord record) &&
                     record != null && record.tankLiters >= 0f)
            {
                litersToRestore = record.tankLiters;
            }

            if (litersToRestore >= 0f && capacity > 0.01f)
                SetFuelNormalized(controller, Mathf.Clamp01(Mathf.Min(litersToRestore, capacity) / capacity));

            CaptureCurrentFuelState();
            ApplyRuntimePayloadMassAndCog(true);
        }

        internal void Update()
        {
            if (_controller == null || _sled == null || !_mod.Settings.alpineTuningEnabled)
            {
                SetHudVisible(false);
                return;
            }

            float now = Time.unscaledTime;
            if (now >= _nextSampleTime)
            {
                _nextSampleTime = now + 0.10f;
                CaptureConsumptionSample(now);
                CaptureCurrentFuelState();
                ApplyRuntimePayloadMassAndCog(false);
            }

            if (now >= _nextHudScan)
            {
                _nextHudScan = now + 1f;
                EnsureNativeHudAdditions();
            }
            UpdateHudText();
            UpdateRefuelButtonAvailability();

            if (_dirty && now >= _nextPersistenceSave)
            {
                _nextPersistenceSave = now + SaveIntervalSeconds;
                SaveState(false);
            }
        }

        internal void BeforeFuelSimulation(SnowmobileController controller)
        {
            if (controller == null || !_mod.Settings.alpineTuningEnabled)
                return;

            float fuel;
            if (TryGetFuelNormalized(controller, out fuel))
                _fuelBeforeSimulation[controller.GetInstanceID()] = fuel;
        }

        internal void AfterFuelSimulation(SnowmobileController controller)
        {
            if (controller == null || !_mod.Settings.alpineTuningEnabled)
                return;

            int id = controller.GetInstanceID();
            if (!_fuelBeforeSimulation.TryGetValue(id, out float before))
                return;
            _fuelBeforeSimulation.Remove(id);

            if (!IsFuelUsageEnabled(controller))
                return;

            float after;
            if (!TryGetFuelNormalized(controller, out after))
                return;

            // Native reverse produces negative drivetrain power and therefore adds
            // fuel. Reflect that positive delta around the pre-step fuel value so
            // the result is identical to consuming abs(drivetrainPower).
            if (after > before + 0.0000001f)
            {
                float erroneousGain = after - before;
                after = Mathf.Clamp01(before - erroneousGain);
                SetFuelNormalized(controller, after);
            }

            if (!_mod.Settings.idleFuelConsumptionEnabled || !IsEngineOn(controller))
                return;

            float capacity = GetFuelCapacity(controller, _sled);
            if (capacity <= 0.01f || before <= 0f)
                return;

            float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float idleLitersPerHour = EstimateIdleLitersPerHour(_sled);
            float requiredNormalizedDrain = idleLitersPerHour / 3600f / capacity * dt;
            float actualNormalizedDrain = Mathf.Max(0f, before - after);
            if (actualNormalizedDrain + 0.0000001f < requiredNormalizedDrain)
            {
                float additional = requiredNormalizedDrain - actualNormalizedDrain;
                SetFuelNormalized(controller, Mathf.Max(0f, after - additional));
            }
        }

        internal bool TryGetCapacityOverflowWarning(
            VehicleScriptableObject sled,
            ResolvedStats preview,
            out string warning)
        {
            warning = null;
            if (sled == null || preview == null)
                return false;

            float liters = GetKnownTankLiters(sled);
            float nextCapacity = Mathf.Max(0.01f, preview.fuelCapacity);
            if (liters <= nextCapacity + 0.01f)
                return false;

            float overflow = liters - nextCapacity;
            warning = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Current fuel is {0:F1} L but this tank holds {1:F1} L. Saving will discard {2:F1} L of overflow.",
                liters,
                nextCapacity,
                overflow);
            return true;
        }

        internal float GetKnownTankLiters(VehicleScriptableObject sled)
        {
            if (sled == null)
                return 0f;

            if (SameSled(sled, _sled) && _controller != null)
            {
                float live;
                if (TryGetTankLiters(_controller, sled, out live))
                    return live;
            }

            string key = Identity(sled);
            if (_state.sleds.TryGetValue(key, out AlpineFuelStateRecord record) && record != null && record.tankLiters >= 0f)
                return record.tankLiters;
            return Mathf.Max(0f, sled.fuelCapacity);
        }

        internal bool HasWornCosmeticBackpack()
        {
            return true;
        }

        internal float BackpackFuelRemaining
        {
            get
            {
                AlpineFuelStateRecord record = CurrentRecord();
                return record != null ? Mathf.Max(0f, record.backpackLiters) : 0f;
            }
        }

        internal float BackpackFuelCapacity
        {
            get
            {
                AlpineFuelStateRecord record = CurrentRecord();
                return record != null ? Mathf.Max(0f, record.backpackCapacityLiters) : 0f;
            }
        }

        internal bool TryRefuelFromBackpack(out string status)
        {
            status = null;
            if (_controller == null || _sled == null || !_mod.Settings.alpineTuningEnabled)
            {
                status = "No active sled.";
                return false;
            }
            if (!IsStationary(_controller))
            {
                status = "Stop the sled first.";
                return false;
            }
            if (!HasWornCosmeticBackpack())
            {
                status = "Wear a cosmetic backpack to use reserve fuel.";
                return false;
            }

            AlpineFuelStateRecord record = CurrentRecord();
            if (record == null || record.backpackLiters <= 0.001f)
            {
                status = "Backpack reserve is empty.";
                return false;
            }

            float capacity = GetFuelCapacity(_controller, _sled);
            float normalized;
            if (capacity <= 0.01f || !TryGetFuelNormalized(_controller, out normalized))
            {
                status = "Native fuel tank is unavailable.";
                return false;
            }

            float currentLiters = Mathf.Clamp01(normalized) * capacity;
            float needed = Mathf.Max(0f, capacity - currentLiters);
            if (needed <= 0.01f)
            {
                status = "Fuel tank is full.";
                return false;
            }

            TrySetEngineOff(_controller);
            float transfer = Mathf.Min(needed, record.backpackLiters);
            record.backpackLiters = Mathf.Max(0f, record.backpackLiters - transfer);
            currentLiters += transfer;
            SetFuelNormalized(_controller, Mathf.Clamp01(currentLiters / capacity));
            record.tankLiters = currentLiters;
            record.lastTankCapacityLiters = capacity;
            _dirty = true;
            _samples.Clear();
            ApplyRuntimePayloadMassAndCog(true);
            status = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Transferred {0:F1} L from backpack.",
                transfer);
            return true;
        }

        internal void RestoreRuntimePayloadMass()
        {
            if (_hasRuntimeBaseline && _mainBody != null)
            {
                try
                {
                    _mainBody.mass = _runtimeBaselineMass;
                    _mainBody.centerOfMass = _runtimeBaselineCenterOfMass;
                }
                catch
                {
                    // Unity may be tearing down the old graph.
                }
            }
            _mainBody = null;
            _hasRuntimeBaseline = false;
            _lastAppliedDynamicMass = float.NaN;
        }

        private void CaptureCurrentFuelState()
        {
            if (!_mod.Settings.persistentFuelLevelsEnabled || _controller == null || _sled == null)
                return;

            float liters;
            if (!TryGetTankLiters(_controller, _sled, out liters))
                return;
            AlpineFuelStateRecord record = GetOrCreateRecord(Identity(_sled));
            float capacity = GetFuelCapacity(_controller, _sled);
            if (Mathf.Abs(record.tankLiters - liters) > 0.002f ||
                Mathf.Abs(record.lastTankCapacityLiters - capacity) > 0.002f)
            {
                record.tankLiters = Mathf.Max(0f, liters);
                record.lastTankCapacityLiters = capacity;
                _dirty = true;
            }
        }

        private void CaptureConsumptionSample(float now)
        {
            float normalized;
            if (!TryGetFuelNormalized(_controller, out normalized))
                return;
            float capacity = GetFuelCapacity(_controller, _sled);
            if (capacity <= 0.01f)
                return;

            if (_lastObservedCapacity > 0f && Mathf.Abs(capacity - _lastObservedCapacity) > 0.01f)
                _samples.Clear();
            _lastObservedCapacity = capacity;

            float liters = normalized * capacity;
            if (_samples.Count > 0 && liters > _samples[_samples.Count - 1].liters + 0.005f)
                _samples.Clear();
            _samples.Add(new FuelSample
            {
                time = now,
                liters = liters,
                position = _controller != null ? _controller.transform.position : Vector3.zero
            });
            float cutoff = now - ConsumptionWindowSeconds;
            while (_samples.Count > 2 && _samples[1].time < cutoff)
                _samples.RemoveAt(0);
        }

        private float CurrentConsumptionLitersPerHour()
        {
            if (_samples.Count < 2)
                return 0f;
            FuelSample first = _samples[0];
            FuelSample last = _samples[_samples.Count - 1];
            float elapsed = last.time - first.time;
            if (elapsed < 0.45f)
                return 0f;
            float used = Mathf.Max(0f, first.liters - last.liters);
            return used / elapsed * 3600f;
        }

        private bool TryCurrentConsumptionLitersPer100Km(out float value)
        {
            value = 0f;
            if (_samples.Count < 2)
                return false;

            float distanceMeters = 0f;
            for (int i = 1; i < _samples.Count; i++)
                distanceMeters += Vector3.Distance(_samples[i - 1].position, _samples[i].position);
            if (distanceMeters < 2.5f)
                return false;

            float usedLiters = Mathf.Max(0f, _samples[0].liters - _samples[_samples.Count - 1].liters);
            value = usedLiters / (distanceMeters / 1000f) * 100f;
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
            value = Mathf.Clamp(value, 0f, 999.9f);
            return true;
        }

        private void EnsureNativeHudAdditions()
        {
            try
            {
                // The rescue prompt can be created long after the normal HUD. Keep
                // scanning for it even when our 2.5-second consumption label is
                // already attached.
                EnsureRescuePromptButton();
                if (_consumptionLabel != null && _consumptionLabel.panel != null)
                    return;
                foreach (UIDocument document in Resources.FindObjectsOfTypeAll<UIDocument>())
                {
                    if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                        continue;
                    VisualElement fuelValue = document.rootVisualElement.Q<VisualElement>("FuelValue") ??
                                              document.rootVisualElement.Q<VisualElement>("FuelSelect");
                    if (fuelValue == null || fuelValue.parent == null)
                        continue;

                    VisualElement host = fuelValue.parent;
                    _consumptionLabel = host.Q<Label>("AlpineFuelConsumptionRate");
                    if (_consumptionLabel == null)
                    {
                        _consumptionLabel = new Label { name = "AlpineFuelConsumptionRate" };
                        _consumptionLabel.style.fontSize = 10f;
                        _consumptionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        _consumptionLabel.style.whiteSpace = WhiteSpace.NoWrap;
                        _consumptionLabel.pickingMode = PickingMode.Ignore;
                        host.Add(_consumptionLabel);
                    }

                    _stationaryRefuelButton = host.Q<ControlIndicatorButton>("AlpineBackpackRefuel");
                    if (_stationaryRefuelButton == null)
                    {
                        _stationaryRefuelButton = new ControlIndicatorButton
                        {
                            name = "AlpineBackpackRefuel",
                            ActionName = "Secondary",
                            DisplayText = "PACK FUEL",
                            focusable = true
                        };
                        int lastFrame = -1;
                        Action invoke = () =>
                        {
                            if (lastFrame == Time.frameCount)
                                return;
                            lastFrame = Time.frameCount;
                            string ignored;
                            TryRefuelFromBackpack(out ignored);
                        };
                        ((Button)_stationaryRefuelButton).clicked += invoke;
                        host.Add(_stationaryRefuelButton);
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Alpine fuel HUD attachment skipped: " + ex.GetType().Name);
            }
        }

        private void EnsureRescuePromptButton()
        {
            if (_rescueRefuelButton != null && _rescueRefuelButton.panel != null)
                return;

            foreach (UIDocument document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (document == null || document.rootVisualElement == null)
                    continue;
                VisualElement rescue = document.rootVisualElement.Q<VisualElement>("FuelRescueContainer");
                if (rescue == null)
                    continue;
                _rescueRefuelButton = rescue.Q<ControlIndicatorButton>("AlpineFuelRescueFromBackpack");
                if (_rescueRefuelButton != null)
                    return;

                _rescueRefuelButton = new ControlIndicatorButton
                {
                    name = "AlpineFuelRescueFromBackpack",
                    ActionName = "Tertiary",
                    DisplayText = "BACKPACK FUEL",
                    focusable = true
                };
                int lastFrame = -1;
                Action invoke = () =>
                {
                    if (lastFrame == Time.frameCount)
                        return;
                    lastFrame = Time.frameCount;
                    string ignored;
                    TryRefuelFromBackpack(out ignored);
                };
                ((Button)_rescueRefuelButton).clicked += invoke;
                rescue.Add(_rescueRefuelButton);
                return;
            }
        }

        private void UpdateHudText()
        {
            if (_consumptionLabel == null || _consumptionLabel.panel == null)
                return;
            float hourlyRate = CurrentConsumptionLitersPerHour();
            AlpineFuelStateRecord record = CurrentRecord();
            string reserve = record != null && record.backpackCapacityLiters > 0.001f
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "  |  PACK {0:F1} L",
                    Mathf.Max(0f, record.backpackLiters))
                : string.Empty;
            float per100Km;
            _consumptionLabel.text = TryCurrentConsumptionLitersPer100Km(out per100Km)
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "2.5s {0:F1} L/100 km  |  {1:F1} L/h{2}",
                    per100Km, hourlyRate, reserve)
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "2.5s {0:F1} L/h{1}",
                    hourlyRate, reserve);
            _consumptionLabel.style.display = DisplayStyle.Flex;
        }

        private void UpdateRefuelButtonAvailability()
        {
            bool available = CanRefuelFromBackpack();
            if (_stationaryRefuelButton != null)
            {
                _stationaryRefuelButton.SetEnabled(available);
                _stationaryRefuelButton.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_rescueRefuelButton != null)
            {
                _rescueRefuelButton.SetEnabled(available);
                _rescueRefuelButton.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool CanRefuelFromBackpack()
        {
            if (_controller == null || _sled == null || !IsStationary(_controller) || !HasWornCosmeticBackpack())
                return false;
            AlpineFuelStateRecord record = CurrentRecord();
            if (record == null || record.backpackLiters <= 0.01f)
                return false;
            float fuel;
            return TryGetFuelNormalized(_controller, out fuel) && fuel < 0.999f;
        }

        private void SetHudVisible(bool visible)
        {
            if (_consumptionLabel != null)
                _consumptionLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_stationaryRefuelButton != null)
                _stationaryRefuelButton.style.display = DisplayStyle.None;
            if (_rescueRefuelButton != null)
                _rescueRefuelButton.style.display = DisplayStyle.None;
        }

        private void RemoveHudAdditions()
        {
            try { _consumptionLabel?.RemoveFromHierarchy(); } catch { }
            try { _stationaryRefuelButton?.RemoveFromHierarchy(); } catch { }
            try { _rescueRefuelButton?.RemoveFromHierarchy(); } catch { }
            _consumptionLabel = null;
            _stationaryRefuelButton = null;
            _rescueRefuelButton = null;
        }

        private void ApplyRuntimePayloadMassAndCog(bool force)
        {
            if (!_mod.Settings.alpineTuningEnabled || !_hasRuntimeBaseline || _mainBody == null || _sled == null)
                return;

            AlpineFuelStateRecord record = CurrentRecord();
            float backpackLiters = record != null ? Mathf.Max(0f, record.backpackLiters) : 0f;
            float backpackCapacity = record != null ? Mathf.Max(0f, record.backpackCapacityLiters) : 0f;
            float backpackContainer = _effect != null && backpackCapacity > 0.001f
                ? Mathf.Max(0f, _effect.backpackContainerMassKg)
                : 0f;
            float backpackMass = backpackContainer + backpackLiters * GasolineDensityKgPerLiter;

            float tankLiters = 0f;
            TryGetTankLiters(_controller, _sled, out tankLiters);
            float factoryCapacity = _baseDefaults != null
                ? Mathf.Max(0f, _baseDefaults.fuelCapacity)
                : Mathf.Max(0f, _sled.fuelCapacity);
            // Treat the native VehicleScriptableObject weight as the factory sled
            // at a full stock tank. This preserves stock mass at stock/full while
            // allowing actual fuel burn, smaller tanks and larger tanks to change
            // mass by the real gasoline delta instead of an arbitrary percentage.
            float tankFuelMassDelta = (tankLiters - factoryCapacity) * GasolineDensityKgPerLiter;

            float dynamicMass = backpackMass + tankFuelMassDelta;
            float targetMass = Mathf.Max(1f, _runtimeBaselineMass + dynamicMass);
            Vector3 com = _runtimeBaselineCenterOfMass;

            Transform tankAnchor;
            if (_effect != null && Mathf.Abs(_effect.tankHardwareMassOffsetKg) > 0.001f &&
                TryFindTankAnchor(out tankAnchor))
            {
                Vector3 local = _mainBody.transform.InverseTransformPoint(tankAnchor.position);
                com = MoveComTowardOrAway(
                    com,
                    local,
                    _effect.tankHardwareMassOffsetKg,
                    Mathf.Max(1f, _runtimeBaselineMass));
            }

            if (Mathf.Abs(tankFuelMassDelta) > 0.001f && TryFindTankAnchor(out tankAnchor))
            {
                Vector3 local = _mainBody.transform.InverseTransformPoint(tankAnchor.position);
                com = MoveComTowardOrAway(com, local, tankFuelMassDelta, targetMass);
            }

            Transform backpackAnchor;
            if (backpackMass > 0.001f && TryFindWornBackpackAnchor(out backpackAnchor))
            {
                Vector3 local = _mainBody.transform.InverseTransformPoint(backpackAnchor.position);
                com = MoveComTowardOrAway(com, local, backpackMass, targetMass);
            }

            // Keep Alpine's payload contribution intentionally minor even for the
            // joke 22 L bag. The weighted move can never cross its anchor; this
            // extra clamp prevents a malformed cosmetic transform from throwing the
            // sled's COM metres outside the chassis.
            Vector3 delta = com - _runtimeBaselineCenterOfMass;
            if (delta.magnitude > 0.18f)
                com = _runtimeBaselineCenterOfMass + delta.normalized * 0.18f;

            if (!force &&
                Mathf.Abs(targetMass - _lastAppliedDynamicMass) < 0.02f &&
                (com - _lastAppliedDynamicCenterOfMass).sqrMagnitude < 0.00000025f)
                return;

            try
            {
                _mainBody.mass = targetMass;
                _mainBody.centerOfMass = com;
                _lastAppliedDynamicMass = targetMass;
                _lastAppliedDynamicCenterOfMass = com;
            }
            catch
            {
                // The body may have been replaced between update and assignment.
            }
        }

        private static Vector3 MoveComTowardOrAway(Vector3 current, Vector3 anchor, float addedMass, float totalMass)
        {
            Vector3 toward = anchor - current;
            if (toward.sqrMagnitude < 0.0000001f || Mathf.Abs(addedMass) < 0.0001f)
                return current;
            if (addedMass > 0f)
            {
                float t = Mathf.Clamp(addedMass / Mathf.Max(1f, totalMass), 0f, 0.20f);
                return Vector3.Lerp(current, anchor, t); // never passes the tank/backpack
            }

            float away = Mathf.Clamp(Mathf.Abs(addedMass) / Mathf.Max(1f, totalMass), 0f, 0.05f);
            return current - toward * away;
        }

        private bool TryFindTankAnchor(out Transform anchor)
        {
            anchor = null;
            if (_controller == null)
                return false;
            try
            {
                foreach (Component component in _controller.GetComponentsInChildren<Component>(true))
                {
                    if (component != null && string.Equals(component.GetType().Name, "GasolineTank", StringComparison.OrdinalIgnoreCase))
                    {
                        anchor = component.transform;
                        return true;
                    }
                }
                foreach (Transform transform in _controller.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                        continue;
                    string name = (transform.name ?? string.Empty).ToLowerInvariant();
                    if ((name.Contains("fuel") || name.Contains("gas")) && name.Contains("tank"))
                    {
                        anchor = transform;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool TryFindWornBackpackAnchor(out Transform anchor)
        {
            anchor = null;
            if (_controller == null)
                return false;

            try
            {
                Transform root = _controller.transform.root;
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (IsActiveBackpackTransform(transform, out anchor))
                        return true;
                }

                // Some driver rigs are outside the sled hierarchy. Only accept an
                // active rendered backpack near the local sled to avoid treating
                // another player's cosmetic as ours.
                foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (gameObject == null || !gameObject.activeInHierarchy ||
                        Vector3.Distance(gameObject.transform.position, _controller.transform.position) > 5f)
                        continue;
                    if (IsActiveBackpackTransform(gameObject.transform, out anchor))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsActiveBackpackTransform(Transform transform, out Transform anchor)
        {
            anchor = null;
            if (transform == null || !transform.gameObject.activeInHierarchy)
                return false;
            string name = (transform.name ?? string.Empty).ToLowerInvariant();
            if (!name.Contains("backpack") && !name.Contains("back_pack"))
                return false;

            Renderer[] renderers = transform.GetComponentsInChildren<Renderer>(true);
            Renderer visible = renderers.FirstOrDefault(renderer =>
                renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy);
            if (visible == null)
                return false;
            anchor = visible.transform;
            return true;
        }

        private static Rigidbody FindMainBody(SnowmobileController controller)
        {
            if (controller == null)
                return null;
            Rigidbody direct = controller.GetComponent<Rigidbody>();
            if (direct != null && !direct.isKinematic)
                return direct;
            return controller.GetComponentsInChildren<Rigidbody>(true)
                .Where(body => body != null && !body.isKinematic)
                .OrderByDescending(body => body.mass)
                .FirstOrDefault();
        }

        private static bool IsStationary(SnowmobileController controller)
        {
            Rigidbody body = FindMainBody(controller);
            return body != null && body.linearVelocity.sqrMagnitude <=
                   StationarySpeedMetersPerSecond * StationarySpeedMetersPerSecond;
        }

        private bool IsFuelUsageEnabled(SnowmobileController controller)
        {
            object value;
            if (TryReadMember(controller, out value, "enableFuelConsumption") && value is bool controllerEnabled)
                return controllerEnabled;

            if (_fuelManager == null || Time.unscaledTime >= _nextFuelManagerScan)
            {
                _nextFuelManagerScan = Time.unscaledTime + 2f;
                _fuelManager = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                    .FirstOrDefault(item => item != null &&
                        string.Equals(item.GetType().Name, "FuelManager", StringComparison.OrdinalIgnoreCase));
            }
            if (_fuelManager != null &&
                TryReadMember(_fuelManager, out value, "FuelUsageActive", "FuelUsageEnabledEffective") &&
                value is bool managerEnabled)
                return managerEnabled;

            // If the new build exposes neither compatibility member, do not invent
            // fuel usage in sessions where the host may have disabled it.
            return false;
        }

        private static bool IsEngineOn(SnowmobileController controller)
        {
            object value;
            return controller != null &&
                   TryReadMember(controller, out value, "IsEngineOn", "isEngineOn") &&
                   value is bool enabled && enabled;
        }

        private static void TrySetEngineOff(SnowmobileController controller)
        {
            if (controller == null)
                return;
            try
            {
                MethodInfo method = controller.GetType().GetMethod(
                    "SetEngineOnOff",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    return;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    method.Invoke(controller, new object[] { false });
            }
            catch { }
        }

        private static float EstimateIdleLitersPerHour(VehicleScriptableObject sled)
        {
            if (sled == null)
                return 1.1f;
            float hp = Mathf.Clamp(sled.horsePower, 20f, 350f);
            float nominal = Mathf.Max(1f, sled.fuelConsumption);
            float rate = (0.48f + hp * 0.0036f) * Mathf.Sqrt(nominal / 20f);
            return Mathf.Clamp(rate, 0.55f, 1.8f);
        }

        private static bool TryGetTankLiters(
            SnowmobileController controller,
            VehicleScriptableObject sled,
            out float liters)
        {
            liters = 0f;
            float fuel;
            if (!TryGetFuelNormalized(controller, out fuel))
                return false;
            float capacity = GetFuelCapacity(controller, sled);
            if (capacity <= 0.01f)
                return false;
            liters = Mathf.Clamp01(fuel) * capacity;
            return true;
        }

        private static float GetFuelCapacity(SnowmobileController controller, VehicleScriptableObject sled)
        {
            object value;
            if (controller != null && TryReadMember(controller, out value, "FuelCapacity") && value != null)
            {
                try
                {
                    float parsed = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (parsed > 0.01f && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                        return parsed;
                }
                catch { }
            }
            return sled != null ? Mathf.Max(0.01f, sled.fuelCapacity) : 0f;
        }

        private static bool TryGetFuelNormalized(SnowmobileController controller, out float fuel)
        {
            fuel = 0f;
            object value;
            if (controller == null || !TryReadMember(controller, out value, "Fuel") || value == null)
                return false;
            try
            {
                fuel = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                return !float.IsNaN(fuel) && !float.IsInfinity(fuel);
            }
            catch
            {
                return false;
            }
        }

        private static bool SetFuelNormalized(SnowmobileController controller, float fuel)
        {
            if (controller == null)
                return false;
            try
            {
                MethodInfo method = controller.GetType().GetMethod(
                    "SetFuel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(float) },
                    null);
                if (method == null)
                    return false;
                method.Invoke(controller, new object[] { Mathf.Clamp01(fuel) });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadMember(object target, out object value, params string[] names)
        {
            value = null;
            if (target == null || names == null)
                return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string name in names)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(name, flags);
                    if (property != null && property.CanRead)
                    {
                        value = property.GetValue(target, null);
                        return true;
                    }
                    FieldInfo field = type.GetField(name, flags);
                    if (field != null)
                    {
                        value = field.GetValue(target);
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private AlpineFuelStateRecord CurrentRecord()
        {
            return _sled != null ? GetOrCreateRecord(Identity(_sled)) : null;
        }

        private AlpineFuelStateRecord GetOrCreateRecord(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "unknown";
            if (!_state.sleds.TryGetValue(key, out AlpineFuelStateRecord record) || record == null)
            {
                record = new AlpineFuelStateRecord();
                _state.sleds[key] = record;
            }
            return record;
        }

        private static string Identity(VehicleScriptableObject sled)
        {
            string value = SledIdentity.StableIdentityKey(sled);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            return sled != null ? AlpineTuningMod.GetSledKey(sled) : "unknown";
        }

        private static bool SameSled(VehicleScriptableObject left, VehicleScriptableObject right)
        {
            if (left == null || right == null)
                return false;
            return string.Equals(Identity(left), Identity(right), StringComparison.OrdinalIgnoreCase);
        }

        private void LoadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return;
                AlpineFuelStateFile loaded = JsonConvert.DeserializeObject<AlpineFuelStateFile>(File.ReadAllText(StatePath));
                if (loaded != null)
                {
                    if (loaded.sleds == null)
                        loaded.sleds = new Dictionary<string, AlpineFuelStateRecord>(StringComparer.OrdinalIgnoreCase);
                    _state = loaded;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Alpine fuel persistence load skipped: " + ex.GetType().Name);
                _state = new AlpineFuelStateFile();
            }
        }

        private void SaveState(bool force)
        {
            if (!_mod.Settings.persistentFuelLevelsEnabled)
                return;
            if (!force && !_dirty)
                return;
            try
            {
                string directory = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                string temp = StatePath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_state, Formatting.Indented));
                if (File.Exists(StatePath))
                    File.Delete(StatePath);
                File.Move(temp, StatePath);
                _dirty = false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Alpine fuel persistence save skipped: " + ex.GetType().Name);
            }
        }
    }
}
