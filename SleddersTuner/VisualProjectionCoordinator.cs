using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlpineTuning
{
    internal enum VisualProjectionTarget
    {
        GaragePreview,
        LiveSled
    }

    internal enum VisualProjectionStatus
    {
        Pending,
        Installed,
        Unavailable,
        Failed,
        Native
    }

    internal sealed class TrackAssemblyRecipe
    {
        public string selectionId;
        public string sourceIdentity;
        public string donorIdentity;
        public float canonicalLengthInches;
        public TrackGraftMode mode;
        public string assemblyHash;
    }

    internal sealed class ProjectionSnapshot
    {
        public readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
        public readonly List<object> addressableHandles = new List<object>();
        public int lifecycleRevision;
    }

    internal sealed class VisualProjectionContext
    {
        public VisualProjectionTarget target;
        public GameObject targetRoot;
        public int controllerInstanceId = int.MinValue;
        public int structureInstanceId = int.MinValue;
        public string desiredProfileChecksum;
        public int lifecycleRevision;
        public TrackAssemblyRecipe installedRecipe;
        public ProjectionSnapshot rollbackSnapshot;
        public VisualProjectionStatus status = VisualProjectionStatus.Native;
        public string unavailableReason;
    }

    /// <summary>
    /// One lifecycle owner for preview and live visual projections. The existing
    /// graft implementation remains the transactional installer; this facade is
    /// the only subsystem allowed to request, cancel, or restore its work.
    /// </summary>
    internal sealed class VisualProjectionCoordinator
    {
        private readonly AlpineVisualPartSystem _tracks;
        private readonly AlpineHeadlightDeleteProjection _headlightDelete = new AlpineHeadlightDeleteProjection();
        private readonly AlpineHeadlightDeleteProjection _garageHeadlightDelete = new AlpineHeadlightDeleteProjection();
        private readonly Dictionary<string, TrackAssemblyRecipe> _recipeCache =
            new Dictionary<string, TrackAssemblyRecipe>(StringComparer.OrdinalIgnoreCase);
        private readonly VisualProjectionContext _garage = new VisualProjectionContext
        {
            target = VisualProjectionTarget.GaragePreview
        };
        private readonly VisualProjectionContext _live = new VisualProjectionContext
        {
            target = VisualProjectionTarget.LiveSled
        };
        private string _liveDesiredVariant;

        internal VisualProjectionCoordinator(AlpineTuningMod mod)
        {
            _tracks = new AlpineVisualPartSystem(mod);
        }

        internal VisualProjectionContext GaragePreview => _garage;
        internal VisualProjectionContext LiveSled => _live;
        internal bool HasPendingChassisSwap => _tracks.HasPendingChassisSwap;
        internal bool NeedsSourceReload => _tracks.NeedsSourceReload;
        internal bool IsCompatibilityScanPending => _tracks.IsCompatibilityScanPending;

        internal void Refresh(VehicleScriptableObject target) => _tracks.Refresh(target);
        internal bool IsCompatible(string id, VehicleScriptableObject target) => _tracks.IsCompatible(id, target);
        internal bool HasAlternateNativeLength(VehicleScriptableObject target) => _tracks.HasAlternateNativeLength(target);
        internal bool HasInstalledChassis(VehicleScriptableObject target) => _tracks.HasInstalledChassis(target);
        internal bool IsInstalledVariant(string id, VehicleScriptableObject target) => _tracks.IsInstalledVariant(id, target);

        internal bool Apply(PartEffect effect, out string reason)
        {
            string requested = effect?.visualTrackVariantId;
            _liveDesiredVariant = requested;
            if (!string.IsNullOrWhiteSpace(requested) && AlpineTuningMod.ActiveSO != null &&
                _tracks.IsInstalledVariant(requested, AlpineTuningMod.ActiveSO))
            {
                _live.status = VisualProjectionStatus.Installed;
                reason = null;
                return true;
            }
            _live.lifecycleRevision++;
            _live.status = VisualProjectionStatus.Pending;
            bool applied = _tracks.Apply(effect, out reason);
            _live.status = applied
                ? (string.IsNullOrWhiteSpace(effect?.visualTrackVariantId)
                    ? VisualProjectionStatus.Native
                    : _tracks.HasPendingChassisSwap
                        ? VisualProjectionStatus.Pending
                        : VisualProjectionStatus.Installed)
                : VisualProjectionStatus.Unavailable;
            _live.unavailableReason = applied ? null : reason;
            if (applied && !string.IsNullOrWhiteSpace(requested) && AlpineTuningMod.ActiveSO != null)
            {
                string source = SledIdentity.StableIdentityKey(AlpineTuningMod.ActiveSO);
                _tracks.TryGetRecipeMetadata(requested, AlpineTuningMod.ActiveSO,
                    out string donorIdentity, out TrackGraftMode mode);
                string key = (SleddersGameBindings.GetCompatibilityReport()?.assemblyLightHash ?? "unknown") +
                             "|" + source + "|" + requested + "|" + (donorIdentity ?? "unknown");
                if (!_recipeCache.TryGetValue(key, out TrackAssemblyRecipe recipe))
                {
                    recipe = new TrackAssemblyRecipe
                    {
                        selectionId = requested,
                        sourceIdentity = source,
                        donorIdentity = donorIdentity,
                        canonicalLengthInches = effect.visualTrackLengthInches,
                        mode = mode,
                        assemblyHash = SleddersGameBindings.GetCompatibilityReport()?.assemblyLightHash
                    };
                    _recipeCache[key] = recipe;
                }
                _live.installedRecipe = recipe;
            }
            return applied;
        }

        internal void SetDesiredProfile(string checksum)
        {
            _live.desiredProfileChecksum = checksum;
        }

        internal void OnControllerInitialized(SnowmobileController controller, VehicleScriptableObject sled)
        {
            _live.lifecycleRevision++;
            _live.controllerInstanceId = controller != null ? controller.GetInstanceID() : int.MinValue;
            _live.targetRoot = controller != null ? controller.gameObject : null;
            SnowmobileStructure structure = controller != null
                ? controller.GetComponentsInChildren<SnowmobileStructure>(true).FirstOrDefault()
                : null;
            _live.structureInstanceId = structure != null ? structure.GetInstanceID() : int.MinValue;
            _live.rollbackSnapshot = new ProjectionSnapshot { lifecycleRevision = _live.lifecycleRevision };
            _live.status = VisualProjectionStatus.Pending;
            _tracks.OnControllerInitialized(controller, sled);
        }

        internal bool RequestGaragePreview(VehicleScriptableObject target, string id, Action<bool, string> completed)
        {
            int revision = ++_garage.lifecycleRevision;
            _garage.status = VisualProjectionStatus.Pending;
            return _tracks.RequestGaragePreview(target, id, (installed, reason) =>
            {
                if (revision != _garage.lifecycleRevision)
                    return;
                _garage.status = installed ? VisualProjectionStatus.Installed : VisualProjectionStatus.Failed;
                _garage.unavailableReason = installed ? null : reason;
                completed?.Invoke(installed, reason);
            });
        }

        internal void Update()
        {
            _tracks.Update();
            if (_live.status != VisualProjectionStatus.Pending || _tracks.HasPendingChassisSwap)
                return;
            if (string.IsNullOrWhiteSpace(_liveDesiredVariant))
                _live.status = VisualProjectionStatus.Native;
            else if (AlpineTuningMod.ActiveSO != null &&
                     _tracks.IsInstalledVariant(_liveDesiredVariant, AlpineTuningMod.ActiveSO))
                _live.status = VisualProjectionStatus.Installed;
            else
            {
                _live.status = VisualProjectionStatus.Failed;
                _live.unavailableReason = "Saved track recipe could not be projected onto the current native graph.";
            }
        }

        internal void VerifyLiveProjection(SnowmobileController controller, VehicleScriptableObject sled)
        {
            if (controller == null || sled == null)
                return;
            int controllerId = controller.GetInstanceID();
            if (_live.controllerInstanceId != controllerId)
                OnControllerInitialized(controller, sled);
            else if (_live.status == VisualProjectionStatus.Installed && !_tracks.HasInstalledChassis(sled))
                _live.status = VisualProjectionStatus.Pending;
        }
        internal void RollbackPendingSwap() => _tracks.RollbackPendingSwap();

        internal void RestoreTrackVisual()
        {
            _live.lifecycleRevision++;
            _tracks.RestoreTrackVisual();
            _live.status = VisualProjectionStatus.Native;
            _live.installedRecipe = null;
            _liveDesiredVariant = null;
            _live.unavailableReason = null;
        }

        internal void RestoreGaragePreview()
        {
            _garage.lifecycleRevision++;
            _tracks.RestoreGaragePreview();
            _garageHeadlightDelete.Restore();
            _garage.status = VisualProjectionStatus.Native;
            _garage.installedRecipe = null;
            _garage.unavailableReason = null;
        }

        internal void InvalidateCatalog() => _tracks.InvalidateCatalog();

        internal bool ApplyHeadlightDelete(
            SnowmobileController controller,
            IEnumerable<GameObject> headlightObjects,
            out string reason)
        {
            return _headlightDelete.Install(controller, headlightObjects, out reason);
        }

        internal void RestoreHeadlightDelete() => _headlightDelete.Restore();

        internal bool RequestGarageHeadlightDeletePreview(
            VehicleScriptableObject target,
            bool installed,
            Action<bool, string> completed)
        {
            _garageHeadlightDelete.Restore();
            int revision = ++_garage.lifecycleRevision;
            if (!SleddersGameBindings.TryRefreshGaragePreview(target, out Transform root, out _, out string reason))
            {
                completed?.Invoke(false, reason ?? "Native garage preview is unavailable.");
                return false;
            }
            if (!installed)
            {
                _garage.status = VisualProjectionStatus.Native;
                completed?.Invoke(true, "Original headlight preview restored.");
                return true;
            }
            List<GameObject> targets = FindHeadlightObjects(root);
            bool success = revision == _garage.lifecycleRevision &&
                           _garageHeadlightDelete.Install(root, targets, out reason);
            _garage.status = success ? VisualProjectionStatus.Installed : VisualProjectionStatus.Failed;
            _garage.unavailableReason = success ? null : reason;
            completed?.Invoke(success, reason);
            return success;
        }

        internal void ConfirmVariantMode(
            AlpineVisualPartSystem.NativeChassisVariant variant,
            TrackGraftMode mode,
            bool refreshUi = true)
        {
            _tracks.ConfirmVariantMode(variant, mode, refreshUi);
        }

        internal void Shutdown()
        {
            _garage.lifecycleRevision++;
            _live.lifecycleRevision++;
            _tracks.Shutdown();
            _headlightDelete.Restore();
            _garageHeadlightDelete.Restore();
            _recipeCache.Clear();
            _garage.status = VisualProjectionStatus.Native;
            _live.status = VisualProjectionStatus.Native;
        }

        private static List<GameObject> FindHeadlightObjects(Transform root)
        {
            var found = new List<GameObject>();
            if (root == null) return found;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == root) continue;
                string name = (child.name ?? string.Empty).ToLowerInvariant();
                if (name.Contains("headlight") || name.Contains("head light") ||
                    name.Contains("headlamp") || name.Contains("head lamp") || name.Contains("lamp lens"))
                    found.Add(child.gameObject);
            }
            return found;
        }
    }
}
