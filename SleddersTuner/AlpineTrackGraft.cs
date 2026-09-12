using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AlpineTuning
{
    internal enum TrackGraftMode
    {
        DirectGraft,
        EmbeddedVariant,
        ScaledFallback
    }

    internal sealed class TrackCompatibilitySignature
    {
        public Vector3 seamCenter;
        public Vector3 forward = Vector3.forward;
        public Vector3 up = Vector3.up;
        public float tunnelWidth;
        public Vector3 trackCenter;
        public Bounds rearBounds;
        public float trackLength;
        public bool animatedTrack;
        public bool contactMesh;
        public bool rearGraph;
    }

    internal sealed class TrackCompatibilityResult
    {
        public bool compatible;
        public string reason;
        public float score;
        public Vector3 alignment;
        public Quaternion rotation = Quaternion.identity;
    }

    /// <summary>
    /// Owns the addressable donor and every live object mutation made by a track
    /// length kit. It deliberately has no code path that writes a
    /// VehicleScriptableObject or changes its assetReference.
    /// </summary>
    internal sealed class AlpineTrackGraft
    {
        private const float TimeoutSeconds = 15f;
        // These are used only by the clearly labelled experimental fallback. The
        // front-anchored scaler does not alter the source chassis or physics graph.
        // 0.55-1.65 covers the native 120-174 inch range without admitting props or
        // malformed addressables as rear-assembly donors.
        private const float MinimumFallbackScale = 0.55f;
        private const float MaximumFallbackScale = 1.65f;
        private static readonly Regex ArcticLengthToken = new Regex(
            @"(?<!\d)(146|154|165)(?!\d)",
            RegexOptions.CultureInvariant);

        private readonly AlpineTuningMod _mod;
        private AlpineVisualPartSystem.NativeChassisVariant _requested;
        private AlpineVisualPartSystem.NativeChassisVariant _installed;
        private VehicleScriptableObject _target;
        private GraftSnapshot _runtimeSnapshot;
        private LoadState _state;
        private float _deadline;
        private bool _recoveryReload;
        private AsyncOperationHandle<GameObject> _runtimeHandle;
        private bool _runtimeHandleValid;
        private GameObject _runtimeHost;
        private SnowmobileStructure _runtimeSource;
        private SnowmobileController _runtimeController;
        private object _runtimeSelection;
        private AlpineVisualPartSystem.NativeChassisVariant _runtimeCandidate;
        private int _runtimeCandidateIndex;

        private PreviewRequest _preview;
        private GraftSnapshot _previewSnapshot;
        private int _previewRevision;
        private readonly List<AsyncOperationHandle<GameObject>> _abandoned =
            new List<AsyncOperationHandle<GameObject>>();
        private CompatibilityScan _scan;

        private enum LoadState
        {
            None,
            WaitingForReload,
            Loading,
            Installing,
            Installed,
            Failed
        }

        private sealed class TrackAssemblyDescriptor
        {
            public GameObject root;
            public SnowmobileStructure structure;
            public TrackRenderer trackRenderer;
            public TrackToSkinnedMesh track;
            public RearAxelController rearAxel;
            public SnowMesh[] trackMeshes;
            public Transform[] otherTrackObjects;
            public TraxGroup traxGroup;
            public Transform traxBody;
            public readonly HashSet<Renderer> renderers = new HashSet<Renderer>();
            public TrackCompatibilitySignature signature;
            public TrackCompatibilityResult compatibility;
            public TrackGraftMode mode;
        }

        private sealed class GraftSnapshot
        {
            public VehicleScriptableObject target;
            public string variantId;
            public SnowmobileStructure source;
            public TrackRenderer trackRenderer;
            public SnowMesh[] trackMeshes;
            public Transform[] otherTrackObjects;
            public TraxGroup traxGroup;
            public Transform traxBody;
            public Component suspension;
            public object rearAxel;
            public MeshInterpretter meshInterpretter;
            public SnowMesh contactMesh;
            public GameObject generatedContactObject;
            public Mesh generatedContactMesh;
            public AsyncOperationHandle<GameObject> handle;
            public bool handleValid;
            public GameObject host;
            public readonly List<RendererState> renderers = new List<RendererState>();
            public readonly List<BehaviourState> behaviours = new List<BehaviourState>();
            public readonly List<ObjectState> objects = new List<ObjectState>();
            public readonly List<TransformState> transforms = new List<TransformState>();
        }

        private sealed class RendererState { public Renderer value; public bool enabled; }
        private sealed class BehaviourState { public Behaviour value; public bool enabled; }
        private sealed class ObjectState { public GameObject value; public bool active; }
        private sealed class TransformState
        {
            public Transform value;
            public Transform parent;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private sealed class PreviewRequest
        {
            public int revision;
            public VehicleScriptableObject target;
            public AlpineVisualPartSystem.NativeChassisVariant variant;
            public int previousSourceId;
            public object selection;
            public Transform previewRoot;
            public SnowmobileStructure source;
            public AsyncOperationHandle<GameObject> handle;
            public bool handleValid;
            public GameObject host;
            public int phase;
            public float deadline;
            public Action<bool, string> completed;
            public AlpineVisualPartSystem.NativeChassisVariant candidate;
            public int candidateIndex;
        }

        private sealed class CompatibilityScan
        {
            public int revision;
            public VehicleScriptableObject target;
            public List<AlpineVisualPartSystem.NativeChassisVariant> candidates;
            public readonly List<AlpineVisualPartSystem.NativeChassisVariant> compatible =
                new List<AlpineVisualPartSystem.NativeChassisVariant>();
            public int index;
            public SnowmobileStructure source;
            public object selection;
            public GameObject host;
            public AsyncOperationHandle<GameObject> handle;
            public bool handleValid;
            public float deadline;
            public Action<IReadOnlyList<AlpineVisualPartSystem.NativeChassisVariant>> completed;
        }

        internal AlpineTrackGraft(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        internal bool HasPendingSwap =>
            _state == LoadState.WaitingForReload ||
            _state == LoadState.Loading ||
            _state == LoadState.Installing;

        internal bool NeedsSourceReload => _state == LoadState.WaitingForReload;

        internal bool HasInstalledSwap(VehicleScriptableObject target)
        {
            return target != null && _runtimeSnapshot?.target == target && _installed != null;
        }

        internal bool IsInstalledVariant(string variantId, VehicleScriptableObject target)
        {
            return HasInstalledSwap(target) &&
                   string.Equals(_installed.id, variantId, StringComparison.OrdinalIgnoreCase);
        }

        internal bool Stage(
            AlpineVisualPartSystem.NativeChassisVariant variant,
            VehicleScriptableObject target,
            out string reason)
        {
            reason = null;
            if (variant?.donor?.assetReference == null ||
                !variant.donor.assetReference.RuntimeKeyIsValid() || target == null)
            {
                reason = "Track donor addressable is unavailable.";
                return false;
            }
            if (IsInstalledVariant(variant.id, target))
                return true;
            if (_requested != null &&
                string.Equals(_requested.id, variant.id, StringComparison.OrdinalIgnoreCase) &&
                HasPendingSwap)
                return true;

            RestoreRuntime();
            _requested = variant;
            _target = target;
            _state = LoadState.WaitingForReload;
            _deadline = Time.unscaledTime + TimeoutSeconds;
            MelonLogger.Msg(
                $"Staged {variant.lengthInches:0.#}\" rear-assembly graft from " +
                $"'{AlpineTuningMod.GetSledDisplayName(variant.donor)}'; source prefab retained.");
            return true;
        }

        internal void OnControllerInitialized(SnowmobileController controller, VehicleScriptableObject sled)
        {
            // ReCreateSnowmobile may retain the controller while replacing its entire
            // child graph. Never let an in-flight completion target that stale graph;
            // carry the requested length forward and bind it to the fresh source.
            if (_state == LoadState.Loading || _state == LoadState.Installing)
            {
                AlpineVisualPartSystem.NativeChassisVariant pending = _requested;
                VehicleScriptableObject pendingTarget = _target;
                CancelRuntimeLoad();
                _requested = pending;
                _target = pendingTarget;
                _state = LoadState.WaitingForReload;
            }
            else if (_state == LoadState.Installed && _installed != null)
            {
                AlpineVisualPartSystem.NativeChassisVariant reinstall = _installed;
                VehicleScriptableObject installedTarget = _target;
                RestoreSnapshot(_runtimeSnapshot);
                _runtimeSnapshot = null;
                _installed = null;
                _requested = reinstall;
                _target = installedTarget;
                _state = LoadState.WaitingForReload;
            }
            if (_state != LoadState.WaitingForReload || _requested == null)
                return;
            if (controller == null || sled == null || _target == null ||
                !string.Equals(SledIdentity.StableIdentityKey(sled),
                    SledIdentity.StableIdentityKey(_target), StringComparison.OrdinalIgnoreCase))
            {
                Fail("source recreation returned another sled", false);
                return;
            }

            _runtimeSource = FindStructure(controller.transform);
            if (!HasAnimatedTrack(_runtimeSource))
            {
                Fail("source sled has no animated track graph", false);
                return;
            }
            _runtimeController = controller;
            _runtimeSelection = SleddersGameBindings.GetCurrentVehicleSelection(controller);
            _runtimeCandidateIndex = 0;
            if (!BeginRuntimeCandidate())
                Fail("no donor addressable could start loading", false);
        }

        private static AlpineVisualPartSystem.NativeChassisVariant CandidateAt(
            AlpineVisualPartSystem.NativeChassisVariant option,
            int index)
        {
            if (option == null) return null;
            IList<AlpineVisualPartSystem.NativeChassisVariant> candidates = option.candidates;
            if (candidates == null || candidates.Count == 0)
                return index == 0 ? option : null;
            return index >= 0 && index < candidates.Count ? candidates[index] : null;
        }

        private bool BeginRuntimeCandidate()
        {
            while ((_runtimeCandidate = CandidateAt(_requested, _runtimeCandidateIndex)) != null)
            {
                VehicleScriptableObject donor = _runtimeCandidate.donor;
                if (donor?.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
                {
                    _runtimeCandidateIndex++;
                    continue;
                }
                _runtimeHost = CreateHost("Alpine Runtime Rear Chassis", _runtimeSource);
                try
                {
                    _runtimeHandle = Addressables.InstantiateAsync(
                        donor.assetReference.RuntimeKey, _runtimeHost.transform, false, true);
                    _runtimeHandleValid = true;
                    _state = LoadState.Loading;
                    _deadline = Time.unscaledTime + TimeoutSeconds;
                    return true;
                }
                catch
                {
                    if (_runtimeHost != null) UnityEngine.Object.Destroy(_runtimeHost);
                    _runtimeHost = null;
                    _runtimeCandidateIndex++;
                }
            }
            _runtimeCandidate = null;
            return false;
        }

        private bool TryNextRuntimeCandidate(string rejectedReason)
        {
            if (_runtimeHandleValid)
            {
                if (_runtimeHandle.IsDone) Release(_runtimeHandle,
                    _runtimeHandle.Status == AsyncOperationStatus.Succeeded ? _runtimeHandle.Result : null);
                else _abandoned.Add(_runtimeHandle);
            }
            _runtimeHandleValid = false;
            if (_runtimeHost != null) UnityEngine.Object.Destroy(_runtimeHost);
            _runtimeHost = null;
            _runtimeCandidateIndex++;
            bool started = BeginRuntimeCandidate();
            if (started)
                MelonLogger.Msg("Trying the next rear-assembly donor after: " + rejectedReason);
            return started;
        }

        internal void Update()
        {
            CleanupAbandoned(false);
            PumpCompatibilityScan();
            PumpRuntime();
            PumpPreview();
            if (HasPendingSwap && Time.unscaledTime > _deadline)
                Fail("track graft timed out", true);
            if (_recoveryReload && AlpineTuningMod.ActiveController != null)
            {
                _recoveryReload = false;
                if (!_mod.ReloadSled(out string status))
                    MelonLogger.Warning("Original sled recovery reload failed: " + status);
            }
        }

        internal void RollbackPendingSwap()
        {
            if (HasPendingSwap)
                Fail("source recreation request failed", false);
        }

        internal void RestoreRuntime()
        {
            CancelRuntimeLoad();
            RestoreSnapshot(_runtimeSnapshot);
            _runtimeSnapshot = null;
            _requested = null;
            _installed = null;
            _target = null;
            _state = LoadState.None;
            _deadline = 0f;
            _recoveryReload = false;
        }

        internal bool RequestGaragePreview(
            VehicleScriptableObject target,
            AlpineVisualPartSystem.NativeChassisVariant variant,
            Action<bool, string> completed)
        {
            CancelPreview(false);
            if (target == null)
            {
                completed?.Invoke(false, "No selected sled is available for preview.");
                return false;
            }

            int oldId = 0;
            object oldSelection = null;
            if (SleddersGameBindings.TryGetGaragePreviewContext(
                    target, out Transform oldRoot, out oldSelection, out _))
            {
                SnowmobileStructure old = FindStructure(oldRoot);
                oldId = old != null ? old.GetInstanceID() : 0;
            }
            if (!SleddersGameBindings.TryRefreshGaragePreview(
                    target, out Transform root, out object selection, out string reason))
            {
                completed?.Invoke(false, reason ?? "Native garage preview is unavailable.");
                return false;
            }

            _previewRevision++;
            _preview = new PreviewRequest
            {
                revision = _previewRevision,
                target = target,
                variant = variant,
                previousSourceId = oldId,
                selection = selection ?? oldSelection,
                previewRoot = root,
                phase = 0,
                deadline = Time.unscaledTime + TimeoutSeconds,
                completed = completed
            };
            return true;
        }

        internal void RestoreGaragePreview()
        {
            VehicleScriptableObject target = _preview?.target ?? _previewSnapshot?.target;
            CancelPreview(false);
            if (target != null)
                SleddersGameBindings.TryRefreshGaragePreview(target, out _, out _, out _);
        }

        internal void Shutdown()
        {
            CancelCompatibilityScan();
            CancelPreview(false);
            RestoreRuntime();
            CleanupAbandoned(true);
        }

        internal void BeginCompatibilityScan(
            VehicleScriptableObject target,
            IEnumerable<AlpineVisualPartSystem.NativeChassisVariant> options,
            Action<IReadOnlyList<AlpineVisualPartSystem.NativeChassisVariant>> completed)
        {
            CancelCompatibilityScan();
            var candidates = (options ?? Array.Empty<AlpineVisualPartSystem.NativeChassisVariant>())
                .Where(option => option != null)
                .SelectMany(option => option.candidates != null && option.candidates.Count > 0
                    ? (IEnumerable<AlpineVisualPartSystem.NativeChassisVariant>)option.candidates
                    : new[] { option })
                .Where(candidate => candidate?.donor?.assetReference != null &&
                                    candidate.donor.assetReference.RuntimeKeyIsValid())
                .ToList();
            _scan = new CompatibilityScan
            {
                revision = ++_previewRevision,
                target = target,
                candidates = candidates,
                deadline = Time.unscaledTime + TimeoutSeconds,
                completed = completed
            };
            if (candidates.Count == 0)
                CompleteCompatibilityScan();
        }

        internal void CancelCompatibilityScan()
        {
            CompatibilityScan scan = _scan;
            _scan = null;
            if (scan == null) return;
            if (scan.handleValid)
            {
                if (scan.handle.IsDone) Release(scan.handle,
                    scan.handle.Status == AsyncOperationStatus.Succeeded ? scan.handle.Result : null);
                else _abandoned.Add(scan.handle);
            }
            if (scan.host != null) UnityEngine.Object.Destroy(scan.host);
        }

        private void PumpCompatibilityScan()
        {
            CompatibilityScan scan = _scan;
            if (scan == null) return;
            if (scan.target == null)
            {
                CompleteCompatibilityScan();
                return;
            }
            if (scan.source == null)
            {
                SnowmobileController active = AlpineTuningMod.ActiveController;
                if (active != null && AlpineTuningMod.ActiveSO != null &&
                    string.Equals(SledIdentity.StableIdentityKey(AlpineTuningMod.ActiveSO),
                        SledIdentity.StableIdentityKey(scan.target), StringComparison.OrdinalIgnoreCase))
                {
                    scan.source = FindStructure(active.transform);
                    scan.selection = SleddersGameBindings.GetCurrentVehicleSelection(active);
                }
                else if (SleddersGameBindings.TryGetGaragePreviewContext(
                             scan.target, out Transform root, out object selection, out _))
                {
                    scan.source = FindStructure(root);
                    scan.selection = selection;
                }
                if (scan.source == null)
                {
                    if (Time.unscaledTime > scan.deadline) CompleteCompatibilityScan();
                    return;
                }
            }

            if (!scan.handleValid)
            {
                if (scan.index >= scan.candidates.Count)
                {
                    CompleteCompatibilityScan();
                    return;
                }
                AlpineVisualPartSystem.NativeChassisVariant candidate = scan.candidates[scan.index];
                scan.host = CreateHost("Alpine Track Compatibility Probe", scan.source);
                try
                {
                    scan.handle = Addressables.InstantiateAsync(
                        candidate.donor.assetReference.RuntimeKey, scan.host.transform, false, true);
                    scan.handleValid = true;
                    scan.deadline = Time.unscaledTime + TimeoutSeconds;
                }
                catch
                {
                    if (scan.host != null) UnityEngine.Object.Destroy(scan.host);
                    scan.host = null;
                    scan.index++;
                }
                return;
            }

            if (!scan.handle.IsDone)
            {
                if (Time.unscaledTime <= scan.deadline) return;
                _abandoned.Add(scan.handle);
                scan.handleValid = false;
                if (scan.host != null) UnityEngine.Object.Destroy(scan.host);
                scan.host = null;
                scan.index++;
                return;
            }

            GameObject donorRoot = scan.handle.Status == AsyncOperationStatus.Succeeded
                ? scan.handle.Result
                : null;
            AlpineVisualPartSystem.NativeChassisVariant current = scan.candidates[scan.index];
            try
            {
                if (donorRoot != null)
                {
                    SnowmobileStructure donor = FindStructure(donorRoot.transform);
                    SleddersGameBindings.TryInitializeSnowmobileStructure(donor, scan.selection, out _);
                    if (TryBuildDescriptor(donorRoot, donor, scan.source, current,
                            out TrackAssemblyDescriptor descriptor, out _, false))
                    {
                        current.confirmedMode = descriptor.mode;
                        current.compatibilityScore = descriptor.compatibility?.score ?? float.MaxValue;
                        scan.compatible.Add(current);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Track compatibility probe rejected a donor safely: " +
                                    ex.GetType().Name);
            }
            finally
            {
                Release(scan.handle, donorRoot);
                scan.handleValid = false;
                if (scan.host != null) UnityEngine.Object.Destroy(scan.host);
                scan.host = null;
                scan.index++;
            }
        }

        private void CompleteCompatibilityScan()
        {
            CompatibilityScan scan = _scan;
            _scan = null;
            if (scan == null) return;
            if (scan.handleValid)
            {
                if (scan.handle.IsDone) Release(scan.handle,
                    scan.handle.Status == AsyncOperationStatus.Succeeded ? scan.handle.Result : null);
                else _abandoned.Add(scan.handle);
            }
            if (scan.host != null) UnityEngine.Object.Destroy(scan.host);
            var winners = new List<AlpineVisualPartSystem.NativeChassisVariant>();
            foreach (IGrouping<string, AlpineVisualPartSystem.NativeChassisVariant> group in
                     scan.compatible.GroupBy(candidate => candidate.id,
                         StringComparer.OrdinalIgnoreCase))
            {
                List<AlpineVisualPartSystem.NativeChassisVariant> ordered = group
                    .OrderBy(candidate => ModeRank(candidate.confirmedMode))
                    .ThenBy(candidate => candidate.compatibilityScore)
                    .ThenByDescending(candidate => candidate.rank)
                    .ThenBy(candidate => AlpineTuningMod.GetVehicleId(candidate.donor),
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ordered.Count == 0) continue;
                AlpineVisualPartSystem.NativeChassisVariant winner = ordered[0];
                winner.candidates = ordered;
                winners.Add(winner);
            }
            try { scan.completed?.Invoke(winners); }
            catch (Exception ex)
            {
                MelonLogger.Warning("Track compatibility completion was ignored safely: " +
                                    ex.GetType().Name);
            }
        }

        private static int ModeRank(TrackGraftMode? mode)
        {
            if (mode == TrackGraftMode.EmbeddedVariant) return 0;
            if (mode == TrackGraftMode.DirectGraft) return 1;
            return 2;
        }

        private void PumpRuntime()
        {
            if (_state != LoadState.Loading || !_runtimeHandleValid || !_runtimeHandle.IsDone)
                return;
            _state = LoadState.Installing;
            GameObject donorRoot = null;
            try
            {
                if (_runtimeHandle.Status != AsyncOperationStatus.Succeeded ||
                    (donorRoot = _runtimeHandle.Result) == null)
                {
                    if (TryNextRuntimeCandidate("donor addressable failed to load"))
                        return;
                    Fail("donor addressable failed to load", false);
                    return;
                }
                SnowmobileStructure donor = FindStructure(donorRoot.transform);
                SleddersGameBindings.TryInitializeSnowmobileStructure(donor, _runtimeSelection, out _);
                if (!TryBuildDescriptor(
                        donorRoot, donor, _runtimeSource, _requested,
                        out TrackAssemblyDescriptor descriptor, out string descriptorReason))
                {
                    if (TryNextRuntimeCandidate(descriptorReason))
                        return;
                    Fail(descriptorReason, false);
                    return;
                }

                GraftSnapshot snapshot;
                string installReason;
                bool installed = descriptor.mode == TrackGraftMode.ScaledFallback
                    ? InstallScaled(_runtimeSource, _runtimeController, descriptor, _requested,
                        out snapshot, out installReason)
                    : InstallDirect(_runtimeSource, _runtimeController, descriptor, _requested,
                        _runtimeHandle, _runtimeHost, false, out snapshot, out installReason);
                if (!installed)
                {
                    if (TryNextRuntimeCandidate(installReason))
                        return;
                    Fail(installReason, false);
                    return;
                }

                if (descriptor.mode == TrackGraftMode.ScaledFallback)
                {
                    Release(_runtimeHandle, donorRoot);
                    UnityEngine.Object.Destroy(_runtimeHost);
                }
                else
                {
                    snapshot.handle = _runtimeHandle;
                    snapshot.handleValid = true;
                    snapshot.host = _runtimeHost;
                }
                _runtimeHandleValid = false;
                _runtimeHost = null;
                _runtimeSnapshot = snapshot;
                _installed = _requested;
                _requested = null;
                _state = LoadState.Installed;
                _deadline = 0f;
                MelonLogger.Msg(
                    $"Installed {_installed.lengthInches:0.#}\" {descriptor.mode}; " +
                    "hood, seat, cockpit, front suspension and vehicle identity preserved.");
            }
            catch (Exception ex)
            {
                Fail("track graft failed safely: " + ex.GetType().Name, false);
            }
        }

        private void PumpPreview()
        {
            PreviewRequest request = _preview;
            if (request == null)
                return;
            if (request.revision != _previewRevision || Time.unscaledTime > request.deadline)
            {
                FinishPreview(false, "Track preview timed out; original preview restored.");
                return;
            }

            if (request.phase == 0)
            {
                if (!SleddersGameBindings.TryGetGaragePreviewContext(
                        request.target, out Transform root, out object selection, out _))
                    return;
                SnowmobileStructure source = FindStructure(root);
                if (source == null ||
                    (request.previousSourceId != 0 && source.GetInstanceID() == request.previousSourceId))
                    return;
                request.source = source;
                request.previewRoot = root;
                request.selection = selection ?? request.selection;
                if (request.variant == null)
                {
                    FinishPreview(true, "Original track preview restored.");
                    return;
                }
                request.candidateIndex = 0;
                if (!BeginPreviewCandidate(request))
                    FinishPreview(false, "No compatible donor addressable could start loading.");
                return;
            }

            if (request.phase != 1 || !request.handleValid || !request.handle.IsDone)
                return;
            request.phase = 2;
            GameObject donorRoot = null;
            try
            {
                if (request.handle.Status != AsyncOperationStatus.Succeeded ||
                    (donorRoot = request.handle.Result) == null)
                {
                    if (TryNextPreviewCandidate(request, "donor failed to load"))
                        return;
                    FinishPreview(false, "Track preview donor failed to load.");
                    return;
                }
                SnowmobileStructure donor = FindStructure(donorRoot.transform);
                SleddersGameBindings.TryInitializeSnowmobileStructure(donor, request.selection, out _);
                if (!TryBuildDescriptor(donorRoot, donor, request.source, request.variant,
                        out TrackAssemblyDescriptor descriptor, out string descriptorReason))
                {
                    if (TryNextPreviewCandidate(request, descriptorReason))
                        return;
                    FinishPreview(false, descriptorReason);
                    return;
                }
                GraftSnapshot snapshot;
                string installReason;
                bool installed = descriptor.mode == TrackGraftMode.ScaledFallback
                    ? InstallScaled(request.source, null, descriptor, request.variant,
                        out snapshot, out installReason)
                    : InstallDirect(request.source, null, descriptor, request.variant,
                        request.handle, request.host, true, out snapshot, out installReason);
                if (!installed)
                {
                    if (TryNextPreviewCandidate(request, installReason))
                        return;
                    FinishPreview(false, installReason);
                    return;
                }
                if (descriptor.mode == TrackGraftMode.ScaledFallback)
                {
                    Release(request.handle, donorRoot);
                    UnityEngine.Object.Destroy(request.host);
                }
                else
                {
                    snapshot.handle = request.handle;
                    snapshot.handleValid = true;
                    snapshot.host = request.host;
                }
                request.handleValid = false;
                request.host = null;
                snapshot.target = request.target;
                _previewSnapshot = snapshot;
                FinishPreview(true, descriptor.mode == TrackGraftMode.ScaledFallback
                    ? "Scaled fallback preview ready."
                    : "Track and rear-chassis preview ready.", false);
            }
            catch (Exception ex)
            {
                FinishPreview(false, "Track preview failed safely: " + ex.GetType().Name);
            }
        }

        private bool BeginPreviewCandidate(PreviewRequest request)
        {
            if (request == null) return false;
            while ((request.candidate = CandidateAt(request.variant, request.candidateIndex)) != null)
            {
                VehicleScriptableObject donor = request.candidate.donor;
                if (donor?.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
                {
                    request.candidateIndex++;
                    continue;
                }
                request.host = CreateHost("Alpine Garage Rear Chassis", request.source);
                try
                {
                    request.handle = Addressables.InstantiateAsync(
                        donor.assetReference.RuntimeKey, request.host.transform, false, true);
                    request.handleValid = true;
                    request.phase = 1;
                    request.deadline = Time.unscaledTime + TimeoutSeconds;
                    return true;
                }
                catch
                {
                    if (request.host != null) UnityEngine.Object.Destroy(request.host);
                    request.host = null;
                    request.candidateIndex++;
                }
            }
            request.candidate = null;
            return false;
        }

        private bool TryNextPreviewCandidate(PreviewRequest request, string rejectedReason)
        {
            if (request == null) return false;
            if (request.handleValid)
            {
                if (request.handle.IsDone) Release(request.handle,
                    request.handle.Status == AsyncOperationStatus.Succeeded ? request.handle.Result : null);
                else _abandoned.Add(request.handle);
            }
            request.handleValid = false;
            if (request.host != null) UnityEngine.Object.Destroy(request.host);
            request.host = null;
            request.candidateIndex++;
            bool started = BeginPreviewCandidate(request);
            if (started)
                MelonLogger.Msg("Preview is trying the next rear-assembly donor after: " + rejectedReason);
            return started;
        }

        private bool TryBuildDescriptor(
            GameObject root,
            SnowmobileStructure donor,
            SnowmobileStructure source,
            AlpineVisualPartSystem.NativeChassisVariant variant,
            out TrackAssemblyDescriptor descriptor,
            out string reason,
            bool confirmMode = true)
        {
            descriptor = null;
            reason = null;
            if (root == null || donor == null || source == null)
            {
                reason = "Source or donor model structure is missing.";
                return false;
            }
            var built = Describe(donor);
            built.root = root;
            if (!TrySignature(source.transform, source, Describe(source),
                    out TrackCompatibilitySignature sourceSignature))
            {
                reason = "Source track or tunnel bounds are not measurable.";
                return false;
            }
            bool hasDirectSignature = TrySignature(
                source.transform, donor, built, out TrackCompatibilitySignature donorSignature);
            if (!hasDirectSignature && !TryFallbackSignature(
                    source.transform, donor, built, out donorSignature))
            {
                reason = "Donor animated track/contact bounds are not measurable.";
                return false;
            }
            built.signature = donorSignature;
            built.compatibility = hasDirectSignature
                ? EvaluateCompatibility(sourceSignature, donorSignature)
                : new TrackCompatibilityResult
                {
                    compatible = false,
                    reason = "donor tunnel seam is unavailable",
                    score = float.MaxValue
                };
            bool embedded = string.Equals(
                    variant.family, "arctic-cat", StringComparison.OrdinalIgnoreCase) &&
                HasArcticVariant(source, variant.length);
            if (embedded)
            {
                built.mode = TrackGraftMode.EmbeddedVariant;
                built.compatibility.compatible = true;
                built.compatibility.alignment = Vector3.zero;
                built.compatibility.rotation = Quaternion.identity;
            }
            else if (built.compatibility.compatible)
            {
                built.mode = TrackGraftMode.DirectGraft;
            }
            else
            {
                float ratio = donorSignature.trackLength / sourceSignature.trackLength;
                if (!IsFinitePositive(ratio) || ratio < MinimumFallbackScale || ratio > MaximumFallbackScale)
                {
                    reason = built.compatibility.reason + "; scaled fallback is outside 0.55-1.65";
                    return false;
                }
                built.mode = TrackGraftMode.ScaledFallback;
            }
            if (confirmMode)
                _mod.VisualParts?.ConfirmVariantMode(variant, built.mode);
            descriptor = built;
            return true;
        }

        internal static TrackCompatibilityResult EvaluateCompatibility(
            TrackCompatibilitySignature source,
            TrackCompatibilitySignature donor)
        {
            var result = new TrackCompatibilityResult();
            if (source == null || donor == null)
            {
                result.reason = "missing compatibility signature";
                return result;
            }
            if (!donor.animatedTrack || !donor.contactMesh || !donor.rearGraph)
            {
                result.reason = "donor is missing animated track, contact mesh, or rear graph";
                return result;
            }
            float forwardAngle = Vector3.Angle(source.forward, donor.forward);
            float upAngle = Vector3.Angle(source.up, donor.up);
            Quaternion forwardRotation = FromToRotation(donor.forward, source.forward);
            Vector3 rotatedUp = Normalize(ProjectOnPlane(
                Rotate(forwardRotation, donor.up), source.forward));
            Vector3 sourceUp = Normalize(ProjectOnPlane(source.up, source.forward));
            Quaternion upRotation = rotatedUp.sqrMagnitude > 0.5f && sourceUp.sqrMagnitude > 0.5f
                ? FromToRotation(rotatedUp, sourceUp)
                : Quaternion.identity;
            Quaternion rotation = Multiply(upRotation, forwardRotation);
            Vector3 alignment = source.seamCenter - Rotate(rotation, donor.seamCenter);
            result.alignment = alignment;
            result.rotation = rotation;
            float widthError = IsFinitePositive(source.tunnelWidth)
                ? Mathf.Abs(donor.tunnelWidth - source.tunnelWidth) / source.tunnelWidth
                : float.MaxValue;
            float seamRms = Mathf.Abs(donor.tunnelWidth - source.tunnelWidth) * 0.5f;
            Vector3 alignedTrack = Rotate(rotation, donor.trackCenter) + alignment;
            Vector3 centerDelta = alignedTrack - source.trackCenter;
            Vector3 lateralAxis = Vector3.Cross(source.up, source.forward).normalized;
            float lateral = Mathf.Abs(Vector3.Dot(centerDelta, lateralAxis));
            float vertical = Mathf.Abs(Vector3.Dot(centerDelta, source.up));
            // A track legitimately reaches a little ahead of the tunnel seam. Measure only
            // additional donor intrusion so equivalent native graphs are not rejected.
            float sourceFront = Vector3.Dot(source.rearBounds.center, source.forward) +
                                BoundsRadius(source.rearBounds, source.forward);
            float donorFront = Vector3.Dot(Rotate(rotation, donor.rearBounds.center) + alignment, source.forward) +
                               BoundsRadius(donor.rearBounds,
                                   Rotate(Conjugate(rotation), source.forward));
            float seamForward = Vector3.Dot(source.seamCenter, source.forward);
            float sourceIntrusion = sourceFront - seamForward;
            float intrusion = donorFront - seamForward -
                              Mathf.Max(0f, sourceIntrusion);
            if (forwardAngle > 5f || upAngle > 5f)
                result.reason = "assembly axes exceed five degrees";
            else if (alignment.magnitude > 0.20f)
                result.reason = "tunnel seam requires more than 0.20 m alignment";
            else if (widthError > 0.15f)
                result.reason = "tunnel widths differ by more than fifteen percent";
            else if (seamRms > 0.06f)
                result.reason = "tunnel seam RMS exceeds 0.06 m";
            else if (lateral > 0.04f || vertical > 0.08f)
                result.reason = "track centerline is outside the mount envelope";
            else if (intrusion > 0.08f)
                result.reason = "rear renderer closure intrudes into the preserved body";
            else
            {
                result.compatible = true;
                result.reason = "compatible rear geometry";
            }
            result.score = forwardAngle + upAngle + alignment.magnitude * 10f +
                           widthError * 10f + lateral * 20f + vertical * 12f +
                           Mathf.Max(0f, intrusion) * 10f;
            return result;
        }

        internal static bool TryResolveFallbackScale(
            float sourceLength,
            float donorLength,
            out float ratio,
            out string reason)
        {
            ratio = 0f;
            reason = null;
            if (!IsFinitePositive(sourceLength) || !IsFinitePositive(donorLength))
            {
                reason = "Track bounds cannot be measured for scaled fallback.";
                return false;
            }
            ratio = donorLength / sourceLength;
            if (ratio < MinimumFallbackScale || ratio > MaximumFallbackScale)
            {
                reason = "Scaled fallback exceeds the supported 0.55-1.65 range.";
                return false;
            }
            return true;
        }

        internal static void CalculateFrontAnchoredAxis(
            float scale,
            float position,
            float ratio,
            float length,
            out float scaled,
            out float shifted)
        {
            scaled = scale * ratio;
            shifted = position + (1f - ratio) * length * 0.5f;
        }

        private static TrackAssemblyDescriptor Describe(SnowmobileStructure structure)
        {
            var value = new TrackAssemblyDescriptor
            {
                structure = structure,
                trackRenderer = structure?.trackRenderer,
                track = structure?.trackRenderer?.track,
                rearAxel = structure != null ? structure.GetComponentInChildren<RearAxelController>(true) : null,
                trackMeshes = structure?.trackMeshes ?? Array.Empty<SnowMesh>(),
                otherTrackObjects = structure?.otherTrackObjects ?? Array.Empty<Transform>(),
                traxGroup = structure?.traxGroup,
                traxBody = structure?.traxBody
            };
            Add(value.renderers, GetField<MeshRenderer[]>(structure, "tunnelParts"));
            Add(value.renderers, GetField<MeshRenderer[]>(structure, "railParts"));
            AddChildren(value.renderers, value.trackRenderer?.transform);
            AddChildren(value.renderers, value.rearAxel?.transform);
            SnowmobileAccessories accessories = structure != null
                ? structure.GetComponentInChildren<SnowmobileAccessories>(true)
                : null;
            if (accessories != null)
            {
                AddChildren(value.renderers, GetField<Transform>(accessories, "tunnelAccessoriesRoot"));
                AddObjects(value.renderers, GetField<IEnumerable<GameObject>>(accessories, "rearPartObjects"));
                AddObjects(value.renderers, GetField<IEnumerable<GameObject>>(accessories, "snowFlapObjects"));
                AddObjects(value.renderers, GetField<IEnumerable<GameObject>>(accessories, "tunnelReflectors"));
            }
            if (structure != null)
            {
                foreach (Renderer renderer in structure.GetComponentsInChildren<Renderer>(true))
                {
                    string name = renderer?.name ?? string.Empty;
                    if ((name.IndexOf("rear bumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("snow flap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("snowflap", StringComparison.OrdinalIgnoreCase) >= 0) &&
                        name.IndexOf("front", StringComparison.OrdinalIgnoreCase) < 0)
                        value.renderers.Add(renderer);
                }
            }
            return value;
        }

        private static bool TrySignature(
            Transform reference,
            SnowmobileStructure structure,
            TrackAssemblyDescriptor descriptor,
            out TrackCompatibilitySignature signature)
        {
            signature = null;
            if (reference == null || structure == null || descriptor == null ||
                !AggregateBounds(reference,
                    GetField<MeshRenderer[]>(structure, "tunnelParts"), out Bounds tunnel) ||
                !AggregateBounds(reference, descriptor.renderers, out Bounds rear) ||
                !TrackBounds(structure, reference, out Bounds track) ||
                !MeasureTrack(structure, reference, out float length))
                return false;
            bool arctic = structure.GetComponentsInChildren<Transform>(true)
                .Any(transform => TryArcticVariant(transform?.name, out _, out _));
            Vector3 forward = reference.InverseTransformDirection(structure.transform.forward).normalized;
            Vector3 up = reference.InverseTransformDirection(structure.transform.up).normalized;
            Vector3 lateral = Vector3.Cross(up, forward).normalized;
            signature = new TrackCompatibilitySignature
            {
                seamCenter = tunnel.center +
                    forward * BoundsRadius(tunnel, forward) +
                    up * BoundsRadius(tunnel, up),
                forward = forward,
                up = up,
                tunnelWidth = BoundsRadius(tunnel, lateral) * 2f,
                trackCenter = track.center,
                rearBounds = rear,
                trackLength = length,
                animatedTrack = HasAnimatedTrack(structure),
                contactMesh = descriptor.trackMeshes.Any(mesh =>
                    mesh != null && mesh.GetComponent<MeshFilter>()?.sharedMesh != null),
                rearGraph = descriptor.rearAxel != null || arctic
            };
            return IsFinitePositive(signature.tunnelWidth) && IsFinitePositive(length);
        }

        private static bool TryFallbackSignature(
            Transform reference,
            SnowmobileStructure structure,
            TrackAssemblyDescriptor descriptor,
            out TrackCompatibilitySignature signature)
        {
            signature = null;
            if (reference == null || structure == null || descriptor == null ||
                !TrackBounds(structure, reference, out Bounds track) ||
                !MeasureTrack(structure, reference, out float length) || !HasAnimatedTrack(structure) ||
                !descriptor.trackMeshes.Any(mesh =>
                    mesh != null && mesh.GetComponent<MeshFilter>()?.sharedMesh != null))
                return false;
            signature = new TrackCompatibilitySignature
            {
                trackCenter = track.center,
                rearBounds = track,
                trackLength = length,
                animatedTrack = true,
                contactMesh = true,
                rearGraph = descriptor.rearAxel != null
            };
            return true;
        }

        private static float BoundsRadius(Bounds bounds, Vector3 axis)
        {
            Vector3 extent = bounds.extents;
            Vector3 direction = new Vector3(
                Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return Vector3.Dot(extent, direction);
        }

        private static Vector3 Normalize(Vector3 value)
        {
            float magnitude = (float)Math.Sqrt(Vector3.Dot(value, value));
            return magnitude > 0.000001f ? value / magnitude : Vector3.zero;
        }

        private static Vector3 ProjectOnPlane(Vector3 value, Vector3 normal)
        {
            float denominator = Vector3.Dot(normal, normal);
            return denominator > 0.000001f
                ? value - normal * (Vector3.Dot(value, normal) / denominator)
                : value;
        }

        private static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 a = Normalize(from);
            Vector3 b = Normalize(to);
            float dot = Math.Max(-1f, Math.Min(1f, Vector3.Dot(a, b)));
            if (dot < -0.999999f)
            {
                Vector3 axis = Normalize(Vector3.Cross(Vector3.right, a));
                if (axis.sqrMagnitude < 0.5f)
                    axis = Normalize(Vector3.Cross(Vector3.up, a));
                return new Quaternion(axis.x, axis.y, axis.z, 0f);
            }
            Vector3 cross = Vector3.Cross(a, b);
            return Normalize(new Quaternion(cross.x, cross.y, cross.z, 1f + dot));
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float magnitude = (float)Math.Sqrt(value.x * value.x + value.y * value.y +
                                               value.z * value.z + value.w * value.w);
            return magnitude > 0.000001f
                ? new Quaternion(value.x / magnitude, value.y / magnitude,
                    value.z / magnitude, value.w / magnitude)
                : Quaternion.identity;
        }

        private static Quaternion Conjugate(Quaternion value)
        {
            return new Quaternion(-value.x, -value.y, -value.z, value.w);
        }

        private static Quaternion Multiply(Quaternion left, Quaternion right)
        {
            return new Quaternion(
                left.w * right.x + left.x * right.w + left.y * right.z - left.z * right.y,
                left.w * right.y - left.x * right.z + left.y * right.w + left.z * right.x,
                left.w * right.z + left.x * right.y - left.y * right.x + left.z * right.w,
                left.w * right.w - left.x * right.x - left.y * right.y - left.z * right.z);
        }

        private static Vector3 Rotate(Quaternion rotation, Vector3 value)
        {
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            float scalar = rotation.w;
            return 2f * Vector3.Dot(vector, value) * vector +
                   (scalar * scalar - Vector3.Dot(vector, vector)) * value +
                   2f * scalar * Vector3.Cross(vector, value);
        }

        private bool InstallDirect(
            SnowmobileStructure source,
            SnowmobileController controller,
            TrackAssemblyDescriptor donor,
            AlpineVisualPartSystem.NativeChassisVariant variant,
            AsyncOperationHandle<GameObject> handle,
            GameObject host,
            bool preview,
            out GraftSnapshot snapshot,
            out string reason)
        {
            snapshot = Capture(source, controller, variant);
            reason = null;
            try
            {
                bool embedded = donor.mode == TrackGraftMode.EmbeddedVariant;
                if (embedded && !ApplyArcticVariant(source, variant.length, snapshot))
                    throw new InvalidOperationException("embedded Arctic tunnel/bumper/rail set is incomplete");

                if (donor.root != null)
                {
                    Transform donorTransform = donor.root.transform;
                    donorTransform.localPosition = Rotate(donor.compatibility.rotation,
                        donorTransform.localPosition) + donor.compatibility.alignment;
                    donorTransform.localRotation = Multiply(donor.compatibility.rotation,
                        donorTransform.localRotation);
                }
                TrackAssemblyDescriptor sourceDescriptor = Describe(source);
                IEnumerable<Renderer> sourceRenderers = embedded
                    ? (IEnumerable<Renderer>)source.trackRenderer.GetComponentsInChildren<Renderer>(true)
                    : sourceDescriptor.renderers;
                SetRenderers(sourceRenderers, false, snapshot);
                SetBehaviour(source.trackRenderer, false, snapshot);
                if (!embedded)
                    SetBehaviour(sourceDescriptor.rearAxel, false, snapshot);

                IsolateDonor(donor, embedded);
                host.SetActive(true);
                source.trackRenderer = donor.trackRenderer;
                source.trackMeshes = donor.trackMeshes;
                source.otherTrackObjects = donor.otherTrackObjects;
                source.traxGroup = donor.traxGroup;
                source.traxBody = donor.traxBody;

                if (!preview && controller != null)
                {
                    Component suspension = SleddersGameBindings.GetSuspensionControllers(controller).FirstOrDefault();
                    if (!embedded && donor.rearAxel != null && suspension != null)
                    {
                        snapshot.suspension = suspension;
                        if (!SleddersGameBindings.TrySetRearAxelController(
                                suspension, donor.rearAxel, out snapshot.rearAxel, out string bindReason))
                            throw new InvalidOperationException(bindReason);
                    }
                    if (!InstallContactMesh(controller, donor, snapshot, out string meshReason))
                        throw new InvalidOperationException(meshReason);
                }
                if (!HasAnimatedTrack(source))
                    throw new InvalidOperationException("installed track graph is invalid");
                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(snapshot);
                snapshot = null;
                reason = ex.Message;
                return false;
            }
        }

        private static bool InstallScaled(
            SnowmobileStructure source,
            SnowmobileController controller,
            TrackAssemblyDescriptor donor,
            AlpineVisualPartSystem.NativeChassisVariant variant,
            out GraftSnapshot snapshot,
            out string reason)
        {
            snapshot = Capture(source, controller, variant);
            reason = null;
            MeasureTrack(source, out float sourceLength);
            if (!TryResolveFallbackScale(sourceLength, donor?.signature?.trackLength ?? 0f,
                    out float ratio, out reason))
                return false;
            try
            {
                TrackToSkinnedMesh track = source.trackRenderer.track;
                if (!TryAxis(GetField<object>(track, "trackLocalLengthDimension"), out int axis))
                    throw new InvalidOperationException("Animated track length axis is ambiguous.");
                float localTrackLength = LocalTrackLength(track, axis);
                if (!IsFinitePositive(localTrackLength))
                    throw new InvalidOperationException("Animated source-track bounds are not measurable.");
                SaveTransform(track.transform, snapshot);
                ScaleAtFront(track.transform, axis, ratio, localTrackLength);
                foreach (SnowMesh mesh in source.trackMeshes ?? Array.Empty<SnowMesh>())
                {
                    if (mesh == null) continue;
                    if (mesh.transform.IsChildOf(track.transform)) continue;
                    SaveTransform(mesh.transform, snapshot);
                    int meshAxis = LongestAxis(mesh.gameObject);
                    ScaleAtFront(mesh.transform, meshAxis, ratio,
                        LocalMeshLength(mesh.gameObject, meshAxis));
                }
                foreach (Transform other in source.otherTrackObjects ?? Array.Empty<Transform>())
                {
                    if (other == null || other == track.transform || other.IsChildOf(track.transform))
                        continue;
                    SaveTransform(other, snapshot);
                    int otherAxis = LongestAxis(other.gameObject);
                    ScaleAtFront(other, otherAxis, ratio,
                        LocalMeshLength(other.gameObject, otherAxis));
                }
                if (source.traxBody != null)
                {
                    bool alreadyScaled = source.traxBody == track.transform ||
                        source.traxBody.IsChildOf(track.transform) ||
                        snapshot.transforms.Any(state => state.value == source.traxBody);
                    if (!alreadyScaled)
                    {
                        SaveTransform(source.traxBody, snapshot);
                        int traxAxis = LongestAxis(source.traxBody.gameObject);
                        ScaleAtFront(source.traxBody, traxAxis, ratio,
                            LocalMeshLength(source.traxBody.gameObject, traxAxis));
                    }
                }
                if (!HasAnimatedTrack(source))
                    throw new InvalidOperationException("Scaled track graph is no longer animated.");
                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(snapshot);
                snapshot = null;
                reason = ex.Message;
                return false;
            }
        }

        private static bool InstallContactMesh(
            SnowmobileController controller,
            TrackAssemblyDescriptor donor,
            GraftSnapshot snapshot,
            out string reason)
        {
            reason = null;
            Component controllerBase = SleddersGameBindings.GetSnowmobileControllerBase(controller);
            Transform controllerTrack = GetField<Transform>(controllerBase, "track");
            MeshInterpretter meshInterpretter = GetField<MeshInterpretter>(controllerBase, "meshInterpretter");
            if (controllerBase == null || controllerTrack == null || meshInterpretter == null)
            {
                reason = "Native track-contact controller is unavailable.";
                return false;
            }
            var combines = new List<CombineInstance>();
            int vertices = 0;
            foreach (SnowMesh item in donor.trackMeshes ?? Array.Empty<SnowMesh>())
            {
                MeshFilter filter = item != null ? item.GetComponent<MeshFilter>() : null;
                if (filter?.sharedMesh == null) continue;
                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = controllerTrack.worldToLocalMatrix * filter.transform.localToWorldMatrix
                });
                vertices += filter.sharedMesh.vertexCount;
            }
            if (combines.Count == 0)
            {
                reason = "Donor track-contact mesh is empty.";
                return false;
            }
            var go = new GameObject("Alpine Donor Track Contact");
            go.transform.SetParent(controllerTrack, false);
            var filterOut = go.AddComponent<MeshFilter>();
            var meshOut = new Mesh { name = "Alpine Donor Track Contact Mesh" };
            if (vertices > 65535) meshOut.indexFormat = IndexFormat.UInt32;
            meshOut.CombineMeshes(combines.ToArray(), true, true);
            filterOut.sharedMesh = meshOut;
            SnowMesh snow = go.AddComponent<SnowMesh>();
            snow.areaMultiplier = donor.trackMeshes.Where(x => x != null)
                .Select(x => x.areaMultiplier).DefaultIfEmpty(1f).Average();
            snapshot.meshInterpretter = meshInterpretter;
            snapshot.contactMesh = meshInterpretter.trackMesh;
            snapshot.generatedContactObject = go;
            snapshot.generatedContactMesh = meshOut;
            meshInterpretter.trackMesh = snow;
            return true;
        }

        private static void IsolateDonor(TrackAssemblyDescriptor donor, bool embedded)
        {
            HashSet<Renderer> visible = embedded
                ? new HashSet<Renderer>(donor.trackRenderer.GetComponentsInChildren<Renderer>(true))
                : new HashSet<Renderer>(donor.renderers);
            foreach (Renderer renderer in donor.root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = renderer.enabled && visible.Contains(renderer);
            foreach (Behaviour behaviour in donor.root.GetComponentsInChildren<Behaviour>(true))
            {
                bool required = behaviour is TrackRenderer || behaviour is TrackToSkinnedMesh ||
                                behaviour is Wheel || (!embedded && behaviour is RearAxelController);
                behaviour.enabled = behaviour.enabled && required;
            }
            foreach (Collider collider in donor.root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in donor.root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (Light light in donor.root.GetComponentsInChildren<Light>(true)) light.enabled = false;
        }

        private static GraftSnapshot Capture(
            SnowmobileStructure source,
            SnowmobileController controller,
            AlpineVisualPartSystem.NativeChassisVariant variant)
        {
            return new GraftSnapshot
            {
                target = controller != null ? AlpineTuningMod.ActiveSO : null,
                variantId = variant?.id,
                source = source,
                trackRenderer = source?.trackRenderer,
                trackMeshes = source?.trackMeshes,
                otherTrackObjects = source?.otherTrackObjects,
                traxGroup = source?.traxGroup,
                traxBody = source?.traxBody
            };
        }

        private static void RestoreSnapshot(GraftSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                if (snapshot.source != null)
                {
                    snapshot.source.trackRenderer = snapshot.trackRenderer;
                    snapshot.source.trackMeshes = snapshot.trackMeshes;
                    snapshot.source.otherTrackObjects = snapshot.otherTrackObjects;
                    snapshot.source.traxGroup = snapshot.traxGroup;
                    snapshot.source.traxBody = snapshot.traxBody;
                }
                if (snapshot.suspension != null)
                    SleddersGameBindings.TrySetRearAxelController(
                        snapshot.suspension, snapshot.rearAxel, out _, out _);
                if (snapshot.meshInterpretter != null)
                    snapshot.meshInterpretter.trackMesh = snapshot.contactMesh;
                foreach (TransformState state in snapshot.transforms.AsEnumerable().Reverse())
                {
                    if (state.value == null) continue;
                    state.value.SetParent(state.parent, false);
                    state.value.localPosition = state.position;
                    state.value.localRotation = state.rotation;
                    state.value.localScale = state.scale;
                }
                foreach (ObjectState state in snapshot.objects.AsEnumerable().Reverse())
                    if (state.value != null) state.value.SetActive(state.active);
                foreach (BehaviourState state in snapshot.behaviours.AsEnumerable().Reverse())
                    if (state.value != null) state.value.enabled = state.enabled;
                foreach (RendererState state in snapshot.renderers.AsEnumerable().Reverse())
                    if (state.value != null) state.value.enabled = state.enabled;
            }
            finally
            {
                if (snapshot.generatedContactObject != null) UnityEngine.Object.Destroy(snapshot.generatedContactObject);
                if (snapshot.generatedContactMesh != null) UnityEngine.Object.Destroy(snapshot.generatedContactMesh);
                if (snapshot.handleValid) Release(snapshot.handle,
                    snapshot.handle.IsDone ? snapshot.handle.Result : null);
                if (snapshot.host != null) UnityEngine.Object.Destroy(snapshot.host);
            }
        }

        private void Fail(string reason, bool reload)
        {
            MelonLogger.Warning("Track/rear-chassis graft rolled back: " + reason + ".");
            CancelRuntimeLoad();
            RestoreSnapshot(_runtimeSnapshot);
            _runtimeSnapshot = null;
            _requested = null;
            _installed = null;
            _target = null;
            _state = LoadState.Failed;
            _deadline = 0f;
            _recoveryReload = reload;
        }

        private void FinishPreview(bool success, string message, bool restore = true)
        {
            PreviewRequest request = _preview;
            _preview = null;
            if (!success)
            {
                if (request != null && request.handleValid)
                {
                    if (request.handle.IsDone) Release(request.handle, request.handle.Result);
                    else _abandoned.Add(request.handle);
                }
                if (request?.host != null) UnityEngine.Object.Destroy(request.host);
                if (restore)
                {
                    RestoreSnapshot(_previewSnapshot);
                    _previewSnapshot = null;
                }
                if (request?.target != null)
                    SleddersGameBindings.TryRefreshGaragePreview(request.target, out _, out _, out _);
            }
            request?.completed?.Invoke(success, message);
        }

        private void CancelPreview(bool refresh)
        {
            PreviewRequest request = _preview;
            _preview = null;
            _previewRevision++;
            if (request != null && request.handleValid)
            {
                if (request.handle.IsDone) Release(request.handle, request.handle.Result);
                else _abandoned.Add(request.handle);
            }
            if (request?.host != null) UnityEngine.Object.Destroy(request.host);
            RestoreSnapshot(_previewSnapshot);
            _previewSnapshot = null;
            if (refresh && request?.target != null)
                SleddersGameBindings.TryRefreshGaragePreview(request.target, out _, out _, out _);
        }

        private void CancelRuntimeLoad()
        {
            if (_runtimeHandleValid)
            {
                if (_runtimeHandle.IsDone) Release(_runtimeHandle, _runtimeHandle.Result);
                else _abandoned.Add(_runtimeHandle);
            }
            _runtimeHandleValid = false;
            if (_runtimeHost != null) UnityEngine.Object.Destroy(_runtimeHost);
            _runtimeHost = null;
                _runtimeSource = null;
                _runtimeController = null;
                _runtimeSelection = null;
                _runtimeCandidate = null;
                _runtimeCandidateIndex = 0;
        }

        private void CleanupAbandoned(bool force)
        {
            for (int i = _abandoned.Count - 1; i >= 0; i--)
            {
                var handle = _abandoned[i];
                if (!force && !handle.IsDone) continue;
                _abandoned.RemoveAt(i);
                if (handle.IsDone) Release(handle,
                    handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null);
                else if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        private static void Release(AsyncOperationHandle<GameObject> handle, GameObject root)
        {
            try
            {
                if (handle.IsValid() && handle.IsDone &&
                    handle.Status == AsyncOperationStatus.Succeeded)
                    Addressables.ReleaseInstance(handle);
                else if (handle.IsValid())
                    Addressables.Release(handle);
                else if (root != null) UnityEngine.Object.Destroy(root);
            }
            catch { if (root != null) UnityEngine.Object.Destroy(root); }
        }

        private static GameObject CreateHost(string name, SnowmobileStructure source)
        {
            var host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            host.transform.SetParent(source.transform.parent, false);
            host.transform.localPosition = source.transform.localPosition;
            host.transform.localRotation = source.transform.localRotation;
            host.transform.localScale = source.transform.localScale;
            return host;
        }

        private static SnowmobileStructure FindStructure(Transform root)
        {
            return root == null ? null : root.GetComponentsInChildren<SnowmobileStructure>(true)
                .FirstOrDefault(value => value != null &&
                    value.name.IndexOf("Alpine", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static bool HasAnimatedTrack(SnowmobileStructure structure)
        {
            TrackToSkinnedMesh track = structure?.trackRenderer?.track;
            return track != null && (GetField<SkinnedMeshRenderer>(track, "smr") != null ||
                track.GetComponent<SkinnedMeshRenderer>() != null ||
                structure.trackRenderer.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);
        }

        private static bool HasArcticVariant(
            SnowmobileStructure source,
            AlpineVisualPartSystem.NativeTrackLength length)
        {
            if (source == null || length == null) return false;
            string token = Mathf.RoundToInt(length.nativeValue).ToString(CultureInfo.InvariantCulture);
            var kinds = new HashSet<string>();
            foreach (Transform transform in source.GetComponentsInChildren<Transform>(true))
            {
                if (!TryArcticVariant(transform.name, out string kind, out string found) || found != token)
                    continue;
                kinds.Add(kind);
            }
            return kinds.Count == 3;
        }

        private static bool ApplyArcticVariant(
            SnowmobileStructure source,
            AlpineVisualPartSystem.NativeTrackLength length,
            GraftSnapshot snapshot)
        {
            if (!HasArcticVariant(source, length)) return false;
            string token = Mathf.RoundToInt(length.nativeValue).ToString(CultureInfo.InvariantCulture);
            foreach (Transform transform in source.GetComponentsInChildren<Transform>(true))
            {
                if (!TryArcticVariant(transform.name, out _, out string found)) continue;
                SaveObject(transform.gameObject, snapshot);
                transform.gameObject.SetActive(found == token);
            }
            return true;
        }

        private static bool TryArcticVariant(string name, out string kind, out string length)
        {
            kind = null;
            length = null;
            string value = name ?? string.Empty;
            if (value.IndexOf("catalyst", StringComparison.OrdinalIgnoreCase) >= 0 &&
                value.IndexOf("tunnel", StringComparison.OrdinalIgnoreCase) >= 0)
                kind = "tunnel";
            else if (value.IndexOf("rear", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     value.IndexOf("bumper", StringComparison.OrdinalIgnoreCase) >= 0)
                kind = "bumper";
            else if (value.IndexOf("liukurunko", StringComparison.OrdinalIgnoreCase) >= 0)
                kind = "rail";
            else
                return false;
            Match match = ArcticLengthToken.Match(value);
            if (!match.Success) return false;
            length = match.Groups[1].Value;
            return true;
        }

        private static bool AggregateBounds(
            Transform reference,
            IEnumerable<Renderer> renderers,
            out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            if (reference == null || renderers == null) return false;
            foreach (Renderer renderer in renderers.Where(x => x != null).Distinct())
            {
                if (!RendererBounds(reference, renderer, out Bounds item)) continue;
                if (!found) { bounds = item; found = true; }
                else { bounds.Encapsulate(item.min); bounds.Encapsulate(item.max); }
            }
            return found;
        }

        private static bool RendererBounds(Transform reference, Renderer renderer, out Bounds bounds)
        {
            bounds = default(Bounds);
            Bounds local;
            if (renderer is SkinnedMeshRenderer skinned) local = skinned.localBounds;
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null) return false;
                local = filter.sharedMesh.bounds;
            }
            Matrix4x4 matrix = reference.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector3 min = local.min, max = local.max;
            bounds = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 1; i < 8; i++)
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z)));
            return true;
        }

        private static bool TrackBounds(
            SnowmobileStructure structure,
            Transform reference,
            out Bounds bounds)
        {
            bounds = default(Bounds);
            return structure?.trackRenderer != null && reference != null && AggregateBounds(
                reference,
                structure.trackRenderer.GetComponentsInChildren<Renderer>(true), out bounds);
        }

        private static bool TrackBounds(SnowmobileStructure structure, out Bounds bounds)
        {
            return TrackBounds(structure, structure?.transform, out bounds);
        }

        private static bool MeasureTrack(SnowmobileStructure structure, out float length)
        {
            return MeasureTrack(structure, structure?.transform, out length);
        }

        private static bool MeasureTrack(
            SnowmobileStructure structure,
            Transform reference,
            out float length)
        {
            length = 0f;
            TrackToSkinnedMesh track = structure?.trackRenderer?.track;
            if (track == null || !TrackBounds(structure, reference, out Bounds bounds)) return false;
            // These bounds are expressed in structure space, while the native enum is in
            // the animated mesh's local space. Longitudinal size is therefore the larger
            // horizontal extent; using the enum here swaps width/length on rotated tracks.
            length = Mathf.Max(bounds.size.x, bounds.size.z);
            return IsFinitePositive(length);
        }

        private static float LocalTrackLength(TrackToSkinnedMesh track, int axis)
        {
            if (track == null) return 0f;
            SkinnedMeshRenderer renderer = GetField<SkinnedMeshRenderer>(track, "rendererToBeConverted") ??
                                           GetField<SkinnedMeshRenderer>(track, "smr") ??
                                           track.GetComponent<SkinnedMeshRenderer>();
            Vector3 size = renderer != null ? renderer.localBounds.size : Vector3.zero;
            float length = axis == 0 ? size.x : axis == 1 ? size.y : size.z;
            return IsFinitePositive(length) ? length : 0f;
        }

        private static float LocalMeshLength(GameObject value, int axis)
        {
            MeshFilter filter = value != null ? value.GetComponent<MeshFilter>() : null;
            Vector3 size = filter?.sharedMesh != null ? filter.sharedMesh.bounds.size : Vector3.zero;
            if (size == Vector3.zero && value != null && AggregateBounds(
                    value.transform, value.GetComponentsInChildren<Renderer>(true), out Bounds bounds))
                size = bounds.size;
            float length = axis == 0 ? size.x : axis == 1 ? size.y : size.z;
            return IsFinitePositive(length) ? length : 0f;
        }

        private static bool TryAxis(object value, out int axis)
        {
            axis = -1;
            try
            {
                axis = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return axis >= 0 && axis <= 2;
            }
            catch { return false; }
        }

        private static int LongestAxis(GameObject value)
        {
            MeshFilter filter = value != null ? value.GetComponent<MeshFilter>() : null;
            Vector3 size = filter?.sharedMesh != null ? filter.sharedMesh.bounds.size : Vector3.right;
            if (filter?.sharedMesh == null && value != null && AggregateBounds(
                    value.transform, value.GetComponentsInChildren<Renderer>(true), out Bounds bounds))
                size = bounds.size;
            if (size.y >= size.x && size.y >= size.z) return 1;
            return size.z >= size.x ? 2 : 0;
        }

        private static void ScaleAtFront(Transform value, int axis, float ratio, float length)
        {
            Vector3 scale = value.localScale, position = value.localPosition;
            if (axis == 0)
                CalculateFrontAnchoredAxis(scale.x, position.x, ratio, length, out scale.x, out position.x);
            else if (axis == 1)
                CalculateFrontAnchoredAxis(scale.y, position.y, ratio, length, out scale.y, out position.y);
            else
                CalculateFrontAnchoredAxis(scale.z, position.z, ratio, length, out scale.z, out position.z);
            value.localScale = scale;
            value.localPosition = position;
        }

        private static void SetRenderers(IEnumerable<Renderer> values, bool enabled, GraftSnapshot snapshot)
        {
            foreach (Renderer value in (values ?? Array.Empty<Renderer>()).Where(x => x != null).Distinct())
            {
                if (!snapshot.renderers.Any(x => x.value == value))
                    snapshot.renderers.Add(new RendererState { value = value, enabled = value.enabled });
                value.enabled = enabled;
            }
        }

        private static void SetBehaviour(Behaviour value, bool enabled, GraftSnapshot snapshot)
        {
            if (value == null) return;
            if (!snapshot.behaviours.Any(x => x.value == value))
                snapshot.behaviours.Add(new BehaviourState { value = value, enabled = value.enabled });
            value.enabled = enabled;
        }

        private static void SaveObject(GameObject value, GraftSnapshot snapshot)
        {
            if (value != null && !snapshot.objects.Any(x => x.value == value))
                snapshot.objects.Add(new ObjectState { value = value, active = value.activeSelf });
        }

        private static void SaveTransform(Transform value, GraftSnapshot snapshot)
        {
            if (value == null || snapshot.transforms.Any(x => x.value == value)) return;
            snapshot.transforms.Add(new TransformState
            {
                value = value,
                parent = value.parent,
                position = value.localPosition,
                rotation = value.localRotation,
                scale = value.localScale
            });
        }

        private static void Add(HashSet<Renderer> set, IEnumerable<Renderer> values)
        {
            if (values == null) return;
            foreach (Renderer value in values) if (value != null) set.Add(value);
        }

        private static void AddChildren(HashSet<Renderer> set, Transform root)
        {
            if (root != null) Add(set, root.GetComponentsInChildren<Renderer>(true));
        }

        private static void AddObjects(HashSet<Renderer> set, IEnumerable<GameObject> values)
        {
            if (values == null) return;
            foreach (GameObject value in values) if (value != null) AddChildren(set, value.transform);
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static T GetField<T>(object owner, string name)
        {
            return SleddersGameBindings.GetFieldValue<T>(owner, name);
        }
    }
}
