using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AlpineTuning
{
    /// <summary>
    /// Presentation-only donor-part projector. It never alters a native physics
    /// graph: validated rear-track grafts remain owned by VisualProjectionCoordinator.
    /// Each donor slot is independently reversible so a bad addressable or an
    /// incompatible hierarchy always leaves the native sled intact.
    /// </summary>
    internal sealed class AlpineSledForgeSystem
    {
        internal sealed class DonorCandidate
        {
            public VehicleScriptableObject sled;
            public string key;
            public string displayName;
            public int compatibilityScore;
            public bool supportsPhysics;
        }

        private sealed class RendererState
        {
            public Renderer renderer;
            public bool enabled;
        }

        private sealed class Projection
        {
            public int revision;
            public Component root;
            public SnowmobileController controller;
            public readonly List<GameObject> instances = new List<GameObject>();
            public readonly List<RendererState> hidden = new List<RendererState>();
            public readonly List<AsyncOperationHandle<GameObject>> handles = new List<AsyncOperationHandle<GameObject>>();
            public string signature;
        }

        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<ulong, Projection> _remote = new Dictionary<ulong, Projection>();
        private Projection _local;
        private GUIStyle _showcaseTagStyle;

        internal AlpineSledForgeSystem(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        internal IReadOnlyList<DonorCandidate> GetCandidates(VehicleScriptableObject source, SledForgeSlot slot)
        {
            string sourceKey = source != null ? AlpineTuningMod.GetSledKey(source) : null;
            return Resources.FindObjectsOfTypeAll<VehicleScriptableObject>()
                .Where(candidate => candidate != null && candidate.assetReference != null &&
                                    candidate.assetReference.RuntimeKeyIsValid() && !candidate.isLocked)
                .Select(candidate => new DonorCandidate
                {
                    sled = candidate,
                    key = AlpineTuningMod.GetSledKey(candidate),
                    displayName = string.IsNullOrWhiteSpace(candidate.displayName) ? candidate.name : candidate.displayName,
                    compatibilityScore = ScoreCompatibility(source, candidate, slot),
                    supportsPhysics = SlotUsesNativePhysics(slot) && ScoreCompatibility(source, candidate, slot) >= 80
                })
                .Where(candidate => !string.Equals(candidate.key, sourceKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.compatibilityScore)
                .ThenBy(candidate => candidate.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static int ScoreCompatibility(VehicleScriptableObject source, VehicleScriptableObject donor, SledForgeSlot slot)
        {
            if (source == null || donor == null || donor.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
                return 0;
            int score = 35;
            string sourceName = ((source.displayName ?? source.name) ?? string.Empty).ToLowerInvariant();
            string donorName = ((donor.displayName ?? donor.name) ?? string.Empty).ToLowerInvariant();
            foreach (string family in new[] { "rmk", "summit", "freeride", "articcat", "arctic", "lynx", "shredder", "brutal", "mxz", "backcountry" })
            {
                if (sourceName.Contains(family) && donorName.Contains(family))
                {
                    score += 40;
                    break;
                }
            }
            if (Mathf.Abs(source.skiStance - donor.skiStance) <= 75f) score += 10;
            if (Mathf.Abs(source.weight - donor.weight) <= 45f) score += 10;
            if (slot == SledForgeSlot.RearAssembly && Mathf.Abs(source.lugHeight - donor.lugHeight) <= 15f) score += 5;
            return Mathf.Clamp(score, 0, 100);
        }

        internal static bool SlotUsesNativePhysics(SledForgeSlot slot)
        {
            return slot == SledForgeSlot.FrontAssembly || slot == SledForgeSlot.RearAssembly;
        }

        internal void ApplyLocal(SnowmobileController controller, VehicleScriptableObject source, TuneProfile profile)
        {
            if (controller == null || source == null || profile?.sledBuild == null)
            {
                RestoreLocal();
                return;
            }
            profile.sledBuild.Normalize();
            Apply(ref _local, controller, controller, source, profile.sledBuild);
        }

        internal void ApplyRemote(ulong senderId, Component root, TuneProfile profile)
        {
            if (senderId == 0 || root == null || profile?.sledBuild == null || !_mod.Settings.receivePeerVisualEquipment)
            {
                ClearRemote(senderId);
                return;
            }
            profile.sledBuild.Normalize();
            _remote.TryGetValue(senderId, out Projection projection);
            VehicleScriptableObject source = SleddersGameBindings.GetVehicleFromController(root as SnowmobileController);
            if (source == null)
                SleddersGameBindings.TryGetRemoteNetworkVehicle(senderId, out source);
            Apply(ref projection, root, root as SnowmobileController, source, profile.sledBuild);
            _remote[senderId] = projection;
        }

        internal void ClearRemote(ulong senderId)
        {
            if (senderId != 0 && _remote.TryGetValue(senderId, out Projection projection))
            {
                Restore(projection);
                _remote.Remove(senderId);
            }
        }

        internal void RestoreLocal()
        {
            Restore(_local);
            _local = null;
        }

        internal void Shutdown()
        {
            RestoreLocal();
            foreach (Projection projection in _remote.Values.ToList()) Restore(projection);
            _remote.Clear();
        }

        internal void DrawShowcaseTags()
        {
            if (!_mod.Settings.showNearbyBuildTags || _mod.Sharing == null || !_mod.Settings.receiveBuildShowcase)
                return;
            Camera camera = Camera.allCameras.FirstOrDefault(item => item != null && item.isActiveAndEnabled && item.targetTexture == null);
            if (camera == null) return;
            if (_showcaseTagStyle == null)
            {
                _showcaseTagStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.78f, 0.91f, 1f, 0.95f) }
                };
            }
            foreach (RemoteActiveTuneState state in _mod.Sharing.RemoteActiveTunes)
            {
                if (state == null || !state.shareBuildShowcase || !state.supportsBuildShowcase || state.senderId == 0)
                    continue;
                if (!SleddersGameBindings.TryFindRemoteSnowmobileRoot(state.senderId, out Component root, out _) || root == null)
                    continue;
                Vector3 screen = camera.WorldToScreenPoint(root.transform.position + Vector3.up * 1.8f);
                if (screen.z <= 0f || screen.x < -160f || screen.x > Screen.width + 160f || screen.y < -50f || screen.y > Screen.height + 50f)
                    continue;
                string rider = string.IsNullOrWhiteSpace(state.senderName) ? "ALPINE RIDER" : state.senderName;
                string build = string.IsNullOrWhiteSpace(state.profileName) ? "Build" : state.profileName;
                GUI.Label(new Rect(screen.x - 150f, Screen.height - screen.y - 32f, 300f, 28f), rider + " · " + build, _showcaseTagStyle);
            }
        }

        private void Apply(ref Projection projection, Component root, SnowmobileController controller,
            VehicleScriptableObject source, SledBuildSpec spec)
        {
            string signature = BuildSignature(source, spec);
            if (projection != null && projection.root == root && string.Equals(projection.signature, signature, StringComparison.Ordinal))
                return;
            Restore(projection);
            projection = new Projection { root = root, controller = controller, signature = signature };
            if (root == null || source == null || spec.selections == null || spec.selections.Count == 0)
                return;

            foreach (SledForgePartSelection selection in spec.selections)
            {
                if (selection == null || SlotUsesNativePhysics(selection.slot))
                    continue;
                VehicleScriptableObject donor = FindDonor(selection);
                if (donor == null || donor.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
                    continue;
                int score = ScoreCompatibility(source, donor, selection.slot);
                if (score < 35)
                    continue;
                RequestSlot(projection, root, selection.slot, donor);
            }
        }

        private void RequestSlot(Projection projection, Component root, SledForgeSlot slot, VehicleScriptableObject donor)
        {
            int revision = projection.revision;
            AsyncOperationHandle<GameObject> handle;
            try { handle = donor.assetReference.LoadAssetAsync<GameObject>(); }
            catch { return; }
            if (!handle.IsValid()) return;
            projection.handles.Add(handle);
            handle.Completed += operation =>
            {
                if (projection == null || revision != projection.revision || operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                    return;
                try { InstallSlot(projection, root, slot, operation.Result); }
                catch (Exception ex) { MelonLogger.Warning("Sled Forge donor slot skipped: " + ex.GetType().Name); }
            };
        }

        private static void InstallSlot(Projection projection, Component root, SledForgeSlot slot, GameObject prefab)
        {
            Transform sourceStructure = root.GetComponentInChildren<SnowmobileStructure>(true)?.transform;
            if (sourceStructure == null) return;
            Transform anchor = FindAnchor(sourceStructure, slot) ?? sourceStructure;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, anchor);
            instance.name = "Alpine Sled Forge " + slot + " - " + prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(body);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
            foreach (Joint joint in instance.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.Destroy(joint);
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(behaviour);

            Renderer[] donorRenderers = instance.GetComponentsInChildren<Renderer>(true);
            Renderer[] selected = donorRenderers.Where(renderer => RendererMatchesSlot(renderer, slot)).ToArray();
            if (selected.Length == 0)
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }
            var selectedIds = new HashSet<int>(selected.Select(renderer => renderer.GetInstanceID()));
            foreach (Renderer renderer in donorRenderers)
                renderer.enabled = selectedIds.Contains(renderer.GetInstanceID());
            foreach (Renderer renderer in sourceStructure.GetComponentsInChildren<Renderer>(true))
            {
                if (!RendererMatchesSlot(renderer, slot)) continue;
                projection.hidden.Add(new RendererState { renderer = renderer, enabled = renderer.enabled });
                renderer.enabled = false;
            }
            projection.instances.Add(instance);
        }

        private static Transform FindAnchor(Transform root, SledForgeSlot slot)
        {
            string[] tokens = Tokens(slot);
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform != root && TokensMatch(transform.name, tokens));
        }

        private static bool RendererMatchesSlot(Renderer renderer, SledForgeSlot slot)
        {
            return renderer != null && TokensMatch(renderer.name + " " + renderer.transform.name, Tokens(slot));
        }

        private static string[] Tokens(SledForgeSlot slot)
        {
            switch (slot)
            {
                case SledForgeSlot.Hood: return new[] { "hood", "frontbody", "cowling" };
                case SledForgeSlot.Seat: return new[] { "seat", "saddle" };
                case SledForgeSlot.Bumper: return new[] { "bumper", "bar" };
                case SledForgeSlot.Handlebars: return new[] { "handle", "bar" };
                case SledForgeSlot.Skis: return new[] { "ski", "spindle" };
                case SledForgeSlot.RunningBoards: return new[] { "running", "board", "rail" };
                case SledForgeSlot.FrontAssembly: return new[] { "a-arm", "arm", "spindle", "front" };
                case SledForgeSlot.RearAssembly: return new[] { "track", "rear", "tunnel", "skid", "rail" };
                default: return new[] { "body", "chassis", "tunnel", "hood", "seat" };
            }
        }

        private static bool TokensMatch(string value, IEnumerable<string> tokens)
        {
            string lowered = (value ?? string.Empty).ToLowerInvariant();
            return tokens.Any(token => lowered.Contains(token));
        }

        private VehicleScriptableObject FindDonor(SledForgePartSelection selection)
        {
            return Resources.FindObjectsOfTypeAll<VehicleScriptableObject>().FirstOrDefault(candidate => candidate != null &&
                (string.Equals(AlpineTuningMod.GetSledKey(candidate), selection.donorSledKey, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(AlpineTuningMod.GetVehicleId(candidate), selection.donorVehicleId, StringComparison.OrdinalIgnoreCase)));
        }

        private static string BuildSignature(VehicleScriptableObject source, SledBuildSpec spec)
        {
            return (source != null ? AlpineTuningMod.GetSledKey(source) : "none") + "|" + spec.revision + "|" +
                   string.Join(";", spec.selections.Select(selection => selection.slot + ":" + selection.donorSledKey + ":" + selection.donorVehicleId));
        }

        private static void Restore(Projection projection)
        {
            if (projection == null) return;
            projection.revision++;
            foreach (RendererState state in projection.hidden)
                if (state?.renderer != null) state.renderer.enabled = state.enabled;
            foreach (GameObject instance in projection.instances)
                if (instance != null) UnityEngine.Object.Destroy(instance);
            foreach (AsyncOperationHandle<GameObject> handle in projection.handles)
                if (handle.IsValid()) Addressables.Release(handle);
            projection.hidden.Clear();
            projection.instances.Clear();
            projection.handles.Clear();
        }
    }
}
