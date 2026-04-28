using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[assembly: MelonInfo(typeof(AlpineTuning.AlpineTuningMod), "Alpine Tuning 2.0", AlpineTuning.AlpineConstants.ModVersion, "Don")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace AlpineTuning
{
    public class AlpineTuningMod : MelonMod
    {
        public static AlpineTuningMod Instance;

        public static VehicleScriptableObject ActiveSO;
        public static SnowmobileController ActiveController;
        public static Respawnable ActiveRespawn;
        public static Vector3 ActiveSpawnPos;
        public static Quaternion ActiveSpawnRot;

        internal PartCatalog Catalog { get; private set; }
        internal TuneStore Store { get; private set; }
        internal AlpinePeerSharing Sharing { get; private set; }

        private readonly List<VehicleScriptableObject> _selectableSleds = new List<VehicleScriptableObject>();
        private readonly HashSet<string> _sledsModifiedByAlpineThisSession = new HashSet<string>();
        private bool _defaultsBuilt;
        private float _nextNativeUiScanTime;

        private static readonly BindingFlags BF =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type _engineAudioControllerType;
        private static Type _engineAudioEnumType;
        private static MethodInfo _miSetEngineType;
        private static MethodInfo _miEngineInit;
        private static MethodInfo _miStopEngineSound;
        private static FieldInfo _fiCurrentEngineType;
        private static bool _engineAudioReflectionResolved;
        private static bool _engineAudioReflectionReady;

        private string _pendingEngineAudioEnumType;
        private string _pendingEngineAudioEnumName;
        private int _pendingEngineAudioEnumRawValue;
        private bool _pendingEngineAudioApply;
        private float _pendingEngineAudioDeadline;
        private float _pendingEngineAudioNextAttemptTime;
        private int _pendingEngineAudioAttemptsRemaining;
        private int _pendingEngineAudioLastControllerId = int.MinValue;
        private bool _pendingEngineAudioLoggedReady;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Catalog = new PartCatalog();
            Store = new TuneStore(Catalog);
            Store.Initialize();
            Sharing = new AlpinePeerSharing(this);
            Sharing.Initialize();

            MelonLogger.Msg("Alpine Tuning 2.0 initialized");
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime >= _nextNativeUiScanTime)
            {
                _nextNativeUiScanTime = Time.unscaledTime + 1f;
                AlpineNativeUi.TryAttachOpenMenus(this);
            }

            Sharing?.Update();

            if (ActiveSO == null)
                return;

            if (!_defaultsBuilt)
                TryBuildDefaults();

            TryCaptureEngineAudioForCurrentSled();
            TryApplyPendingEngineAudioSwap();
        }

        internal string LocalAuthorName
        {
            get
            {
                if (Sharing != null && !string.IsNullOrWhiteSpace(Sharing.LocalName))
                    return Sharing.LocalName;

                return Environment.UserName;
            }
        }

        internal IReadOnlyList<VehicleScriptableObject> SelectableSleds
        {
            get
            {
                TryBuildDefaults();
                return _selectableSleds;
            }
        }

        public static string GetSledKey(VehicleScriptableObject sled)
        {
            return sled == null
                ? "UNKNOWN"
                : sled.name.Trim().Replace(' ', '_');
        }

        public static string GetVehicleId(VehicleScriptableObject sled)
        {
            if (sled == null)
                return null;

            var field = sled.GetType().GetField("vehicleId", BF);
            var value = field?.GetValue(sled) as string;
            return !string.IsNullOrWhiteSpace(value) ? value : GetSledKey(sled);
        }

        internal VehicleScriptableObject ResolveTargetSled(object menuContext)
        {
            if (menuContext is VehicleScriptableObject sled)
                return sled;

            if (menuContext is VehicleSelectionUiController)
            {
                var fromMenu = TryGetVehicleSelectionSled(menuContext);
                if (fromMenu != null)
                    return fromMenu;
            }

            if (menuContext is PauseUIController pause)
            {
                var controller = GetFieldValue<SnowmobileController>(pause, "CHJANEKOEDG");
                var so = GetVehicleFromController(controller);
                if (so != null)
                    return so;
            }

            return ActiveSO;
        }

        internal TuneProfile CreateWorkingProfile(VehicleScriptableObject sled)
        {
            TryBuildDefaults();
            EnsureDefaultsForSled(sled);

            var profile = Store.CreateWorkingProfile(sled, LocalAuthorName);
            Catalog.EnsureProfileSelections(profile);
            PreviewProfile(profile, sled);
            return profile;
        }

        internal ResolvedStats PreviewProfile(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (profile == null || sled == null)
                return null;

            var computation = ComputeProfile(profile, sled);
            profile.resolvedStats = computation.stats;
            profile.requiresReload = computation.requiresReload;
            profile.targetSledKey = GetSledKey(sled);
            profile.targetVehicleId = GetVehicleId(sled);
            return computation.stats;
        }

        internal bool ApplyProfile(TuneProfile profile, VehicleScriptableObject sled, bool persist, bool reloadIfNeeded)
        {
            if (profile == null || sled == null)
                return false;

            try
            {
                TryBuildDefaults();
                Catalog.EnsureProfileSelections(profile);

                var computation = ComputeProfile(profile, sled);

                // Always return to the captured stock baseline before applying a profile.
                // This makes part application idempotent: changing from one Alpine build to
                // another never multiplies against values left behind by the previous build.
                ApplyDefaultsToSled(sled, computation.baseDefaults);
                if (sled == ActiveSO)
                    ApplyRuntimeDefaults(computation.baseDefaults);

                ApplyStatsToSled(sled, computation);
                if (sled == ActiveSO)
                {
                    ApplyRuntimeController(computation, profile);
                    ApplyAccessoryMode(computation.mergedEffect.accessoryMode, computation.baseDefaults);
                }

                profile.resolvedStats = computation.stats;
                profile.requiresReload = computation.requiresReload;
                profile.targetSledKey = GetSledKey(sled);
                profile.targetVehicleId = GetVehicleId(sled);

                QueueEngineAudioSwap(computation.audioDefaults, computation.audioSource);

                if (persist)
                    Store.SaveProfile(profile, true);

                _sledsModifiedByAlpineThisSession.Add(GetSledKey(sled));

                MelonLogger.Msg(
                    $"Applied Alpine tune '{profile.name}' to {sled.name}: " +
                    $"HP={computation.stats.horsePower:F1}, PF={computation.stats.powerFactor:F2}, " +
                    $"Lug={computation.stats.lugHeight:F1}, Fric={computation.stats.friction:F2}");

                if (reloadIfNeeded && computation.requiresReload && sled == ActiveSO)
                    ReloadSled();

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ApplyProfile failed: {ex}");
                return false;
            }
        }

        internal void SaveProfile(TuneProfile profile, VehicleScriptableObject sled, bool makeActive)
        {
            if (profile == null || sled == null)
                return;

            PreviewProfile(profile, sled);
            Store.SaveProfile(profile, makeActive);
        }

        internal void DeleteProfile(string profileId)
        {
            Store.DeleteProfile(profileId);
        }

        internal void ResetToFactory(VehicleScriptableObject sled, bool reloadIfActive)
        {
            if (sled == null)
                return;

            TryBuildDefaults();
            string sledKey = GetSledKey(sled);
            var defaults = Store.GetDefaults(sledKey);
            if (defaults == null)
            {
                MelonLogger.Warning($"No defaults found for {sledKey}; reset skipped.");
                return;
            }

            ApplyDefaultsToSled(sled, defaults);
            Store.SetActiveProfile(sledKey, null);
            _sledsModifiedByAlpineThisSession.Remove(sledKey);

            if (sled == ActiveSO)
            {
                ApplyRuntimeDefaults(defaults);
                ApplyAccessoryMode("stock", defaults);
                QueueEngineAudioSwap(defaults, sled);
                if (reloadIfActive)
                    ReloadSled();
            }
        }

        internal List<TuneProfile> ProfilesForSled(VehicleScriptableObject sled)
        {
            if (sled == null)
                return new List<TuneProfile>();

            return Store.GetProfilesForSled(GetSledKey(sled));
        }

        internal void PublishProfile(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (Sharing == null || profile == null)
                return;

            PreviewProfile(profile, sled);
            Sharing.PublishProfile(profile);
        }

        internal void ImportSharedProfile(TuneProfile profile)
        {
            Store.ImportSharedProfile(profile);
        }

        internal void RequestSharedProfile(ulong peerId, string profileId)
        {
            Sharing?.RequestProfile(peerId, profileId);
        }

        internal bool ApplySharedProfile(string profileId)
        {
            if (Sharing == null)
                return false;

            var profile = Sharing.GetPayload(profileId);
            if (profile == null)
                return false;

            VehicleScriptableObject target = FindSledByKey(profile.targetSledKey) ?? ActiveSO;
            Store.ImportSharedProfile(profile);
            return ApplyProfile(profile, target, true, false);
        }

        internal VehicleScriptableObject FindSledByKey(string sledKey)
        {
            TryBuildDefaults();
            return _selectableSleds.FirstOrDefault(s => GetSledKey(s) == sledKey);
        }

        internal void ReloadSled()
        {
            try
            {
                var controllerType = typeof(Controller);
                var instanceProp = controllerType.GetProperty("PKMPAOKMHCB", BF);
                var controllerInstance = instanceProp?.GetValue(null);
                if (controllerInstance == null)
                {
                    MelonLogger.Error("ReloadSled: Controller singleton not found.");
                    return;
                }

                MethodInfo trySpawnMethod = controllerInstance.GetType().GetMethod(
                    "TrySpawnPlayer",
                    BF,
                    null,
                    new[] { typeof(Transform), typeof(bool) },
                    null);

                if (trySpawnMethod == null)
                {
                    trySpawnMethod = controllerInstance.GetType()
                        .GetMethods(BF)
                        .FirstOrDefault(m =>
                            m.Name == "TrySpawnPlayer" &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(Transform)) &&
                            m.GetParameters()[1].ParameterType == typeof(bool));
                }

                if (trySpawnMethod == null)
                {
                    MelonLogger.Error("ReloadSled: TrySpawnPlayer overload not found.");
                    return;
                }

                Transform spawnTransform = ActiveController != null ? ActiveController.transform : null;
                if (spawnTransform == null)
                {
                    MelonLogger.Error("ReloadSled: Active controller transform is null.");
                    return;
                }

                if (_pendingEngineAudioApply)
                {
                    _pendingEngineAudioDeadline = Time.unscaledTime + 12f;
                    _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.35f;
                    _pendingEngineAudioAttemptsRemaining = Mathf.Max(_pendingEngineAudioAttemptsRemaining, 24);
                    _pendingEngineAudioLastControllerId = int.MinValue;
                    _pendingEngineAudioLoggedReady = false;
                }

                trySpawnMethod.Invoke(controllerInstance, new object[] { spawnTransform, true });
                MelonLogger.Msg("Alpine Tuning 2.0 triggered sled reload.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ReloadSled failed: {ex}");
            }
        }

        private void TryBuildDefaults()
        {
            if (_defaultsBuilt)
                return;

            try
            {
                BuildSelectableSledList();

                if (_selectableSleds.Count == 0 && ActiveSO != null)
                    _selectableSleds.Add(ActiveSO);

                if (_selectableSleds.Count == 0)
                    return;

                foreach (var sled in _selectableSleds)
                {
                    EnsureDefaultsForSled(sled);
                    RefreshStatDefaultsFromCleanLoad(sled);
                }

                Store.MigrateLegacyPresets(_selectableSleds, LocalAuthorName);
                _defaultsBuilt = true;
                MelonLogger.Msg($"Alpine defaults ready for {_selectableSleds.Count} selectable sleds.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"TryBuildDefaults error: {ex}");
            }
        }

        private void BuildSelectableSledList()
        {
            if (_selectableSleds.Count > 0)
                return;

            var lists = Resources.FindObjectsOfTypeAll<VehicleListScriptableObject>();
            if (lists == null || lists.Length == 0)
            {
                MelonLogger.Warning("No VehicleListScriptableObject found; garage tuning list will be limited.");
                return;
            }

            var list = lists[0];
            var prop = typeof(VehicleListScriptableObject).GetProperty("SelectableVehicles", BF);
            var vehicles = prop?.GetValue(list) as VehicleScriptableObject[];

            if (vehicles == null)
            {
                var field = typeof(VehicleListScriptableObject).GetField("vehicles", BF);
                vehicles = field?.GetValue(list) as VehicleScriptableObject[];
            }

            if (vehicles != null)
                _selectableSleds.AddRange(vehicles.Where(v => v != null));
        }

        private void EnsureDefaultsForSled(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            var defaults = Store.GetDefaults(key);
            if (defaults == null)
            {
                if (_sledsModifiedByAlpineThisSession.Contains(key))
                {
                    MelonLogger.Warning(
                        $"Skipped default capture for '{key}' because Alpine has already modified it this session.");
                    return;
                }

                defaults = SledDefaults.FromSled(sled, key);
                TryPopulateDefaultAudioToken(defaults, sled, false);
                Store.PutDefaults(defaults);
            }

            if (sled == ActiveSO && ActiveController != null)
            {
                CaptureRuntimeDefaults(defaults, ActiveController);
                Store.PutDefaults(defaults);
            }
        }

        private void RefreshStatDefaultsFromCleanLoad(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            if (_sledsModifiedByAlpineThisSession.Contains(key))
                return;

            var existing = Store.GetDefaults(key);
            if (existing == null || !StoredStatsDifferFromSled(existing, sled))
                return;

            var refreshed = SledDefaults.FromSled(sled, key);

            // Keep non-stat metadata that cannot always be recovered from the clean ScriptableObject.
            refreshed.engineAudioEnumType = existing.engineAudioEnumType;
            refreshed.engineAudioEnumName = existing.engineAudioEnumName;
            refreshed.engineAudioEnumRawValue = existing.engineAudioEnumRawValue;
            refreshed.controller = existing.controller ?? new ControllerDefaults();

            Store.PutDefaults(refreshed);
            MelonLogger.Msg(
                $"Refreshed stock stat baseline for '{key}' from clean game load. " +
                "This prevents Alpine profiles from compounding on stale tuned defaults.");
        }

        private static bool StoredStatsDifferFromSled(SledDefaults defaults, VehicleScriptableObject sled)
        {
            return Differs(defaults.horsePower, sled.horsePower, 0.01f) ||
                   Differs(defaults.powerFactor, sled.powerFactor, 0.001f) ||
                   Differs(defaults.lugHeight, sled.lugHeight, 0.01f) ||
                   Differs(defaults.friction, sled.coefficientOfFriction, 0.001f) ||
                   Differs(defaults.weight, sled.weight, 0.01f) ||
                   Differs(defaults.skiStance, sled.skiStance, 0.001f) ||
                   Differs(defaults.skisXDistanceOffset, sled.skisXDistanceOffset, 0.001f) ||
                   defaults.isTurboOn != sled.isTurboOn ||
                   !StatsDefaultsMatch(defaults, sled) ||
                   !AccessoryDefaultsMatch(defaults, sled) ||
                   Differs(defaults.centerOfMassOffset.ToVector3(), sled.centerOfMassOffset, 0.001f) ||
                   Differs(defaults.driverCenterOfMassOffset.ToVector3(), sled.driverCenterOfMassOffset, 0.001f);
        }

        private static bool StatsDefaultsMatch(SledDefaults defaults, VehicleScriptableObject sled)
        {
            if (sled.snowmobileStats == null)
                return !defaults.hasSnowmobileStats;

            return defaults.hasSnowmobileStats &&
                   Differs(defaults.statsPower, sled.snowmobileStats.power, 0.001f) == false &&
                   Differs(defaults.statsClimbing, sled.snowmobileStats.climbing, 0.001f) == false &&
                   Differs(defaults.statsAgility, sled.snowmobileStats.agility, 0.001f) == false;
        }

        private static bool AccessoryDefaultsMatch(SledDefaults defaults, VehicleScriptableObject sled)
        {
            return defaults.hasAccessoryDefaults &&
                   defaults.hasWindshield == sled.hasWindshield &&
                   defaults.hasSnowFlaps == sled.hasSnowFlaps &&
                   defaults.hasRemovableRearParts == sled.hasRemovableRearParts &&
                   defaults.hasTunnelAccessories == sled.hasTunnelAccessories;
        }

        private static bool Differs(float a, float b, float epsilon)
        {
            return Mathf.Abs(a - b) > epsilon;
        }

        private static bool Differs(Vector3 a, Vector3 b, float epsilon)
        {
            return (a - b).sqrMagnitude > epsilon * epsilon;
        }

        private void TryApplyActiveProfileForCurrentSled()
        {
            if (ActiveSO == null)
                return;

            var profile = Store.GetActiveProfileForSled(GetSledKey(ActiveSO));
            if (profile == null)
                return;

            MelonLogger.Msg($"Auto-applying active Alpine profile '{profile.name}' for {ActiveSO.name}.");
            ApplyProfile(TuneStore.Clone(profile), ActiveSO, false, false);
        }

        private TuneComputation ComputeProfile(TuneProfile profile, VehicleScriptableObject sled)
        {
            EnsureDefaultsForSled(sled);

            string sledKey = GetSledKey(sled);
            var baseDefaults = Store.GetDefaults(sledKey);
            if (baseDefaults == null)
            {
                if (_sledsModifiedByAlpineThisSession.Contains(sledKey))
                    throw new InvalidOperationException($"Cannot compute tune for '{sledKey}' without stock defaults.");

                baseDefaults = SledDefaults.FromSled(sled, sledKey);
                Store.PutDefaults(baseDefaults);
            }

            var engineDefaults = baseDefaults;
            var audioDefaults = baseDefaults;
            var audioSource = sled;

            if (!string.IsNullOrWhiteSpace(profile.donorSledKey))
            {
                var donorDefaults = Store.GetDefaults(profile.donorSledKey);
                var donorSled = FindSledByKey(profile.donorSledKey);
                if (donorDefaults != null)
                {
                    engineDefaults = donorDefaults;
                    audioDefaults = donorDefaults;
                    audioSource = donorSled ?? sled;
                    TryPopulateDefaultAudioToken(audioDefaults, audioSource, false);
                }
            }

            var effect = new PartEffect();
            var parts = new List<TunePart>();
            bool requiresReload = false;

            Catalog.EnsureProfileSelections(profile);
            foreach (string category in PartCatalog.OrderedCategories)
            {
                string partId = profile.GetPartId(category);
                var part = Catalog.Find(partId) ?? Catalog.Find(Catalog.DefaultPartId(category));
                if (part == null)
                    continue;

                parts.Add(part);
                requiresReload |= part.requiresReload;
                MergeEffect(effect, part.effect);
            }

            var fine = profile.fineTune ?? new FineTuneSettings();
            profile.fineTune = fine;
            ClampFineTune(fine);

            float powerTrim = 1f + fine.powerTrimPercent / 100f;
            float tractionTrim = 1f + fine.tractionTrimPercent / 100f;
            float weightTrim = 1f + fine.weightTrimPercent / 100f;

            float hp = engineDefaults.horsePower * effect.horsePowerMultiplier * powerTrim;
            float pf = engineDefaults.powerFactor * effect.powerFactorMultiplier * powerTrim;
            float lug = baseDefaults.lugHeight * effect.lugHeightMultiplier + effect.lugHeightOffset;
            float friction = baseDefaults.friction * effect.frictionMultiplier * tractionTrim;
            float weight = (baseDefaults.weight * effect.weightMultiplier + effect.weightOffset) * weightTrim;

            Vector3 com =
                baseDefaults.centerOfMassOffset.ToVector3() +
                effect.centerOfMassDelta.ToVector3() +
                new Vector3(0f, fine.centerOfMassYTrim, fine.centerOfMassZTrim);

            Vector3 driverCom =
                baseDefaults.driverCenterOfMassOffset.ToVector3() +
                effect.driverCenterOfMassDelta.ToVector3();

            float skiStance =
                baseDefaults.skiStance +
                effect.skiStanceOffset +
                fine.skiStanceTrim;

            float skisXDistanceOffset =
                baseDefaults.skisXDistanceOffset +
                effect.skisXDistanceOffset;

            hp = ClampRelative(hp, engineDefaults.horsePower, 0.60f, 2.30f, 20f, 420f);
            pf = ClampRelative(pf, engineDefaults.powerFactor, 0.55f, 1.85f, 0.20f, 3.50f);
            lug = ClampRelative(lug, baseDefaults.lugHeight, 0.50f, 1.85f, 1f, 80f);
            friction = ClampRelative(friction, baseDefaults.friction, 0.55f, 1.65f, 0.05f, 3.00f);
            if (baseDefaults.weight > 1f)
                weight = Mathf.Clamp(weight, baseDefaults.weight * 0.75f, baseDefaults.weight * 1.35f);
            else
                weight = Mathf.Max(1f, weight);

            com = ClampVectorOffset(com, baseDefaults.centerOfMassOffset.ToVector3(), new Vector3(0.10f, 0.24f, 0.28f));
            driverCom = ClampVectorOffset(driverCom, baseDefaults.driverCenterOfMassOffset.ToVector3(), new Vector3(0.10f, 0.16f, 0.16f));
            skiStance = ClampOffset(skiStance, baseDefaults.skiStance, 0.18f, 0f, 4f);
            skisXDistanceOffset = ClampOffset(skisXDistanceOffset, baseDefaults.skisXDistanceOffset, 0.12f, -1f, 1f);

            bool turbo = baseDefaults.isTurboOn || effect.isTurbo;
            string engineText = !string.IsNullOrWhiteSpace(effect.engineText)
                ? effect.engineText
                : baseDefaults.engineText;

            return new TuneComputation
            {
                baseDefaults = baseDefaults,
                engineDefaults = engineDefaults,
                audioDefaults = audioDefaults,
                audioSource = audioSource,
                parts = parts,
                mergedEffect = effect,
                requiresReload = requiresReload,
                stats = new ResolvedStats
                {
                    horsePower = hp,
                    powerFactor = pf,
                    lugHeight = lug,
                    friction = friction,
                    weight = weight,
                    skiStance = skiStance,
                    skisXDistanceOffset = skisXDistanceOffset,
                    isTurboOn = turbo,
                    engineText = engineText,
                    centerOfMassOffset = Vec3Data.From(com),
                    driverCenterOfMassOffset = Vec3Data.From(driverCom)
                }
            };
        }

        private static void MergeEffect(PartEffect target, PartEffect source)
        {
            target.horsePowerMultiplier *= source.horsePowerMultiplier;
            target.powerFactorMultiplier *= source.powerFactorMultiplier;
            target.lugHeightMultiplier *= source.lugHeightMultiplier;
            target.lugHeightOffset += source.lugHeightOffset;
            target.frictionMultiplier *= source.frictionMultiplier;
            target.weightMultiplier *= source.weightMultiplier;
            target.weightOffset += source.weightOffset;
            target.skiStanceOffset += source.skiStanceOffset;
            target.skisXDistanceOffset += source.skisXDistanceOffset;
            target.centerOfMassDelta = Vec3Data.From(target.centerOfMassDelta.ToVector3() + source.centerOfMassDelta.ToVector3());
            target.driverCenterOfMassDelta = Vec3Data.From(target.driverCenterOfMassDelta.ToVector3() + source.driverCenterOfMassDelta.ToVector3());
            target.isTurbo |= source.isTurbo;
            if (!string.IsNullOrWhiteSpace(source.engineText))
                target.engineText = source.engineText;
            target.throttleExponentDelta += source.throttleExponentDelta;
            target.rpmSensitivityMultiplier *= source.rpmSensitivityMultiplier;
            target.rpmSensitivityDownMultiplier *= source.rpmSensitivityDownMultiplier;
            target.clutchRpmMinOffset += source.clutchRpmMinOffset;
            target.clutchRpmMaxOffset += source.clutchRpmMaxOffset;
            target.minThrottleOnClutchEngagementOffset += source.minThrottleOnClutchEngagementOffset;
            target.wheelieThresholdOffset += source.wheelieThresholdOffset;
            target.stabilizerDampingMultiplier *= source.stabilizerDampingMultiplier;
            target.trackSpeedDampingMultiplier *= source.trackSpeedDampingMultiplier;
            target.trackSpeedGyroMultiplier *= source.trackSpeedGyroMultiplier;
            if (!string.IsNullOrWhiteSpace(source.accessoryMode))
                target.accessoryMode = source.accessoryMode;
        }

        private static float ClampRelative(float value, float baseline, float minMult, float maxMult, float absoluteMin, float absoluteMax)
        {
            if (baseline > 0.01f)
                return Mathf.Clamp(value, Mathf.Max(absoluteMin, baseline * minMult), Mathf.Min(absoluteMax, baseline * maxMult));

            return Mathf.Clamp(value, absoluteMin, absoluteMax);
        }

        private static float ClampOffset(float value, float baseline, float maxDelta, float absoluteMin, float absoluteMax)
        {
            return Mathf.Clamp(value, Mathf.Max(absoluteMin, baseline - maxDelta), Mathf.Min(absoluteMax, baseline + maxDelta));
        }

        private static Vector3 ClampVectorOffset(Vector3 value, Vector3 baseline, Vector3 maxDelta)
        {
            return new Vector3(
                ClampOffset(value.x, baseline.x, maxDelta.x, -10f, 10f),
                ClampOffset(value.y, baseline.y, maxDelta.y, -10f, 10f),
                ClampOffset(value.z, baseline.z, maxDelta.z, -10f, 10f));
        }

        private static Vector3 ClampVectorRelative(Vector3 value, Vector3 baseline, float minMult, float maxMult)
        {
            return new Vector3(
                ClampRelativeSigned(value.x, baseline.x, minMult, maxMult),
                ClampRelativeSigned(value.y, baseline.y, minMult, maxMult),
                ClampRelativeSigned(value.z, baseline.z, minMult, maxMult));
        }

        private static float ClampRelativeSigned(float value, float baseline, float minMult, float maxMult)
        {
            if (Mathf.Abs(baseline) <= 0.001f)
                return Mathf.Clamp(value, -10f, 10f);

            float a = baseline * minMult;
            float b = baseline * maxMult;
            return Mathf.Clamp(value, Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private static void ClampFineTune(FineTuneSettings fine)
        {
            if (fine == null)
                return;

            fine.powerTrimPercent = Mathf.Clamp(fine.powerTrimPercent, -10f, 10f);
            fine.tractionTrimPercent = Mathf.Clamp(fine.tractionTrimPercent, -10f, 10f);
            fine.weightTrimPercent = Mathf.Clamp(fine.weightTrimPercent, -8f, 8f);
            fine.clutchTrimPercent = Mathf.Clamp(fine.clutchTrimPercent, -10f, 10f);
            fine.centerOfMassYTrim = Mathf.Clamp(fine.centerOfMassYTrim, -0.08f, 0.08f);
            fine.centerOfMassZTrim = Mathf.Clamp(fine.centerOfMassZTrim, -0.12f, 0.12f);
            fine.skiStanceTrim = Mathf.Clamp(fine.skiStanceTrim, -0.08f, 0.08f);
        }

        private static float SafeRatio(float value, float baseline)
        {
            return Mathf.Abs(baseline) > 0.001f ? value / baseline : 1f;
        }

        private static void ApplyStatsToSled(VehicleScriptableObject sled, TuneComputation computation)
        {
            var stats = computation.stats;
            sled.horsePower = stats.horsePower;
            sled.powerFactor = stats.powerFactor;
            sled.lugHeight = stats.lugHeight;
            sled.coefficientOfFriction = stats.friction;
            sled.weight = stats.weight;
            sled.skiStance = stats.skiStance;
            sled.skisXDistanceOffset = stats.skisXDistanceOffset;
            sled.isTurboOn = stats.isTurboOn;
            sled.engineText = stats.engineText;
            sled.centerOfMassOffset = stats.centerOfMassOffset.ToVector3();
            sled.driverCenterOfMassOffset = stats.driverCenterOfMassOffset.ToVector3();

            if (sled.snowmobileStats != null)
            {
                ApplySnowmobileStatBars(sled.snowmobileStats, computation);
            }
        }

        private static void ApplySnowmobileStatBars(SnowmobileStats statBars, TuneComputation computation)
        {
            var defaults = computation.baseDefaults;
            if (defaults != null && defaults.hasSnowmobileStats)
            {
                float powerRatio = Mathf.Clamp(SafeRatio(computation.stats.horsePower, computation.engineDefaults.horsePower), 0.60f, 1.80f);
                float climbRatio = Mathf.Clamp(
                    (SafeRatio(computation.stats.lugHeight, defaults.lugHeight) +
                     SafeRatio(computation.stats.friction, defaults.friction)) * 0.5f,
                    0.55f,
                    1.65f);
                float agilityRatio = Mathf.Clamp(SafeRatio(defaults.weight, computation.stats.weight), 0.75f, 1.35f);

                statBars.power = Mathf.Clamp01(defaults.statsPower * powerRatio);
                statBars.climbing = Mathf.Clamp01(defaults.statsClimbing * climbRatio);
                statBars.agility = Mathf.Clamp01(defaults.statsAgility * agilityRatio);
                return;
            }

            statBars.power = Mathf.Clamp01(computation.stats.horsePower / 250f);
            statBars.climbing = Mathf.Clamp01((computation.stats.lugHeight / 60f + computation.stats.friction / 2.4f) * 0.5f);
            statBars.agility = Mathf.Clamp01(1.1f - computation.stats.weight / 450f);
        }

        private static void ApplyDefaultsToSled(VehicleScriptableObject sled, SledDefaults defaults)
        {
            sled.horsePower = defaults.horsePower;
            sled.powerFactor = defaults.powerFactor;
            sled.lugHeight = defaults.lugHeight;
            sled.coefficientOfFriction = defaults.friction;
            sled.weight = defaults.weight;
            sled.skiStance = defaults.skiStance;
            sled.skisXDistanceOffset = defaults.skisXDistanceOffset;
            sled.isTurboOn = defaults.isTurboOn;
            sled.engineText = defaults.engineText;
            sled.centerOfMassOffset = defaults.centerOfMassOffset.ToVector3();
            sled.driverCenterOfMassOffset = defaults.driverCenterOfMassOffset.ToVector3();

            if (defaults.hasSnowmobileStats && sled.snowmobileStats != null)
            {
                sled.snowmobileStats.power = defaults.statsPower;
                sled.snowmobileStats.climbing = defaults.statsClimbing;
                sled.snowmobileStats.agility = defaults.statsAgility;
            }

            if (defaults.hasAccessoryDefaults)
            {
                sled.hasWindshield = defaults.hasWindshield;
                sled.hasSnowFlaps = defaults.hasSnowFlaps;
                sled.hasRemovableRearParts = defaults.hasRemovableRearParts;
                sled.hasTunnelAccessories = defaults.hasTunnelAccessories;
            }
        }

        private void ApplyRuntimeController(TuneComputation computation, TuneProfile profile)
        {
            if (ActiveController == null || computation == null || computation.baseDefaults == null)
                return;

            var defaults = computation.baseDefaults.controller;
            var effect = computation.mergedEffect;
            var fine = profile.fineTune ?? new FineTuneSettings();
            profile.fineTune = fine;
            ClampFineTune(fine);

            float clutchTrim = 1f + fine.clutchTrimPercent / 100f;

            if (defaults.hasThrottleExponent)
                SetFloatField(ActiveController, "throttleExponent", ClampOffset(defaults.throttleExponent + effect.throttleExponentDelta, defaults.throttleExponent, 0.20f, 0.25f, 4f));

            if (defaults.hasRpmSensitivity)
                SetFloatField(ActiveController, "rpmSensitivity", ClampRelative(defaults.rpmSensitivity * effect.rpmSensitivityMultiplier, defaults.rpmSensitivity, 0.50f, 1.70f, 0.05f, 10f));

            if (defaults.hasRpmSensitivityDown)
                SetFloatField(ActiveController, "rpmSensitivityDown", ClampRelative(defaults.rpmSensitivityDown * effect.rpmSensitivityDownMultiplier, defaults.rpmSensitivityDown, 0.50f, 1.70f, 0.05f, 10f));

            float clutchMin = defaults.hasClutchRpmMin
                ? ClampRelative((defaults.clutchRpmMin + effect.clutchRpmMinOffset) * clutchTrim, defaults.clutchRpmMin, 0.75f, 1.35f, 0f, 14000f)
                : 0f;

            float clutchMax = defaults.hasClutchRpmMax
                ? ClampRelative((defaults.clutchRpmMax + effect.clutchRpmMaxOffset) * clutchTrim, defaults.clutchRpmMax, 0.75f, 1.35f, 0f, 14000f)
                : 0f;

            if (defaults.hasClutchRpmMin && defaults.hasClutchRpmMax && clutchMax < clutchMin + 100f)
                clutchMax = Mathf.Min(14000f, clutchMin + 100f);

            if (defaults.hasClutchRpmMin)
                SetFloatField(ActiveController, "clutchRpmMin", clutchMin);

            if (defaults.hasClutchRpmMax)
                SetFloatField(ActiveController, "clutchRpmMax", clutchMax);

            if (defaults.hasMinThrottleOnClutchEngagement)
                SetFloatField(ActiveController, "minThrottleOnClutchEngagement", Mathf.Clamp01(defaults.minThrottleOnClutchEngagement + effect.minThrottleOnClutchEngagementOffset));

            if (defaults.hasWheelieThreshold)
                SetFloatField(ActiveController, "wheelieThreshold", ClampOffset(defaults.wheelieThreshold + effect.wheelieThresholdOffset, defaults.wheelieThreshold, 0.25f, 0.05f, 3f));

            ApplyStabilizerRuntime(defaults, effect);
        }

        private void ApplyRuntimeDefaults(SledDefaults defaults)
        {
            if (ActiveController == null || defaults == null)
                return;

            var runtime = defaults.controller;
            if (runtime.hasThrottleExponent) SetFloatField(ActiveController, "throttleExponent", runtime.throttleExponent);
            if (runtime.hasRpmSensitivity) SetFloatField(ActiveController, "rpmSensitivity", runtime.rpmSensitivity);
            if (runtime.hasRpmSensitivityDown) SetFloatField(ActiveController, "rpmSensitivityDown", runtime.rpmSensitivityDown);
            if (runtime.hasClutchRpmMin) SetFloatField(ActiveController, "clutchRpmMin", runtime.clutchRpmMin);
            if (runtime.hasClutchRpmMax) SetFloatField(ActiveController, "clutchRpmMax", runtime.clutchRpmMax);
            if (runtime.hasMinThrottleOnClutchEngagement) SetFloatField(ActiveController, "minThrottleOnClutchEngagement", runtime.minThrottleOnClutchEngagement);
            if (runtime.hasWheelieThreshold) SetFloatField(ActiveController, "wheelieThreshold", runtime.wheelieThreshold);

            object stabilizer = GetStabilizer(ActiveController);
            if (stabilizer == null)
                return;

            if (runtime.hasStabilizerDamping) SetFieldValue(stabilizer, "damping", runtime.stabilizerDamping.ToVector3());
            if (runtime.hasTrackSpeedDamping) SetFieldValue(stabilizer, "trackSpeedDamping", runtime.trackSpeedDamping.ToVector3());
            if (runtime.hasTrackSpeedGyroMultiplier) SetFieldValue(stabilizer, "trackSpeedGyroMultiplier", runtime.trackSpeedGyroMultiplier);
        }

        private void ApplyStabilizerRuntime(ControllerDefaults defaults, PartEffect effect)
        {
            object stabilizer = GetStabilizer(ActiveController);
            if (stabilizer == null)
                return;

            if (defaults.hasStabilizerDamping)
                SetFieldValue(stabilizer, "damping", ClampVectorRelative(defaults.stabilizerDamping.ToVector3() * effect.stabilizerDampingMultiplier, defaults.stabilizerDamping.ToVector3(), 0.50f, 1.80f));

            if (defaults.hasTrackSpeedDamping)
                SetFieldValue(stabilizer, "trackSpeedDamping", ClampVectorRelative(defaults.trackSpeedDamping.ToVector3() * effect.trackSpeedDampingMultiplier, defaults.trackSpeedDamping.ToVector3(), 0.50f, 1.80f));

            if (defaults.hasTrackSpeedGyroMultiplier)
                SetFieldValue(stabilizer, "trackSpeedGyroMultiplier", ClampRelative(defaults.trackSpeedGyroMultiplier * effect.trackSpeedGyroMultiplier, defaults.trackSpeedGyroMultiplier, 0.60f, 1.50f, 0.01f, 10f));
        }

        private void CaptureRuntimeDefaults(SledDefaults defaults, SnowmobileController controller)
        {
            if (defaults == null || controller == null)
                return;

            CaptureFloat(controller, "throttleExponent", v => { defaults.controller.hasThrottleExponent = true; defaults.controller.throttleExponent = v; });
            CaptureFloat(controller, "rpmSensitivity", v => { defaults.controller.hasRpmSensitivity = true; defaults.controller.rpmSensitivity = v; });
            CaptureFloat(controller, "rpmSensitivityDown", v => { defaults.controller.hasRpmSensitivityDown = true; defaults.controller.rpmSensitivityDown = v; });
            CaptureFloat(controller, "clutchRpmMin", v => { defaults.controller.hasClutchRpmMin = true; defaults.controller.clutchRpmMin = v; });
            CaptureFloat(controller, "clutchRpmMax", v => { defaults.controller.hasClutchRpmMax = true; defaults.controller.clutchRpmMax = v; });
            CaptureFloat(controller, "minThrottleOnClutchEngagement", v => { defaults.controller.hasMinThrottleOnClutchEngagement = true; defaults.controller.minThrottleOnClutchEngagement = v; });
            CaptureFloat(controller, "wheelieThreshold", v => { defaults.controller.hasWheelieThreshold = true; defaults.controller.wheelieThreshold = v; });

            object stabilizer = GetStabilizer(controller);
            if (stabilizer == null)
                return;

            if (TryGetFieldValue(stabilizer, "damping", out Vector3 damping))
            {
                defaults.controller.hasStabilizerDamping = true;
                defaults.controller.stabilizerDamping = Vec3Data.From(damping);
            }

            if (TryGetFieldValue(stabilizer, "trackSpeedDamping", out Vector3 trackSpeedDamping))
            {
                defaults.controller.hasTrackSpeedDamping = true;
                defaults.controller.trackSpeedDamping = Vec3Data.From(trackSpeedDamping);
            }

            if (TryGetFieldValue(stabilizer, "trackSpeedGyroMultiplier", out float gyro))
            {
                defaults.controller.hasTrackSpeedGyroMultiplier = true;
                defaults.controller.trackSpeedGyroMultiplier = gyro;
            }
        }

        private static void CaptureFloat(object target, string fieldName, Action<float> capture)
        {
            if (TryGetFieldValue(target, fieldName, out float value))
                capture(value);
        }

        private static object GetStabilizer(object controller)
        {
            return GetFieldValue<object>(controller, "BFJKIBCBFHJ") ??
                   GetFieldValue<object>(controller, "BFJKIBCBFH");
        }

        private void ApplyAccessoryMode(string accessoryMode, SledDefaults defaults)
        {
            if (ActiveController == null)
                return;

            try
            {
                Type type = Type.GetType("SnowmobileAccessories, Assembly-CSharp");
                if (type == null)
                    return;

                var components = ActiveController.GetComponentsInChildren(type, true);
                if (components == null || components.Length == 0)
                    return;

                object accessories = components[0];
                bool utility = accessoryMode == "utility";
                bool raceTrim = accessoryMode == "race_trim";

                if (utility || raceTrim)
                {
                    SetGameObjectListActive(accessories, "windshieldObjects", utility);
                    SetGameObjectListActive(accessories, "snowFlapObjects", utility);
                    SetGameObjectListActive(accessories, "rearPartObjects", utility);
                    SetGameObjectListActive(accessories, "tunnelReflectors", utility);
                    return;
                }

                ApplyAccessoryDefaults(accessories, defaults);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Accessory mode apply skipped: {ex.Message}");
            }
        }

        private static void ApplyAccessoryDefaults(object accessories, SledDefaults defaults)
        {
            if (accessories == null || defaults == null || !defaults.hasAccessoryDefaults)
                return;

            SetGameObjectListActive(accessories, "windshieldObjects", defaults.hasWindshield);
            SetGameObjectListActive(accessories, "snowFlapObjects", defaults.hasSnowFlaps);
            SetGameObjectListActive(accessories, "rearPartObjects", defaults.hasRemovableRearParts);
            SetGameObjectListActive(accessories, "tunnelReflectors", defaults.hasTunnelAccessories);
        }

        private static void SetGameObjectListActive(object owner, string fieldName, bool active)
        {
            var field = owner.GetType().GetField(fieldName, BF);
            var enumerable = field?.GetValue(owner) as System.Collections.IEnumerable;
            if (enumerable == null)
                return;

            foreach (object item in enumerable)
            {
                if (item is GameObject go)
                    go.SetActive(active);
            }
        }

        private static VehicleScriptableObject TryGetVehicleSelectionSled(object menu)
        {
            object selection = GetFieldValue<object>(menu, "DICGGOJLMJP") ?? GetFieldValue<object>(menu, "IHKCPAEBKID");
            if (selection == null)
                return null;

            return GetFieldValue<VehicleScriptableObject>(selection, "KJFNKMCOKLL");
        }

        private static VehicleScriptableObject GetVehicleFromController(SnowmobileController controller)
        {
            if (controller == null)
                return null;

            var prop = typeof(SnowmobileController).GetProperty("GKMNAIKNNMJ", BF);
            var so = prop?.GetValue(controller) as VehicleScriptableObject;
            if (so != null)
                return so;

            return GetFieldValue<VehicleScriptableObject>(controller, "KJFNKMCOKLL");
        }

        private void OnLocalSledInitialized(SnowmobileController controller, Vector3 spawnPos, Quaternion spawnRot)
        {
            ActiveController = controller;
            ActiveSO = GetVehicleFromController(controller);
            ActiveRespawn = controller != null ? controller.GetComponent<Respawnable>() : null;
            ActiveSpawnPos = spawnPos;
            ActiveSpawnRot = spawnRot;

            if (ActiveSO == null)
            {
                MelonLogger.Warning("LocalInit detected no VehicleScriptableObject.");
                return;
            }

            TryBuildDefaults();
            RefreshStatDefaultsFromCleanLoad(ActiveSO);
            EnsureDefaultsForSled(ActiveSO);
            TryApplyActiveProfileForCurrentSled();
            MelonLogger.Msg($"Detected local sled '{ActiveSO.name}' for Alpine Tuning 2.0.");
        }

        private static T GetFieldValue<T>(object target, string fieldName)
        {
            if (target == null)
                return default;

            var field = target.GetType().GetField(fieldName, BF);
            if (field == null)
                return default;

            object value = field.GetValue(target);
            if (value is T typed)
                return typed;

            return default;
        }

        private static bool TryGetFieldValue<T>(object target, string fieldName, out T value)
        {
            value = default;
            if (target == null)
                return false;

            var field = target.GetType().GetField(fieldName, BF);
            if (field == null)
                return false;

            object raw = field.GetValue(target);
            if (!(raw is T typed))
                return false;

            value = typed;
            return true;
        }

        private static void SetFloatField(object target, string fieldName, float value)
        {
            SetFieldValue(target, fieldName, value);
        }

        private static void SetFieldValue(object target, string fieldName, object value)
        {
            if (target == null)
                return;

            var field = target.GetType().GetField(fieldName, BF);
            if (field == null)
                return;

            field.SetValue(target, value);
        }

        private static bool HasEngineAudioToken(SledDefaults defaults)
        {
            return defaults != null &&
                   !string.IsNullOrWhiteSpace(defaults.engineAudioEnumType) &&
                   (!string.IsNullOrWhiteSpace(defaults.engineAudioEnumName) ||
                    defaults.engineAudioEnumRawValue != 0);
        }

        private static MethodInfo FindMethodByNameAndParamCount(Type type, string name, int count)
        {
            return type.GetMethods(BF)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == count);
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
                    .GetMethods(BF)
                    .FirstOrDefault(m => m.Name == "SetEngineType" && m.GetParameters().Length == 1);

                _fiCurrentEngineType = _engineAudioControllerType.GetField("GILHLLEEAEH", BF);
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

        private static Component FindActiveEngineAudioController()
        {
            if (ActiveController == null || !ResolveEngineAudioReflection())
                return null;

            try
            {
                return ActiveController
                    .GetComponentsInChildren(_engineAudioControllerType, true)
                    .FirstOrDefault() as Component;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadActiveEngineAudioToken(out string enumTypeName, out string enumName, out int enumRawValue)
        {
            enumTypeName = null;
            enumName = null;
            enumRawValue = 0;

            if (!ResolveEngineAudioReflection() || _fiCurrentEngineType == null)
                return false;

            Component audioController = FindActiveEngineAudioController();
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

        private static bool TryReadEngineAudioTokenFromVehicleSO(
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
                foreach (var field in sled.GetType().GetFields(BF))
                {
                    if (field.FieldType != _engineAudioEnumType)
                        continue;

                    object value = field.GetValue(sled);
                    enumTypeName = value.GetType().AssemblyQualifiedName;
                    enumName = Enum.GetName(value.GetType(), value);
                    enumRawValue = Convert.ToInt32(value);
                    return true;
                }

                foreach (var prop in sled.GetType().GetProperties(BF))
                {
                    if (prop.PropertyType != _engineAudioEnumType || !prop.CanRead)
                        continue;

                    object value = prop.GetValue(sled);
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

        private bool TryPopulateDefaultAudioToken(SledDefaults defaults, VehicleScriptableObject sourceSO, bool allowLiveCapture)
        {
            if (defaults == null || HasEngineAudioToken(defaults))
                return HasEngineAudioToken(defaults);

            if (TryReadEngineAudioTokenFromVehicleSO(sourceSO, out var enumType, out var enumName, out var enumRaw))
            {
                defaults.engineAudioEnumType = enumType;
                defaults.engineAudioEnumName = enumName;
                defaults.engineAudioEnumRawValue = enumRaw;
                return true;
            }

            if (allowLiveCapture && sourceSO == ActiveSO &&
                TryReadActiveEngineAudioToken(out enumType, out enumName, out enumRaw))
            {
                defaults.engineAudioEnumType = enumType;
                defaults.engineAudioEnumName = enumName;
                defaults.engineAudioEnumRawValue = enumRaw;
                return true;
            }

            return false;
        }

        private void TryCaptureEngineAudioForCurrentSled()
        {
            if (ActiveSO == null)
                return;

            var defaults = Store.GetDefaults(GetSledKey(ActiveSO));
            if (defaults == null || HasEngineAudioToken(defaults))
                return;

            if (TryPopulateDefaultAudioToken(defaults, ActiveSO, true))
                Store.PutDefaults(defaults);
        }

        private void QueueEngineAudioSwap(SledDefaults audioDefaults, VehicleScriptableObject audioSourceSO)
        {
            if (!HasEngineAudioToken(audioDefaults))
                TryPopulateDefaultAudioToken(audioDefaults, audioSourceSO, audioSourceSO == ActiveSO);

            if (!HasEngineAudioToken(audioDefaults))
                return;

            _pendingEngineAudioEnumType = audioDefaults.engineAudioEnumType;
            _pendingEngineAudioEnumName = audioDefaults.engineAudioEnumName;
            _pendingEngineAudioEnumRawValue = audioDefaults.engineAudioEnumRawValue;
            _pendingEngineAudioApply = true;
            _pendingEngineAudioDeadline = Time.unscaledTime + 12f;
            _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.10f;
            _pendingEngineAudioAttemptsRemaining = 24;
            _pendingEngineAudioLastControllerId = int.MinValue;
            _pendingEngineAudioLoggedReady = false;
        }

        private void TryApplyPendingEngineAudioSwap()
        {
            if (!_pendingEngineAudioApply)
                return;

            if (Time.unscaledTime > _pendingEngineAudioDeadline)
            {
                _pendingEngineAudioApply = false;
                return;
            }

            if (Time.unscaledTime < _pendingEngineAudioNextAttemptTime || !ResolveEngineAudioReflection())
                return;

            Component audioController = FindActiveEngineAudioController();
            if (audioController == null)
            {
                _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.15f;
                return;
            }

            int controllerId = audioController.GetInstanceID();
            if (controllerId != _pendingEngineAudioLastControllerId)
            {
                _pendingEngineAudioLastControllerId = controllerId;
                _pendingEngineAudioAttemptsRemaining = Mathf.Max(_pendingEngineAudioAttemptsRemaining, 18);
                _pendingEngineAudioLoggedReady = false;
            }

            try
            {
                Type enumType = Type.GetType(_pendingEngineAudioEnumType) ?? _engineAudioEnumType;
                object desiredValue = !string.IsNullOrWhiteSpace(_pendingEngineAudioEnumName) &&
                                      Enum.IsDefined(enumType, _pendingEngineAudioEnumName)
                    ? Enum.Parse(enumType, _pendingEngineAudioEnumName)
                    : Enum.ToObject(enumType, _pendingEngineAudioEnumRawValue);

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

                if (!_pendingEngineAudioLoggedReady)
                {
                    MelonLogger.Msg($"Engine audio target applied: {_pendingEngineAudioEnumName} ({_pendingEngineAudioEnumRawValue}).");
                    _pendingEngineAudioLoggedReady = true;
                }

                _pendingEngineAudioAttemptsRemaining--;
                if (_pendingEngineAudioAttemptsRemaining <= 0)
                    _pendingEngineAudioApply = false;

                _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.20f;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Engine audio swap retry failed: {ex.Message}");
                _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.35f;
            }
        }

        [HarmonyPatch(typeof(SnowmobileController), "LocalInit")]
        public static class PatchLocalInit
        {
            public static void Postfix(SnowmobileController __instance, Vector3 KMFHFHOFBFH, Quaternion LPNJFGKBIIC)
            {
                try
                {
                    Instance?.OnLocalSledInitialized(__instance, KMFHFHOFBFH, LPNJFGKBIIC);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Alpine LocalInit patch failed: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(PauseUIController), "Pause")]
        public static class PatchPauseOpen
        {
            public static void Postfix(PauseUIController __instance)
            {
                AlpineNativeUi.AttachToPause(Instance, __instance);
            }
        }
    }
}
