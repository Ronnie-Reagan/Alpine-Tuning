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
            public Transform structure;
            public SnowmobileController controller;
            public readonly List<GameObject> instances = new List<GameObject>();
            public readonly List<RendererState> hidden = new List<RendererState>();
            public readonly List<AsyncOperationHandle<GameObject>> handles = new List<AsyncOperationHandle<GameObject>>();
            public readonly List<Material> materials = new List<Material>();
            public readonly HashSet<SledForgeSlot> selectedSlots = new HashSet<SledForgeSlot>();
            public readonly Dictionary<SledForgeSlot, SlotState> slots = new Dictionary<SledForgeSlot, SlotState>();
            public string signature;
        }

        private sealed class SlotState
        {
            public SledForgePartSelection selection;
            public string status = "Waiting";
            public string reason;
            public int attempts;
            public float retryAt;
            public int loadRevision;
            public float loadDeadline;
            public AsyncOperationHandle<GameObject> pendingHandle;
            public readonly List<Transform> fitHosts = new List<Transform>();
            public readonly Dictionary<Transform, Vector3> mounts = new Dictionary<Transform, Vector3>();
        }

        internal string GarageSlotStatus(SledForgeSlot slot)
        {
            if (_garage == null || !_garage.slots.TryGetValue(slot, out var state)) return "Native appearance";
            return state.status + (string.IsNullOrWhiteSpace(state.reason) ? string.Empty : " · " + state.reason);
        }

        internal void RetryGaragePreview()
        {
            Restore(_garage);
            _garage = null;
            _nextGarageProbe = 0f;
            UpdateGaragePreview();
        }

        internal void UpdateRuntimeProjections()
        {
            if (_local != null && _local.root == null) RestoreLocal();
            foreach (ulong sender in _remote.Where(pair => pair.Value == null || pair.Value.root == null).Select(pair => pair.Key).ToList())
                ClearRemote(sender);
            RefreshProjection(_local);
            foreach (Projection projection in _remote.Values.ToList()) RefreshProjection(projection);
        }

        private void RefreshProjection(Projection projection)
        {
            if (projection == null || projection.root == null) return;
            foreach (RendererState state in projection.hidden) AlpineVisualAnchors.Hide(state.renderer, projection);
            foreach (SlotState state in projection.slots.Values.ToList())
            {
                if (state.status == "Loading" && Time.unscaledTime >= state.loadDeadline)
                {
                    state.loadRevision++;
                    projection.handles.Remove(state.pendingHandle);
                    if (state.pendingHandle.IsValid()) Addressables.Release(state.pendingHandle);
                    SlotFailed(state, "Donor load timed out");
                }
                if (state.status == "Retrying" && Time.unscaledTime >= state.retryAt)
                    RequestSlot(projection, projection.root, state);
            }
        }

        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<ulong, Projection> _remote = new Dictionary<ulong, Projection>();
        private readonly Dictionary<string, int> _candidatePages = new Dictionary<string, int>();
        private Projection _local;
        private Projection _garage;
        private VehicleScriptableObject _garageTarget;
        private TuneProfile _garageProfile;
        private float _nextGarageProbe;
        private VehicleSelectionUiController _garageOwner;

        internal void PreviewNativeGarage(VehicleSelectionUiController owner, VehicleScriptableObject target)
        {
            _garageOwner = owner;
            if (AlpineNativeUi.IsGarageTuningOpen || target == null) return;
            TuneProfile profile = _mod.Store.GetCurrentSetupForSled(AlpineTuningMod.GetSledKey(target), AlpineTuningMod.GetVehicleId(target)) ??
                _mod.Store.GetActiveProfileForSled(AlpineTuningMod.GetSledKey(target), AlpineTuningMod.GetVehicleId(target));
            PreviewGarage(target, profile);
        }

        internal void PreviewGarage(VehicleScriptableObject target, TuneProfile profile)
        {
            _garageTarget = target;
            _garageProfile = profile;
            if (profile?.sledBuild == null || profile.baseline == AlpineSetupBaseline.SleddersDefault)
            {
                Restore(_garage);
                _garage = null;
                _garageProfile = null;
            }
            _nextGarageProbe = 0f;
            UpdateGaragePreview();
        }

        internal void UpdateGaragePreview()
        {
            if (_garage != null && _garage.root == null)
            {
                Restore(_garage);
                _garage = null;
            }
            if (!AlpineNativeUi.IsGarageTuningOpen &&
                (_garageOwner == null || !_garageOwner.gameObject.activeInHierarchy))
            {
                RestoreGaragePreview();
                return;
            }
            if (_garageTarget == null || _garageProfile?.sledBuild == null || Time.unscaledTime < _nextGarageProbe)
                return;
            _nextGarageProbe = Time.unscaledTime + 0.25f;
            if (SleddersGameBindings.TryGetGaragePreviewContext(_garageTarget, out Transform root, out _, out _))
            {
                SnowmobileStructure structure = root.GetComponentInChildren<SnowmobileStructure>(true);
                if (structure != null)
                    Apply(ref _garage, structure, null, _garageTarget, _garageProfile.sledBuild);
            }
        }

        internal void RestoreGaragePreview()
        {
            Restore(_garage);
            _garage = null;
            _garageTarget = null;
            _garageProfile = null;
            _garageOwner = null;
        }
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

        internal int CandidatePage(VehicleScriptableObject source, SledForgeSlot slot, int pageCount)
        {
            string key = AlpineTuningMod.GetSledKey(source) + ":" + slot;
            _candidatePages.TryGetValue(key, out int page);
            return Mathf.Clamp(page, 0, Math.Max(0, pageCount - 1));
        }

        internal void AdvanceCandidatePage(VehicleScriptableObject source, SledForgeSlot slot, int pageCount)
        {
            if (pageCount < 1) return;
            string key = AlpineTuningMod.GetSledKey(source) + ":" + slot;
            _candidatePages[key] = (CandidatePage(source, slot, pageCount) + 1) % pageCount;
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

        internal void ClearAllRemote()
        {
            foreach (Projection projection in _remote.Values.ToList()) Restore(projection);
            _remote.Clear();
        }

        internal void Shutdown()
        {
            RestoreLocal();
            RestoreGaragePreview();
            ClearAllRemote();
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
            Transform structure = root != null ? root.GetComponentInChildren<SnowmobileStructure>(true)?.transform : null;
            if (projection != null && projection.root == root && projection.structure == structure &&
                string.Equals(projection.signature, signature, StringComparison.Ordinal))
            {
                foreach (SledForgePartSelection selection in spec.selections)
                    if (projection.slots.TryGetValue(selection.slot, out var state))
                    {
                        state.selection = selection;
                        ApplyFit(state);
                    }
                RefreshProjection(projection);
                return;
            }
            Restore(projection);
            projection = new Projection { root = root, structure = structure, controller = controller, signature = signature };
            if (root == null || source == null || spec.selections == null || spec.selections.Count == 0)
                return;

            foreach (SledForgePartSelection selection in spec.selections)
                if (selection != null) projection.selectedSlots.Add(selection.slot);

            foreach (SledForgePartSelection selection in spec.selections)
            {
                if (selection == null || SlotUsesNativePhysics(selection.slot))
                    continue;
                var state = new SlotState { selection = selection };
                projection.slots.Add(selection.slot, state);
                RequestSlot(projection, root, state);
            }
        }

        private void RequestSlot(Projection projection, Component root, SlotState state)
        {
            VehicleScriptableObject donor = FindDonor(state.selection);
            state.attempts++;
            if (donor == null || donor.isLocked || donor.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
            {
                SlotFailed(state, "Donor asset unavailable");
                return;
            }
            state.status = "Loading";
            state.reason = null;
            int revision = projection.revision;
            AsyncOperationHandle<GameObject> handle;
            try { handle = Addressables.LoadAssetAsync<GameObject>(donor.assetReference.RuntimeKey); }
            catch (Exception ex) { SlotFailed(state, "Load rejected: " + ex.GetType().Name); return; }
            if (!handle.IsValid()) { SlotFailed(state, "Invalid load handle"); return; }
            int loadRevision = ++state.loadRevision;
            state.pendingHandle = handle;
            state.loadDeadline = Time.unscaledTime + 15f;
            projection.handles.Add(handle);
            handle.Completed += operation =>
            {
                if (revision != projection.revision || loadRevision != state.loadRevision || root == null)
                    return;
                if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                {
                    projection.handles.Remove(handle);
                    if (handle.IsValid()) Addressables.Release(handle);
                    SlotFailed(state, "Asset load failed");
                    return;
                }
                int objectsBefore = projection.instances.Count, materialsBefore = projection.materials.Count;
                int hiddenBefore = projection.hidden.Count;
                try
                {
                    InstallSlot(projection, root, state, operation.Result);
                    state.status = "Installed";
                }
                catch (Exception ex)
                {
                    RollbackSlot(projection, objectsBefore, materialsBefore, hiddenBefore);
                    state.fitHosts.Clear();
                    projection.handles.Remove(handle);
                    if (handle.IsValid()) Addressables.Release(handle);
                    state.status = "Native fallback";
                    state.reason = ex is InvalidOperationException ? ex.Message : ex.GetType().Name;
                    MelonLogger.Warning("Sled Forge " + state.selection.slot + " retained native appearance: " + state.reason);
                }
            };
        }

        private static void SlotFailed(SlotState state, string reason)
        {
            state.status = state.attempts < 3 ? "Retrying" : "Native fallback";
            state.reason = reason;
            state.retryAt = Time.unscaledTime + state.attempts * 2f;
        }

        private static void InstallSlot(Projection projection, Component root, SlotState state, GameObject prefab)
        {
            SledForgeSlot slot = state.selection.slot;
            Transform sourceStructure = root.GetComponentInChildren<SnowmobileStructure>(true)?.transform;
            if (sourceStructure == null) throw new InvalidOperationException("Source structure unavailable");
            // Capture native renderers BEFORE parenting any donor beneath the source.
            Renderer[] nativeRenderers = sourceStructure.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !AlpineVisualAnchors.Projected(renderer))
                .ToArray();
            GameObject host = new GameObject("Alpine Forge staging");
            host.SetActive(false);
            GameObject instance;
            try { instance = UnityEngine.Object.Instantiate(prefab, host.transform); }
            finally { UnityEngine.Object.Destroy(host); }
            projection.instances.Add(instance);
            AlpineVisualAnchors.Own(instance.transform);
            instance.SetActive(false);
            instance.name = "Alpine Sled Forge " + slot + " - " + prefab.name;
            instance.transform.SetParent(sourceStructure, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            SnowmobileStructure donorGraph = instance.GetComponentInChildren<SnowmobileStructure>(true);
            SnowmobileStructure sourceGraph = sourceStructure.GetComponent<SnowmobileStructure>();
            Transform donorStructure = donorGraph != null ? donorGraph.transform : null;
            if (donorStructure == null) throw new InvalidOperationException("Donor structure unavailable");
            // Use the native structure frame directly. Prefab ancestors can carry
            // translation, rotation AND scale; cancelling only their world rotation
            // leaves donor panels offset or scaled on some sled families.
            if (donorStructure != instance.transform)
                donorStructure.SetParent(instance.transform, false);
            donorStructure.localPosition = Vector3.zero;
            donorStructure.localRotation = Quaternion.identity;
            donorStructure.localScale = Vector3.one;
            var nativeToHide = new HashSet<Renderer>();
            var movingRenderers = new HashSet<Renderer>();
            bool movingSlot = slot == SledForgeSlot.Skis || slot == SledForgeSlot.Handlebars;
            if (movingSlot)
            {
                foreach (AlpineVisualAnchorRole role in AlpineVisualAnchors.Roles(slot))
                {
                    Transform donorAnchor = AlpineVisualAnchors.Native(donorGraph, role);
                    Transform sourceAnchor = AlpineVisualAnchors.Native(sourceGraph, role);
                    if (donorAnchor == null || sourceAnchor == null || !AlpineVisualAnchors.Under(donorAnchor, donorStructure) ||
                        !AlpineVisualAnchors.ActiveBelow(donorAnchor, donorStructure)) continue;
                    Renderer[] donorParts = AlpineForgeGeometry.HighestDetail(donorAnchor.GetComponentsInChildren<Renderer>(true)
                        .Where(item => AlpineVisualAnchors.ActiveBelow(item.transform, donorAnchor)));
                    Renderer[] nativeParts = nativeRenderers.Where(item => AlpineVisualAnchors.Under(item.transform, sourceAnchor) &&
                        AlpineVisualAnchors.ActiveBelow(item.transform, sourceAnchor)).ToArray();
                    if (donorParts.Length == 0 || nativeParts.Length == 0 || donorParts.Any(item => !(item is MeshRenderer))) continue;
                    GameObject pivotHost = new GameObject("Alpine Forge " + role);
                    pivotHost.SetActive(false);
                    pivotHost.transform.SetParent(sourceAnchor, false);
                    projection.instances.Add(pivotHost);
                    AlpineVisualAnchors.Own(pivotHost.transform);
                    donorAnchor.SetParent(pivotHost.transform, false);
                    donorAnchor.localPosition = Vector3.zero;
                    donorAnchor.localRotation = Quaternion.identity;
                    donorAnchor.localScale = Vector3.one;
                    state.fitHosts.Add(pivotHost.transform);
                    movingRenderers.UnionWith(donorParts);
                    nativeToHide.UnionWith(nativeParts);
                }
                if (movingRenderers.Count == 0) throw new InvalidOperationException("No unambiguous rigid moving-part pivots");
                int expected = slot == SledForgeSlot.Skis ? 2 : 1;
                if (state.fitHosts.Count < expected) state.reason = "Unmapped moving parts remain native";
            }
            var protectedDonorParts = new HashSet<Renderer>(instance.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => AlpineVisualAnchors.Moving(renderer, donorGraph)));
            var donorSlotParts = SlotMeshes(donorGraph, slot, projection.selectedSlots);
            var nativeSlotParts = SlotMeshes(sourceGraph, slot, projection.selectedSlots);
            Renderer[] donorRenderers = instance.GetComponentsInChildren<Renderer>(true).Concat(movingRenderers).Distinct().ToArray();
            Renderer[] selected = movingSlot ? movingRenderers.ToArray() : AlpineForgeGeometry.HighestDetail(donorRenderers.Where(renderer =>
                AlpineVisualAnchors.Under(renderer.transform, donorStructure) && !protectedDonorParts.Contains(renderer) &&
                AlpineVisualAnchors.ActiveBelow(renderer.transform, donorStructure) && donorSlotParts.Contains(renderer)));
            if (selected.Length == 0) throw new InvalidOperationException("No separate donor meshes for this slot");
            if (selected.Any(renderer => !(renderer is MeshRenderer)))
                throw new InvalidOperationException("Slot requires an independently articulated or skinned assembly");
            if (!movingSlot)
            {
                Renderer[] nativeParts = nativeRenderers.Where(renderer => nativeSlotParts.Contains(renderer) &&
                    !AlpineVisualAnchors.Moving(renderer, sourceGraph) && AlpineVisualAnchors.ActiveBelow(renderer.transform, sourceStructure)).ToArray();
                if (nativeParts.Length == 0) throw new InvalidOperationException("Native slot is combined with other panels or cannot be identified");
                // Front and rear bumpers have distinct mounts. Never align their
                // union as a single assembly on sleds of different lengths.
                foreach (bool rear in slot == SledForgeSlot.Bumper ? new[] { false, true } : new[] { false })
                {
                    Renderer[] donorGroup = selected.Where(renderer => slot != SledForgeSlot.Bumper || AlpineForgeGeometry.RearBumper(renderer.name) == rear).ToArray();
                    Renderer[] sourceGroup = nativeParts.Where(renderer => slot != SledForgeSlot.Bumper || AlpineForgeGeometry.RearBumper(renderer.name) == rear).ToArray();
                    if (donorGroup.Length == 0 && sourceGroup.Length == 0) continue;
                    if (donorGroup.Length == 0 || sourceGroup.Length == 0 ||
                        !AlpineForgeGeometry.TryBounds(sourceStructure, AlpineForgeGeometry.HighestDetail(sourceGroup), out Bounds sourceBounds) ||
                        !AlpineForgeGeometry.TryBounds(donorStructure, donorGroup, out Bounds donorBounds) ||
                        !AlpineForgeGeometry.TryScale(sourceBounds, donorBounds, slot, out Vector3 automaticScale))
                        throw new InvalidOperationException("Recipient and donor mounts cannot be matched safely");
                    GameObject fitHost = new GameObject("Alpine Forge recipient mount " + slot);
                    fitHost.SetActive(false);
                    fitHost.transform.SetParent(sourceStructure, false);
                    projection.instances.Add(fitHost);
                    AlpineVisualAnchors.Own(fitHost.transform);
                    state.fitHosts.Add(fitHost.transform);
                    state.mounts[fitHost.transform] = AlpineForgeGeometry.Mount(sourceBounds, slot);
                    GameObject alignment = new GameObject("Donor geometry alignment");
                    alignment.transform.SetParent(fitHost.transform, false);
                    foreach (Renderer renderer in donorGroup)
                    {
                        // Preserve the complete authored mesh transform, including
                        // model import rotations and centimetre-to-metre scales.
                        renderer.transform.SetParent(alignment.transform, true);
                    }
                    alignment.transform.localScale = automaticScale;
                    alignment.transform.localPosition = -Vector3.Scale(AlpineForgeGeometry.Mount(donorBounds, slot), automaticScale);
                    nativeToHide.UnionWith(sourceGroup);
                }
            }
            foreach (Joint joint in instance.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(joint);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (Light light in instance.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach (AudioSource audio in instance.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
            // Moving branches have been detached into owned pivot hosts. Strip
            // every newly created host, not just the donor's original root.
            foreach (Transform fitHost in state.fitHosts)
            {
                foreach (Joint joint in fitHost.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(joint);
                foreach (Collider collider in fitHost.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (Rigidbody body in fitHost.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                foreach (MonoBehaviour behaviour in fitHost.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (Animator animator in fitHost.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                foreach (LODGroup group in fitHost.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
                foreach (Light light in fitHost.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
                foreach (AudioSource audio in fitHost.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
            }

            var selectedIds = new HashSet<int>(selected.Select(renderer => renderer.GetInstanceID()));
            foreach (Renderer renderer in donorRenderers)
                renderer.enabled = selectedIds.Contains(renderer.GetInstanceID());
            foreach (Renderer renderer in selected)
            {
                // Own material copies: never change shared native/prefab assets.
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    Material material = new Material(materials[i]);
                    foreach (string property in new[] { "_Cull", "_CullMode", "_CullModeForward" })
                        if (material.HasProperty(property)) material.SetFloat(property, 0f);
                    materials[i] = material;
                    projection.materials.Add(material);
                }
                renderer.sharedMaterials = materials;
            }
            ApplyFit(state);
            instance.SetActive(true);
            foreach (Transform fitHost in state.fitHosts) fitHost.gameObject.SetActive(true);
            if (!selected.Any(renderer => renderer.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Donor meshes are inactive");
            foreach (Renderer renderer in nativeRenderers)
            {
                if (!nativeToHide.Contains(renderer)) continue;
                // A renderer may match more than one slot. Snapshot its original
                // state once so restoring overlapping slots cannot leave it hidden.
                if (!projection.hidden.Any(entry => entry.renderer == renderer))
                    projection.hidden.Add(new RendererState { renderer = renderer, enabled = renderer.enabled });
                AlpineVisualAnchors.Hide(renderer, projection);
            }
        }

        private static void ApplyFit(SlotState state)
        {
            SledForgeFit fit = state.selection.fit;
            foreach (Transform host in state.fitHosts)
            {
                if (host == null) continue;
                state.mounts.TryGetValue(host, out Vector3 mount);
                host.localPosition = mount + (fit?.position?.ToVector3() ?? Vector3.zero);
                host.localRotation = Quaternion.Euler(fit?.rotation?.ToVector3() ?? Vector3.zero);
                host.localScale = Vector3.one * (fit?.scale ?? 1f);
            }
        }

        private static void RollbackSlot(Projection projection, int objectsBefore, int materialsBefore, int hiddenBefore)
        {
            for (int i = projection.hidden.Count - 1; i >= hiddenBefore; i--)
            {
                AlpineVisualAnchors.Release(projection.hidden[i].renderer, projection);
                projection.hidden.RemoveAt(i);
            }
            for (int i = projection.instances.Count - 1; i >= objectsBefore; i--)
            {
                GameObject item = projection.instances[i];
                if (item != null)
                {
                    AlpineVisualAnchors.Disown(item.transform);
                    item.SetActive(false);
                    UnityEngine.Object.Destroy(item);
                }
                projection.instances.RemoveAt(i);
            }
            for (int i = projection.materials.Count - 1; i >= materialsBefore; i--)
            {
                if (projection.materials[i] != null) UnityEngine.Object.Destroy(projection.materials[i]);
                projection.materials.RemoveAt(i);
            }
        }

        private static bool RendererMatchesProjectionSlot(Renderer renderer, SledForgeSlot slot,
            HashSet<SledForgeSlot> selectedSlots)
        {
            if (!RendererMatchesSlot(renderer, slot)) return false;
            // A dedicated selection owns its panels even if its load fails: native
            // fallback remains visible rather than being hidden by the body shell.
            return slot != SledForgeSlot.BodyShell || !selectedSlots.Any(other =>
                other != SledForgeSlot.BodyShell && !SlotUsesNativePhysics(other) &&
                RendererMatchesSlot(renderer, other));
        }

        private static HashSet<Renderer> SlotMeshes(SnowmobileStructure structure, SledForgeSlot slot,
            HashSet<SledForgeSlot> selectedSlots)
        {
            var result = new HashSet<Renderer>();
            if (structure == null) return result;
            Renderer[] all = structure.GetComponentsInChildren<Renderer>(true)
                .ToArray();
            result.UnionWith(all.Where(renderer => RendererMatchesProjectionSlot(renderer, slot, selectedSlots)));
            // Names provide semantic assemblies; use native paint names only for
            // otherwise opaque hood/seat meshes, never as whole assembly lists.
            string field = slot == SledForgeSlot.Hood ? "hoodPartNames" : slot == SledForgeSlot.Seat ? "seatPartNames" : null;
            string[] names = field == null ? null : SleddersGameBindings.GetFieldValue<string[]>(structure, field);
            if (names != null)
            {
                var registered = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                result.UnionWith(all.Where(renderer => registered.Contains(renderer.name) &&
                    !new[] { SledForgeSlot.Hood, SledForgeSlot.Seat, SledForgeSlot.RunningBoards, SledForgeSlot.Bumper, SledForgeSlot.BodyShell }
                        .Any(other => other != slot && other != SledForgeSlot.BodyShell && AlpineForgeGeometry.Matches(renderer.name, other)) &&
                    renderer.name.IndexOf("tank", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("belly", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("tunnel", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) < 0));
            }
            if (slot == SledForgeSlot.BodyShell)
                foreach (SledForgeSlot other in selectedSlots.Where(other => other != SledForgeSlot.BodyShell && !SlotUsesNativePhysics(other)))
                    result.ExceptWith(SlotMeshes(structure, other, selectedSlots));
            return result;
        }

        private static bool RendererMatchesSlot(Renderer renderer, SledForgeSlot slot)
        {
            return renderer != null && AlpineForgeGeometry.Matches(renderer.name, slot);
        }

        private VehicleScriptableObject FindDonor(SledForgePartSelection selection)
        {
            return Resources.FindObjectsOfTypeAll<VehicleScriptableObject>().FirstOrDefault(candidate => candidate != null &&
                (string.Equals(AlpineTuningMod.GetSledKey(candidate), selection.donorSledKey, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(AlpineTuningMod.GetVehicleId(candidate), selection.donorVehicleId, StringComparison.OrdinalIgnoreCase)));
        }

        private static string BuildSignature(VehicleScriptableObject source, SledBuildSpec spec)
        {
            return (source != null ? AlpineTuningMod.GetSledKey(source) : "none") + "|" +
                   string.Join(";", spec.selections.Select(selection => selection.slot + ":" + selection.donorSledKey + ":" + selection.donorVehicleId));
        }

        private static void Restore(Projection projection)
        {
            if (projection == null) return;
            projection.revision++;
            RollbackSlot(projection, 0, 0, 0);
            foreach (AsyncOperationHandle<GameObject> handle in projection.handles)
                if (handle.IsValid()) Addressables.Release(handle);
            projection.handles.Clear();
            projection.slots.Clear();
        }
    }
}
