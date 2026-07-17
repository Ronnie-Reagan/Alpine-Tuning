using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: MelonInfo(typeof(AlpineTuning.AlpineTuningMod), "Alpine Tuning", AlpineTuning.AlpineConstants.ModVersion, "Alpine Tuning")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace AlpineTuning
{
    internal enum HeadlightBindingCaptureResult
    {
        None,
        Saved,
        Cancelled,
        TimedOut,
        SaveFailed
    }

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
        // World-space Y is a map coordinate, not a trustworthy altitude source.
        // Native Sledders does not derate horsepower from transform.position.y.
        private const float CurrentSetupSaveDelaySeconds = 0.35f;
        private bool _defaultsBuilt;
        private float _nextNativeUiScanTime;
        private float _nextSelectableSledRefreshTime;
        private float _nextCurrentSetupFlushTime;
        private bool _shutdownComplete;

        private string _pendingEngineAudioEnumType;
        private string _pendingEngineAudioEnumName;
        private int _pendingEngineAudioEnumRawValue;
        private bool _pendingEngineAudioApply;
        private float _pendingEngineAudioDeadline;
        private float _pendingEngineAudioNextAttemptTime;
        private int _pendingEngineAudioAttemptsRemaining;
        private int _pendingEngineAudioLastControllerId = int.MinValue;
        private bool _pendingEngineAudioLoggedReady;
        private int _lastAppliedEngineAudioVehicleControllerId = int.MinValue;
        private string _lastAppliedEngineAudioEnumType;
        private string _lastAppliedEngineAudioEnumName;
        private int _lastAppliedEngineAudioEnumRawValue;
        private RuntimeHeadlightDefaults _activeHeadlightDefaults;
        private int _headlightDefaultsControllerId = int.MinValue;
        private RuntimeAccessoryDefaults _activeAccessoryDefaults;
        private int _accessoryDefaultsControllerId = int.MinValue;
        private RuntimeNativePhysicsDefaults _activeNativePhysicsDefaults;
        private int _nativePhysicsDefaultsControllerId = int.MinValue;
        private SpawnValueSignature _activeSpawnValues;
        private int _spawnValuesControllerId = int.MinValue;
        private bool? _activeHeadlightOverride;
        private bool _waitingForHeadlightKeyboardBinding;
        private bool _waitingForHeadlightControllerBinding;
        private float _headlightBindingCaptureDeadline;
        private HeadlightBindingCaptureResult _headlightBindingCaptureResult;
        private int _headlightBindingCancelFrame = -1;
        private float _nextHeadlightToggleTime;
        private bool _lastHeadlightToggleHadTarget;
        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        private sealed class RuntimeHeadlightDefaults
        {
            public readonly List<RuntimeHeadlightDefault> lights = new List<RuntimeHeadlightDefault>();
            public SnowmobileController controller;
            public bool nativeSwitchEnabled;
        }

        private sealed class RuntimeHeadlightDefault
        {
            public Light light;
            public HeadLight nativeHeadlight;
            public Color color;
            public float intensity;
            public float range;
            public float spotAngle;
            public Quaternion localRotation;
            public bool enabled;
            public bool hasTunedValues;
            public Color tunedColor;
            public float tunedIntensity;
            public float tunedRange;
            public float tunedSpotAngle;
            public Quaternion tunedLocalRotation;
        }

        // HeadLight.Refresh multiplies its fade curve by this captured native
        // intensity. Keep it synchronized with Alpine's staged brightness so the
        // native refresh path does not overwrite the selected output every frame.
        private static readonly FieldInfo NativeHeadlightBaseIntensityField =
            AccessTools.Field(typeof(HeadLight), "KDHFJKFPBBM");

        private sealed class RuntimeAccessoryDefaults
        {
            public readonly List<RuntimeAccessoryDefault> objects = new List<RuntimeAccessoryDefault>();
        }

        private sealed class RuntimeAccessoryDefault
        {
            public GameObject gameObject;
            public bool active;
        }

        private sealed class RuntimeNativePhysicsDefaults
        {
            public SnowmobileController controller;
            public readonly List<RuntimeNativePhysicsField> fields = new List<RuntimeNativePhysicsField>();
        }

        private sealed class RuntimeNativePhysicsField
        {
            public object target;
            public string fieldName;
            public double value;
            public NativePhysicsValueKind kind;
        }

        private sealed class SpawnValueSignature
        {
            public string stableIdentity;
            public float horsePower;
            public bool hasMaxRpm;
            public float maxRpm;
            public float lugHeight;
            public float friction;
            public float weight;
            public float skiStance;
            public float skisXDistanceOffset;
            public bool isTurboOn;
            public Vector3 centerOfMassOffset;
            public Vector3 driverCenterOfMassOffset;

            public static SpawnValueSignature FromSled(VehicleScriptableObject sled)
            {
                if (sled == null)
                    return null;

                return new SpawnValueSignature
                {
                    stableIdentity = SledIdentity.StableIdentityKey(sled),
                    horsePower = sled.horsePower,
                    hasMaxRpm = IsFinitePositive(sled.maxRpm),
                    maxRpm = sled.maxRpm,
                    lugHeight = sled.lugHeight,
                    friction = sled.coefficientOfFriction,
                    weight = sled.weight,
                    skiStance = sled.skiStance,
                    skisXDistanceOffset = sled.skisXDistanceOffset,
                    isTurboOn = sled.isTurboOn,
                    centerOfMassOffset = sled.centerOfMassOffset,
                    driverCenterOfMassOffset = sled.driverCenterOfMassOffset
                };
            }
        }

        internal enum NativePhysicsValueKind
        {
            PowerEfficiency,
            DrivetrainSpeed,
            TrackMass,
            AntiRollBar,
            TrackRigidityFront,
            TrackRigidityRear,
            FrontSpring,
            FrontDamper,
            FrontCompressionDamping,
            FrontReboundDamping,
            RearSpring,
            RearDamper,
            RearCompressionDamping,
            RearReboundDamping,
            BrakeForce,
            SkisMaxAngle,
            ToeAngle,
            LeftCamberFactor,
            RightCamberFactor,
            SkiGrip,
            TrackGrip
        }

        internal enum NativePhysicsSubsystem
        {
            Drivetrain,
            Brake,
            Suspension,
            Steering,
            SkiGrip,
            TrackGrip
        }

        private sealed class PendingCurrentSetupSave
        {
            public string sledKey;
            public string vehicleId;
            public float dueTime;
        }

        private sealed class GarageSelectionState
        {
            public VehicleSelectionUiController controller;
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
            if (!AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                RemoteReplication = new AlpineRemoteReplication();
                Sharing = new AlpinePeerSharing(this);
                Sharing.Initialize();
            }

            MelonLogger.Msg(
                $"Alpine Tuning {AlpineConstants.ModVersion} initialized. " +
                $"Schema={AlpineConstants.SchemaVersion}, Catalog={AlpineConstants.CatalogVersion}");
        }

        public override void OnUpdate()
        {
            PruneDestroyedActiveRuntime();

            if (Time.unscaledTime >= _nextNativeUiScanTime)
            {
                bool attached = AlpineNativeUi.TryAttachOpenMenus(this);
                _nextNativeUiScanTime = Time.unscaledTime + (attached || AlpineNativeUi.HasAttachedMenus ? 3f : 0.20f);
            }

            Sharing?.Update();
            FlushPendingCurrentSetups(false);
            UpdateHeadlightInputBinding();
            PrepareHeadlightOverride();

            if (ActiveSO == null)
                return;

            if (!_defaultsBuilt)
                TryBuildDefaults();

            TryCaptureEngineAudioForCurrentSled();
            TryApplyPendingEngineAudioSwap();
        }

        private void PruneDestroyedActiveRuntime()
        {
            if (ActiveController != null)
                return;

            bool hadRuntimeState = ActiveSO != null ||
                                   ActiveRespawn != null ||
                                   _activeSpawnValues != null ||
                                   _activeHeadlightDefaults != null ||
                                   _activeAccessoryDefaults != null ||
                                   _activeNativePhysicsDefaults != null;
            if (!hadRuntimeState)
                return;

            MelonLogger.Msg("Cleared Alpine's stale local-sled runtime state after world teardown.");
            ActiveSO = null;
            ActiveController = null;
            ActiveRespawn = null;
            ActiveSpawnPos = default;
            ActiveSpawnRot = default;
            _activeSpawnValues = null;
            _spawnValuesControllerId = int.MinValue;
            _activeHeadlightDefaults = null;
            _headlightDefaultsControllerId = int.MinValue;
            _activeAccessoryDefaults = null;
            _accessoryDefaultsControllerId = int.MinValue;
            _activeNativePhysicsDefaults = null;
            _nativePhysicsDefaultsControllerId = int.MinValue;
            _activeHeadlightOverride = null;
            _pendingEngineAudioApply = false;
            _pendingEngineAudioEnumType = null;
            _pendingEngineAudioEnumName = null;
            _pendingEngineAudioEnumRawValue = 0;
            _pendingEngineAudioAttemptsRemaining = 0;
            _pendingEngineAudioLastControllerId = int.MinValue;
            _pendingEngineAudioLoggedReady = false;
            _lastAppliedEngineAudioVehicleControllerId = int.MinValue;
            _lastAppliedEngineAudioEnumType = null;
            _lastAppliedEngineAudioEnumName = null;
            _lastAppliedEngineAudioEnumRawValue = 0;
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
                AlpineNativeUi.DetachGarageSessions();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine shutdown UI restoration skipped: {ex.GetType().Name}");
            }

            try
            {
                FlushPendingCurrentSetups(true);
                Sharing?.Shutdown();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine shutdown sharing cleanup skipped: {ex.GetType().Name}");
            }

            try
            {
                RestoreAlpineMutationsBeforeShutdown();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine shutdown state restoration skipped: {ex.GetType().Name}");
            }

            _pendingEngineAudioApply = false;
            _pendingEngineAudioEnumType = null;
            _pendingEngineAudioEnumName = null;
            _pendingEngineAudioEnumRawValue = 0;
            _pendingEngineAudioAttemptsRemaining = 0;
            _pendingEngineAudioLastControllerId = int.MinValue;
            _pendingEngineAudioLoggedReady = false;
            _lastAppliedEngineAudioVehicleControllerId = int.MinValue;
            _lastAppliedEngineAudioEnumType = null;
            _lastAppliedEngineAudioEnumName = null;
            _lastAppliedEngineAudioEnumRawValue = 0;
            _activeHeadlightDefaults = null;
            _headlightDefaultsControllerId = int.MinValue;
            _activeAccessoryDefaults = null;
            _accessoryDefaultsControllerId = int.MinValue;
            _activeNativePhysicsDefaults = null;
            _nativePhysicsDefaultsControllerId = int.MinValue;
            _activeSpawnValues = null;
            _spawnValuesControllerId = int.MinValue;
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

        private void RestoreAlpineMutationsBeforeShutdown()
        {
            if (Store == null)
                return;

            var candidates = new List<VehicleScriptableObject>(_selectableSleds);
            if (ActiveSO != null && !candidates.Any(sled => ReferenceEquals(sled, ActiveSO)))
                candidates.Add(ActiveSO);

            var restoredInstances = new HashSet<int>();
            foreach (VehicleScriptableObject sled in candidates)
            {
                if (sled == null ||
                    !restoredInstances.Add(sled.GetInstanceID()) ||
                    !IsSledModifiedByAlpine(sled))
                {
                    continue;
                }

                SledDefaults defaults = Store.GetDefaults(GetSledKey(sled), GetVehicleId(sled));
                if (defaults != null)
                    ApplyDefaultsToSled(sled, defaults);
            }

            if (ActiveController != null && ActiveSO != null)
            {
                RestoreNativePhysicsDefaults();
                ApplyHeadlightDefaults();
                RestoreAccessoryDefaults();

                SledDefaults defaults = Store.GetDefaults(GetSledKey(ActiveSO), GetVehicleId(ActiveSO));
                if (defaults != null)
                {
                    ApplyRuntimeDefaults(defaults);

                    Component audioController = FindActiveEngineAudioController();
                    if (audioController != null && HasEngineAudioToken(defaults))
                    {
                        SleddersGameBindings.TryApplyEngineAudioToken(
                            audioController,
                            defaults.engineAudioEnumType,
                            defaults.engineAudioEnumName,
                            defaults.engineAudioEnumRawValue,
                            out _);
                    }
                }

                // LocalInit copies VSO horsepower, track contact, body mass/COM,
                // and ski offsets into spawned native components. Rebuild once
                // from the now-restored stock asset so hot-unloading Alpine cannot
                // leave those copied values tuned in the current ride.
                if (!SleddersGameBindings.TryReCreateSnowmobile(
                        ActiveController,
                        out var recreateReason))
                {
                    MelonLogger.Warning(
                        $"Stock sled recreation during Alpine shutdown was unavailable: {recreateReason}");
                }
            }

            _sledsModifiedByAlpineThisSession.Clear();
        }

        internal string LocalAuthorName
        {
            get { return AlpineConstants.DefaultProfileAuthor; }
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

        internal bool IsCapturingHeadlightKeyboardBinding =>
            _waitingForHeadlightKeyboardBinding;

        internal bool IsCapturingHeadlightControllerBinding =>
            _waitingForHeadlightControllerBinding;

        internal bool WasHeadlightBindingCancelHandledThisFrame =>
            _headlightBindingCancelFrame == Time.frameCount;

        internal HeadlightBindingCaptureResult ConsumeHeadlightBindingCaptureResult()
        {
            HeadlightBindingCaptureResult result = _headlightBindingCaptureResult;
            _headlightBindingCaptureResult = HeadlightBindingCaptureResult.None;
            return result;
        }

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
                RefreshSelectableSledsIfDue(false);
                return _selectableSleds;
            }
        }

        public static string GetSledKey(VehicleScriptableObject sled)
        {
            if (sled == null)
                return "UNKNOWN";
            string source = !string.IsNullOrWhiteSpace(sled.name)
                ? sled.name
                : sled.displayName;
            return NormalizeSledKey(source);
        }

        internal static string NormalizeSledKey(string source)
        {
            string value = (source ?? string.Empty).Trim();
            if (value.Length == 0)
                return "UNKNOWN";

            var key = new System.Text.StringBuilder(value.Length);
            bool replacedUnsafeCharacter = false;
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '_' ||
                    character == '-' || character == '.')
                {
                    key.Append(character);
                }
                else if (char.IsWhiteSpace(character))
                {
                    // Preserve the historical ordinary-name mapping.
                    key.Append('_');
                    replacedUnsafeCharacter |= character != ' ';
                }
                else
                {
                    key.Append('_');
                    replacedUnsafeCharacter = true;
                }
            }

            string normalized = key.Length > 0 ? key.ToString() : "UNKNOWN";
            if (!replacedUnsafeCharacter && normalized.Length <= 96)
                return normalized;

            uint hash = 2166136261u;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            string suffix = "_" + hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            int prefixLength = Math.Max(1, 96 - suffix.Length);
            if (normalized.Length > prefixLength)
                normalized = normalized.Substring(0, prefixLength);
            return normalized + suffix;
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
                    CacheGarageSelection(garage, sled, source);
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

            // Never substitute the previously ridden sled for an unresolved garage
            // selection. In that context a disabled tuner is safer than editing the
            // wrong vehicle. Pause/runtime callers may still use ActiveSO.
            if (sled == null && ActiveSO != null && !(menuContext is VehicleSelectionUiController))
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
            if (computation == null || computation.stats == null)
            {
                profile.resolvedStats = null;
                profile.requiresReload = false;
                return null;
            }
            profile.resolvedStats = computation.stats;
            profile.requiresReload = computation.requiresReload;
            profile.targetSledKey = GetSledKey(sled);
            profile.targetVehicleId = GetVehicleId(sled);
            return computation.stats;
        }

        internal void PreviewProfilesWithSharedEnvironment(
            VehicleScriptableObject sled,
            params TuneProfile[] profiles)
        {
            if (sled == null || profiles == null || profiles.Length == 0)
                return;

            // Sledders exposes no atmospheric correction input in its propulsion
            // model. Resolve every reference through the same configured preview
            // path so factory/current/candidate remain directly comparable.
            foreach (TuneProfile profile in profiles)
            {
                if (profile != null)
                    PreviewProfile(profile, sled);
            }
        }

        internal bool ApplyProfile(TuneProfile profile, VehicleScriptableObject sled, bool persist, bool reloadIfNeeded)
        {
            string ignored;
            return ApplyProfile(profile, sled, persist, reloadIfNeeded, out ignored);
        }

        internal bool ApplyProfile(TuneProfile profile, VehicleScriptableObject sled, bool persist, bool reloadIfNeeded, out string status)
        {
            return ApplyProfile(profile, sled, persist, reloadIfNeeded, out status, true);
        }

        private bool ApplyProfile(
            TuneProfile profile,
            VehicleScriptableObject sled,
            bool persist,
            bool reloadIfNeeded,
            out string status,
            bool notifyActive)
        {
            status = null;
            bool persisted = false;
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
                if (computation == null || computation.stats == null)
                {
                    status = computation?.unavailableReason ?? "Setup comparison is unavailable.";
                    return false;
                }

                // Always return to the captured stock baseline before applying a profile.
                // This makes part application idempotent: changing from one Alpine build to
                // another never multiplies against values left behind by the previous build.
                if (!IsSledModifiedByAlpine(sled) &&
                    !HasEngineAudioToken(computation.baseDefaults) &&
                    TryPopulateDefaultAudioToken(computation.baseDefaults, sled, false))
                {
                    Store.PutDefaults(computation.baseDefaults);
                }

                profile.resolvedStats = computation.stats;
                profile.requiresReload = computation.requiresReload;
                profile.targetSledKey = GetSledKey(sled);
                profile.targetVehicleId = GetVehicleId(sled);

                // Persistence is the irreversible boundary. Do it before
                // mutating the live asset/runtime so a failed write cannot leave
                // an installed setup that the next launch cannot recover.
                if (persist && !Store.SaveProfile(profile, true))
                {
                    TuneProfile written = Store.GetProfile(profile.profileId);
                    bool profileWasWritten = written != null &&
                        string.Equals(written.checksum, profile.checksum, StringComparison.OrdinalIgnoreCase);
                    status = profileWasWritten
                        ? "Setup saved, but default selection failed."
                        : "Setup save failed.";
                    MelonLogger.Warning(status);
                    return false;
                }
                persisted = persist;

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

                if (sled == ActiveSO)
                    QueueEngineAudioSwap(computation.audioDefaults, computation.audioSource);

                if (persist && notifyActive)
                    NotifyActiveTuneChanged(profile, sled);

                if (reloadIfNeeded && computation.requiresReload && sled == ActiveSO)
                {
                    if (!ReloadSled(out string reloadStatus))
                    {
                        // A failed recreate leaves the original controller alive,
                        // but ReloadSled deliberately restored its captured stock
                        // objects before trying either rebuild path. Put the live
                        // tune-only fields back so the old graph is not left in a
                        // mixed stock/tuned state. Spawn-copied fields remain
                        // pending until a later successful rebuild.
                        if (ActiveController != null)
                        {
                            ApplyRuntimeController(computation, profile);
                            ApplyHeadlightRuntime(computation.mergedEffect, profile);
                            ApplyAccessoryMode(computation.mergedEffect.accessoryMode, computation.baseDefaults);
                        }
                        status = persist
                            ? "Setup saved; rebuild failed."
                            : "Rebuild failed.";
                        if (!string.IsNullOrWhiteSpace(reloadStatus))
                            MelonLogger.Warning(reloadStatus);
                        return false;
                    }
                    status = persist ? "Setup saved and ready." : "Setup ready.";
                }
                else
                    status = persist ? "Setup saved." : "Setup updated.";
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ApplyProfile failed: {ex.GetType().Name}");
                status = persisted
                    ? "Setup saved; install failed."
                    : "Setup update failed.";
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
                MelonLogger.Error($"SaveProfile failed: {ex.GetType().Name}");
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
                if (!QueueCurrentSetupSave(profile, sled, true))
                {
                    status = "Setup slot saved, but the current setup record could not be preserved.";
                    return false;
                }

                status = "Setup saved.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Setup save failed: {ex.GetType().Name}";
                MelonLogger.Error($"SaveCurrentSetupAsSlot failed: {ex.GetType().Name}");
                return false;
            }
        }

        internal bool SaveCurrentSetupAsNewSlot(
            TuneProfile profile,
            VehicleScriptableObject sled,
            out string status)
        {
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a sled first.";
                return false;
            }

            TuneProfile copy = TuneStore.Clone(profile);
            copy.sourceProfileId = !string.IsNullOrWhiteSpace(profile.setupSlotId)
                ? profile.setupSlotId
                : profile.profileId;
            copy.profileId = Guid.NewGuid().ToString("N");
            copy.createdUnixTime = 0;
            copy.updatedUnixTime = 0;
            copy.setupSlotId = null;
            copy.setupSlotName = null;
            copy.setupEdited = true;
            copy.isCurrentSetup = true;
            copy.checksum = null;
            if (!copy.usesAutomaticName && !string.IsNullOrWhiteSpace(copy.name))
                copy.name += " Copy";

            if (!SaveCurrentSetupAsSlot(copy, sled, out status))
                return false;

            profile.profileId = copy.profileId;
            profile.name = copy.name;
            profile.usesAutomaticName = copy.usesAutomaticName;
            profile.createdUnixTime = copy.createdUnixTime;
            profile.updatedUnixTime = copy.updatedUnixTime;
            profile.setupSlotId = copy.setupSlotId;
            profile.setupSlotName = copy.setupSlotName;
            profile.setupEdited = false;
            profile.isCurrentSetup = true;
            profile.checksum = copy.checksum;
            status = "Saved as new.";
            return true;
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

                var defaultProfile = TuneStore.Clone(profile);
                Catalog.EnsureProfileSelections(defaultProfile);
                PreviewProfile(defaultProfile, sled);
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
                profile.name = defaultProfile.name;
                profile.usesAutomaticName = defaultProfile.usesAutomaticName;
                profile.setupSlotId = defaultProfile.profileId;
                profile.setupSlotName = defaultProfile.name;
                profile.setupEdited = false;
                profile.isCurrentSetup = true;

                string applyStatus;
                if (!UpdateCurrentSetup(profile, sled, out applyStatus))
                {
                    status = string.IsNullOrWhiteSpace(applyStatus)
                        ? "Default saved; equip failed."
                        : "Default saved; " + applyStatus;
                    return false;
                }

                NotifyActiveTuneChanged(profile, sled);

                status = HasRuntimeInstanceForSled(sled)
                    ? "Default setup saved and active."
                    : "Default setup saved for next ride.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Default setup save failed: {ex.GetType().Name}";
                MelonLogger.Error($"SaveCurrentSetupAsDefault failed: {ex.GetType().Name}");
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
                profile.setupEdited = IsSetupEditedFromBaseline(profile, sled);
                PreviewProfile(profile, sled);

                bool hasRuntime = HasRuntimeInstanceForSled(sled);
                VehicleScriptableObject applyTarget = hasRuntime && ActiveSO != null ? ActiveSO : sled;
                // Commit the draft before touching the live sled. A failed
                // runtime mutation must never be able to discard the user's
                // latest part/slider/headlight choice.
                if (!QueueCurrentSetupSave(profile, sled, true))
                {
                    status = "Current Setup save failed.";
                    return false;
                }

                string applyStatus;
                if (!ApplyProfile(profile, applyTarget, false, false, out applyStatus, false))
                {
                    status = "Current Setup preserved; " +
                             (string.IsNullOrWhiteSpace(applyStatus)
                                 ? "install failed."
                                 : applyStatus);
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
                status = $"Setup update failed: {ex.GetType().Name}";
                MelonLogger.Warning($"UpdateCurrentSetup failed: {ex.GetType().Name}");
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
            status = $"Equipped {profile.name}.";
            return true;
        }

        internal bool SetDefaultSetup(
            TuneProfile profile,
            VehicleScriptableObject sled,
            out TuneProfile equipped,
            out string status)
        {
            equipped = null;
            status = null;
            if (profile == null || sled == null)
            {
                status = "Select a setup slot first.";
                return false;
            }

            bool saved = Store.SetActiveProfile(
                GetSledKey(sled),
                GetVehicleId(sled),
                profile.profileId);
            if (!saved)
            {
                status = "Default setup could not be selected.";
                return false;
            }

            if (!EquipSetupSlot(profile, sled, out equipped, out status))
            {
                status = string.IsNullOrWhiteSpace(status)
                    ? "Default selected; equip failed."
                    : "Default selected; " + status;
                return false;
            }

            NotifyActiveTuneChanged(equipped, sled);
            status = $"{profile.name} is now the default and equipped.";
            return true;
        }

        private bool IsSetupEditedFromBaseline(TuneProfile profile, VehicleScriptableObject sled)
        {
            if (profile == null || sled == null)
                return false;

            TuneProfile baseline = null;
            if (!string.IsNullOrWhiteSpace(profile.setupSlotId))
                baseline = Store.GetProfile(profile.setupSlotId);

            if (baseline == null)
            {
                baseline = Store.GetActiveProfileForSled(
                    GetSledKey(sled),
                    GetVehicleId(sled));
            }

            baseline = baseline != null
                ? TuneStore.Clone(baseline)
                : Catalog.CreateDefaultProfile(sled, LocalAuthorName);
            Catalog.EnsureProfileSelections(baseline);
            return !SetupContentMatches(profile, baseline);
        }

        private static bool SetupContentMatches(TuneProfile left, TuneProfile right)
        {
            if (left == null || right == null)
                return false;

            string leftDonor = SledIdentity.StableIdentityKey(
                left.donorSledKey,
                left.donorVehicleId);
            string rightDonor = SledIdentity.StableIdentityKey(
                right.donorSledKey,
                right.donorVehicleId);
            if (!string.Equals(leftDonor, rightDonor, StringComparison.OrdinalIgnoreCase) ||
                left.headlightEnabled != right.headlightEnabled)
            {
                return false;
            }

            foreach (string category in PartCatalog.OrderedCategories)
            {
                if (!string.Equals(
                        left.GetPartId(category),
                        right.GetPartId(category),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            FineTuneSettings a = left.fineTune ?? new FineTuneSettings();
            FineTuneSettings b = right.fineTune ?? new FineTuneSettings();
            const float epsilon = 0.00001f;
            return !Differs(a.powerTrimPercent, b.powerTrimPercent, epsilon) &&
                   !Differs(a.tractionTrimPercent, b.tractionTrimPercent, epsilon) &&
                   !Differs(a.weightTrimPercent, b.weightTrimPercent, epsilon) &&
                   !Differs(a.clutchTrimPercent, b.clutchTrimPercent, epsilon) &&
                   !Differs(a.centerOfMassYTrim, b.centerOfMassYTrim, epsilon) &&
                   !Differs(a.centerOfMassZTrim, b.centerOfMassZTrim, epsilon) &&
                   !Differs(a.skiStanceTrim, b.skiStanceTrim, epsilon);
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
            renamed.usesAutomaticName = false;
            bool saved = Store.SaveProfile(renamed, false);
            if (saved)
            {
                TuneProfile current = Store.GetCurrentSetupForSled(GetSledKey(sled), GetVehicleId(sled));
                if (current != null &&
                    string.Equals(current.setupSlotId, profile.profileId, StringComparison.OrdinalIgnoreCase))
                {
                    current.setupSlotName = trimmed;
                    if (!current.setupEdited)
                        current.name = trimmed;
                    if (!QueueCurrentSetupSave(current, sled, true))
                    {
                        // Keep the slot and current record aligned. If disk state
                        // rejects the current-record update, restore the original
                        // slot metadata before reporting failure.
                        TuneProfile original = TuneStore.Clone(profile);
                        if (original != null && Store.SaveProfile(original, false))
                        {
                            status = "Setup rename failed.";
                            return false;
                        }

                        status = "Setup renamed; current name sync failed.";
                        return true;
                    }
                }
            }
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
            duplicate.name = null;
            duplicate.usesAutomaticName = true;
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

        internal bool DeleteProfile(string profileId)
        {
            return Store.DeleteProfile(profileId);
        }

        internal bool GetSetupSlotUsage(
            TuneProfile profile,
            VehicleScriptableObject sled,
            out bool isCurrent,
            out bool isDefault)
        {
            isCurrent = false;
            isDefault = false;
            if (profile == null || sled == null)
                return false;

            string sledKey = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            isCurrent = Store.IsProfileCurrentForSled(profile.profileId, sledKey, vehicleId);
            isDefault = Store.IsProfileDefaultForSled(profile.profileId, sledKey, vehicleId);
            return isCurrent || isDefault;
        }

        internal List<TuneProfile> ArchivedProfilesForSled(VehicleScriptableObject sled)
        {
            if (sled == null)
                return new List<TuneProfile>();

            return Store.GetArchivedProfilesForSled(GetSledKey(sled), GetVehicleId(sled));
        }

        internal bool RestoreArchivedSetup(string profileId, out TuneProfile restored, out string status)
        {
            restored = null;
            if (Store.RestoreLatestArchivedProfile(profileId, out restored))
            {
                status = "Setup restored.";
                return true;
            }

            status = "Restore failed.";
            return false;
        }

        internal List<TuneHistoryEntry> ProfileHistoryForSled(
            VehicleScriptableObject sled,
            int maximumEntries = 20)
        {
            if (sled == null)
                return new List<TuneHistoryEntry>();

            return Store.GetProfileHistoryForSled(
                GetSledKey(sled),
                GetVehicleId(sled),
                maximumEntries);
        }

        internal bool RestoreProfileHistory(
            TuneHistoryEntry entry,
            out TuneProfile restored,
            out string status)
        {
            restored = null;
            if (entry == null ||
                !Store.RestoreProfileHistory(entry.sourceProfileId, entry.historyId, out restored))
            {
                status = "History restore failed.";
                return false;
            }

            status = "History restored as new.";
            return true;
        }

        internal bool ResetToFactory(VehicleScriptableObject sled, bool reloadIfActive)
        {
            if (sled == null)
                return false;

            try
            {
                TryBuildDefaults();
                string sledKey = GetSledKey(sled);
                bool activeNativePhysicsMayBeTuned = sled == ActiveSO && IsSledModifiedByAlpine(sled);
                var defaults = Store.GetDefaults(sledKey, GetVehicleId(sled));
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
                if (!Store.SetActiveProfile(sledKey, GetVehicleId(sled), null))
                    return false;

                var stockSetup = Catalog.CreateDefaultProfile(sled, LocalAuthorName);
                stockSetup.name = "Stock Setup";
                stockSetup.setupSlotId = null;
                stockSetup.setupSlotName = "Stock Setup";
                stockSetup.setupEdited = false;
                stockSetup.isCurrentSetup = true;
                PreviewProfile(stockSetup, sled);
                if (!QueueCurrentSetupSave(stockSetup, sled, true))
                    return false;

                NotifyActiveTuneCleared(sled);

                if (sled == ActiveSO)
                {
                    ApplyRuntimeDefaults(defaults);
                    ApplyHeadlightDefaults();
                    ApplyAccessoryMode("stock", defaults);
                    QueueEngineAudioSwap(defaults, sled);
                    if (reloadIfActive || activeNativePhysicsMayBeTuned)
                    {
                        if (!ReloadSled(out string reloadStatus))
                        {
                            MelonLogger.Warning(string.IsNullOrWhiteSpace(reloadStatus)
                                ? "Factory reset is saved but the live sled rebuild failed."
                                : reloadStatus);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ResetToFactory failed: {ex.GetType().Name}");
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
                MelonLogger.Error($"PublishProfile failed: {ex.GetType().Name}");
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
                MelonLogger.Warning($"Active Alpine tune broadcast skipped: {ex.GetType().Name}");
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
                MelonLogger.Warning($"Active Alpine tune clear broadcast skipped: {ex.GetType().Name}");
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
                if (computation == null || computation.stats == null)
                {
                    status = computation?.unavailableReason ?? "Remote setup engine is unavailable.";
                    return false;
                }
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
                status = $"Remote setup install failed: {ex.GetType().Name}";
                MelonLogger.Warning(status);
                return false;
            }
        }

        internal TuneProfile ImportSharedProfile(TuneProfile profile)
        {
            return Store.ImportSharedProfile(profile);
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

        internal VehicleScriptableObject FindSledByIdentity(string sledKey, string vehicleId)
        {
            TryBuildDefaults();
            RefreshSelectableSledsIfDue(false);

            if (!string.IsNullOrWhiteSpace(vehicleId))
            {
                var byVehicleId = _selectableSleds.FirstOrDefault(s =>
                    string.Equals(GetVehicleId(s), vehicleId, StringComparison.OrdinalIgnoreCase));

                if (byVehicleId != null)
                    return byVehicleId;

                if (SledIdentity.HasNativeVehicleIdentity(sledKey, vehicleId))
                    return null;
            }

            if (!string.IsNullOrWhiteSpace(sledKey))
            {
                var byLegacyKey = _selectableSleds
                    .Where(s => string.Equals(GetSledKey(s), sledKey, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();
                return byLegacyKey.Count == 1 ? byLegacyKey[0] : null;
            }

            return null;
        }

        internal bool CanResolveSledTarget(string sledKey, string vehicleId)
        {
            return FindSledByIdentity(sledKey, vehicleId) != null;
        }

        internal bool HasRuntimeInstanceForSled(VehicleScriptableObject sled)
        {
            if (sled == null || ActiveController == null || ActiveSO == null)
                return false;

            return SledIdentity.FromSled(sled, "target", false, false)
                .Matches(ActiveSO);
        }

        internal void NoteGarageSledSelectionChanged(
            VehicleSelectionUiController controller,
            VehicleScriptableObject sled,
            string source)
        {
            RegisterSelectableSled(sled, source);
            if (CacheGarageSelection(controller, sled, source))
                AlpineNativeUi.NotifyGarageSelectionChanged(controller);
        }

        private bool CacheGarageSelection(
            VehicleSelectionUiController controller,
            VehicleScriptableObject sled,
            string source)
        {
            if (controller == null || sled == null)
                return false;

            int controllerId = controller.GetInstanceID();
            string stableKey = SledIdentity.StableIdentityKey(sled);
            if (string.IsNullOrWhiteSpace(stableKey))
                return false;

            if (_garageSelectionsByController.TryGetValue(controllerId, out var existing) &&
                ReferenceEquals(existing.controller, controller) &&
                ReferenceEquals(existing.sled, sled) &&
                string.Equals(existing.stableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _garageSelectionsByController[controllerId] = new GarageSelectionState
            {
                controller = controller,
                sled = sled,
                source = source,
                stableKey = stableKey
            };

            return true;
        }

        internal void ForgetGarageSelection(VehicleSelectionUiController controller)
        {
            if (controller == null)
                return;

            int controllerId = controller.GetInstanceID();
            if (_garageSelectionsByController.TryGetValue(controllerId, out var state) &&
                state != null &&
                ReferenceEquals(state.controller, controller))
            {
                _garageSelectionsByController.Remove(controllerId);
            }
        }

        private bool DoesActiveRuntimeMatch(SledIdentity identity)
        {
            return identity != null &&
                   ActiveController != null &&
                   ActiveSO != null &&
                   identity.Matches(ActiveSO);
        }

        private bool QueueCurrentSetupSave(TuneProfile profile, VehicleScriptableObject sled, bool writeNow)
        {
            if (profile == null || sled == null || Store == null)
                return false;

            string sledKey = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            string displayName = GetSledDisplayName(sled);
            string key = SledIdentity.StableIdentityKey(sledKey, vehicleId);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            bool accepted = Store.SetCurrentSetup(
                profile,
                sledKey,
                vehicleId,
                displayName,
                profile.setupSlotId,
                profile.setupSlotName,
                profile.setupEdited,
                writeNow);
            if (!accepted)
            {
                MelonLogger.Warning($"Current Setup could not be preserved for {displayName}.");
                return false;
            }

            if (writeNow)
            {
                _pendingCurrentSetupSaves.Remove(key);
                MelonLogger.Msg($"Current Setup preserved for {displayName}.");
                return true;
            }

            _pendingCurrentSetupSaves[key] = new PendingCurrentSetupSave
            {
                sledKey = sledKey,
                vehicleId = vehicleId,
                dueTime = Time.unscaledTime + CurrentSetupSaveDelaySeconds
            };
            return true;
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
            string ignored;
            ReloadSled(out ignored);
        }

        internal bool ReloadSled(out string status)
        {
            status = null;
            try
            {
                PruneDestroyedActiveRuntime();
                SnowmobileController expectedController = ActiveController;
                if (expectedController == null || ActiveSO == null)
                {
                    status = "No live selected sled is available to rebuild.";
                    return false;
                }

                // ReCreateSnowmobile can keep the controller while replacing its
                // child physics graph. Restore every captured per-object value
                // while that old graph is still alive so LocalInit cannot capture
                // an Alpine-tuned brake, geometry, or contact grip as stock.
                RestoreCapturedNativePhysicsDefaults();
                ApplyHeadlightDefaults();
                RestoreAccessoryDefaults();

                if (_pendingEngineAudioApply)
                {
                    _pendingEngineAudioDeadline = Time.unscaledTime + 12f;
                    _pendingEngineAudioNextAttemptTime = Time.unscaledTime + 0.35f;
                    _pendingEngineAudioAttemptsRemaining = Mathf.Max(_pendingEngineAudioAttemptsRemaining, 24);
                    _pendingEngineAudioLastControllerId = int.MinValue;
                    _pendingEngineAudioLoggedReady = false;
                }

                // This is the same path Sledders uses after native vehicle/cosmetic
                // changes and preserves the game's last-valid spawn state.
                if (SleddersGameBindings.TryReCreateSnowmobile(
                        expectedController,
                        out var nativeReason))
                {
                    MelonLogger.Msg("Alpine Tuning triggered native sled recreation.");
                    status = "Sled rebuilt with the preserved setup.";
                    return true;
                }

                Transform spawnTransform = expectedController != null
                    ? expectedController.transform
                    : null;
                if (spawnTransform == null)
                {
                    status = $"Sled rebuild failed: {nativeReason}; active controller is unavailable.";
                    MelonLogger.Error("ReloadSled: " + status);
                    return false;
                }

                if (!SleddersGameBindings.TrySpawnPlayer(spawnTransform, true, out var fallbackReason))
                {
                    status = $"Sled rebuild failed: native path {nativeReason}; fallback {fallbackReason}.";
                    MelonLogger.Error("ReloadSled: " + status);
                    return false;
                }

                MelonLogger.Msg("Alpine Tuning triggered fallback sled reload.");
                status = "Sled rebuilt with the preserved setup.";
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ReloadSled failed: {ex.GetType().Name}");
                status = $"Sled rebuild failed: {ex.GetType().Name}";
                return false;
            }
        }

        private bool IsSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return false;

            string identity = SledIdentity.StableIdentityKey(sled);
            return !string.IsNullOrWhiteSpace(identity) &&
                   _sledsModifiedByAlpineThisSession.Contains(identity);
        }

        private void MarkSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string identity = SledIdentity.StableIdentityKey(sled);
            if (!string.IsNullOrWhiteSpace(identity))
                _sledsModifiedByAlpineThisSession.Add(identity);
        }

        private void UnmarkSledModifiedByAlpine(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string identity = SledIdentity.StableIdentityKey(sled);
            if (!string.IsNullOrWhiteSpace(identity))
                _sledsModifiedByAlpineThisSession.Remove(identity);
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

                Store.RefreshKnownVehicleIdentities(_selectableSleds);

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
                MelonLogger.Error($"TryBuildDefaults error: {ex.GetType().Name}");
            }
        }

        private List<VehicleScriptableObject> BuildSelectableSledList()
        {
            var added = new List<VehicleScriptableObject>();
            _selectableSleds.RemoveAll(sled => sled == null);
            var seen = new HashSet<string>(
                _selectableSleds
                    .Where(sled => sled != null)
                    .Select(SledIdentity.StableIdentityKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);
            var lists = Resources.FindObjectsOfTypeAll<VehicleListScriptableObject>();
            if (lists != null)
            {
                foreach (var list in lists)
                {
                    foreach (var sled in SleddersGameBindings.GetSelectableVehicles(list))
                    {
                        if (AddSelectableSled(sled, seen))
                            added.Add(sled);
                    }
                }
            }

            var loadedSleds = Resources.FindObjectsOfTypeAll<VehicleScriptableObject>();
            if (loadedSleds != null)
            {
                foreach (var sled in loadedSleds)
                {
                    if (AddSelectableSled(sled, seen))
                        added.Add(sled);
                }
            }

            if (AddSelectableSled(ActiveSO, seen))
                added.Add(ActiveSO);

            if (added.Count > 0)
            {
                _selectableSleds.Sort((a, b) =>
                    string.Compare(GetSledDisplayName(a), GetSledDisplayName(b), StringComparison.OrdinalIgnoreCase));
            }

            if (_selectableSleds.Count == 0)
                MelonLogger.Warning("No VehicleScriptableObject found; garage tuning list will be limited.");
            else if (added.Count > 0)
                MelonLogger.Msg($"Alpine discovered {_selectableSleds.Count} sled donor candidate(s).");

            return added;
        }

        private bool AddSelectableSled(VehicleScriptableObject sled, HashSet<string> seen)
        {
            if (sled == null || seen == null)
                return false;

            string key = SledIdentity.StableIdentityKey(sled);
            if (string.IsNullOrWhiteSpace(key))
                key = GetSledKey(sled);

            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                return false;

            _selectableSleds.Add(sled);
            return true;
        }

        private void RegisterSelectableSled(VehicleScriptableObject sled, string source)
        {
            if (sled == null)
                return;

            string stableKey = SledIdentity.StableIdentityKey(sled);
            int existingIndex = string.IsNullOrWhiteSpace(stableKey)
                ? -1
                : _selectableSleds.FindIndex(candidate =>
                    candidate != null &&
                    string.Equals(
                        SledIdentity.StableIdentityKey(candidate),
                        stableKey,
                        StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                // A mod can reload its ScriptableObject while retaining the same
                // stable vehicle identity. Prefer the instance handed to us by the
                // native garage/runtime lifecycle over an older discovery result.
                if (ReferenceEquals(_selectableSleds[existingIndex], sled))
                    return;

                _selectableSleds[existingIndex] = sled;
                if (_defaultsBuilt)
                {
                    RefreshStatDefaultsFromCleanLoad(sled);
                    EnsureDefaultsForSled(sled);
                }
                return;
            }

            var seen = new HashSet<string>(
                _selectableSleds
                    .Where(candidate => candidate != null)
                    .Select(SledIdentity.StableIdentityKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

            if (!AddSelectableSled(sled, seen))
                return;

            _selectableSleds.Sort((a, b) =>
                string.Compare(GetSledDisplayName(a), GetSledDisplayName(b), StringComparison.OrdinalIgnoreCase));

            if (_defaultsBuilt)
            {
                Store?.RefreshKnownVehicleIdentities(_selectableSleds);
                RefreshStatDefaultsFromCleanLoad(sled);
                EnsureDefaultsForSled(sled);
                Store?.MigrateLegacyPresets(new[] { sled }, LocalAuthorName);
            }

            MelonLogger.Msg(
                $"Alpine registered late-loaded sled '{GetSledDisplayName(sled)}' " +
                $"from {(!string.IsNullOrWhiteSpace(source) ? source : "runtime discovery")}.");
        }

        private void RefreshSelectableSledsIfDue(bool force)
        {
            if (!_defaultsBuilt)
                return;

            float now = Time.unscaledTime;
            if (!force && now < _nextSelectableSledRefreshTime)
                return;

            _nextSelectableSledRefreshTime = now + 10f;
            List<VehicleScriptableObject> added = BuildSelectableSledList();
            if (added.Count == 0)
                return;

            Store?.RefreshKnownVehicleIdentities(_selectableSleds);

            foreach (VehicleScriptableObject sled in added)
            {
                RefreshStatDefaultsFromCleanLoad(sled);
                EnsureDefaultsForSled(sled);
            }

            Store?.MigrateLegacyPresets(added, LocalAuthorName);
            MelonLogger.Msg($"Alpine initialized {added.Count} late-loaded sled donor candidate(s).");
        }

        private void EnsureDefaultsForSled(
            VehicleScriptableObject sled,
            bool forceRuntimeCapture = false)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            bool modifiedByAlpine = IsSledModifiedByAlpine(sled);
            var defaults = Store.GetDefaults(key, vehicleId);
            bool legacyIdentityMismatch = defaults != null &&
                                          !string.Equals(vehicleId, key, StringComparison.OrdinalIgnoreCase) &&
                                          !string.Equals(defaults.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase);
            if (defaults == null || legacyIdentityMismatch)
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
                if (!forceRuntimeCapture &&
                    modifiedByAlpine &&
                    HasCapturedControllerDefaults(defaults))
                {
                    LogDefaultCaptureSkipped(key, "active controller has already been tuned by Alpine");
                    return;
                }

                if (forceRuntimeCapture)
                    defaults.controller = new ControllerDefaults();

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
                   controller.hasStabilizerDamping ||
                   controller.hasTrackSpeedDamping ||
                   controller.hasTrackSpeedGyroMultiplier;
        }

        private void RefreshStatDefaultsFromCleanLoad(VehicleScriptableObject sled)
        {
            if (sled == null)
                return;

            string key = GetSledKey(sled);
            string vehicleId = GetVehicleId(sled);
            if (IsSledModifiedByAlpine(sled))
            {
                LogDefaultCaptureSkipped(key, "scriptable object has already been tuned by Alpine");
                return;
            }

            var existing = Store.GetDefaults(key, vehicleId);
            if (existing == null || !StoredStatsDifferFromSled(existing, sled))
                return;

            var refreshed = SledDefaults.FromSled(sled, key);

            // Keep non-stat metadata that cannot always be recovered from the clean ScriptableObject.
            refreshed.engineAudioEnumType = existing.engineAudioEnumType;
            refreshed.engineAudioEnumName = existing.engineAudioEnumName;
            refreshed.engineAudioEnumRawValue = existing.engineAudioEnumRawValue;
            refreshed.controller = existing.controller ?? new ControllerDefaults();
            refreshed.nativePhysics = existing.nativePhysics ?? new NativePhysicsDefaults();

            Store.PutDefaults(refreshed);
            MelonLogger.Msg(
                $"Refreshed stock stat baseline for '{key}' from clean game load. " +
                "This prevents Alpine profiles from compounding on stale tuned defaults.");
        }

        private static bool StoredStatsDifferFromSled(SledDefaults defaults, VehicleScriptableObject sled)
        {
            return Differs(defaults.horsePower, sled.horsePower, 0.01f) ||
                   Differs(defaults.powerFactor, sled.powerFactor, 0.001f) ||
                   (!defaults.hasMaxRpm && IsFinitePositive(sled.maxRpm)) ||
                   (defaults.hasMaxRpm && Differs(defaults.maxRpm, sled.maxRpm, 0.1f)) ||
                   Differs(defaults.lugHeight, sled.lugHeight, 0.01f) ||
                   Differs(defaults.friction, sled.coefficientOfFriction, 0.001f) ||
                   Differs(defaults.weight, sled.weight, 0.01f) ||
                   Differs(defaults.skiStance, sled.skiStance, 0.1f) ||
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

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
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

            MelonLogger.Msg($"Equipping preserved Alpine setup for {ActiveSO.name}.");
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
            var baseDefaults = Store.GetDefaults(sledKey, GetVehicleId(sled));
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

            bool hasDonorReference = !string.IsNullOrWhiteSpace(profile.donorSledKey) ||
                                     !string.IsNullOrWhiteSpace(profile.donorVehicleId);
            if (hasDonorReference)
            {
                var donorSled = FindSledByIdentity(profile.donorSledKey, profile.donorVehicleId);
                var donorDefaults = Store.GetDefaults(
                    profile.donorSledKey,
                    !string.IsNullOrWhiteSpace(profile.donorVehicleId)
                        ? profile.donorVehicleId
                        : donorSled != null ? GetVehicleId(donorSled) : null);
                if (donorSled == null || donorDefaults == null)
                {
                    return new TuneComputation
                    {
                        baseDefaults = baseDefaults,
                        engineDefaults = null,
                        audioDefaults = baseDefaults,
                        audioSource = sled,
                        stats = null,
                        requiresReload = false,
                        unavailableReason = "Saved engine is unavailable; choose Stock or another engine."
                    };
                }

                engineDefaults = donorDefaults;
                audioDefaults = donorDefaults;
                audioSource = donorSled;
                TryPopulateDefaultAudioToken(audioDefaults, audioSource, false);
            }

            var effect = new PartEffect();
            var parts = new List<TunePart>();
            bool requiresReload;

            Catalog.EnsureProfileSelections(profile);
            foreach (string category in PartCatalog.OrderedCategories)
            {
                string partId = profile.GetPartId(category);
                var part = Catalog.Find(partId) ?? Catalog.Find(Catalog.DefaultPartId(category));
                if (part == null)
                    continue;

                parts.Add(part);
                AlpineTuneMath.MergeEffect(effect, part.effect);
            }

            var fine = profile.fineTune ?? new FineTuneSettings();
            profile.fineTune = fine;
            var resolvedStats = AlpineTuneMath.ComputeStats(
                baseDefaults,
                engineDefaults,
                parts,
                effect,
                fine);

            // These values are copied from VehicleScriptableObject into the native
            // sled bodies/track/mesh during LocalInit. Mutating the asset while a
            // ride is already alive cannot update that native state safely.
            bool activeSpawnDiffers = ActiveSpawnValuesDiffer(
                sled,
                resolvedStats,
                out bool comparedActiveSpawn);
            requiresReload = comparedActiveSpawn
                ? activeSpawnDiffers
                : NativeSpawnValuesDiffer(baseDefaults, resolvedStats);

            return new TuneComputation
            {
                baseDefaults = baseDefaults,
                engineDefaults = engineDefaults,
                audioDefaults = audioDefaults,
                audioSource = audioSource,
                parts = parts,
                mergedEffect = effect,
                requiresReload = requiresReload,
                stats = resolvedStats
            };
        }

        internal static bool NativeSpawnValuesDiffer(SledDefaults defaults, ResolvedStats stats)
        {
            if (defaults == null || stats == null)
                return false;

            return Differs(defaults.horsePower, stats.horsePower, 0.01f) ||
                   defaults.hasMaxRpm != stats.hasMaxRpm ||
                   (defaults.hasMaxRpm && Differs(defaults.maxRpm, stats.maxRpm, 0.1f)) ||
                   Differs(defaults.lugHeight, stats.lugHeight, 0.01f) ||
                   Differs(defaults.friction, stats.friction, 0.001f) ||
                   Differs(defaults.weight, stats.weight, 0.01f) ||
                   Differs(defaults.skiStance, stats.skiStance, 0.1f) ||
                   Differs(defaults.skisXDistanceOffset, stats.skisXDistanceOffset, 0.0001f) ||
                   Differs(defaults.centerOfMassOffset.ToVector3(), stats.centerOfMassOffset.ToVector3(), 0.0001f) ||
                   Differs(defaults.driverCenterOfMassOffset.ToVector3(), stats.driverCenterOfMassOffset.ToVector3(), 0.0001f) ||
                   defaults.isTurboOn != stats.isTurboOn;
        }

        private bool ActiveSpawnValuesDiffer(
            VehicleScriptableObject sled,
            ResolvedStats stats,
            out bool compared)
        {
            compared = false;
            if (sled == null ||
                stats == null ||
                ActiveController == null ||
                _activeSpawnValues == null ||
                _spawnValuesControllerId != ActiveController.GetInstanceID())
            {
                return false;
            }

            string identity = SledIdentity.StableIdentityKey(sled);
            if (string.IsNullOrWhiteSpace(identity) ||
                !string.Equals(
                    identity,
                    _activeSpawnValues.stableIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            compared = true;
            return Differs(_activeSpawnValues.horsePower, stats.horsePower, 0.01f) ||
                   _activeSpawnValues.hasMaxRpm != stats.hasMaxRpm ||
                   (_activeSpawnValues.hasMaxRpm && Differs(_activeSpawnValues.maxRpm, stats.maxRpm, 0.1f)) ||
                   Differs(_activeSpawnValues.lugHeight, stats.lugHeight, 0.01f) ||
                   Differs(_activeSpawnValues.friction, stats.friction, 0.001f) ||
                   Differs(_activeSpawnValues.weight, stats.weight, 0.01f) ||
                   Differs(_activeSpawnValues.skiStance, stats.skiStance, 0.1f) ||
                   Differs(_activeSpawnValues.skisXDistanceOffset, stats.skisXDistanceOffset, 0.0001f) ||
                   Differs(_activeSpawnValues.centerOfMassOffset, stats.centerOfMassOffset.ToVector3(), 0.0001f) ||
                   Differs(_activeSpawnValues.driverCenterOfMassOffset, stats.driverCenterOfMassOffset.ToVector3(), 0.0001f) ||
                   _activeSpawnValues.isTurboOn != stats.isTurboOn;
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
            if (stats.hasMaxRpm)
                sled.maxRpm = stats.maxRpm;
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

        internal static void ApplySnowmobileStatBars(SnowmobileStats statBars, TuneComputation computation)
        {
            var defaults = computation.baseDefaults;
            if (defaults != null && defaults.hasSnowmobileStats)
            {
                // SnowmobileStats belongs to the recipient sled. An engine donor's
                // defaults are the numerator source, not the stock comparison
                // denominator; otherwise every donor swap incorrectly reads 1.0.
                float powerRatio = Mathf.Clamp(SafeRatio(computation.stats.horsePower, defaults.horsePower), 0.60f, 1.80f);
                float climbRatio = Mathf.Clamp(
                    (SafeRatio(computation.stats.lugHeight, defaults.lugHeight) +
                     SafeRatio(computation.stats.friction, defaults.friction)) * 0.5f,
                    0.55f,
                    1.65f);
                float agilityRatio = Mathf.Clamp(SafeRatio(defaults.weight, computation.stats.weight), 0.75f, 1.35f);

                statBars.power = Mathf.Clamp(defaults.statsPower * powerRatio, 0f, 100f);
                statBars.climbing = Mathf.Clamp(defaults.statsClimbing * climbRatio, 0f, 100f);
                statBars.agility = Mathf.Clamp(defaults.statsAgility * agilityRatio, 0f, 100f);
                return;
            }

            statBars.power = Mathf.Clamp(computation.stats.horsePower / 250f * 100f, 0f, 100f);
            statBars.climbing = Mathf.Clamp(
                (computation.stats.lugHeight / 60f + computation.stats.friction / 2.4f) * 50f,
                0f,
                100f);
            statBars.agility = Mathf.Clamp((1.1f - computation.stats.weight / 450f) * 100f, 0f, 100f);
        }

        private static void ApplyDefaultsToSled(VehicleScriptableObject sled, SledDefaults defaults)
        {
            sled.horsePower = defaults.horsePower;
            sled.powerFactor = defaults.powerFactor;
            if (defaults.hasMaxRpm)
                sled.maxRpm = defaults.maxRpm;
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

            if (defaults.hasThrottleExponent && Mathf.Abs(effect.throttleExponentDelta) > 0.0001f)
                SetFloatField(ActiveController, "throttleExponent", ClampOffset(defaults.throttleExponent + effect.throttleExponentDelta, defaults.throttleExponent, 0.20f, 0.25f, 4f));

            if (defaults.hasRpmSensitivity)
                SetFloatField(ActiveController, "rpmSensitivity", AlpineTuneMath.ResolveRpmSensitivity(defaults.rpmSensitivity, effect));

            if (defaults.hasRpmSensitivityDown)
                SetFloatField(ActiveController, "rpmSensitivityDown", AlpineTuneMath.ResolveRpmSensitivityDown(defaults.rpmSensitivityDown, effect));

            AlpineTuneMath.ResolvedClutchRange clutch =
                AlpineTuneMath.ResolveClutchRange(defaults, effect, fine);
            if (clutch.HasMinimum)
                SetFloatField(ActiveController, "clutchRpmMin", clutch.Minimum);

            if (clutch.HasMaximum)
                SetFloatField(ActiveController, "clutchRpmMax", clutch.Maximum);

            if (defaults.hasMinThrottleOnClutchEngagement &&
                Mathf.Abs(effect.minThrottleOnClutchEngagementOffset) > 0.0001f)
                SetFloatField(ActiveController, "minThrottleOnClutchEngagement", Mathf.Clamp01(defaults.minThrottleOnClutchEngagement + effect.minThrottleOnClutchEngagementOffset));

            ApplyStabilizerRuntime(defaults, effect);
            ApplyNativePhysicsRuntime(effect);
        }

        private void ApplyRuntimeDefaults(SledDefaults defaults)
        {
            if (ActiveController == null || defaults == null)
                return;

            RestoreNativePhysicsDefaults();

            var runtime = defaults.controller;
            if (runtime.hasThrottleExponent) SetFloatField(ActiveController, "throttleExponent", runtime.throttleExponent);
            if (runtime.hasRpmSensitivity) SetFloatField(ActiveController, "rpmSensitivity", runtime.rpmSensitivity);
            if (runtime.hasRpmSensitivityDown) SetFloatField(ActiveController, "rpmSensitivityDown", runtime.rpmSensitivityDown);
            if (runtime.hasClutchRpmMin) SetFloatField(ActiveController, "clutchRpmMin", runtime.clutchRpmMin);
            if (runtime.hasClutchRpmMax) SetFloatField(ActiveController, "clutchRpmMax", runtime.clutchRpmMax);
            if (runtime.hasMinThrottleOnClutchEngagement) SetFloatField(ActiveController, "minThrottleOnClutchEngagement", runtime.minThrottleOnClutchEngagement);
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
            _headlightBindingCaptureResult = HeadlightBindingCaptureResult.None;
        }

        internal void BeginHeadlightControllerBind()
        {
            _waitingForHeadlightControllerBinding = true;
            _waitingForHeadlightKeyboardBinding = false;
            _headlightBindingCaptureDeadline = Time.unscaledTime + 8f;
            _headlightBindingCaptureResult = HeadlightBindingCaptureResult.None;
        }

        internal void CancelHeadlightBindingCapture()
        {
            CompleteHeadlightBindingCapture(HeadlightBindingCaptureResult.Cancelled, true);
        }

        private void CompleteHeadlightBindingCapture(
            HeadlightBindingCaptureResult result,
            bool handledCancelInput)
        {
            _waitingForHeadlightKeyboardBinding = false;
            _waitingForHeadlightControllerBinding = false;
            _headlightBindingCaptureDeadline = 0f;
            _headlightBindingCaptureResult = result;
            if (handledCancelInput)
                _headlightBindingCancelFrame = Time.frameCount;
        }

        internal bool ClearHeadlightBinding()
        {
            bool previousEnabled = Settings.headlightToggleEnabled;
            string previousKeyboard = Settings.headlightKeyboardKey;
            string previousController = Settings.headlightControllerButton;
            bool previousConfigured = Settings.headlightBindingConfigured;
            int previousRevision = Settings.headlightBindingRevision;
            Settings.headlightToggleEnabled = false;
            Settings.headlightKeyboardKey = null;
            Settings.headlightControllerButton = null;
            Settings.headlightBindingConfigured = false;
            Settings.headlightBindingRevision = 2;
            if (SaveSettings())
                return true;

            Settings.headlightToggleEnabled = previousEnabled;
            Settings.headlightKeyboardKey = previousKeyboard;
            Settings.headlightControllerButton = previousController;
            Settings.headlightBindingConfigured = previousConfigured;
            Settings.headlightBindingRevision = previousRevision;
            return false;
        }

        private void UpdateHeadlightInputBinding()
        {
            var settings = Settings;

            if (_waitingForHeadlightKeyboardBinding || _waitingForHeadlightControllerBinding)
            {
                // Escape is always the clear cancellation route and can never
                // become the hotkey itself. This also prevents a garage Back
                // press from being captured as a lighting binding.
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelHeadlightBindingCapture();
                    return;
                }

                if (Time.unscaledTime > _headlightBindingCaptureDeadline)
                {
                    CompleteHeadlightBindingCapture(HeadlightBindingCaptureResult.TimedOut, false);
                    return;
                }

                KeyCode captured;
                if (TryCaptureBindingKey(_waitingForHeadlightControllerBinding, out captured))
                {
                    bool previousEnabled = settings.headlightToggleEnabled;
                    string previousKeyboard = settings.headlightKeyboardKey;
                    string previousController = settings.headlightControllerButton;
                    bool previousConfigured = settings.headlightBindingConfigured;
                    int previousRevision = settings.headlightBindingRevision;
                    if (_waitingForHeadlightControllerBinding)
                        settings.headlightControllerButton = captured.ToString();
                    else
                        settings.headlightKeyboardKey = captured.ToString();

                    settings.headlightBindingConfigured = true;
                    settings.headlightBindingRevision = 2;
                    settings.headlightToggleEnabled = true;
                    settings.Normalize();
                    bool saved = SaveSettings();
                    if (!saved)
                    {
                        settings.headlightToggleEnabled = previousEnabled;
                        settings.headlightKeyboardKey = previousKeyboard;
                        settings.headlightControllerButton = previousController;
                        settings.headlightBindingConfigured = previousConfigured;
                        settings.headlightBindingRevision = previousRevision;
                        MelonLogger.Warning("Headlight binding capture could not be saved; previous binding restored.");
                    }
                    CompleteHeadlightBindingCapture(
                        saved
                            ? HeadlightBindingCaptureResult.Saved
                            : HeadlightBindingCaptureResult.SaveFailed,
                        false);
                }

                return;
            }

            // The garage owns Secondary/F (and controller context actions) while
            // its tuning rail is open. Do not let a configured lighting hotkey
            // consume the same physical input in that UI state.
            if (AlpineNativeUi.IsGarageTuningOpen)
                return;

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

                if (code == KeyCode.Escape || code == KeyCode.Backspace ||
                    code == KeyCode.Return || code == KeyCode.KeypadEnter ||
                    code == KeyCode.Space || code == KeyCode.Tab ||
                    IsReservedControllerUiButton(code))
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

        private static bool IsReservedControllerUiButton(KeyCode code)
        {
            string name = code.ToString();
            if (!name.StartsWith("Joystick", StringComparison.OrdinalIgnoreCase))
                return false;

            int marker = name.LastIndexOf("Button", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return false;

            string suffix = name.Substring(marker + "Button".Length);
            return string.Equals(suffix, "0", StringComparison.Ordinal) ||
                   string.Equals(suffix, "1", StringComparison.Ordinal);
        }

        internal bool ToggleHeadlightsForActiveSled(out string status)
        {
            status = null;

            if (ActiveSO == null || ActiveController == null)
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

            bool? previousMode = profile.headlightEnabled;
            profile.headlightEnabled = desired;
            string applyStatus;
            if (!UpdateCurrentSetup(profile, ActiveSO, out applyStatus))
            {
                if (string.IsNullOrWhiteSpace(applyStatus) ||
                    !applyStatus.StartsWith("Current Setup preserved;", StringComparison.OrdinalIgnoreCase))
                {
                    profile.headlightEnabled = previousMode;
                }
                status = string.IsNullOrWhiteSpace(applyStatus)
                    ? "Headlight toggle could not be applied."
                    : applyStatus;
                return false;
            }

            SetActiveHeadlightsEnabled(desired);
            status = desired ? "Headlights on." : "Headlights off.";
            return true;
        }

        internal bool AreActiveHeadlightsOn()
        {
            // Light.enabled is only the rendered result. Native HeadLight.Refresh
            // disables it during daylight and while the engine is stopped, so it
            // cannot represent the rider's logical on/off selection.
            if (ActiveController != null)
                return ActiveController.isHeadlightOn;

            if (_activeHeadlightOverride.HasValue)
                return _activeHeadlightOverride.Value;

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
            // UpdateGraphics passes this native switch to HeadLight.Refresh. That
            // method owns the fade curve and SnowmobileStructure emissive material;
            // changing only Light.enabled leaves the body glow and native state out
            // of sync and is overwritten again on the next graphics update.
            bool illuminate = enabled;
            if (ActiveController != null)
            {
                ActiveController.isHeadlightOn = enabled;
                illuminate = enabled && ActiveController.isEngineOn;

                // Follow the exact native UpdateGraphics path. Refresh owns the
                // fade curve, day/night gate, projected light and
                // SnowmobileStructure.SetHeadlightEmission material channel.
                HeadLight[] nativeHeadlights =
                    ActiveController.GetComponentsInChildren<HeadLight>(true);
                if (nativeHeadlights != null && nativeHeadlights.Length > 0)
                {
                    foreach (HeadLight headlight in nativeHeadlights)
                    {
                        if (headlight != null)
                        {
                            headlight.Refresh(illuminate);
                            // Refresh intentionally applies the native time-of-day
                            // gate. A saved Force On override is the one mode that
                            // must remain visibly on during daylight; retain the
                            // native fade/emission update, then override only the
                            // projected light's daytime gate.
                            if (_activeHeadlightOverride == true && illuminate)
                            {
                                Light projectedLight = headlight.GetComponent<Light>();
                                if (projectedLight != null)
                                    projectedLight.enabled = true;
                            }
                        }
                    }
                    return;
                }
            }

            // Compatibility fallback for a future controller graph that exposes
            // Light components but not the verified 1.1.6 HeadLight component.
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights == null)
                return;

            foreach (var light in lights)
            {
                if (light != null)
                    light.enabled = illuminate;
            }
        }

        private void EnforceHeadlightOverride()
        {
            if (ActiveSO == null)
                return;

            if (_activeHeadlightDefaults != null)
            {
                foreach (RuntimeHeadlightDefault defaults in _activeHeadlightDefaults.lights)
                {
                    if (defaults == null || defaults.light == null || !defaults.hasTunedValues)
                        continue;

                    defaults.light.color = defaults.tunedColor;
                    defaults.light.range = defaults.tunedRange;
                    defaults.light.spotAngle = defaults.tunedSpotAngle;
                    defaults.light.transform.localRotation = defaults.tunedLocalRotation;

                    // HeadLight.Refresh owns the fade curve and writes the
                    // projected intensity from this native baseline. Reassert the
                    // baseline without calling Refresh (or forcing full intensity)
                    // a second time during LateUpdate.
                    if (defaults.nativeHeadlight != null && NativeHeadlightBaseIntensityField != null)
                        NativeHeadlightBaseIntensityField.SetValue(defaults.nativeHeadlight, defaults.tunedIntensity);
                    else
                        defaults.light.intensity = defaults.tunedIntensity;
                }
            }

            if (!_activeHeadlightOverride.HasValue)
                return;

            bool logicalState = _activeHeadlightOverride.Value;
            bool illuminate = logicalState &&
                              (ActiveController == null || ActiveController.isEngineOn);
            if (ActiveController != null)
                ActiveController.isHeadlightOn = logicalState;

            // Native UpdateGraphics has already advanced HeadLight.Refresh once
            // this frame. Only Force On's deliberate daylight override and Force
            // Off's hard disable are needed here; calling Refresh again would
            // advance its fade curve multiple times per frame.
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights == null)
                return;
            foreach (Light light in lights)
            {
                if (light != null)
                    light.enabled = illuminate;
            }
        }

        private void PrepareHeadlightOverride()
        {
            if (ActiveSO == null || ActiveController == null || !_activeHeadlightOverride.HasValue)
                return;

            // Publish the saved logical mode before native UpdateGraphics. The
            // game's own Refresh call then advances fade/emission exactly once.
            ActiveController.isHeadlightOn = _activeHeadlightOverride.Value;
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

            var captured = new RuntimeHeadlightDefaults
            {
                controller = ActiveController,
                nativeSwitchEnabled = ActiveController.isHeadlightOn
            };
            var lights = SleddersGameBindings.GetHeadlightLights(ActiveController);
            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light == null)
                        continue;

                    HeadLight nativeHeadlight = light.GetComponent<HeadLight>();
                    float nativeIntensity = light.intensity;
                    if (nativeHeadlight != null && NativeHeadlightBaseIntensityField != null)
                    {
                        object capturedIntensity = NativeHeadlightBaseIntensityField.GetValue(nativeHeadlight);
                        if (capturedIntensity is float value && value > 0.0001f)
                            nativeIntensity = value;
                    }

                    captured.lights.Add(new RuntimeHeadlightDefault
                    {
                        light = light,
                        nativeHeadlight = nativeHeadlight,
                        color = light.color,
                        intensity = nativeIntensity,
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

            if (_activeHeadlightDefaults.controller != null)
            {
                _activeHeadlightDefaults.controller.isHeadlightOn =
                    _activeHeadlightDefaults.nativeSwitchEnabled;
            }

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
                if (defaults.nativeHeadlight != null && NativeHeadlightBaseIntensityField != null)
                    NativeHeadlightBaseIntensityField.SetValue(defaults.nativeHeadlight, defaults.intensity);
                defaults.hasTunedValues = false;
            }

            SetActiveHeadlightsEnabled(_activeHeadlightDefaults.nativeSwitchEnabled);
        }

        private void CaptureAccessoryDefaultsForActiveController(bool replace)
        {
            if (ActiveController == null)
            {
                _activeAccessoryDefaults = null;
                _accessoryDefaultsControllerId = int.MinValue;
                return;
            }

            int controllerId = ActiveController.GetInstanceID();
            if (!replace && _activeAccessoryDefaults != null && _accessoryDefaultsControllerId == controllerId)
                return;

            var captured = new RuntimeAccessoryDefaults();
            var seen = new HashSet<int>();
            object[] components = SleddersGameBindings.GetSnowmobileAccessories(ActiveController);
            string[] fields = { "windshieldObjects", "snowFlapObjects", "rearPartObjects", "tunnelReflectors" };
            if (components != null)
            {
                foreach (object component in components)
                {
                    if (component == null)
                        continue;

                    foreach (string field in fields)
                    {
                        object value = SleddersGameBindings.GetFieldValue<object>(component, field);
                        if (!(value is System.Collections.IEnumerable objects))
                            continue;

                        foreach (object item in objects)
                        {
                            GameObject gameObject = item as GameObject;
                            if (gameObject == null || !seen.Add(gameObject.GetInstanceID()))
                                continue;

                            captured.objects.Add(new RuntimeAccessoryDefault
                            {
                                gameObject = gameObject,
                                active = gameObject.activeSelf
                            });
                        }
                    }
                }
            }

            _activeAccessoryDefaults = captured;
            _accessoryDefaultsControllerId = controllerId;
        }

        private void RestoreAccessoryDefaults()
        {
            CaptureAccessoryDefaultsForActiveController(false);
            if (_activeAccessoryDefaults == null)
                return;

            foreach (RuntimeAccessoryDefault defaults in _activeAccessoryDefaults.objects)
            {
                if (defaults != null && defaults.gameObject != null)
                    defaults.gameObject.SetActive(defaults.active);
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

            bool nativeSwitchEnabled = _activeHeadlightOverride ??
                                       _activeHeadlightDefaults.nativeSwitchEnabled;
            if (ActiveController != null)
                ActiveController.isHeadlightOn = nativeSwitchEnabled;

            float pitch = Mathf.Clamp(effect.headlightPitchOffsetDegrees, -5f, 5f);
            foreach (var defaults in _activeHeadlightDefaults.lights)
            {
                if (defaults == null || defaults.light == null)
                    continue;

                defaults.tunedColor = effect.hasHeadlightColor ? effect.headlightColor : defaults.color;
                defaults.tunedIntensity = Mathf.Approximately(effect.headlightIntensityMultiplier, 1f)
                    ? defaults.intensity
                    : Mathf.Clamp(
                        defaults.intensity * effect.headlightIntensityMultiplier,
                        0f,
                        Mathf.Max(defaults.intensity * 2.5f, defaults.intensity + 0.01f));
                defaults.tunedRange = Mathf.Approximately(effect.headlightRangeMultiplier, 1f)
                    ? defaults.range
                    : Mathf.Clamp(
                        defaults.range * effect.headlightRangeMultiplier,
                        0f,
                        Mathf.Max(defaults.range * 2.0f, defaults.range + 0.01f));
                defaults.tunedSpotAngle = Mathf.Approximately(effect.headlightSpotAngleMultiplier, 1f)
                    ? defaults.spotAngle
                    : Mathf.Clamp(
                        defaults.spotAngle * effect.headlightSpotAngleMultiplier,
                        10f,
                        160f);
                defaults.tunedLocalRotation = Mathf.Approximately(pitch, 0f)
                    ? defaults.localRotation
                    : defaults.localRotation * Quaternion.Euler(pitch, 0f, 0f);
                defaults.hasTunedValues = true;
                defaults.light.color = defaults.tunedColor;
                defaults.light.intensity = defaults.tunedIntensity;
                defaults.light.range = defaults.tunedRange;
                defaults.light.spotAngle = defaults.tunedSpotAngle;
                defaults.light.transform.localRotation = defaults.tunedLocalRotation;
                if (defaults.nativeHeadlight != null && NativeHeadlightBaseIntensityField != null)
                    NativeHeadlightBaseIntensityField.SetValue(defaults.nativeHeadlight, defaults.tunedIntensity);

                if (profile != null && profile.headlightEnabled.HasValue)
                {
                    defaults.light.enabled = profile.headlightEnabled.Value &&
                                             (ActiveController == null || ActiveController.isEngineOn);
                }
            }

            SetActiveHeadlightsEnabled(nativeSwitchEnabled);
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

            if (defaults.hasTrackSpeedGyroMultiplier &&
                !Mathf.Approximately(effect.trackSpeedGyroMultiplier, 1f))
                SetFieldValue(stabilizer, "trackSpeedGyroMultiplier", ClampRelative(defaults.trackSpeedGyroMultiplier * effect.trackSpeedGyroMultiplier, defaults.trackSpeedGyroMultiplier, 0.60f, 1.50f, 0.01f, 10f));
        }

        private void CaptureNativePhysicsDefaultsForActiveController(bool force = false)
        {
            if (ActiveController == null)
            {
                _activeNativePhysicsDefaults = null;
                _nativePhysicsDefaultsControllerId = int.MinValue;
                return;
            }

            int controllerId = ActiveController.GetInstanceID();
            if (!force &&
                _activeNativePhysicsDefaults != null &&
                _nativePhysicsDefaultsControllerId == controllerId &&
                ReferenceEquals(_activeNativePhysicsDefaults.controller, ActiveController))
            {
                return;
            }

            var captured = new RuntimeNativePhysicsDefaults
            {
                controller = ActiveController
            };

            foreach (Component mesh in SleddersGameBindings.GetMeshInterpreters(ActiveController))
            {
                CaptureNativePhysicsField(captured, mesh, "powerEfficiency", NativePhysicsValueKind.PowerEfficiency);
                CaptureNativePhysicsField(captured, mesh, "drivetrainMinSpeed", NativePhysicsValueKind.DrivetrainSpeed);
                CaptureNativePhysicsField(captured, mesh, "drivetrainMaxSpeed1", NativePhysicsValueKind.DrivetrainSpeed);
                CaptureNativePhysicsField(captured, mesh, "drivetrainMaxSpeed2", NativePhysicsValueKind.DrivetrainSpeed);
                CaptureNativePhysicsField(captured, mesh, "trackMass", NativePhysicsValueKind.TrackMass);
                CaptureNativePhysicsField(captured, mesh, "breakForce", NativePhysicsValueKind.BrakeForce);
            }

            foreach (Component suspension in SleddersGameBindings.GetSuspensionControllers(ActiveController))
            {
                CaptureNativePhysicsField(captured, suspension, "antiRollBarFactor", NativePhysicsValueKind.AntiRollBar);
                CaptureNativePhysicsField(captured, suspension, "trackRigidityFront", NativePhysicsValueKind.TrackRigidityFront);
                CaptureNativePhysicsField(captured, suspension, "trackRigidityRear", NativePhysicsValueKind.TrackRigidityRear);
                CaptureNativeShock(captured, suspension, "frontSuspension", true);
                CaptureNativeShock(captured, suspension, "rearSuspension", false);
            }

            Component controllerBase = SleddersGameBindings.GetSnowmobileControllerBase(ActiveController);
            if (controllerBase != null)
            {
                CaptureNativePhysicsField(captured, controllerBase, "skisMaxAngle", NativePhysicsValueKind.SkisMaxAngle);
                CaptureNativePhysicsField(captured, controllerBase, "toeAngle", NativePhysicsValueKind.ToeAngle);

                Component leftSki = SleddersGameBindings.GetControllerBaseSki(controllerBase, true);
                Component rightSki = SleddersGameBindings.GetControllerBaseSki(controllerBase, false);
                CaptureNativePhysicsField(captured, leftSki, "camberFactor", NativePhysicsValueKind.LeftCamberFactor);
                CaptureNativePhysicsField(captured, rightSki, "camberFactor", NativePhysicsValueKind.RightCamberFactor);
            }

            foreach (Component skiContactBase in SleddersGameBindings.GetSkiHardSurfaceContactBases(ActiveController))
                CaptureNativePhysicsField(captured, skiContactBase, "grip", NativePhysicsValueKind.SkiGrip);

            foreach (Component trackContactBase in SleddersGameBindings.GetTrackHardSurfaceContactBases(ActiveController))
                CaptureNativePhysicsField(captured, trackContactBase, "grip", NativePhysicsValueKind.TrackGrip);

            _activeNativePhysicsDefaults = captured;
            _nativePhysicsDefaultsControllerId = controllerId;

            if (captured.fields.Count > 0)
            {
                MelonLogger.Msg(
                    $"Captured {captured.fields.Count} native drivetrain/handling values.");
            }
            else
            {
                MelonLogger.Warning(
                    "Native drivetrain/handling defaults were unavailable; only verified fallback tuning remains active.");
            }
        }

        private static void CaptureNativeShock(
            RuntimeNativePhysicsDefaults captured,
            object suspension,
            string fieldName,
            bool front)
        {
            object shock = GetFieldValue<object>(suspension, fieldName);
            if (!IsNativePhysicsTargetAlive(shock))
                return;

            CaptureNativeShockSettings(captured, shock, "soft", front);
            CaptureNativeShockSettings(captured, shock, "hard", front);
        }

        private void CaptureNativePhysicsPreviewDefaults(SledDefaults defaults)
        {
            if (defaults == null || _activeNativePhysicsDefaults == null)
                return;

            var preview = new NativePhysicsDefaults();
            SetPreviewValue(preview, "powerEfficiency", NativePhysicsValueKind.PowerEfficiency, "powerEfficiency");
            SetPreviewValue(preview, "drivetrainMinSpeed", NativePhysicsValueKind.DrivetrainSpeed, "drivetrainMinSpeed");
            SetPreviewValue(preview, "drivetrainMaxSpeed1", NativePhysicsValueKind.DrivetrainSpeed, "drivetrainMaxSpeed1");
            SetPreviewValue(preview, "drivetrainMaxSpeed2", NativePhysicsValueKind.DrivetrainSpeed, "drivetrainMaxSpeed2");
            SetPreviewValue(preview, "trackMass", NativePhysicsValueKind.TrackMass, "trackMass");
            SetPreviewValue(preview, "brakeForce", NativePhysicsValueKind.BrakeForce, "breakForce");
            SetPreviewValue(preview, "antiRollBar", NativePhysicsValueKind.AntiRollBar, null);
            SetPreviewValue(preview, "trackRigidityFront", NativePhysicsValueKind.TrackRigidityFront, null);
            SetPreviewValue(preview, "trackRigidityRear", NativePhysicsValueKind.TrackRigidityRear, null);
            SetPreviewValue(preview, "frontSpring", NativePhysicsValueKind.FrontSpring, null);
            SetPreviewValue(preview, "frontDamper", NativePhysicsValueKind.FrontDamper, null);
            SetPreviewValue(preview, "frontCompressionDamping", NativePhysicsValueKind.FrontCompressionDamping, null);
            SetPreviewValue(preview, "frontReboundDamping", NativePhysicsValueKind.FrontReboundDamping, null);
            SetPreviewValue(preview, "rearSpring", NativePhysicsValueKind.RearSpring, null);
            SetPreviewValue(preview, "rearDamper", NativePhysicsValueKind.RearDamper, null);
            SetPreviewValue(preview, "rearCompressionDamping", NativePhysicsValueKind.RearCompressionDamping, null);
            SetPreviewValue(preview, "rearReboundDamping", NativePhysicsValueKind.RearReboundDamping, null);
            SetPreviewValue(preview, "skisMaxAngle", NativePhysicsValueKind.SkisMaxAngle, null);
            SetPreviewValue(preview, "toeAngle", NativePhysicsValueKind.ToeAngle, null);
            SetPreviewValue(preview, "leftCamberFactor", NativePhysicsValueKind.LeftCamberFactor, null);
            SetPreviewValue(preview, "rightCamberFactor", NativePhysicsValueKind.RightCamberFactor, null);
            SetPreviewValue(preview, "skiGrip", NativePhysicsValueKind.SkiGrip, null);
            SetPreviewValue(preview, "trackGrip", NativePhysicsValueKind.TrackGrip, null);
            defaults.nativePhysics = preview;
        }

        private void SetPreviewValue(
            NativePhysicsDefaults preview,
            string valueName,
            NativePhysicsValueKind kind,
            string fieldName)
        {
            if (preview == null || !TryMeanNativePhysicsValue(kind, fieldName, out float value))
                return;

            // Keep the serialized cache explicit and simple for UI/dyno readers.
            switch (valueName)
            {
                case "powerEfficiency": preview.hasPowerEfficiency = true; preview.powerEfficiency = value; break;
                case "drivetrainMinSpeed": preview.hasDrivetrainMinSpeed = true; preview.drivetrainMinSpeed = value; break;
                case "drivetrainMaxSpeed1": preview.hasDrivetrainMaxSpeed1 = true; preview.drivetrainMaxSpeed1 = value; break;
                case "drivetrainMaxSpeed2": preview.hasDrivetrainMaxSpeed2 = true; preview.drivetrainMaxSpeed2 = value; break;
                case "trackMass": preview.hasTrackMass = true; preview.trackMass = value; break;
                case "brakeForce": preview.hasBrakeForce = true; preview.brakeForce = value; break;
                case "antiRollBar": preview.hasAntiRollBar = true; preview.antiRollBar = value; break;
                case "trackRigidityFront": preview.hasTrackRigidityFront = true; preview.trackRigidityFront = value; break;
                case "trackRigidityRear": preview.hasTrackRigidityRear = true; preview.trackRigidityRear = value; break;
                case "frontSpring": preview.hasFrontSpring = true; preview.frontSpring = value; break;
                case "frontDamper": preview.hasFrontDamper = true; preview.frontDamper = value; break;
                case "frontCompressionDamping": preview.hasFrontCompressionDamping = true; preview.frontCompressionDamping = value; break;
                case "frontReboundDamping": preview.hasFrontReboundDamping = true; preview.frontReboundDamping = value; break;
                case "rearSpring": preview.hasRearSpring = true; preview.rearSpring = value; break;
                case "rearDamper": preview.hasRearDamper = true; preview.rearDamper = value; break;
                case "rearCompressionDamping": preview.hasRearCompressionDamping = true; preview.rearCompressionDamping = value; break;
                case "rearReboundDamping": preview.hasRearReboundDamping = true; preview.rearReboundDamping = value; break;
                case "skisMaxAngle": preview.hasSkisMaxAngle = true; preview.skisMaxAngle = value; break;
                case "toeAngle": preview.hasToeAngle = true; preview.toeAngle = value; break;
                case "leftCamberFactor": preview.hasLeftCamberFactor = true; preview.leftCamberFactor = value; break;
                case "rightCamberFactor": preview.hasRightCamberFactor = true; preview.rightCamberFactor = value; break;
                case "skiGrip": preview.hasSkiGrip = true; preview.skiGrip = value; break;
                case "trackGrip": preview.hasTrackGrip = true; preview.trackGrip = value; break;
            }
        }

        private bool TryMeanNativePhysicsValue(
            NativePhysicsValueKind kind,
            string fieldName,
            out float value)
        {
            value = 0f;
            if (_activeNativePhysicsDefaults == null)
                return false;

            double total = 0d;
            int count = 0;
            foreach (RuntimeNativePhysicsField field in _activeNativePhysicsDefaults.fields)
            {
                if (field == null || field.kind != kind ||
                    (!string.IsNullOrEmpty(fieldName) &&
                     !string.Equals(field.fieldName, fieldName, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (double.IsNaN(field.value) || double.IsInfinity(field.value))
                    continue;

                total += field.value;
                count++;
            }

            if (count == 0)
                return false;

            double mean = total / count;
            if (mean < -float.MaxValue || mean > float.MaxValue)
                return false;

            value = (float)mean;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void CaptureNativeShockSettings(
            RuntimeNativePhysicsDefaults captured,
            object shock,
            string fieldName,
            bool front)
        {
            object settings = GetFieldValue<object>(shock, fieldName);
            if (!IsNativePhysicsTargetAlive(settings))
                return;

            CaptureNativePhysicsField(
                captured,
                settings,
                "springFactor",
                front ? NativePhysicsValueKind.FrontSpring : NativePhysicsValueKind.RearSpring);
            CaptureNativePhysicsField(
                captured,
                settings,
                "damperFactor",
                front ? NativePhysicsValueKind.FrontDamper : NativePhysicsValueKind.RearDamper);
            CaptureNativePhysicsField(
                captured,
                settings,
                "compressionRatio",
                front ? NativePhysicsValueKind.FrontCompressionDamping : NativePhysicsValueKind.RearCompressionDamping);
            CaptureNativePhysicsField(
                captured,
                settings,
                "compressionFastRatio",
                front ? NativePhysicsValueKind.FrontCompressionDamping : NativePhysicsValueKind.RearCompressionDamping);
            CaptureNativePhysicsField(
                captured,
                settings,
                "reboundRatio",
                front ? NativePhysicsValueKind.FrontReboundDamping : NativePhysicsValueKind.RearReboundDamping);
            CaptureNativePhysicsField(
                captured,
                settings,
                "reboundFastRatio",
                front ? NativePhysicsValueKind.FrontReboundDamping : NativePhysicsValueKind.RearReboundDamping);
        }

        private static void CaptureNativePhysicsField(
            RuntimeNativePhysicsDefaults captured,
            object target,
            string fieldName,
            NativePhysicsValueKind kind)
        {
            if (captured == null || !IsNativePhysicsTargetAlive(target))
                return;

            if (captured.fields.Any(existing =>
                    ReferenceEquals(existing.target, target) &&
                    string.Equals(existing.fieldName, fieldName, StringComparison.Ordinal)))
            {
                return;
            }

            if (!SleddersGameBindings.TryGetNumericField(target, fieldName, out var value))
                return;

            captured.fields.Add(new RuntimeNativePhysicsField
            {
                target = target,
                fieldName = fieldName,
                value = value,
                kind = kind
            });
        }

        private void RestoreNativePhysicsDefaults()
        {
            CaptureNativePhysicsDefaultsForActiveController();
            RestoreCapturedNativePhysicsDefaults();
        }

        private void RestoreCapturedNativePhysicsDefaults()
        {
            if (_activeNativePhysicsDefaults == null)
                return;

            foreach (RuntimeNativePhysicsField field in _activeNativePhysicsDefaults.fields)
            {
                if (field == null || !IsNativePhysicsTargetAlive(field.target))
                    continue;

                SleddersGameBindings.SetNumericField(field.target, field.fieldName, field.value);
            }
        }

        private void ApplyNativePhysicsRuntime(PartEffect effect)
        {
            if (effect == null)
                return;

            RestoreNativePhysicsDefaults();
            if (_activeNativePhysicsDefaults == null)
                return;

            foreach (IGrouping<NativePhysicsSubsystem, RuntimeNativePhysicsField> subsystem in
                     _activeNativePhysicsDefaults.fields
                         .Where(field => field != null)
                         .GroupBy(field => NativePhysicsSubsystemFor(field.kind)))
            {
                bool subsystemFailed = false;
                foreach (RuntimeNativePhysicsField field in subsystem)
                {
                    if (!IsNativePhysicsTargetAlive(field.target))
                        continue;

                    float multiplier = NativePhysicsMultiplier(effect, field.kind);
                    double tunedValue = ScaleNativePhysicsValue(field.value, multiplier, field.kind);
                    if (!SleddersGameBindings.SetNumericField(field.target, field.fieldName, tunedValue))
                    {
                        subsystemFailed = true;
                        break;
                    }
                }

                if (!subsystemFailed)
                    continue;

                foreach (RuntimeNativePhysicsField field in subsystem)
                {
                    if (IsNativePhysicsTargetAlive(field.target))
                        SleddersGameBindings.SetNumericField(field.target, field.fieldName, field.value);
                }
                MelonLogger.Warning(
                    $"Native {subsystem.Key.ToString().ToLowerInvariant()} tuning was rolled back because one captured field became unavailable.");
            }
        }

        internal static NativePhysicsSubsystem NativePhysicsSubsystemFor(NativePhysicsValueKind kind)
        {
            switch (kind)
            {
                case NativePhysicsValueKind.BrakeForce:
                    return NativePhysicsSubsystem.Brake;
                case NativePhysicsValueKind.AntiRollBar:
                case NativePhysicsValueKind.TrackRigidityFront:
                case NativePhysicsValueKind.TrackRigidityRear:
                case NativePhysicsValueKind.FrontSpring:
                case NativePhysicsValueKind.FrontDamper:
                case NativePhysicsValueKind.FrontCompressionDamping:
                case NativePhysicsValueKind.FrontReboundDamping:
                case NativePhysicsValueKind.RearSpring:
                case NativePhysicsValueKind.RearDamper:
                case NativePhysicsValueKind.RearCompressionDamping:
                case NativePhysicsValueKind.RearReboundDamping:
                    return NativePhysicsSubsystem.Suspension;
                case NativePhysicsValueKind.SkisMaxAngle:
                case NativePhysicsValueKind.ToeAngle:
                case NativePhysicsValueKind.LeftCamberFactor:
                case NativePhysicsValueKind.RightCamberFactor:
                    return NativePhysicsSubsystem.Steering;
                case NativePhysicsValueKind.SkiGrip:
                    return NativePhysicsSubsystem.SkiGrip;
                case NativePhysicsValueKind.TrackGrip:
                    return NativePhysicsSubsystem.TrackGrip;
                default:
                    return NativePhysicsSubsystem.Drivetrain;
            }
        }

        private static float NativePhysicsMultiplier(PartEffect effect, NativePhysicsValueKind kind)
        {
            switch (kind)
            {
                case NativePhysicsValueKind.PowerEfficiency:
                    return effect.nativePowerEfficiencyMultiplier;
                case NativePhysicsValueKind.DrivetrainSpeed:
                    return effect.nativeDrivetrainSpeedMultiplier;
                case NativePhysicsValueKind.TrackMass:
                    return effect.nativeTrackMassMultiplier;
                case NativePhysicsValueKind.AntiRollBar:
                    return effect.nativeAntiRollBarMultiplier;
                case NativePhysicsValueKind.TrackRigidityFront:
                    return effect.nativeTrackRigidityFrontMultiplier;
                case NativePhysicsValueKind.TrackRigidityRear:
                    return effect.nativeTrackRigidityRearMultiplier;
                case NativePhysicsValueKind.FrontSpring:
                    return effect.nativeFrontSpringMultiplier;
                case NativePhysicsValueKind.FrontDamper:
                    return effect.nativeFrontDamperMultiplier;
                case NativePhysicsValueKind.FrontCompressionDamping:
                    return effect.nativeFrontCompressionDampingMultiplier;
                case NativePhysicsValueKind.FrontReboundDamping:
                    return effect.nativeFrontReboundDampingMultiplier;
                case NativePhysicsValueKind.RearSpring:
                    return effect.nativeRearSpringMultiplier;
                case NativePhysicsValueKind.RearDamper:
                    return effect.nativeRearDamperMultiplier;
                case NativePhysicsValueKind.RearCompressionDamping:
                    return effect.nativeRearCompressionDampingMultiplier;
                case NativePhysicsValueKind.RearReboundDamping:
                    return effect.nativeRearReboundDampingMultiplier;
                case NativePhysicsValueKind.BrakeForce:
                    return effect.nativeBrakeForceMultiplier;
                case NativePhysicsValueKind.SkisMaxAngle:
                    return effect.nativeSkisMaxAngleMultiplier;
                case NativePhysicsValueKind.ToeAngle:
                    return effect.nativeToeAngleMultiplier;
                case NativePhysicsValueKind.LeftCamberFactor:
                case NativePhysicsValueKind.RightCamberFactor:
                    return effect.nativeCamberFactorMultiplier;
                case NativePhysicsValueKind.SkiGrip:
                    return effect.nativeSkiGripMultiplier;
                case NativePhysicsValueKind.TrackGrip:
                    return effect.nativeTrackGripMultiplier;
                default:
                    return 1f;
            }
        }

        internal static double ScaleNativePhysicsValue(
            double baseline,
            float multiplier,
            NativePhysicsValueKind kind)
        {
            if (double.IsNaN(baseline) || double.IsInfinity(baseline) ||
                float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
                Math.Abs(baseline) < 0.0000001d)
            {
                return baseline;
            }

            float minimumMultiplier;
            float maximumMultiplier;
            switch (kind)
            {
                case NativePhysicsValueKind.PowerEfficiency:
                case NativePhysicsValueKind.DrivetrainSpeed:
                    minimumMultiplier = 0.75f;
                    maximumMultiplier = 1.25f;
                    break;
                case NativePhysicsValueKind.TrackMass:
                    minimumMultiplier = 0.75f;
                    maximumMultiplier = 1.35f;
                    break;
                case NativePhysicsValueKind.BrakeForce:
                    minimumMultiplier = 0.80f;
                    maximumMultiplier = 1.20f;
                    break;
                case NativePhysicsValueKind.SkisMaxAngle:
                    minimumMultiplier = 0.90f;
                    maximumMultiplier = 1.10f;
                    break;
                case NativePhysicsValueKind.ToeAngle:
                    minimumMultiplier = 0.70f;
                    maximumMultiplier = 1.30f;
                    break;
                case NativePhysicsValueKind.LeftCamberFactor:
                case NativePhysicsValueKind.RightCamberFactor:
                    minimumMultiplier = 0.80f;
                    maximumMultiplier = 1.20f;
                    break;
                case NativePhysicsValueKind.SkiGrip:
                case NativePhysicsValueKind.TrackGrip:
                    minimumMultiplier = 0.75f;
                    maximumMultiplier = 1.35f;
                    break;
                case NativePhysicsValueKind.AntiRollBar:
                case NativePhysicsValueKind.TrackRigidityFront:
                case NativePhysicsValueKind.TrackRigidityRear:
                    minimumMultiplier = 0.60f;
                    maximumMultiplier = 1.60f;
                    break;
                default:
                    minimumMultiplier = 0.65f;
                    maximumMultiplier = 1.50f;
                    break;
            }

            return baseline * Mathf.Clamp(multiplier, minimumMultiplier, maximumMultiplier);
        }

        private static bool IsNativePhysicsTargetAlive(object target)
        {
            if (target == null)
                return false;

            if (target is UnityEngine.Object unityObject)
                return unityObject != null;

            return true;
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
            CaptureNativePhysicsPreviewDefaults(defaults);
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

            CaptureAccessoryDefaultsForActiveController(false);
            if (string.IsNullOrWhiteSpace(accessoryMode) ||
                string.Equals(accessoryMode, "stock", StringComparison.OrdinalIgnoreCase))
            {
                RestoreAccessoryDefaults();
                return;
            }

            try
            {
                var components = SleddersGameBindings.GetSnowmobileAccessories(ActiveController);
                if (components == null || components.Length == 0)
                    return;

                bool utility = accessoryMode == "utility";
                bool raceTrim = accessoryMode == "race_trim";

                if (utility || raceTrim)
                {
                    foreach (object accessories in components)
                    {
                        if (accessories == null)
                            continue;

                        SetGameObjectListActive(accessories, "windshieldObjects", utility);
                        SetGameObjectListActive(accessories, "snowFlapObjects", utility);
                        SetGameObjectListActive(accessories, "rearPartObjects", utility);
                        SetGameObjectListActive(accessories, "tunnelReflectors", utility);
                    }
                    return;
                }

                // Unknown accessory modes are ignored so vanilla customization is preserved.
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Accessory mode apply skipped: {ex.GetType().Name}");
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

        private static VehicleScriptableObject VehicleFromLocalInitArgs(object[] args)
        {
            if (args == null)
                return null;

            foreach (object argument in args)
            {
                if (argument is VehicleScriptableObject direct)
                    return direct;

                VehicleScriptableObject enveloped =
                    SleddersGameBindings.GetFieldValue<VehicleScriptableObject>(argument, "KJFNKMCOKLL");
                if (enveloped != null)
                    return enveloped;
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

        private void OnLocalSledInitializing(
            SnowmobileController controller,
            VehicleScriptableObject sled)
        {
            if (_shutdownComplete || sled == null || Store == null || Catalog == null)
                return;

            RegisterSelectableSled(sled, "local sled pre-initialization");
            TryBuildDefaults();
            RefreshStatDefaultsFromCleanLoad(sled);
            EnsureDefaultsForSled(sled);

            TuneProfile preserved =
                Store.GetCurrentSetupForSled(GetSledKey(sled), GetVehicleId(sled)) ??
                Store.GetActiveProfileForSled(GetSledKey(sled), GetVehicleId(sled));
            if (preserved == null)
                return;

            // LocalInit copies VehicleScriptableObject values into the native body,
            // track, skis and audio graph. Put the preserved setup on that source
            // object before the copy occurs; the postfix then captures the newly
            // initialized runtime baselines and applies live-only controller fields.
            TuneProfile profile = TuneStore.Clone(preserved);
            Catalog.EnsureProfileSelections(profile);
            TuneComputation computation = ComputeProfile(profile, sled);
            if (computation == null || computation.stats == null)
            {
                MelonLogger.Warning(computation?.unavailableReason ??
                                    "Preserved Alpine setup could not be resolved before native initialization.");
                return;
            }
            ApplyDefaultsToSled(sled, computation.baseDefaults);
            ApplyStatsToSled(sled, computation);
            ApplyEngineAudioToSled(sled, computation.audioDefaults, computation.audioSource);
            MarkSledModifiedByAlpine(sled);

            MelonLogger.Msg(
                $"Prepared preserved Alpine setup before native initialization for {sled.name}.");
        }

        private void OnLocalSledInitialized(SnowmobileController controller, Vector3 spawnPos, Quaternion spawnRot)
        {
            if (_shutdownComplete)
                return;

            // LocalInit can announce a replacement before Unity destroys the old
            // controller. Put every per-object Alpine mutation back first so the
            // abandoned live graph cannot retain brake, geometry, grip, lighting,
            // or accessory changes.
            if (ActiveController != null && ActiveController != controller)
            {
                RestoreNativePhysicsDefaults();
                ApplyHeadlightDefaults();
                RestoreAccessoryDefaults();
            }

            _activeHeadlightOverride = null;
            ActiveController = controller;
            ActiveSO = GetVehicleFromController(controller);
            _activeSpawnValues = SpawnValueSignature.FromSled(ActiveSO);
            _spawnValuesControllerId = controller != null
                ? controller.GetInstanceID()
                : int.MinValue;
            ActiveRespawn = controller != null ? controller.GetComponent<Respawnable>() : null;
            ActiveSpawnPos = spawnPos;
            ActiveSpawnRot = spawnRot;
            CaptureHeadlightDefaultsForActiveController(true);
            CaptureAccessoryDefaultsForActiveController(true);
            // ReCreateSnowmobile can rebuild the controller's child physics graph
            // while retaining the same controller instance. LocalInit is the
            // authoritative lifecycle boundary, so never reuse component targets
            // captured before this initialization.
            CaptureNativePhysicsDefaultsForActiveController(true);

            if (ActiveSO == null)
            {
                MelonLogger.Warning("LocalInit detected no VehicleScriptableObject.");
                return;
            }

            TryBuildDefaults();
            RegisterSelectableSled(ActiveSO, "local sled initialization");
            RefreshStatDefaultsFromCleanLoad(ActiveSO);
            // LocalInit has just built a fresh native controller graph. Capture
            // that exact controller/stabilizer baseline even though the prefix
            // temporarily marked the source VSO as Alpine-modified.
            EnsureDefaultsForSled(ActiveSO, true);
            bool appliedActive = TryApplyActiveProfileForCurrentSled();
            if (!appliedActive)
                NotifyActiveTuneCleared(ActiveSO);
            MelonLogger.Msg($"Detected local sled '{ActiveSO.name}' for Alpine Tuning {AlpineConstants.ModVersion}.");
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

            var defaults = Store.GetDefaults(GetSledKey(ActiveSO), GetVehicleId(ActiveSO));
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

            int vehicleControllerId = ActiveController != null
                ? ActiveController.GetInstanceID()
                : int.MinValue;
            bool sameAsApplied =
                vehicleControllerId != int.MinValue &&
                vehicleControllerId == _lastAppliedEngineAudioVehicleControllerId &&
                string.Equals(audioDefaults.engineAudioEnumType, _lastAppliedEngineAudioEnumType, StringComparison.Ordinal) &&
                string.Equals(audioDefaults.engineAudioEnumName, _lastAppliedEngineAudioEnumName, StringComparison.Ordinal) &&
                audioDefaults.engineAudioEnumRawValue == _lastAppliedEngineAudioEnumRawValue;
            bool alreadyQueued =
                _pendingEngineAudioApply &&
                string.Equals(audioDefaults.engineAudioEnumType, _pendingEngineAudioEnumType, StringComparison.Ordinal) &&
                string.Equals(audioDefaults.engineAudioEnumName, _pendingEngineAudioEnumName, StringComparison.Ordinal) &&
                audioDefaults.engineAudioEnumRawValue == _pendingEngineAudioEnumRawValue;

            if (sameAsApplied || alreadyQueued)
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

                _lastAppliedEngineAudioVehicleControllerId = ActiveController != null
                    ? ActiveController.GetInstanceID()
                    : int.MinValue;
                _lastAppliedEngineAudioEnumType = _pendingEngineAudioEnumType;
                _lastAppliedEngineAudioEnumName = _pendingEngineAudioEnumName;
                _lastAppliedEngineAudioEnumRawValue = _pendingEngineAudioEnumRawValue;
                _pendingEngineAudioApply = false;
            }
            catch (Exception ex)
            {
                _pendingEngineAudioApply = false;
                MelonLogger.Warning($"Engine audio swap failed: {ex.GetType().Name}");
            }
        }

        [HarmonyPatch(typeof(VehicleSelectionUiController), "Close")]
        private static class PatchGarageClose
        {
            public static bool Prefix(VehicleSelectionUiController __instance)
            {
                return AlpineNativeUi.AllowGarageControllerClose(__instance);
            }
        }

        [HarmonyPatch]
        private static class PatchGarageSelectionItem
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
                    MelonLogger.Warning($"Garage selection hook skipped: {ex.GetType().Name}");
                }
            }
        }

        [HarmonyPatch]
        private static class PatchGaragePreviewStats
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
                    MelonLogger.Warning($"Garage preview hook skipped: {ex.GetType().Name}");
                }
            }
        }

        [HarmonyPatch]
        private static class PatchGarageCustomizationRebuild
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
                    MelonLogger.Warning($"Garage customization hook skipped: {ex.GetType().Name}");
                }
            }
        }

        [HarmonyPatch(typeof(SnowmobileController), "LocalInit")]
        private static class PatchLocalInit
        {
            public static void Prefix(SnowmobileController __instance, object[] __args)
            {
                try
                {
                    Instance?.OnLocalSledInitializing(
                        __instance,
                        VehicleFromLocalInitArgs(__args));
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Alpine LocalInit preparation failed: {ex.GetType().Name}");
                }
            }

            public static void Postfix(SnowmobileController __instance, Vector3 KMFHFHOFBFH, Quaternion LPNJFGKBIIC)
            {
                try
                {
                    Instance?.OnLocalSledInitialized(__instance, KMFHFHOFBFH, LPNJFGKBIIC);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Alpine LocalInit patch failed: {ex.GetType().Name}");
                }
            }
        }

    }
}
