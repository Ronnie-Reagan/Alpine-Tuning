using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AlpineTuning
{
    /// <summary>
    /// Independently gated local experiments. Every mutation is snapshotted and
    /// reversible; no network-owned identifier or entitlement state is changed.
    /// </summary>
    internal sealed class AlpineExperimentalSystems
    {
        private readonly AlpineTuningMod _mod;
        private readonly AlpineControllerInput _input = new AlpineControllerInput();
        private readonly List<VehicleListSnapshot> _vehicleLists = new List<VehicleListSnapshot>();
        private AlpineWalker _walker;
        private SnowmobileController _controller;
        private bool _hiddenVehiclesInstalled;
        private bool _warningLogged;
        private AsyncOperationHandle<GameObject> _propHandle;
        private bool _propHandleValid;
        private bool _propLoading;
        private int _propRevision;
        private GameObject _propInstance;
        private string _installedPropKey;
        private string _installedPropSettingsSignature;
        private Rigidbody _propBody;
        private float _propBodyMass;
        private Vector3 _propBodyCenterOfMass;
        private readonly List<RendererState> _sourceRenderers = new List<RendererState>();

        internal sealed class PropCandidate
        {
            public string key;
            public string displayName;
            public LevelPropScriptableObject asset;
            public bool isSledProp;
        }

        private sealed class RendererState
        {
            public Renderer renderer;
            public bool enabled;
        }

        private sealed class VehicleListSnapshot
        {
            public VehicleListScriptableObject owner;
            public object vehicles;
        }

        internal AlpineExperimentalSystems(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        internal bool IsWalking => _walker != null;
        internal IReadOnlyList<PropCandidate> PropCandidates => DiscoverPropCandidates();

        internal void OnControllerInitialized(SnowmobileController controller)
        {
            if (_walker != null)
                Remount();
            RestorePropProjection();
            _controller = controller;
        }

        internal void OnSledReset(SnowmobileController controller)
        {
            _controller = controller;
            if (_walker != null && controller != null)
                _walker.Teleport(controller.transform.position + controller.transform.right * 1.2f + Vector3.up * 0.5f);
        }

        internal void Update()
        {
            AlpineUserSettings settings = _mod.Settings;
            if ((settings.experimentalHiddenVehicles || settings.experimentalPropVehicles ||
                 settings.experimentalTrackCompatibility) && !_warningLogged)
            {
                _warningLogged = true;
                MelonLogger.Warning("Alpine experimental systems are local-only and can visually desync from other clients.");
            }

            if (settings.experimentalHiddenVehicles && !_hiddenVehiclesInstalled)
                InstallHiddenVehicles();
            else if (!settings.experimentalHiddenVehicles && _hiddenVehiclesInstalled)
                RestoreHiddenVehicles();

            UpdatePropProjection(settings);
            // Dismount/walking was removed from the public surface after live
            // testing showed that native rider and camera hierarchies cannot be
            // safely detached as a generic projection. Never leave a rider clone
            // active when loading settings created by an earlier experimental build.
            if (_walker != null) Remount();
        }

        internal void LateUpdate()
        {
            _walker?.LateUpdate();
        }

        internal void SuspendRuntime()
        {
            if (_walker != null)
                Remount();
            RestoreHiddenVehicles();
            RestorePropProjection();
        }

        internal void Shutdown()
        {
            SuspendRuntime();
            _controller = null;
        }

        internal void OnSceneChanged()
        {
            SuspendRuntime();
            _controller = null;
        }

        private bool BindingPressed(string keyboard, string controller)
        {
            bool key = Enum.TryParse(keyboard, true, out KeyCode parsed) && Input.GetKeyDown(parsed);
            return key || _input.BindingPressed(controller);
        }

        private void UpdatePropProjection(AlpineUserSettings settings)
        {
            if (!settings.experimentalPropVehicles || string.IsNullOrWhiteSpace(settings.experimentalPropAssetKey) || _controller == null)
            {
                if (_propInstance != null || _propLoading) RestorePropProjection();
                return;
            }
            string requestedSignature = PropSettingsSignature(settings);
            if (_propInstance != null && string.Equals(_installedPropKey, settings.experimentalPropAssetKey, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_installedPropSettingsSignature, requestedSignature, StringComparison.Ordinal))
                    return;
                RestorePropProjection();
            }
            if (_propLoading) return;

            PropCandidate candidate = DiscoverPropCandidates().FirstOrDefault(item =>
                string.Equals(item.key, settings.experimentalPropAssetKey, StringComparison.OrdinalIgnoreCase));
            if (candidate?.asset == null)
            {
                DisableFailedPropSelection(settings.experimentalPropAssetKey);
                return;
            }
            RestorePropProjection();
            try
            {
                _propLoading = true;
                AsyncOperationHandle<GameObject> handle = candidate.asset.asset.LoadAssetAsync<GameObject>();
                if (!handle.IsValid())
                    throw new InvalidOperationException("invalid prop addressable handle");
                _propHandle = handle;
                _propHandleValid = true;
                int revision = ++_propRevision;
                string requestedKey = candidate.key;
                _propHandle.Completed += operation =>
                {
                    if (revision != _propRevision)
                        return;
                    _propLoading = false;
                    if (!_mod.Settings.experimentalPropVehicles || _controller == null ||
                        !string.Equals(_mod.Settings.experimentalPropAssetKey, requestedKey, StringComparison.OrdinalIgnoreCase) ||
                        operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                    {
                        RestorePropProjection();
                        return;
                    }
                InstallProp(operation.Result, requestedKey, candidate.isSledProp);
                };
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("World-prop load rejected: " + ex.GetType().Name);
                RestorePropProjection();
                DisableFailedPropSelection(settings.experimentalPropAssetKey);
            }
        }

        private IReadOnlyList<PropCandidate> DiscoverPropCandidates()
        {
            return Resources.FindObjectsOfTypeAll<LevelPropScriptableObject>()
                .Where(prop => prop != null && !prop.isGpuInstanced && !prop.providesFuelStation &&
                               prop.asset != null && prop.asset.RuntimeKeyIsValid() &&
                               prop.baseBoundsMaxExtent > 0.05f && prop.baseBoundsMaxExtent < 80f)
                .Select(prop => new PropCandidate
                {
                    key = prop.asset.RuntimeKey.ToString(),
                    displayName = string.IsNullOrWhiteSpace(prop.displayName) ? prop.name : prop.displayName,
                    asset = prop,
                    isSledProp = IsStaticSledProp(string.IsNullOrWhiteSpace(prop.displayName) ? prop.name : prop.displayName)
                })
                .GroupBy(item => item.key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void InstallProp(GameObject prefab, string key, bool isSledProp)
        {
            try
            {
                _propBody = _controller.GetComponentInChildren<Rigidbody>();
                if (_propBody == null) throw new InvalidOperationException("missing source rigidbody");
                _propBodyMass = _propBody.mass;
                _propBodyCenterOfMass = _propBody.centerOfMass;
                _propInstance = UnityEngine.Object.Instantiate(prefab, _propBody.transform);
                _propInstance.name = "Alpine Prop Vehicle - " + prefab.name;
                AlpineUserSettings settings = _mod.Settings;
                _propInstance.transform.localPosition = settings.experimentalPropPosition.ToVector3();
                _propInstance.transform.localRotation = Quaternion.Euler(settings.experimentalPropRotation.ToVector3());
                _propInstance.transform.localScale = settings.experimentalPropScale.ToVector3();

                foreach (Rigidbody rigidbody in _propInstance.GetComponentsInChildren<Rigidbody>(true))
                    if (rigidbody != _propBody) UnityEngine.Object.Destroy(rigidbody);
                foreach (Joint joint in _propInstance.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.Destroy(joint);
                // The native sled remains the sole collision and drivetrain graph.
                // Removing prop colliders makes map vehicles and scenery safe visual
                // bodies rather than attaching unknown compound physics to the sled.
                foreach (Collider collider in _propInstance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
                foreach (MonoBehaviour behaviour in _propInstance.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(behaviour);

                SnowmobileStructure sourceStructure = _controller.GetComponentInChildren<SnowmobileStructure>(true);
                if (sourceStructure == null)
                    throw new InvalidOperationException("missing source sled structure");
                HashSet<string> boundMotionGroups = isSledProp
                    ? BindSledPropMotionGroups(_propInstance, sourceStructure)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Renderer renderer in sourceStructure.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.transform.IsChildOf(_propInstance.transform)) continue;
                    if (isSledProp && IsSourceMotionRenderer(renderer, out string group) &&
                        !boundMotionGroups.Contains(group))
                        continue;
                    _sourceRenderers.Add(new RendererState { renderer = renderer, enabled = renderer.enabled });
                    renderer.enabled = false;
                }
                _propBody.mass = Mathf.Clamp(settings.experimentalPropMassKg, 50f, 1000f);
                _propBody.centerOfMass = settings.experimentalPropCenterOfMass.ToVector3();
                _installedPropKey = key;
                _installedPropSettingsSignature = PropSettingsSignature(settings);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("World-prop projection rolled back: " + ex.GetType().Name);
                RestorePropProjection();
                DisableFailedPropSelection(key);
            }
        }

        private void DisableFailedPropSelection(string key)
        {
            AlpineUserSettings settings = _mod.Settings;
            if (!string.Equals(settings.experimentalPropAssetKey, key, StringComparison.OrdinalIgnoreCase))
                return;
            settings.experimentalPropAssetKey = null;
            settings.experimentalPropVehicles = false;
            _mod.SaveSettings();
            AlpineNativeUi.RefreshAttachedGarage();
        }

        private static bool IsStaticSledProp(string value)
        {
            string name = (value ?? string.Empty).ToLowerInvariant();
            return name.Contains("snowmobile") || name.Contains("sled") ||
                   name.Contains("ski-doo") || name.Contains("skidoo") ||
                   name.Contains("polaris") || name.Contains("articcat") ||
                   name.Contains("arctic cat") || name.Contains("lynx") ||
                   name.Contains("rmk") || name.Contains("summit") ||
                   name.Contains("freeride") || name.Contains("shredder") ||
                   name.Contains("brutal") || name.Contains("backcountry") ||
                   name.Contains("mxz");
        }

        private static HashSet<string> BindSledPropMotionGroups(GameObject prop, SnowmobileStructure source)
        {
            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[]> definition in MotionGroups())
            {
                Transform propGroup = FindNamedTransform(prop.transform, definition.Value);
                Transform sourceAnchor = FindNamedTransform(source.transform, definition.Value);
                if (propGroup == null || sourceAnchor == null || propGroup == prop.transform)
                    continue;
                propGroup.SetParent(sourceAnchor, true);
                bound.Add(definition.Key);
            }
            return bound;
        }

        private static bool IsSourceMotionRenderer(Renderer renderer, out string group)
        {
            group = null;
            string name = (renderer.name + " " + renderer.transform.name).ToLowerInvariant();
            foreach (KeyValuePair<string, string[]> definition in MotionGroups())
            {
                if (definition.Value.Any(token => name.Contains(token)))
                {
                    group = definition.Key;
                    return true;
                }
            }
            return false;
        }

        private static Transform FindNamedTransform(Transform root, IEnumerable<string> tokens)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate != null && candidate != root &&
                    tokens.Any(token => candidate.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static IEnumerable<KeyValuePair<string, string[]>> MotionGroups()
        {
            yield return new KeyValuePair<string, string[]>("skis", new[] { "ski", "spindle" });
            yield return new KeyValuePair<string, string[]>("handlebars", new[] { "handle", "bar" });
            yield return new KeyValuePair<string, string[]>("track", new[] { "track", "trax" });
        }

        private void RestorePropProjection()
        {
            _propRevision++;
            foreach (RendererState state in _sourceRenderers)
                if (state?.renderer != null) state.renderer.enabled = state.enabled;
            _sourceRenderers.Clear();
            if (_propBody != null)
            {
                _propBody.mass = _propBodyMass;
                _propBody.centerOfMass = _propBodyCenterOfMass;
            }
            if (_propInstance != null) UnityEngine.Object.Destroy(_propInstance);
            _propInstance = null;
            _propBody = null;
            _installedPropKey = null;
            _installedPropSettingsSignature = null;
            _propLoading = false;
            if (_propHandleValid)
            {
                if (_propHandle.IsValid()) Addressables.Release(_propHandle);
                _propHandleValid = false;
            }
        }

        private static string PropSettingsSignature(AlpineUserSettings settings)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F3}|{1:F3}|{2:F3}|{3:F2}|{4:F2}|{5:F2}|{6:F3}|{7:F3}|{8:F3}|{9:F1}|{10:F3}|{11:F3}|{12:F3}",
                settings.experimentalPropPosition.x, settings.experimentalPropPosition.y, settings.experimentalPropPosition.z,
                settings.experimentalPropRotation.x, settings.experimentalPropRotation.y, settings.experimentalPropRotation.z,
                settings.experimentalPropScale.x, settings.experimentalPropScale.y, settings.experimentalPropScale.z,
                settings.experimentalPropMassKg,
                settings.experimentalPropCenterOfMass.x, settings.experimentalPropCenterOfMass.y, settings.experimentalPropCenterOfMass.z);
        }

        private void InstallHiddenVehicles()
        {
            RestoreHiddenVehicles();
            try
            {
                VehicleScriptableObject[] all = Resources.FindObjectsOfTypeAll<VehicleScriptableObject>();
                foreach (VehicleListScriptableObject list in Resources.FindObjectsOfTypeAll<VehicleListScriptableObject>())
                {
                    if (list == null) continue;
                    object original = SleddersGameBindings.GetFieldValue<object>(list, "vehicles");
                    List<VehicleScriptableObject> native = EnumerateVehicles(original).ToList();
                    var identities = new HashSet<string>(native.Where(v => v != null).Select(SledIdentity.StableIdentityKey), StringComparer.OrdinalIgnoreCase);
                    List<VehicleScriptableObject> additions = all.Where(candidate =>
                        IsSafeHiddenVehicle(candidate) && !identities.Contains(SledIdentity.StableIdentityKey(candidate))).ToList();
                    if (additions.Count == 0) continue;
                    _vehicleLists.Add(new VehicleListSnapshot { owner = list, vehicles = original });
                    native.AddRange(additions);
                    if (!AssignVehicleCollection(list, original, native))
                        _vehicleLists.RemoveAt(_vehicleLists.Count - 1);
                }
                _hiddenVehiclesInstalled = _vehicleLists.Count > 0;
                if (_hiddenVehiclesInstalled)
                    AlpineNativeUi.RefreshAttachedGarage();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Hidden vehicle discovery stopped safely: " + ex.GetType().Name);
                RestoreHiddenVehicles();
            }
        }

        private void RestoreHiddenVehicles()
        {
            foreach (VehicleListSnapshot snapshot in _vehicleLists)
                if (snapshot?.owner != null)
                    SleddersGameBindings.SetFieldValue(snapshot.owner, "vehicles", snapshot.vehicles);
            _vehicleLists.Clear();
            _hiddenVehiclesInstalled = false;
        }

        private static IEnumerable<VehicleScriptableObject> EnumerateVehicles(object collection)
        {
            if (!(collection is IEnumerable enumerable)) yield break;
            foreach (object item in enumerable)
                if (item is VehicleScriptableObject vehicle) yield return vehicle;
        }

        private static bool AssignVehicleCollection(VehicleListScriptableObject list, object original, List<VehicleScriptableObject> vehicles)
        {
            if (original != null && original.GetType().IsArray)
                return SleddersGameBindings.SetFieldValue(list, "vehicles", vehicles.ToArray());
            Type collectionType = original?.GetType();
            if (collectionType != null)
            {
                try
                {
                    object clone = Activator.CreateInstance(collectionType);
                    MethodInfo add = collectionType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                    if (add != null)
                    {
                        foreach (VehicleScriptableObject vehicle in vehicles) add.Invoke(clone, new object[] { vehicle });
                        return SleddersGameBindings.SetFieldValue(list, "vehicles", clone);
                    }
                }
                catch { }
            }
            return false;
        }

        private static bool IsSafeHiddenVehicle(VehicleScriptableObject vehicle)
        {
            if (vehicle == null || vehicle.assetReference == null || !vehicle.assetReference.RuntimeKeyIsValid() ||
                vehicle.isLocked || string.IsNullOrWhiteSpace(SledIdentity.StableIdentityKey(vehicle)))
                return false;
            string name = (vehicle.name ?? string.Empty).ToLowerInvariant();
            if (name.Contains("locked") || name.Contains("dlc") || name.Contains("entitlement"))
                return false;
            foreach (FieldInfo field in vehicle.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string fieldName = field.Name.ToLowerInvariant();
                if (fieldName.Contains("locked") && field.FieldType == typeof(bool) && (bool)field.GetValue(vehicle))
                    return false;
                if ((fieldName.Contains("entitlement") || fieldName.Contains("dlc")) && field.GetValue(vehicle) != null)
                    return false;
            }
            return true;
        }

        private void Dismount()
        {
            if (_controller == null || _walker != null)
                return;
            try
            {
                Rigidbody body = _controller.GetComponentInChildren<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                try { _controller.SetEngineOnOff(false); } catch { SleddersGameBindings.SetFieldValue(_controller, "isEngineOn", false); }
                _walker = AlpineWalker.Create(_controller);
                if (_walker == null)
                    return;
                _controller.enabled = false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Dismount failed safely: " + ex.GetType().Name);
                Remount();
            }
        }

        private void Remount()
        {
            try { _walker?.Dispose(); } catch { }
            _walker = null;
            if (_controller != null)
                _controller.enabled = true;
        }

        private bool CanRemount()
        {
            if (_walker == null || _controller == null ||
                Vector3.Distance(_walker.Position, _controller.transform.position) > 4f)
                return false;
            Vector3 start = _walker.Position + Vector3.up * 1.4f;
            Vector3 end = _controller.transform.position + Vector3.up * 0.8f;
            if (!Physics.Linecast(start, end, out RaycastHit hit))
                return true;
            return hit.transform == _controller.transform || hit.transform.IsChildOf(_controller.transform);
        }

        private sealed class AlpineWalker : IDisposable
        {
            private readonly GameObject _root;
            private readonly CharacterController _character;
            private readonly Animator _animator;
            private readonly Dictionary<HumanBodyBones, Quaternion> _boneDefaults = new Dictionary<HumanBodyBones, Quaternion>();
            private float _verticalSpeed;
            private float _stride;
            private readonly List<RendererState> _nativeDriverRenderers = new List<RendererState>();
            private Camera _camera;
            private Vector3 _cameraNativePosition;
            private bool _cameraMoved;
            private readonly Vector3 _parkedSledPosition;
            private WalkerState _state;

            private enum WalkerState { Idle, Start, Walk, Run, Jump, Fall, Land, Remount }

            internal Vector3 Position => _root != null ? _root.transform.position : Vector3.zero;

            private AlpineWalker(GameObject root, CharacterController character, Animator animator, Vector3 parkedSledPosition)
            {
                _root = root;
                _character = character;
                _animator = animator;
                _parkedSledPosition = parkedSledPosition;
                CaptureBone(HumanBodyBones.LeftUpperLeg);
                CaptureBone(HumanBodyBones.RightUpperLeg);
                CaptureBone(HumanBodyBones.LeftUpperArm);
                CaptureBone(HumanBodyBones.RightUpperArm);
                CaptureBone(HumanBodyBones.Spine);
            }

            internal static AlpineWalker Create(SnowmobileController sled)
            {
                var root = new GameObject("Alpine Procedural Walker");
                root.transform.position = sled.transform.position + sled.transform.right * 1.2f + Vector3.up * 0.5f;
                root.transform.rotation = Quaternion.Euler(0f, sled.transform.eulerAngles.y, 0f);
                var character = root.AddComponent<CharacterController>();
                character.height = 1.75f;
                character.radius = 0.28f;
                character.center = new Vector3(0f, 0.88f, 0f);

                Animator native = sled.GetComponentsInChildren<Animator>(true).FirstOrDefault(animator => animator != null && animator.isHuman);
                Animator clonedAnimator = null;
                if (native != null)
                {
                    GameObject body = UnityEngine.Object.Instantiate(native.gameObject, root.transform);
                    body.name = "Alpine Walker Body";
                    body.transform.localPosition = Vector3.zero;
                    body.transform.localRotation = Quaternion.identity;
                    foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(rb);
                    foreach (Collider collider in body.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
                    foreach (MonoBehaviour behaviour in body.GetComponentsInChildren<MonoBehaviour>(true))
                        UnityEngine.Object.Destroy(behaviour);
                    clonedAnimator = body.GetComponent<Animator>();
                    if (clonedAnimator != null) clonedAnimator.enabled = false;
                }
                var walker = new AlpineWalker(root, character, clonedAnimator, sled.transform.position);
                if (native != null)
                {
                    foreach (Renderer renderer in native.GetComponentsInChildren<Renderer>(true))
                    {
                        walker._nativeDriverRenderers.Add(new RendererState { renderer = renderer, enabled = renderer.enabled });
                        renderer.enabled = false;
                    }
                }
                return walker;
            }

            internal void Update()
            {
                RestoreCamera();
                Camera camera = Camera.allCameras.FirstOrDefault(item => item != null && item.isActiveAndEnabled && item.targetTexture == null);
                Vector3 forward = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized : _root.transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                Vector3 direction = Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
                float speed = Input.GetKey(KeyCode.LeftShift) ? 6f : 3.2f;
                if (_character.isGrounded)
                {
                    bool wasAirborne = _state == WalkerState.Jump || _state == WalkerState.Fall;
                    _verticalSpeed = -1f;
                    if (Input.GetKeyDown(KeyCode.Space)) _verticalSpeed = 5.2f;
                    _state = Input.GetKeyDown(KeyCode.Space) ? WalkerState.Jump :
                        wasAirborne ? WalkerState.Land :
                        input.sqrMagnitude < 0.01f ? WalkerState.Idle :
                        Input.GetKey(KeyCode.LeftShift) ? WalkerState.Run : WalkerState.Walk;
                }
                else
                {
                    _verticalSpeed += Physics.gravity.y * Time.deltaTime;
                    _state = _verticalSpeed > 0f ? WalkerState.Jump : WalkerState.Fall;
                }
                _character.Move((direction * speed + Vector3.up * _verticalSpeed) * Time.deltaTime);
                if (direction.sqrMagnitude > 0.01f)
                    _root.transform.rotation = Quaternion.Slerp(_root.transform.rotation, Quaternion.LookRotation(direction), 12f * Time.deltaTime);
                ApplyProceduralPose(input.magnitude * speed);
                UpdateFirstPersonBody(camera);
            }

            internal void LateUpdate()
            {
                Camera active = Camera.allCameras
                    .Where(item => item != null && item.isActiveAndEnabled && item.targetTexture == null)
                    .OrderByDescending(item => item.depth).FirstOrDefault();
                if (active == null) return;
                _camera = active;
                _cameraNativePosition = active.transform.position;
                Vector3 walkerDelta = _root.transform.position - _parkedSledPosition;
                active.transform.position += walkerDelta;
                _cameraMoved = true;
            }

            internal void Teleport(Vector3 position)
            {
                RestoreCamera();
                _root.transform.position = position;
                _verticalSpeed = 0f;
            }

            private void ApplyProceduralPose(float speed)
            {
                if (_animator == null) return;
                _stride += Time.deltaTime * Mathf.Max(1f, speed * 1.8f);
                float swing = Mathf.Sin(_stride) * Mathf.Clamp(speed * 6f, 0f, 32f);
                RotateBone(HumanBodyBones.LeftUpperLeg, Vector3.right * swing);
                RotateBone(HumanBodyBones.RightUpperLeg, Vector3.right * -swing);
                RotateBone(HumanBodyBones.LeftUpperArm, Vector3.right * -swing * 0.7f);
                RotateBone(HumanBodyBones.RightUpperArm, Vector3.right * swing * 0.7f);
                RotateBone(HumanBodyBones.Spine, Vector3.forward * Mathf.Sin(_stride * 0.5f) * 2f);
            }

            private void UpdateFirstPersonBody(Camera camera)
            {
                if (_animator == null) return;
                bool firstPerson = camera != null && (Vector3.Distance(camera.transform.position, _root.transform.position + Vector3.up * 1.55f) < 1.2f ||
                    camera.name.IndexOf("first", StringComparison.OrdinalIgnoreCase) >= 0);
                foreach (Renderer renderer in _animator.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    string name = renderer.name.ToLowerInvariant();
                    bool intersects = name.Contains("head") || name.Contains("helmet") || name.Contains("torso") || name.Contains("chest");
                    renderer.shadowCastingMode = firstPerson && intersects
                        ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            private void RestoreCamera()
            {
                if (_cameraMoved && _camera != null) _camera.transform.position = _cameraNativePosition;
                _cameraMoved = false;
                _camera = null;
            }

            private void CaptureBone(HumanBodyBones bone)
            {
                Transform transform = _animator != null ? _animator.GetBoneTransform(bone) : null;
                if (transform != null) _boneDefaults[bone] = transform.localRotation;
            }

            private void RotateBone(HumanBodyBones bone, Vector3 euler)
            {
                Transform transform = _animator.GetBoneTransform(bone);
                if (transform != null && _boneDefaults.TryGetValue(bone, out Quaternion baseline))
                    transform.localRotation = baseline * Quaternion.Euler(euler);
            }

            public void Dispose()
            {
                RestoreCamera();
                foreach (RendererState state in _nativeDriverRenderers)
                    if (state?.renderer != null) state.renderer.enabled = state.enabled;
                if (_root != null) UnityEngine.Object.Destroy(_root);
            }
        }
    }
}
