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
        internal AlpineRemoteReplication RemoteReplication { get; private set; }

        private readonly List<VehicleScriptableObject> _selectableSleds = new List<VehicleScriptableObject>();
        private readonly HashSet<string> _sledsModifiedByAlpineThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _defaultCaptureSkipLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingCurrentSetupSave> _pendingCurrentSetupSaves =
            new Dictionary<string, PendingCurrentSetupSave>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, GarageSelectionState> _garageSelectionsByController =
            new Dictionary<int, GarageSelectionState>();
        // Sledders vehicle physics use Unity world units; set false if a map's world Y is not altitude-like.
        private const bool UseWorldYAsAltitudeMeters = true;
        private const float CurrentSetupSaveDelaySeconds = 0.35f;
        private bool _defaultsBuilt;
        private float _nextNativeUiScanTime;
        private float _nextCurrentSetupFlushTime;
        private bool _shutdownComplete;
        private string _lastResolvedTargetLogKey;

        private string _pendingEngineAudioEnumType;
        private string _pendingEngineAudioEnumName;
        private int _pendingEngineAudioEnumRawValue;
        private bool _pendingEngineAudioApply;
        private float _pendingEngineAudioDeadline;
        private float _pendingEngineAudioNextAttemptTime;
        private int _pendingEngineAudioAttemptsRemaining;
        private int _pendingEngineAudioLastControllerId = int.MinValue;
        private bool _pendingEngineAudioLoggedReady;
        private RuntimeHeadlightDefaults _activeHeadlightDefaults;
        private int _headlightDefaultsControllerId = int.MinValue;
        private bool? _activeHeadlightOverride;
        private bool _waitingForHeadlightKeyboardBinding;
        private bool _waitingForHeadlightControllerBinding;
        private float _headlightBindingCaptureDeadline;
        private float _nextHeadlightToggleTime;
        private bool _lastHeadlightToggleHadTarget;
        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        private sealed class RuntimeHeadlightDefaults
        {
            public readonly List<RuntimeHeadlightDefault> lights = new List<RuntimeHeadlightDefault>();
        }

        private sealed class RuntimeHeadlightDefault
        {
            public Light light;
            public Color color;
            public float intensity;
            public float range;
            public float spotAngle;
            public Quaternion localRotation;
            public bool enabled;
        }

        private sealed class PendingCurrentSetupSave
        {
            public string sledKey;
            public string vehicleId;
            public float dueTime;
        }

        private sealed class GarageSelectionState
        {
            public VehicleScriptableObject sled;
            public string source;
            public string stableKey;
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            SleddersGameBindings.Initialize();
            Catalog = new PartCatalog();
            Store = new TuneStore(Catalog);
            Store.Initialize();
            RemoteReplication = new AlpineRemoteReplication();
            Sharing = new AlpinePeerSharing(this);
            Sharing.Initialize();

            MelonLogger.Msg(
                $"Alpine Tuning 2.0 initialized. Mod={AlpineConstants.ModVersion}, " +
                $"Schema={AlpineConstants.SchemaVersion}, Catalog={AlpineConstants.CatalogVersion}");
            MelonLogger.Msg(SleddersGameBindings.CapabilitySummary);
            MelonLogger.Msg(Store.DiagnosticsSummary);
            MelonLogger.Msg($"Peer sharing available: {Sharing.IsAvailable}");
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime >= _nextNativeUiScanTime)
            {
                bool attached = AlpineNativeUi.TryAttachOpenMenus(this);
                _nextNativeUiScanTime = Time.unscaledTime + (attached || AlpineNativeUi.HasAttachedMenus ? 3f : 0.20f);
            }

            Sharing?.Update();
            FlushPendingCurrentSetups(false);
            UpdateHeadlightInputBinding();
            EnforceHeadlightOverride();

            if (ActiveSO == null)
                return;

            if (!_defaultsBuilt)
                TryBuildDefaults();

            TryCaptureEngineAudioForCurrentSled();
            TryApplyPendingEngineAudioSwap();
        }

        public override void OnLateUpdate()
        {
            EnforceHeadlightOverride();
        }

        public override void OnDeinitializeMelon()
        {
            ShutdownRuntime();
        }

        public override void OnApplicationQuit()
        {
            ShutdownRuntime();
        }

        private void ShutdownRuntime()
        {
            if (_shutdownComplete)
                return;

            _shutdownComplete = true;

            try
            {
                FlushPendingCurrentSetups(true);
                Sharing?.Shutdown();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine shutdown sharing cleanup skipped: {ex.Message}");
            }

            _pendingEngineAudioApply = false;
            _pendingEngineAudioEnumType = null;
            _pendingEngineAudioEnumName = null;
            _pendingEngineAudioEnumRawValue = 0;
            _pendingEngineAudioAttemptsRemaining = 0;
            _pendingEngineAudioLastControllerId = int.MinValue;
            _pendingEngineAudioLoggedReady = false;
            _activeHeadlightDefaults = null;
            _headlightDefaultsControllerId = int.MinValue;
            _activeHeadlightOverride = null;

            if (Instance == this)
                Instance = null;

            ActiveSO = null;
            ActiveController = null;
            ActiveRespawn = null;
            ActiveSpawnPos = default;
            ActiveSpawnRot = default;
            _garageSelectionsByController.Clear();
        }

        internal string LocalAuthorName
        {
            get
            {
                if (Sharing != null && !string.IsNullOrWhiteSpace(Sharing.LocalName))
                    return Sharing.LocalName;

                return "Alpine-User";
            }
        }

        internal AlpineUserSettings Settings
        {
            get { return Store != null ? Store.Settings : new AlpineUserSettings(); }
        }

        internal bool SaveSettings()
        {
            return Store != null && Store.SaveSettings();
        }

        internal bool IsCapturingHeadlightBinding =>
            _waitingForHeadlightKeyboardBinding || _waitingForHeadlightControllerBinding;

        internal string HeadlightBindingCaptureLabel
        {
            get
            {
                if (_waitingForHeadlightKeyboardBinding)
                    return "Press the keyboard key to save as the headlight hotkey.";

                if (_waitingForHeadlightControllerBinding)
                    return "Press the controller button to save as the headlight hotkey.";

                return null;
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

        public static string GetSledDisplayName(VehicleScriptableObject sled)
        {
            if (sled == null)
                return "Sled";

            if (!string.IsNullOrWhiteSpace(sled.displayName))
                return sled.displayName;

            return !string.IsNullOrWhiteSpace(sled.name) ? sled.name : "Sled";
        }

        public static string GetVehicleId(VehicleScriptableObject sled)
        {
            if (sled == null)
                return null;

            return SleddersGameBindings.GetVehicleId(sled, GetSledKey(sled));
        }

        internal VehicleScriptableObject ResolveTargetSled(object menuContext)
        {
            return ResolveTargetSledContext(menuContext).sled;
        }

        internal ResolvedSledTarget ResolveTargetSledContext(object menuContext)
        {
            VehicleScriptableObject sled = null;
            string source = null;
            bool fromGarage = false;
            bool fromRuntime = false;

            if (menuContext is VehicleScriptableObject direct)
            {
                sled = direct;
                source = "direct sled";
            }
            else if (menuContext is VehicleSelectionUiController garage)
            {
                sled = SleddersGameBindings.TryGetVehicleSelectionSled(garage, out source);
                if (sled != null)
                {
                    CacheGarageSelection(garage, sled, source, true);
                    fromGarage = true;
                }
                else if (TryGetCachedGarageSelection(garage, out sled, out source))
                {
                    fromGarage = true;
                }
            }
            else if (menuContext is PauseUIController pause)
            {
                var controller = SleddersGameBindings.GetPauseController(pause);
                sled = SleddersGameBindings.GetVehicleFromController(controller);
                source = sled != null ? "pause runtime sled" : null;
                fromRuntime = sled != null;
            }

            if (sled == null && ActiveSO != null)
            {
                sled = ActiveSO;
                source = "runtime fallback";
                fromRuntime = true;
            }

            var identity = SledIdentity.FromSled(sled, source, fromGarage, fromRuntime);
            bool hasRuntime = identity != null && DoesActiveRuntimeMatch(identity);
            var resolved = new ResolvedSledTarget
            {
                sled = sled,
                identity = identity,
                hasRuntimeInstance = hasRuntime,
                status = sled == null
                    ? "Select a sled to edit its setup"
                    : hasRuntime
                        ? "Updated"
                        : "Ready"
            };

            LogResolvedTargetChange(resolved);
            return resolved;
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

        internal AlpinePerformanceEstimate EstimatePerformance(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (profile == null || sled == null)
                return new AlpinePerformanceEstimate();

            var computation = ComputeProfile(profile, sled);
            profile.resolvedStats = computation.stats;
            profile.requiresReload = computation.requiresReload;

            return AlpinePerformanceEstimate.Build(
                GetSledDisplayName(sled),
                CurrentSetupDisplayName(profile),
                computation.baseDefaults,
                computation);
        }

        internal bool ApplyProfile(TuneProfile profile, VehicleScriptableObject sled, bool persist, bool reloadIfNeeded)
        {
            string ignored;
            return ApplyProfile(profile, sled, persist, reloadIfNeeded, out ignored);
        }

        internal bool ApplyProfile(TuneProfile profile, VehicleScriptableObject sled, bool persist, bool reloadIfNeeded, out string status)
        {
            return ApplyProfile(profile, sled, persist, reloadIfNeeded, out status, true, true);
        }

        private bool ApplyProfile(
            TuneProfile profile,
            VehicleScriptableObject sled,
            bool persist,
            bool reloadIfNeeded,
            out string status,
            bool notifyActive,
            bool logApplied)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to edit its setup.";
                return false;
            }

            try
            {
                TryBuildDefaults();
                Catalog.EnsureProfileSelections(profile);

                var computation = ComputeProfile(profile, sled);

                // Always return to the captured stock baseline before applying a profile.
                // This makes part application idempotent: changing from one Alpine build to
                // another never multiplies against values left behind by the previous build.
                if (!IsSledModifiedByAlpine(sled) &&
                    !HasEngineAudioToken(computation.baseDefaults) &&
                    TryPopulateDefaultAudioToken(computation.baseDefaults, sled, false))
                {
                    Store.PutDefaults(computation.baseDefaults);
                }

                ApplyDefaultsToSled(sled, computation.baseDefaults);
                if (sled == ActiveSO)
                {
                    ApplyRuntimeDefaults(computation.baseDefaults);
                    ApplyHeadlightDefaults();
                }

                ApplyStatsToSled(sled, computation);
                ApplyEngineAudioToSled(sled, computation.audioDefaults, computation.audioSource);
                if (sled == ActiveSO)
                {
                    ApplyRuntimeController(computation, profile);
                    ApplyHeadlightRuntime(computation.mergedEffect, profile);
                    ApplyAccessoryMode(computation.mergedEffect.accessoryMode, computation.baseDefaults);
                }

                MarkSledModifiedByAlpine(sled);

                profile.resolvedStats = computation.stats;
                profile.requiresReload = computation.requiresReload;
                profile.targetSledKey = GetSledKey(sled);
                profile.targetVehicleId = GetVehicleId(sled);

                if (sled == ActiveSO)
                    QueueEngineAudioSwap(computation.audioDefaults, computation.audioSource);

                if (persist && !Store.SaveProfile(profile, true))
                {
                    status = "Setup installed, but saving failed.";
                    MelonLogger.Warning(status);
                    return false;
                }

                if (persist && notifyActive)
                    NotifyActiveTuneChanged(profile, sled);

                if (logApplied)
                    LogAppliedTune(profile, sled, computation);

                if (reloadIfNeeded && computation.requiresReload && sled == ActiveSO)
                    ReloadSled();

                if (reloadIfNeeded && computation.requiresReload && sled == ActiveSO)
                    status = persist ? "Setup saved and ready." : "Setup ready.";
                else
                    status = persist ? "Setup saved." : "Setup updated.";
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ApplyProfile failed: {ex}");
                status = $"Setup update failed: {ex.Message}";
                return false;
            }
        }

        internal bool SaveProfile(TuneProfile profile, VehicleScriptableObject sled, bool makeActive)
        {
            if (profile == null || sled == null)
                return false;

            try
            {
                PreviewProfile(profile, sled);
                bool saved = Store.SaveProfile(profile, makeActive);
                if (saved && makeActive)
                    NotifyActiveTuneChanged(profile, sled);

                return saved;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SaveProfile failed: {ex}");
                return false;
            }
        }

        internal bool SaveCurrentSetupAsSlot(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to save its setup.";
                return false;
            }

            try
            {
                Catalog.EnsureProfileSelections(profile);
                PreviewProfile(profile, sled);

                if (string.IsNullOrWhiteSpace(profile.name))
                    profile.name = $"{GetSledDisplayName(sled)} Setup";

                bool saved = Store.SaveProfile(profile, false);
                if (!saved)
                {
                    status = "Setup save failed.";
                    return false;
                }

                profile.setupSlotId = profile.profileId;
                profile.setupSlotName = profile.name;
                profile.setupEdited = false;
                profile.isCurrentSetup = true;
                QueueCurrentSetupSave(profile, sled, true);

                status = "Setup saved.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Setup save failed: {ex.Message}";
                MelonLogger.Error($"SaveCurrentSetupAsSlot failed: {ex}");
                return false;
            }
        }

        internal bool SaveCurrentSetupAsDefault(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to save its default setup.";
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(profile.name))
                    profile.name = $"{GetSledDisplayName(sled)} Default Setup";

                string applyStatus;
                if (!UpdateCurrentSetup(profile, sled, out applyStatus))
                {
                    status = string.IsNullOrWhiteSpace(applyStatus)
                        ? "Default setup could not be applied."
                        : applyStatus;
                    return false;
                }

                var defaultProfile = TuneStore.Clone(profile);
                defaultProfile.isCurrentSetup = false;
                defaultProfile.setupEdited = false;
                defaultProfile.setupSlotId = null;
                defaultProfile.setupSlotName = null;

                bool saved = Store.SaveProfile(defaultProfile, true);
                if (!saved)
                {
                    status = "Default setup save failed.";
                    return false;
                }

                profile.profileId = defaultProfile.profileId;
                profile.setupSlotId = defaultProfile.profileId;
                profile.setupSlotName = defaultProfile.name;
                profile.setupEdited = false;
                profile.isCurrentSetup = true;
                QueueCurrentSetupSave(profile, sled, true);
                NotifyActiveTuneChanged(profile, sled);

                status = HasRuntimeInstanceForSled(sled)
                    ? "Default setup saved and active."
                    : "Default setup saved for next ride.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Default setup save failed: {ex.Message}";
                MelonLogger.Error($"SaveCurrentSetupAsDefault failed: {ex}");
                return false;
            }
        }

        internal bool UpdateCurrentSetup(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to edit its setup.";
                return false;
            }

            try
            {
                Catalog.EnsureProfileSelections(profile);
                profile.targetSledKey = GetSledKey(sled);
                profile.targetVehicleId = GetVehicleId(sled);
                profile.isCurrentSetup = true;
                profile.setupEdited = true;
                PreviewProfile(profile, sled);

                QueueCurrentSetupSave(profile, sled, false);

                bool hasRuntime = HasRuntimeInstanceForSled(sled);
                VehicleScriptableObject applyTarget = hasRuntime && ActiveSO != null ? ActiveSO : sled;
                string applyStatus;
                if (!ApplyProfile(profile, applyTarget, false, false, out applyStatus, false, false))
                {
                    status = string.IsNullOrWhiteSpace(applyStatus)
                        ? "Current Setup preserved."
                        : applyStatus;
                    return false;
                }

                status = hasRuntime && profile.requiresReload
                    ? "Ready for next ride."
                    : hasRuntime
                        ? "Setup updated."
                        : "Current Setup preserved.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Setup update failed: {ex.Message}";
                MelonLogger.Warning($"UpdateCurrentSetup failed: {ex.Message}");
                return false;
            }
        }

        internal bool EquipSetupSlot(TuneProfile profile, VehicleScriptableObject sled, out TuneProfile equipped, out string status)
        {
            equipped = null;
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a setup slot first.";
                return false;
            }

            equipped = TuneStore.Clone(profile);
            equipped.setupSlotId = profile.profileId;
            equipped.setupSlotName = profile.name;
            equipped.setupEdited = false;
            equipped.isCurrentSetup = true;

            if (!UpdateCurrentSetup(equipped, sled, out status))
                return false;

            equipped.setupEdited = false;
            QueueCurrentSetupSave(equipped, sled, true);
            status = $"Equipped {profile.name}.";
            return true;
        }

        internal bool SetDefaultSetup(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a setup slot first.";
                return false;
            }

            bool saved = Store.SetActiveProfile(GetSledKey(sled), profile.profileId);
            status = saved ? "Default setup selected." : "Default setup could not be selected.";
            return saved;
        }

        internal bool RenameSetupSlot(TuneProfile profile, VehicleScriptableObject sled, string newName, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a setup slot first.";
                return false;
            }

            string trimmed = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                status = "Setup name cannot be empty.";
                return false;
            }

            var renamed = TuneStore.Clone(profile);
            renamed.name = trimmed;
            bool saved = Store.SaveProfile(renamed, false);
            status = saved ? "Setup renamed." : "Setup rename failed.";
            return saved;
        }

        internal TuneProfile DuplicateSetupSlot(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a setup slot first.";
                return null;
            }

            var duplicate = TuneStore.Clone(profile);
            duplicate.profileId = Guid.NewGuid().ToString("N");
            duplicate.name = string.IsNullOrWhiteSpace(profile.name)
                ? "Copied Setup"
                : profile.name + " Copy";
            duplicate.setupSlotId = duplicate.profileId;
            duplicate.setupSlotName = duplicate.name;
            duplicate.setupEdited = false;
            duplicate.isCurrentSetup = false;

            if (!Store.SaveProfile(duplicate, false))
            {
                status = "Setup duplicate failed.";
                return null;
            }

            status = "Setup duplicated.";
            return duplicate;
        }

        internal void DeleteProfile(string profileId)
        {
            Store.DeleteProfile(profileId);
        }

        internal bool ResetToFactory(VehicleScriptableObject sled, bool reloadIfActive)
        {
            if (sled == null)
                return false;

            try
            {
                TryBuildDefaults();
                string sledKey = GetSledKey(sled);
                var defaults = Store.GetDefaults(sledKey);
                if (defaults == null)
                {
                    MelonLogger.Warning($"No defaults found for {sledKey}; reset skipped.");
                    return false;
                }

                if (!IsSledModifiedByAlpine(sled) &&
                    !HasEngineAudioToken(defaults) &&
                    TryPopulateDefaultAudioToken(defaults, sled, false))
                {
                    Store.PutDefaults(defaults);
                }

                ApplyDefaultsToSled(sled, defaults);
                UnmarkSledModifiedByAlpine(sled);
                if (!Store.SetActiveProfile(sledKey, null))
                    return false;

                var stockSetup = Catalog.CreateDefaultProfile(sled, LocalAuthorName);
                stockSetup.name = "Stock Setup";
                stockSetup.setupSlotId = null;
                stockSetup.setupSlotName = "Stock Setup";
                stockSetup.setupEdited = false;
                stockSetup.isCurrentSetup = true;
                PreviewProfile(stockSetup, sled);
                QueueCurrentSetupSave(stockSetup, sled, true);

                NotifyActiveTuneCleared(sled);

                if (sled == ActiveSO)
                {
                    ApplyRuntimeDefaults(defaults);
                    ApplyHeadlightDefaults();
                    ApplyAccessoryMode("stock", defaults);
                    QueueEngineAudioSwap(defaults, sled);
                    if (reloadIfActive)
                        ReloadSled();
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ResetToFactory failed: {ex}");
                return false;
            }
        }

        internal List<TuneProfile> ProfilesForSled(VehicleScriptableObject sled)
        {
            if (sled == null)
                return new List<TuneProfile>();

            return Store.GetProfilesForSled(GetSledKey(sled), GetVehicleId(sled));
        }

        internal bool PublishProfile(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (Sharing == null || profile == null)
                return false;

            try
            {
                PreviewProfile(profile, sled);
                return Sharing.PublishProfile(profile);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PublishProfile failed: {ex}");
                return false;
            }
        }

        internal void NotifyActiveTuneChanged(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (Sharing == null || profile == null || sled == null)
                return;

            var settings = Settings;
            if (settings == null || (!settings.shareMySetup && !settings.alwaysShareMySetup))
                return;

            try
            {
                var clone = TuneStore.Clone(profile);
                PreviewProfile(clone, sled);
                Sharing.BroadcastActiveTune(clone);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Active Alpine tune broadcast skipped: {ex.Message}");
            }
        }

        internal void NotifyActiveTuneCleared(VehicleScriptableObject sled)
        {
            if (Sharing == null)
                return;

            try
            {
                Sharing.BroadcastActiveTuneClear(
                    sled != null ? GetSledKey(sled) : null,
                    sled != null ? GetVehicleId(sled) : null);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Active Alpine tune clear broadcast skipped: {ex.Message}");
            }
        }

        internal bool TryApplyRemoteRuntimeTune(ulong senderId, TuneProfile profile, out string status)
        {
            status = null;
            if (senderId == 0 || profile == null)
            {
                status = "Remote setup state is invalid.";
                return false;
            }

            if (!TuneStore.TryValidateProfileForCatalog(profile, Catalog, true, true, out var reason))
            {
                status = $"Remote setup rejected: {reason}.";
                return false;
            }

            VehicleScriptableObject target = FindSledByIdentity(profile.targetSledKey, profile.targetVehicleId);
            if (target == null)
            {
                status = "Remote setup target is not compatible with this install.";
                return false;
            }

            try
            {
                var clone = TuneStore.Clone(profile);
                Catalog.EnsureProfileSelections(clone);
                var computation = ComputeProfile(clone, target);
                clone.resolvedStats = computation.stats;
                clone.requiresReload = computation.requiresReload;
                if (Settings == null || !Settings.receivePeerSetups)
                {
                    status = "Remote setup received, but receiving peer setups is off.";
                    return false;
                }

                var runtimeSettings = new AlpineUserSettings
                {
                    receivePeerAudio = Settings.receivePeerAudio,
                    receivePeerLighting = Settings.receivePeerLighting,
                    receivePeerVisualEquipment = Settings.receivePeerVisualEquipment
                };

                RemoteActiveTuneState peerState;
                if (Sharing != null && Sharing.TryGetRemoteActiveState(senderId, out peerState) && peerState != null)
                {
                    if (!peerState.shareSetup)
                    {
                        status = "Remote setup detected, but that rider is not sharing setup effects.";
                        return false;
                    }

                    runtimeSettings.receivePeerAudio &= peerState.shareAudio;
                    runtimeSettings.receivePeerLighting &= peerState.shareLighting;
                    runtimeSettings.receivePeerVisualEquipment &= peerState.shareVisualEquipment;
                }

                return RemoteReplication != null &&
                       RemoteReplication.TryApply(senderId, clone, computation, runtimeSettings, out status);
            }
            catch (Exception ex)
            {
                status = $"Remote setup install failed: {ex.Message}";
                MelonLogger.Warning(status);
                return false;
            }
        }

        internal TuneProfile ImportSharedProfile(TuneProfile profile)
        {
            return Store.ImportSharedProfile(profile);
        }

        internal bool RequestSharedProfile(ulong peerId, string profileId)
        {
            return Sharing != null && Sharing.RequestProfile(peerId, profileId);
        }

        internal bool ApplySharedProfile(string profileId)
        {
            string ignored;
            return ApplySharedProfile(0, profileId, out ignored);
        }

        internal bool ApplySharedProfile(ulong senderId, string profileId, out string status)
        {
            status = null;
            if (Sharing == null)
            {
                status = "Peer sharing is unavailable.";
                return false;
            }

            var profile = senderId != 0
                ? Sharing.GetPayload(senderId, profileId, out status)
                : Sharing.GetPayload(profileId);
            if (profile == null)
            {
                if (string.IsNullOrWhiteSpace(status))
                    status = "Shared payload is missing or expired.";
                return false;
            }

            VehicleScriptableObject target = FindSledByIdentity(profile.targetSledKey, profile.targetVehicleId);
            if (target == null)
            {
                status = "Shared setup target is not compatible with this install.";
                return false;
            }

            var imported = Store.ImportSharedProfile(profile);
            if (imported == null)
            {
                status = "Shared setup import failed.";
                return false;
            }

            TuneProfile equipped;
            return EquipSetupSlot(imported, target, out equipped, out status);
        }

        internal VehicleScriptableObject FindSledByKey(string sledKey)
        {
            TryBuildDefaults();
            return _selectableSleds.FirstOrDefault(s => GetSledKey(s) == sledKey);
        }

        internal VehicleScriptableObject FindSledByIdentity(string sledKey, string vehicleId)
        {
            TryBuildDefaults();

            if (!string.IsNullOrWhiteSpace(vehicleId))
            {
                var byVehicleId = _selectableSleds.FirstOrDefault(s =>
                    string.Equals(GetVehicleId(s), vehicleId, StringComparison.OrdinalIgnoreCase));

                if (byVehicleId != null)
                    return byVehicleId;
            }

            if (!string.IsNullOrWhiteSpace(sledKey))
                return _selectableSleds.FirstOrDefault(s =>
                    string.Equals(GetSledKey(s), sledKey, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        internal bool CanResolveSledTarget(string sledKey, string vehicleId)
        {
            return FindSledByIdentity(sledKey, vehicleId) != null;
        }

        internal bool HasRuntimeInstanceForSled(VehicleScriptableObject sled)
        {
            if (sled == null || ActiveSO == null)
                return false;

            return SledIdentity.FromSled(sled, "target", false, false)
                .Matches(ActiveSO);
        }

        internal void NoteGarageSledSelectionChanged(
            VehicleSelectionUiController controller,
            VehicleScriptableObject sled,
            string source)
        {
            if (CacheGarageSelection(controller, sled, source, true))
                AlpineNativeUi.NotifyGarageSelectionChanged(controller);
        }

        private bool CacheGarageSelection(
            VehicleSelectionUiController controller,
            VehicleScriptableObject sled,
            string source,
            bool logChange)
        {
            if (controller == null || sled == null)
                return false;

            int controllerId = controller.GetInstanceID();
            string stableKey = SledIdentity.StableIdentityKey(sled);
            if (string.IsNullOrWhiteSpace(stableKey))
                return false;

            if (_garageSelectionsByController.TryGetValue(controllerId, out var existing) &&
                string.Equals(existing.stableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _garageSelectionsByController[controllerId] = new GarageSelectionState
            {
                sled = sled,
                source = source,
                stableKey = stableKey
            };

            if (logChange)
            {
                MelonLogger.Msg(
                    $"Garage sled selection changed: {GetSledDisplayName(sled)} " +
                    $"(sledKey={GetSledKey(sled)}, vehicleId={GetVehicleId(sled) ?? "none"}, source={source ?? "garage"}).");
            }

            return true;
        }

        private bool TryGetCachedGarageSelection(
            VehicleSelectionUiController controller,
            out VehicleScriptableObject sled,
            out string source)
        {
            sled = null;
            source = null;
            if (controller == null)
                return false;

            int controllerId = controller.GetInstanceID();
            if (!_garageSelectionsByController.TryGetValue(controllerId, out var state) ||
                state == null ||
                state.sled == null)
            {
                return false;
            }

            sled = state.sled;
            source = state.source ?? "garage selection";
            return true;
        }

        internal string CurrentSetupDisplayName(TuneProfile profile)
        {
            if (profile == null)
                return "Current Setup";

            if (!string.IsNullOrWhiteSpace(profile.setupSlotName))
                return profile.setupSlotName + (profile.setupEdited ? "*" : string.Empty);

            return (string.IsNullOrWhiteSpace(profile.name) ? "Current Setup" : profile.name) +
                   (profile.setupEdited ? "*" : string.Empty);
        }

        private bool DoesActiveRuntimeMatch(SledIdentity identity)
        {
            return identity != null && ActiveSO != null && identity.Matches(ActiveSO);
        }

        private void LogResolvedTargetChange(ResolvedSledTarget resolved)
        {
            if (resolved == null || !resolved.HasSled)
                return;

            string key =
                resolved.identity.StableKey + "|" +
                (resolved.identity.fromGarageSelection ? "garage" : resolved.identity.fromRuntime ? "runtime" : "direct") + "|" +
                (resolved.hasRuntimeInstance ? "live" : "preserved");

            if (string.Equals(key, _lastResolvedTargetLogKey, StringComparison.OrdinalIgnoreCase))
                return;

            _lastResolvedTargetLogKey = key;
            MelonLogger.Msg(
                $"Sled setup target: {resolved.identity.displayName} " +
                $"(sledKey={resolved.identity.sledKey}, vehicleId={resolved.identity.vehicleId ?? "none"}, " +
                $"source={resolved.identity.source ?? "unknown"}, runtime={(resolved.hasRuntimeInstance ? "matched" : "not spawned")}).");
        }

        private void QueueCurrentSetupSave(TuneProfile profile, VehicleScriptableObject sled, bool writeNow)
        {
            if (profile == null || sled == null || Store == null)
                return;

            string sledKey = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            string displayName = GetSledDisplayName(sled);
            string key = SledIdentity.StableIdentityKey(sledKey, vehicleId);
            if (string.IsNullOrWhiteSpace(key))
                return;

            Store.SetCurrentSetup(
                profile,
                sledKey,
                vehicleId,
                displayName,
                profile.setupSlotId,
                profile.setupSlotName,
                profile.setupEdited,
                writeNow);

            if (writeNow)
            {
                _pendingCurrentSetupSaves.Remove(key);
                MelonLogger.Msg($"Current Setup preserved for {displayName}.");
                return;
            }

            _pendingCurrentSetupSaves[key] = new PendingCurrentSetupSave
            {
                sledKey = sledKey,
                vehicleId = vehicleId,
                dueTime = Time.unscaledTime + CurrentSetupSaveDelaySeconds
            };
        }

        private void FlushPendingCurrentSetups(bool force)
        {
            if (Store == null || _pendingCurrentSetupSaves.Count == 0)
                return;

            float now = Time.unscaledTime;
            if (!force && now < _nextCurrentSetupFlushTime)
                return;

            _nextCurrentSetupFlushTime = now + 0.10f;
            var ready = _pendingCurrentSetupSaves
                .Where(pair => force || now >= pair.Value.dueTime)
                .Select(pair => pair.Key)
                .ToList();

            foreach (string key in ready)
            {
                var pending = _pendingCurrentSetupSaves[key];
                if (Store.FlushCurrentSetup(pending.sledKey, pending.vehicleId))
                    _pendingCurrentSetupSaves.Remove(key);
            }
        }

        internal bool HasActiveHeadlightRuntimeBinding()
        {
            CaptureHeadlightDefaultsForActiveController(false);
            return _activeHeadlightDefaults != null && _activeHeadlightDefaults.lights.Count > 0;
        }

        internal void ReloadSled()
        {
            try
            {
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

                if (!SleddersGameBindings.TrySpawnPlayer(spawnTransform, true, out var reason))
                {
                    MelonLogger.Error($"ReloadSled: {reason}");
                    return;
                }

                MelonLogger.Msg("Alpine Tuning 2.0 triggered sled reload.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ReloadSled failed: {ex}");
            }
        }

        private bool IsSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return false;

            string sledKey = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            return _sledsModifiedByAlpineThisSession.Contains(sledKey) ||
                   (!string.IsNullOrWhiteSpace(vehicleId) && _sledsModifiedByAlpineThisSession.Contains(vehicleId));
        }

        private void MarkSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            _sledsModifiedByAlpineThisSession.Add(GetSledKey(sled));
            string vehicleId = GetVehicleId(sled);
            if (!string.IsNullOrWhiteSpace(vehicleId))
                _sledsModifiedByAlpineThisSession.Add(vehicleId);
        }

        private void UnmarkSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            _sledsModifiedByAlpineThisSession.Remove(GetSledKey(sled));
            string vehicleId = GetVehicleId(sled);
            if (!string.IsNullOrWhiteSpace(vehicleId))
                _sledsModifiedByAlpineThisSession.Remove(vehicleId);
        }

        private void LogDefaultCaptureSkipped(string sledKey, string reason)
        {
            string key = sledKey + "|" + reason;
            if (!_defaultCaptureSkipLogged.Add(key))
                return;

            MelonLogger.Warning(
                $"Skipped default capture for '{sledKey}' because {reason}. " +
                "Stored clean defaults were preserved.");
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
                    RefreshStatDefaultsFromCleanLoad(sled);
                    EnsureDefaultsForSled(sled);
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

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lists = Resources.FindObjectsOfTypeAll<VehicleListScriptableObject>();
            if (lists != null)
            {
                foreach (var list in lists)
                {
                    foreach (var sled in SleddersGameBindings.GetSelectableVehicles(list))
                        AddSelectableSled(sled, seen);
                }
            }

            var loadedSleds = Resources.FindObjectsOfTypeAll<VehicleScriptableObject>();
            if (loadedSleds != null)
            {
                foreach (var sled in loadedSleds)
                    AddSelectableSled(sled, seen);
            }

            AddSelectableSled(ActiveSO, seen);

            _selectableSleds.Sort((a, b) =>
                string.Compare(GetSledDisplayName(a), GetSledDisplayName(b), StringComparison.OrdinalIgnoreCase));

            if (_selectableSleds.Count == 0)
                MelonLogger.Warning("No VehicleScriptableObject found; garage tuning list will be limited.");
            else
                MelonLogger.Msg($"Alpine discovered {_selectableSleds.Count} sled donor candidate(s).");
        }

        private void AddSelectableSled(VehicleScriptableObject sled, HashSet<string> seen)
        {
            if (sled == null || seen == null)
                return;

            string key = SledIdentity.StableIdentityKey(sled);
            if (string.IsNullOrWhiteSpace(key))
                key = GetSledKey(sled);

            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                return;

            _selectableSleds.Add(sled);
        }

        private void EnsureDefaultsForSled(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            bool modifiedByAlpine = IsSledModifiedByAlpine(sled);
            var defaults = Store.GetDefaults(key);
            if (defaults == null)
            {
                if (modifiedByAlpine)
                {
                    LogDefaultCaptureSkipped(key, "no stored defaults exist yet");
                    return;
                }

                defaults = SledDefaults.FromSled(sled, key);
                TryPopulateDefaultAudioToken(defaults, sled, false);
                Store.PutDefaults(defaults);
            }

            if (sled == ActiveSO && ActiveController != null)
            {
                if (modifiedByAlpine && HasCapturedControllerDefaults(defaults))
                {
                    LogDefaultCaptureSkipped(key, "active controller has already been tuned by Alpine");
                    return;
                }

                CaptureRuntimeDefaults(defaults, ActiveController);
                Store.PutDefaults(defaults);
            }
        }

        private static bool HasCapturedControllerDefaults(SledDefaults defaults)
        {
            if (defaults == null || defaults.controller == null)
                return false;

            var controller = defaults.controller;
            return controller.hasThrottleExponent ||
                   controller.hasRpmSensitivity ||
                   controller.hasRpmSensitivityDown ||
                   controller.hasClutchRpmMin ||
                   controller.hasClutchRpmMax ||
                   controller.hasMinThrottleOnClutchEngagement ||
                   controller.hasWheelieThreshold ||
                   controller.hasStabilizerDamping ||
                   controller.hasTrackSpeedDamping ||
                   controller.hasTrackSpeedGyroMultiplier;
        }

        private void RefreshStatDefaultsFromCleanLoad(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            if (IsSledModifiedByAlpine(sled))
            {
                LogDefaultCaptureSkipped(key, "scriptable object has already been tuned by Alpine");
                return;
            }

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

        private static bool Differs(float a, float b, float epsilon)
        {
            return Mathf.Abs(a - b) > epsilon;
        }

        private static bool Differs(Vector3 a, Vector3 b, float epsilon)
        {
            return (a - b).sqrMagnitude > epsilon * epsilon;
        }

        private bool TryApplyActiveProfileForCurrentSled()
        {
            if (ActiveSO == null)
                return false;

            var profile =
                Store.GetCurrentSetupForSled(GetSledKey(ActiveSO), GetVehicleId(ActiveSO)) ??
                Store.GetActiveProfileForSled(GetSledKey(ActiveSO), GetVehicleId(ActiveSO));
            if (profile == null)
                return false;

            MelonLogger.Msg($"Equipping preserved Alpine setup '{profile.name}' for {ActiveSO.name}.");
            var activeClone = TuneStore.Clone(profile);
            if (ApplyProfile(activeClone, ActiveSO, false, false))
            {
                NotifyActiveTuneChanged(activeClone, ActiveSO);
                return true;
            }

            return false;
        }

        private TuneComputation ComputeProfile(TuneProfile profile, VehicleScriptableObject sled)
        {
            EnsureDefaultsForSled(sled);

            string sledKey = GetSledKey(sled);
            var baseDefaults = Store.GetDefaults(sledKey);
            if (baseDefaults == null)
            {
                if (IsSledModifiedByAlpine(sled))
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
                AlpineTuneMath.MergeEffect(effect, part.effect);
            }

            var fine = profile.fineTune ?? new FineTuneSettings();
            profile.fineTune = fine;
            var simulationInput = BuildEngineSimulationInput(sled);
            EngineSimulationResult simulationResult;
            var resolvedStats = AlpineTuneMath.ComputeStats(
                baseDefaults,
                engineDefaults,
                parts,
                effect,
                fine,
                simulationInput,
                out simulationResult);

            return new TuneComputation
            {
                baseDefaults = baseDefaults,
                engineDefaults = engineDefaults,
                audioDefaults = audioDefaults,
                audioSource = audioSource,
                parts = parts,
                mergedEffect = effect,
                simulationInput = simulationInput,
                simulationResult = simulationResult,
                requiresReload = requiresReload,
                stats = resolvedStats
            };
        }

        private EngineSimulationInput BuildEngineSimulationInput(VehicleScriptableObject sled)
        {
            var input = new EngineSimulationInput
            {
                altitudeCompensationEnabled = UseWorldYAsAltitudeMeters,
                hasThrottle01 = false,
                throttle01 = 0f,
                hasNormalizedRpm = false,
                normalizedRpm = 0f,
                hasLoad01 = false,
                load01 = 0f
            };

            if (!UseWorldYAsAltitudeMeters || sled == null || sled != ActiveSO || ActiveController == null)
                return input;

            Transform transform = ActiveController.transform;
            if (transform != null)
            {
                input.hasAltitudeMeters = true;
                input.altitudeMeters = transform.position.y;
            }

            Rigidbody body = ActiveController.GetComponent<Rigidbody>();
            if (body != null)
            {
                input.hasSpeedMetersPerSecond = true;
                input.speedMetersPerSecond = body.linearVelocity.magnitude;
            }

            return input;
        }

        private static void LogAppliedTune(TuneProfile profile, VehicleScriptableObject sled, TuneComputation computation)
        {
            if (computation == null || computation.stats == null)
                return;

            MelonLogger.Msg(
                $"Installed Alpine setup '{profile.name}' to {sled.name}: " +
                $"HP={computation.stats.horsePower:F1}, PF={computation.stats.powerFactor:F2}, " +
                $"Paddle={TrackSpecResolver.FormatPaddleHeight(computation.stats.lugHeight)}, " +
                $"Fric={computation.stats.friction:F2}");

            var simulation = computation.simulationResult;
            var gains = simulation != null ? simulation.gains : null;
            if (simulation == null || gains == null || computation.engineDefaults == null)
                return;

            MelonLogger.Msg(
                "Alpine power resolve: " +
                $"baseHP={computation.engineDefaults.horsePower:F1}, " +
                $"engineGain={FormatGain(gains.engineHorsepowerGain)}, " +
                $"turboGain={FormatGain(gains.turboHorsepowerGain)}, " +
                $"intakeGain={FormatGain(gains.intakeHorsepowerGain)}, " +
                $"fineGain={FormatGain(gains.fineTuneHorsepowerGain)}, " +
                $"beforeEnvHP={simulation.horsepowerBeforeEnvironment:F1}, " +
                $"altitude={simulation.altitudeMeters:F0}m, " +
                $"pressure={simulation.altitudePressureRatio:F3}, " +
                $"turboComp={simulation.turboAltitudeCompensation:F2}, " +
                $"air={simulation.effectiveAirRatio:F3}, " +
                $"finalHP={simulation.horsepowerAfterEnvironment:F1}, " +
                $"pfGain={FormatGain(gains.TotalPowerFactorGain)}, " +
                $"beforeEnvPF={simulation.powerFactorBeforeEnvironment:F2}, " +
                $"finalPF={simulation.powerFactorAfterEnvironment:F2}, " +
                $"boostLimit={simulation.boostLimitPsi:F1}psi, " +
                $"estBoost={simulation.estimatedBoostPsi:F1}psi, " +
                $"estManifold={simulation.estimatedManifoldPressureKpa:F0}kPa");
        }

        private static string FormatGain(float gain)
        {
            return (gain * 100f).ToString("+0.0;-0.0;0.0") + "%";
        }

        private static void MergeEffect(PartEffect target, PartEffect source)
        {
            AlpineTuneMath.MergeEffect(target, source);
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

            if (HasEngineAudioToken(defaults) &&
                !SleddersGameBindings.TryApplyEngineAudioTokenToVehicle(
                    sled,
                    defaults.engineAudioEnumType,
                    defaults.engineAudioEnumName,
                    defaults.engineAudioEnumRawValue,
                    out var audioReason))
            {
                MelonLogger.Warning($"Could not restore stock engine audio on sled data: {audioReason}");
            }

            if (defaults.hasSnowmobileStats && sled.snowmobileStats != null)
            {
                sled.snowmobileStats.power = defaults.statsPower;
                sled.snowmobileStats.climbing = defaults.statsClimbing;
                sled.snowmobileStats.agility = defaults.statsAgility;
            }

            // Do not restore cosmetic/accessory flags here. Alpine tuning owns the
            // performance fields above; vanilla cosmetics and removed parts should
            // remain exactly as the player left them unless an explicit Alpine
            // equipment option is selected.
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
            float boostResponse = Mathf.Clamp(effect.boostResponseMultiplier, 0.70f, 1.35f);

            if (defaults.hasThrottleExponent)
                SetFloatField(ActiveController, "throttleExponent", ClampOffset(defaults.throttleExponent + effect.throttleExponentDelta, defaults.throttleExponent, 0.20f, 0.25f, 4f));

            if (defaults.hasRpmSensitivity)
                SetFloatField(ActiveController, "rpmSensitivity", ClampRelative(defaults.rpmSensitivity * effect.rpmSensitivityMultiplier * boostResponse, defaults.rpmSensitivity, 0.50f, 1.70f, 0.05f, 10f));

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

        internal void BeginHeadlightKeyboardBind()
        {
            _waitingForHeadlightKeyboardBinding = true;
            _waitingForHeadlightControllerBinding = false;
            _headlightBindingCaptureDeadline = Time.unscaledTime + 8f;
        }

        internal void BeginHeadlightControllerBind()
        {
            _waitingForHeadlightControllerBinding = true;
            _waitingForHeadlightKeyboardBinding = false;
            _headlightBindingCaptureDeadline = Time.unscaledTime + 8f;
        }

        internal void ClearHeadlightBinding()
        {
            Settings.headlightToggleEnabled = false;
            Settings.headlightKeyboardKey = null;
            Settings.headlightControllerButton = null;
            Settings.headlightBindingConfigured = false;
            SaveSettings();
        }

        private void UpdateHeadlightInputBinding()
        {
            var settings = Settings;

            if (_waitingForHeadlightKeyboardBinding || _waitingForHeadlightControllerBinding)
            {
                if (Time.unscaledTime > _headlightBindingCaptureDeadline)
                {
                    _waitingForHeadlightKeyboardBinding = false;
                    _waitingForHeadlightControllerBinding = false;
                    return;
                }

                KeyCode captured;
                if (TryCaptureBindingKey(_waitingForHeadlightControllerBinding, out captured))
                {
                    if (_waitingForHeadlightControllerBinding)
                        settings.headlightControllerButton = captured.ToString();
                    else
                        settings.headlightKeyboardKey = captured.ToString();

                    settings.headlightBindingConfigured = true;
                    settings.headlightToggleEnabled = true;
                    settings.Normalize();
                    SaveSettings();
                    _waitingForHeadlightKeyboardBinding = false;
                    _waitingForHeadlightControllerBinding = false;
                }

                return;
            }

            if (!settings.headlightToggleEnabled || Time.unscaledTime < _nextHeadlightToggleTime)
                return;

            if (!BindingPressed(settings.headlightKeyboardKey) &&
                !BindingPressed(settings.headlightControllerButton))
            {
                return;
            }

            _nextHeadlightToggleTime = Time.unscaledTime + 0.25f;
            string status;
            bool toggled = ToggleHeadlightsForActiveSled(out status);
            if (toggled || !_lastHeadlightToggleHadTarget)
            {
                _lastHeadlightToggleHadTarget = toggled;
                MelonLogger.Msg(status);
            }
        }

        private static bool BindingPressed(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
                return false;

            KeyCode code;
            return Enum.TryParse(keyName, true, out code) && Input.GetKeyDown(code);
        }

        private static bool TryCaptureBindingKey(bool controllerOnly, out KeyCode captured)
        {
            captured = KeyCode.None;

            foreach (KeyCode code in AllKeyCodes)
            {
                if (code == KeyCode.None || !Input.GetKeyDown(code))
                    continue;

                bool controller = code.ToString().StartsWith("Joystick", StringComparison.OrdinalIgnoreCase);
                bool mouse = code.ToString().StartsWith("Mouse", StringComparison.OrdinalIgnoreCase);
                if (controllerOnly != controller)
                    continue;
                if (!controllerOnly && mouse)
                    continue;

                captured = code;
                return true;
            }

            return false;
        }

        internal bool ToggleHeadlightsForActiveSled(out string status)
        {
            status = null;

            if (ActiveSO == null)
            {
                status = "Headlight toggle skipped: no current sled.";
                return false;
            }

            bool current = AreActiveHeadlightsOn();
            bool desired = !current;

            var profile =
                Store.GetCurrentSetupForSled(GetSledKey(ActiveSO), GetVehicleId(ActiveSO)) ??
                CreateWorkingProfile(ActiveSO);

            if (profile == null)
            {
                status = "Headlight toggle skipped: no current setup.";
                return false;
            }

            profile.headlightEnabled = desired;
            string applyStatus;
            UpdateCurrentSetup(profile, ActiveSO, out applyStatus);
            SetActiveHeadlightsEnabled(desired);
            status = desired ? "Headlights on." : "Headlights off.";
            return true;
        }

        internal bool SetSetupHeadlightEnabled(TuneProfile profile, VehicleScriptableObject sled, bool enabled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to edit headlights.";
                return false;
            }

            profile.headlightEnabled = enabled;
            bool updated = UpdateCurrentSetup(profile, sled, out status);
            if (sled == ActiveSO)
                SetActiveHeadlightsEnabled(enabled);

            return updated;
        }

        internal bool ClearSetupHeadlightOverride(TuneProfile profile, VehicleScriptableObject sled, out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled to edit headlights.";
                return false;
            }

            profile.headlightEnabled = null;
            bool updated = UpdateCurrentSetup(profile, sled, out status);
            if (updated)
                status = "Headlights follow game time.";

            return updated;
        }

        internal bool AreActiveHeadlightsOn()
        {
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights == null || lights.Length == 0)
                return false;

            foreach (var light in lights)
            {
                if (light != null && light.enabled)
                    return true;
            }

            return false;
        }

        private void SetActiveHeadlightsEnabled(bool enabled)
        {
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights == null)
                return;

            foreach (var light in lights)
            {
                if (light != null)
                    light.enabled = enabled;
            }
        }

        private void EnforceHeadlightOverride()
        {
            if (ActiveSO == null || !_activeHeadlightOverride.HasValue)
                return;

            SetActiveHeadlightsEnabled(_activeHeadlightOverride.Value);
        }

        private void CaptureHeadlightDefaultsForActiveController(bool replace)
        {
            if (ActiveController == null)
            {
                _activeHeadlightDefaults = null;
                _headlightDefaultsControllerId = int.MinValue;
                return;
            }

            int controllerId = ActiveController.GetInstanceID();
            if (!replace && _activeHeadlightDefaults != null && _headlightDefaultsControllerId == controllerId)
                return;

            var captured = new RuntimeHeadlightDefaults();
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light == null)
                        continue;

                    captured.lights.Add(new RuntimeHeadlightDefault
                    {
                        light = light,
                        color = light.color,
                        intensity = light.intensity,
                        range = light.range,
                        spotAngle = light.spotAngle,
                        localRotation = light.transform.localRotation,
                        enabled = light.enabled
                    });
                }
            }

            _activeHeadlightDefaults = captured;
            _headlightDefaultsControllerId = controllerId;
        }

        private void ApplyHeadlightDefaults()
        {
            CaptureHeadlightDefaultsForActiveController(false);
            _activeHeadlightOverride = null;
            if (_activeHeadlightDefaults == null)
                return;

            foreach (var defaults in _activeHeadlightDefaults.lights)
            {
                if (defaults == null || defaults.light == null)
                    continue;

                defaults.light.color = defaults.color;
                defaults.light.intensity = defaults.intensity;
                defaults.light.range = defaults.range;
                defaults.light.spotAngle = defaults.spotAngle;
                defaults.light.transform.localRotation = defaults.localRotation;
                defaults.light.enabled = defaults.enabled;
            }
        }

        private void ApplyHeadlightRuntime(PartEffect effect, TuneProfile profile)
        {
            if (effect == null)
                return;

            _activeHeadlightOverride = profile != null ? profile.headlightEnabled : null;
            CaptureHeadlightDefaultsForActiveController(false);
            if (_activeHeadlightDefaults == null)
                return;

            float pitch = Mathf.Clamp(effect.headlightPitchOffsetDegrees, -5f, 5f);
            foreach (var defaults in _activeHeadlightDefaults.lights)
            {
                if (defaults == null || defaults.light == null)
                    continue;

                defaults.light.color = effect.hasHeadlightColor ? effect.headlightColor : defaults.color;
                defaults.light.intensity = Mathf.Clamp(
                    defaults.intensity * effect.headlightIntensityMultiplier,
                    0f,
                    Mathf.Max(defaults.intensity * 2.5f, defaults.intensity + 0.01f));
                defaults.light.range = Mathf.Clamp(
                    defaults.range * effect.headlightRangeMultiplier,
                    0f,
                    Mathf.Max(defaults.range * 2.0f, defaults.range + 0.01f));
                defaults.light.spotAngle = Mathf.Clamp(
                    defaults.spotAngle * effect.headlightSpotAngleMultiplier,
                    10f,
                    160f);
                defaults.light.transform.localRotation = defaults.localRotation * Quaternion.Euler(pitch, 0f, 0f);

                if (profile != null && profile.headlightEnabled.HasValue)
                    defaults.light.enabled = profile.headlightEnabled.Value;
            }
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
            return SleddersGameBindings.GetStabilizer(controller);
        }

        private void ApplyAccessoryMode(string accessoryMode, SledDefaults defaults)
        {
            if (ActiveController == null)
                return;

            if (string.IsNullOrWhiteSpace(accessoryMode) ||
                string.Equals(accessoryMode, "stock", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var components = SleddersGameBindings.GetSnowmobileAccessories(ActiveController);
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

                // Unknown accessory modes are ignored so vanilla customization is preserved.
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Accessory mode apply skipped: {ex.Message}");
            }
        }

        private static void SetGameObjectListActive(object owner, string fieldName, bool active)
        {
            SleddersGameBindings.SetGameObjectListActive(owner, fieldName, active);
        }

        private static VehicleScriptableObject TryGetVehicleSelectionSled(object menu)
        {
            return SleddersGameBindings.TryGetVehicleSelectionSled(menu);
        }

        private static MethodBase GetGarageSelectionMethod(string methodName)
        {
            try
            {
                return AccessTools.Method(typeof(VehicleSelectionUiController), methodName);
            }
            catch
            {
                return null;
            }
        }

        private static VehicleScriptableObject FirstVehicleScriptableObjectArg(object[] args)
        {
            if (args == null)
                return null;

            foreach (object arg in args)
            {
                if (arg is VehicleScriptableObject sled)
                    return sled;
            }

            return null;
        }

        private static void NotifyGarageSelectionFromArgs(
            VehicleSelectionUiController controller,
            object[] args,
            string source)
        {
            VehicleScriptableObject sled = FirstVehicleScriptableObjectArg(args);
            if (sled == null)
                return;

            Instance?.NoteGarageSledSelectionChanged(controller, sled, source);
        }

        private static void NotifyGarageSelectionFromController(
            VehicleSelectionUiController controller,
            string source)
        {
            if (controller == null)
                return;

            string resolvedSource;
            VehicleScriptableObject sled = SleddersGameBindings.TryGetVehicleSelectionSled(controller, out resolvedSource);
            if (sled == null)
                return;

            Instance?.NoteGarageSledSelectionChanged(
                controller,
                sled,
                string.IsNullOrWhiteSpace(resolvedSource) ? source : resolvedSource);
        }

        private static VehicleScriptableObject GetVehicleFromController(SnowmobileController controller)
        {
            return SleddersGameBindings.GetVehicleFromController(controller);
        }

        private void OnLocalSledInitialized(SnowmobileController controller, Vector3 spawnPos, Quaternion spawnRot)
        {
            _activeHeadlightOverride = null;
            ActiveController = controller;
            ActiveSO = GetVehicleFromController(controller);
            ActiveRespawn = controller != null ? controller.GetComponent<Respawnable>() : null;
            ActiveSpawnPos = spawnPos;
            ActiveSpawnRot = spawnRot;
            CaptureHeadlightDefaultsForActiveController(true);

            if (ActiveSO == null)
            {
                MelonLogger.Warning("LocalInit detected no VehicleScriptableObject.");
                return;
            }

            TryBuildDefaults();
            RefreshStatDefaultsFromCleanLoad(ActiveSO);
            EnsureDefaultsForSled(ActiveSO);
            bool appliedActive = TryApplyActiveProfileForCurrentSled();
            if (!appliedActive)
                NotifyActiveTuneCleared(ActiveSO);
            MelonLogger.Msg($"Detected local sled '{ActiveSO.name}' for Alpine Tuning 2.0.");
        }

        private static T GetFieldValue<T>(object target, string fieldName)
        {
            return SleddersGameBindings.GetFieldValue<T>(target, fieldName);
        }

        private static bool TryGetFieldValue<T>(object target, string fieldName, out T value)
        {
            return SleddersGameBindings.TryGetFieldValue(target, fieldName, out value);
        }

        private static void SetFloatField(object target, string fieldName, float value)
        {
            SleddersGameBindings.SetFloatField(target, fieldName, value);
        }

        private static void SetFieldValue(object target, string fieldName, object value)
        {
            SleddersGameBindings.SetFieldValue(target, fieldName, value);
        }

        private static bool HasEngineAudioToken(SledDefaults defaults)
        {
            return defaults != null &&
                   !string.IsNullOrWhiteSpace(defaults.engineAudioEnumType) &&
                   (!string.IsNullOrWhiteSpace(defaults.engineAudioEnumName) ||
                    defaults.engineAudioEnumRawValue != 0);
        }

        private static bool ResolveEngineAudioReflection()
        {
            return SleddersGameBindings.EngineAudioAvailable;
        }

        private static Component FindActiveEngineAudioController()
        {
            return SleddersGameBindings.FindEngineAudioController(ActiveController);
        }

        private static bool TryReadActiveEngineAudioToken(out string enumTypeName, out string enumName, out int enumRawValue)
        {
            return SleddersGameBindings.TryReadActiveEngineAudioToken(
                ActiveController,
                out enumTypeName,
                out enumName,
                out enumRawValue);
        }

        private static bool TryReadEngineAudioTokenFromVehicleSO(
            VehicleScriptableObject sled,
            out string enumTypeName,
            out string enumName,
            out int enumRawValue)
        {
            return SleddersGameBindings.TryReadEngineAudioTokenFromVehicle(
                sled,
                out enumTypeName,
                out enumName,
                out enumRawValue);
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

            if (allowLiveCapture && sourceSO == ActiveSO && IsSledModifiedByAlpine(sourceSO))
            {
                LogDefaultCaptureSkipped(GetSledKey(sourceSO), "engine audio live capture would read a tuned sled");
                return false;
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

            if (IsSledModifiedByAlpine(ActiveSO))
            {
                LogDefaultCaptureSkipped(GetSledKey(ActiveSO), "engine audio live capture would read a tuned sled");
                return;
            }

            var defaults = Store.GetDefaults(GetSledKey(ActiveSO));
            if (defaults == null || HasEngineAudioToken(defaults))
                return;

            if (TryPopulateDefaultAudioToken(defaults, ActiveSO, true))
                Store.PutDefaults(defaults);
        }

        private void ApplyEngineAudioToSled(
            VehicleScriptableObject sled,
            SledDefaults audioDefaults,
            VehicleScriptableObject audioSourceSO)
        {
            if (sled == null || audioDefaults == null)
                return;

            if (!HasEngineAudioToken(audioDefaults))
                TryPopulateDefaultAudioToken(audioDefaults, audioSourceSO, false);

            if (!HasEngineAudioToken(audioDefaults))
                return;

            if (!SleddersGameBindings.TryApplyEngineAudioTokenToVehicle(
                    sled,
                    audioDefaults.engineAudioEnumType,
                    audioDefaults.engineAudioEnumName,
                    audioDefaults.engineAudioEnumRawValue,
                    out var reason))
            {
                MelonLogger.Warning($"Could not apply engine audio to sled data: {reason}");
            }
        }

        private void QueueEngineAudioSwap(SledDefaults audioDefaults, VehicleScriptableObject audioSourceSO)
        {
            if (!HasEngineAudioToken(audioDefaults))
                TryPopulateDefaultAudioToken(audioDefaults, audioSourceSO, false);

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
                _pendingEngineAudioAttemptsRemaining--;
                if (_pendingEngineAudioAttemptsRemaining <= 0)
                {
                    _pendingEngineAudioApply = false;
                    return;
                }

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
                if (!SleddersGameBindings.TryApplyEngineAudioToken(
                        audioController,
                        _pendingEngineAudioEnumType,
                        _pendingEngineAudioEnumName,
                        _pendingEngineAudioEnumRawValue,
                        out var reason))
                {
                    throw new InvalidOperationException(reason);
                }

                if (!_pendingEngineAudioLoggedReady)
                {
                    MelonLogger.Msg($"Engine audio target applied: {_pendingEngineAudioEnumName} ({_pendingEngineAudioEnumRawValue}).");
                    _pendingEngineAudioLoggedReady = true;
                }

                _pendingEngineAudioApply = false;
            }
            catch (Exception ex)
            {
                _pendingEngineAudioApply = false;
                MelonLogger.Warning($"Engine audio swap failed: {ex.Message}");
            }
        }

        [HarmonyPatch]
        public static class PatchGarageSelectionItem
        {
            private const string MethodName = "JLLLALEALEK";

            public static bool Prepare()
            {
                return GetGarageSelectionMethod(MethodName) != null;
            }

            public static MethodBase TargetMethod()
            {
                return GetGarageSelectionMethod(MethodName);
            }

            public static void Postfix(VehicleSelectionUiController __instance, object[] __args)
            {
                try
                {
                    NotifyGarageSelectionFromArgs(__instance, __args, "garage selection");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage selection hook skipped: {ex.Message}");
                }
            }
        }

        [HarmonyPatch]
        public static class PatchGaragePreviewStats
        {
            private const string MethodName = "EAANABMPMLK";

            public static bool Prepare()
            {
                return GetGarageSelectionMethod(MethodName) != null;
            }

            public static MethodBase TargetMethod()
            {
                return GetGarageSelectionMethod(MethodName);
            }

            public static void Postfix(VehicleSelectionUiController __instance, object[] __args)
            {
                try
                {
                    NotifyGarageSelectionFromArgs(__instance, __args, "garage preview");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage preview hook skipped: {ex.Message}");
                }
            }
        }

        [HarmonyPatch]
        public static class PatchGarageDisplayStats
        {
            private const string MethodName = "DADIAJNKOCI";

            public static bool Prepare()
            {
                return GetGarageSelectionMethod(MethodName) != null;
            }

            public static MethodBase TargetMethod()
            {
                return GetGarageSelectionMethod(MethodName);
            }

            public static void Postfix(VehicleSelectionUiController __instance, object[] __args)
            {
                try
                {
                    NotifyGarageSelectionFromArgs(__instance, __args, "garage display");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage display hook skipped: {ex.Message}");
                }
            }
        }

        [HarmonyPatch]
        public static class PatchGarageCustomizationRebuild
        {
            private const string MethodName = "RebuildCustomizationView";

            public static bool Prepare()
            {
                return GetGarageSelectionMethod(MethodName) != null;
            }

            public static MethodBase TargetMethod()
            {
                return GetGarageSelectionMethod(MethodName);
            }

            public static void Postfix(VehicleSelectionUiController __instance)
            {
                try
                {
                    NotifyGarageSelectionFromController(__instance, "garage customization");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage customization hook skipped: {ex.Message}");
                }
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
