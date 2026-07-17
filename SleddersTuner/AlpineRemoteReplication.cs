using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal sealed class AlpineRemoteReplication
    {
        private static readonly string[] AccessoryObjectFields =
        {
            "windshieldObjects",
            "snowFlapObjects",
            "rearPartObjects",
            "tunnelReflectors"
        };

        private readonly Dictionary<ulong, RemoteMutationState> _mutationsBySender =
            new Dictionary<ulong, RemoteMutationState>();

        public bool TryApply(
            ulong senderId,
            TuneProfile profile,
            TuneComputation computation,
            AlpineUserSettings settings,
            out string status)
        {
            status = null;

            if (senderId == 0 || profile == null || computation == null)
            {
                status = "Remote tune apply skipped: invalid active tune state.";
                return false;
            }

            if (!SleddersGameBindings.TryFindRemoteSnowmobileRoot(senderId, out var root, out var mapReason) ||
                root == null)
            {
                status = "Remote active tune received, waiting for remote sled instance mapping.";
                return false;
            }

            if (!DiscardReplacedRootState(senderId, root))
            {
                status = "Remote tune apply deferred: the previous remote sled state could not be restored yet.";
                return false;
            }

            if (!SleddersGameBindings.TryGetRemoteNetworkVehicle(senderId, out var remoteVehicle) ||
                remoteVehicle == null)
            {
                status = "Remote sled found, waiting for remote vehicle identity.";
                return false;
            }

            if (!TargetMatches(remoteVehicle, profile))
            {
                ClearSender(senderId);
                status = "Remote tune skipped: remote sled target does not match active tune.";
                return false;
            }

            RemoteMutationState state = GetOrCreateState(senderId, root);
            string applicationSignature = BuildApplicationSignature(profile, settings);
            if (!string.IsNullOrWhiteSpace(profile.checksum) &&
                !state.engineAudioRestorePending &&
                string.Equals(state.lastAppliedSignature, applicationSignature, StringComparison.Ordinal))
            {
                status = "Remote tune already applied to current sled instance.";
                return true;
            }

            var applied = new List<string>();
            var skipped = new List<string>();
            PartEffect effect = computation.mergedEffect ?? new PartEffect();
            settings = settings ?? new AlpineUserSettings();

            if (settings.receivePeerAudio)
                ApplyEngineAudio(state, root, computation.audioDefaults, applied, skipped);
            else
            {
                RestoreEngineAudio(state);
                skipped.Add("engine audio sharing off");
            }

            if (settings.receivePeerLighting)
                ApplyHeadlights(state, root, effect, applied, skipped);
            else
            {
                RestoreHeadlights(state);
                skipped.Add("headlight sharing off");
            }

            if (settings.receivePeerVisualEquipment)
                ApplyAccessories(state, root, effect.accessoryMode, applied, skipped);
            else
            {
                RestoreAccessories(state);
                skipped.Add("visual equipment sharing off");
            }

            if (!string.IsNullOrWhiteSpace(profile.checksum))
                state.lastAppliedSignature = applicationSignature;

            if (applied.Count > 0)
            {
                status = "Applied remote runtime tune: " + string.Join(", ", applied) + ".";
                return true;
            }

            status = skipped.Count > 0
                ? "Remote tune mapped, but runtime bindings were unavailable: " + string.Join(", ", skipped) + "."
                : "Remote tune mapped; no runtime-safe effects were selected.";
            return true;
        }

        public void ClearSender(ulong senderId)
        {
            if (senderId == 0)
                return;

            if (!_mutationsBySender.TryGetValue(senderId, out var state))
                return;

            RestoreAll(state);
            if (!state.engineAudioRestorePending)
                _mutationsBySender.Remove(senderId);
        }

        public void Shutdown()
        {
            foreach (var state in new List<RemoteMutationState>(_mutationsBySender.Values))
                RestoreAll(state);

            _mutationsBySender.Clear();
        }

        private static bool TargetMatches(VehicleScriptableObject remoteVehicle, TuneProfile profile)
        {
            if (remoteVehicle == null || profile == null)
                return false;

            string remoteVehicleId = AlpineTuningMod.GetVehicleId(remoteVehicle);
            if (!string.IsNullOrWhiteSpace(profile.targetVehicleId) &&
                string.Equals(remoteVehicleId, profile.targetVehicleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string remoteSledKey = AlpineTuningMod.GetSledKey(remoteVehicle);
            if (SledIdentity.HasNativeVehicleIdentity(remoteSledKey, remoteVehicleId) ||
                SledIdentity.HasNativeVehicleIdentity(profile.targetSledKey, profile.targetVehicleId))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(profile.targetSledKey) &&
                   string.Equals(remoteSledKey, profile.targetSledKey, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildApplicationSignature(TuneProfile profile, AlpineUserSettings settings)
        {
            settings = settings ?? new AlpineUserSettings();
            return (profile != null ? profile.checksum : null) + "|" +
                   (settings.receivePeerAudio ? "a1" : "a0") + "|" +
                   (settings.receivePeerLighting ? "l1" : "l0") + "|" +
                   (settings.receivePeerVisualEquipment ? "v1" : "v0");
        }

        private RemoteMutationState GetOrCreateState(ulong senderId, Component root)
        {
            if (_mutationsBySender.TryGetValue(senderId, out var existing) &&
                existing != null &&
                existing.rootId == root.GetInstanceID())
            {
                return existing;
            }

            var created = new RemoteMutationState
            {
                rootId = root.GetInstanceID()
            };
            _mutationsBySender[senderId] = created;
            return created;
        }

        private bool DiscardReplacedRootState(ulong senderId, Component root)
        {
            if (!_mutationsBySender.TryGetValue(senderId, out var existing) ||
                existing == null ||
                existing.rootId == root.GetInstanceID())
            {
                return true;
            }

            RestoreAll(existing);
            if (existing.engineAudioRestorePending)
                return false;

            _mutationsBySender.Remove(senderId);
            return true;
        }

        private static bool HasEngineAudioToken(SledDefaults defaults)
        {
            return defaults != null &&
                   !string.IsNullOrWhiteSpace(defaults.engineAudioEnumType) &&
                   (!string.IsNullOrWhiteSpace(defaults.engineAudioEnumName) ||
                    defaults.engineAudioEnumRawValue != 0);
        }

        private static void ApplyEngineAudio(
            RemoteMutationState state,
            Component root,
            SledDefaults audioDefaults,
            List<string> applied,
            List<string> skipped)
        {
            if (!HasEngineAudioToken(audioDefaults))
            {
                RestoreEngineAudio(state);
                skipped.Add("engine audio token missing");
                return;
            }

            Component audioController = SleddersGameBindings.FindEngineAudioController(root);
            if (audioController == null)
            {
                RestoreEngineAudio(state);
                skipped.Add("engine audio controller");
                return;
            }

            if (!CaptureEngineAudioDefault(state, audioController))
            {
                skipped.Add("engine audio baseline unavailable");
                return;
            }

            if (SleddersGameBindings.TryApplyEngineAudioToken(
                    audioController,
                    audioDefaults.engineAudioEnumType,
                    audioDefaults.engineAudioEnumName,
                    audioDefaults.engineAudioEnumRawValue,
                    out var reason))
            {
                state.engineAudioRestorePending = false;
                applied.Add("engine audio");
                return;
            }

            skipped.Add("engine audio: " + reason);
        }

        private static bool CaptureEngineAudioDefault(RemoteMutationState state, Component audioController)
        {
            if (state.engineAudio != null && state.engineAudio.controller == audioController)
                return true;

            if (state.engineAudio != null)
            {
                RestoreEngineAudio(state);
                if (state.engineAudio != null)
                    return false;
            }

            object value = SleddersGameBindings.GetFieldValue<object>(audioController, "GILHLLEEAEH");
            if (value == null || !value.GetType().IsEnum)
                return false;

            try
            {
                Type enumType = value.GetType();
                state.engineAudio = new RemoteEngineAudioDefault
                {
                    controller = audioController,
                    enumTypeName = enumType.AssemblyQualifiedName,
                    enumName = Enum.GetName(enumType, value),
                    enumRawValue = Convert.ToInt32(value)
                };
                state.engineAudioRestorePending = false;
                return true;
            }
            catch
            {
                state.engineAudio = null;
                return false;
            }
        }

        private static void RestoreEngineAudio(RemoteMutationState state)
        {
            if (state == null || state.engineAudio == null)
            {
                if (state != null)
                    state.engineAudioRestorePending = false;
                return;
            }

            RemoteEngineAudioDefault defaults = state.engineAudio;
            if (defaults.controller == null)
            {
                state.engineAudio = null;
                state.engineAudioRestorePending = false;
                return;
            }

            if (SleddersGameBindings.TryApplyEngineAudioToken(
                    defaults.controller,
                    defaults.enumTypeName,
                    defaults.enumName,
                    defaults.enumRawValue,
                    out _))
            {
                state.engineAudio = null;
                state.engineAudioRestorePending = false;
                return;
            }

            state.engineAudioRestorePending = true;
        }

        private static void ApplyHeadlights(
            RemoteMutationState state,
            Component root,
            PartEffect effect,
            List<string> applied,
            List<string> skipped)
        {
            var lights = SleddersGameBindings.GetHeadlightLights(root);
            if (lights == null || lights.Length == 0)
            {
                skipped.Add("headlights");
                return;
            }

            RemoteHeadlightDefaults defaults = state.headlights ?? new RemoteHeadlightDefaults();
            CaptureHeadlightDefaults(defaults, lights);
            if (defaults.lights.Count == 0)
            {
                skipped.Add("headlight baseline unavailable");
                return;
            }

            state.headlights = defaults;

            float pitch = Mathf.Clamp(effect != null ? effect.headlightPitchOffsetDegrees : 0f, -5f, 5f);
            foreach (var item in defaults.lights)
            {
                if (item == null || item.light == null)
                    continue;

                item.light.color = effect != null && effect.hasHeadlightColor ? effect.headlightColor : item.color;
                item.light.intensity = Mathf.Clamp(
                    item.intensity * (effect != null ? effect.headlightIntensityMultiplier : 1f),
                    0f,
                    Mathf.Max(item.intensity * 2.5f, item.intensity + 0.01f));
                item.light.range = Mathf.Clamp(
                    item.range * (effect != null ? effect.headlightRangeMultiplier : 1f),
                    0f,
                    Mathf.Max(item.range * 2.0f, item.range + 0.01f));
                item.light.spotAngle = Mathf.Clamp(
                    item.spotAngle * (effect != null ? effect.headlightSpotAngleMultiplier : 1f),
                    10f,
                    160f);
                item.light.transform.localRotation = item.localRotation * Quaternion.Euler(pitch, 0f, 0f);
            }

            applied.Add("headlights");
        }

        private static void CaptureHeadlightDefaults(
            RemoteHeadlightDefaults captured,
            IEnumerable<Light> lights)
        {
            foreach (var light in lights)
            {
                if (light == null || !captured.instanceIds.Add(light.GetInstanceID()))
                    continue;

                captured.lights.Add(new RemoteHeadlightDefault
                {
                    light = light,
                    color = light.color,
                    intensity = light.intensity,
                    range = light.range,
                    spotAngle = light.spotAngle,
                    localRotation = light.transform.localRotation
                });
            }
        }

        private static void RestoreHeadlights(RemoteMutationState state)
        {
            if (state == null || state.headlights == null)
                return;

            foreach (var defaults in state.headlights.lights)
            {
                if (defaults == null || defaults.light == null)
                    continue;

                try
                {
                    defaults.light.color = defaults.color;
                    defaults.light.intensity = defaults.intensity;
                    defaults.light.range = defaults.range;
                    defaults.light.spotAngle = defaults.spotAngle;
                    defaults.light.transform.localRotation = defaults.localRotation;
                }
                catch
                {
                }
            }

            state.headlights = null;
        }

        private static void ApplyAccessories(
            RemoteMutationState state,
            Component root,
            string accessoryMode,
            List<string> applied,
            List<string> skipped)
        {
            if (string.IsNullOrWhiteSpace(accessoryMode))
            {
                RestoreAccessories(state);
                skipped.Add("accessory mode");
                return;
            }

            if (string.Equals(accessoryMode, "stock", StringComparison.OrdinalIgnoreCase))
            {
                RestoreAccessories(state);
                skipped.Add("stock accessory mode restored local visual equipment");
                return;
            }

            bool utility = string.Equals(accessoryMode, "utility", StringComparison.OrdinalIgnoreCase);
            bool raceTrim = string.Equals(accessoryMode, "race_trim", StringComparison.OrdinalIgnoreCase);
            if (!utility && !raceTrim)
            {
                RestoreAccessories(state);
                skipped.Add("unknown accessory mode keeps local visual equipment");
                return;
            }

            var components = SleddersGameBindings.GetSnowmobileAccessories(root);
            if (components == null || components.Length == 0)
            {
                skipped.Add("accessories");
                return;
            }

            if (!CaptureAccessoryDefaults(state, components))
            {
                skipped.Add("accessory baseline unavailable");
                return;
            }

            foreach (object accessories in components)
            {
                if (accessories == null)
                    continue;

                foreach (string fieldName in AccessoryObjectFields)
                    SetGameObjectListActive(accessories, fieldName, utility);
            }

            applied.Add("accessories");
        }

        private static bool CaptureAccessoryDefaults(
            RemoteMutationState state,
            IEnumerable<Component> components)
        {
            RemoteAccessoryDefaults captured = state.accessories ?? new RemoteAccessoryDefaults();
            foreach (object component in components)
            {
                if (component == null)
                    continue;

                foreach (string fieldName in AccessoryObjectFields)
                {
                    object value = SleddersGameBindings.GetFieldValue<object>(component, fieldName);
                    if (!(value is System.Collections.IEnumerable objects))
                        continue;

                    foreach (object item in objects)
                    {
                        GameObject gameObject = item as GameObject;
                        if (gameObject == null || !captured.instanceIds.Add(gameObject.GetInstanceID()))
                            continue;

                        captured.objects.Add(new RemoteAccessoryDefault
                        {
                            gameObject = gameObject,
                            active = gameObject.activeSelf
                        });
                    }
                }
            }

            if (captured.objects.Count == 0)
                return false;

            state.accessories = captured;
            return true;
        }

        private static void RestoreAccessories(RemoteMutationState state)
        {
            if (state == null || state.accessories == null)
                return;

            foreach (var defaults in state.accessories.objects)
            {
                if (defaults == null || defaults.gameObject == null)
                    continue;

                try
                {
                    defaults.gameObject.SetActive(defaults.active);
                }
                catch
                {
                }
            }

            state.accessories = null;
        }

        private static void RestoreAll(RemoteMutationState state)
        {
            if (state == null)
                return;

            RestoreEngineAudio(state);
            RestoreHeadlights(state);
            RestoreAccessories(state);
            state.lastAppliedSignature = null;
        }

        private static void SetGameObjectListActive(object owner, string fieldName, bool active)
        {
            SleddersGameBindings.SetGameObjectListActive(owner, fieldName, active);
        }

        private sealed class RemoteHeadlightDefaults
        {
            public readonly List<RemoteHeadlightDefault> lights = new List<RemoteHeadlightDefault>();
            public readonly HashSet<int> instanceIds = new HashSet<int>();
        }

        private sealed class RemoteHeadlightDefault
        {
            public Light light;
            public Color color;
            public float intensity;
            public float range;
            public float spotAngle;
            public Quaternion localRotation;
        }

        private sealed class RemoteAccessoryDefaults
        {
            public readonly List<RemoteAccessoryDefault> objects = new List<RemoteAccessoryDefault>();
            public readonly HashSet<int> instanceIds = new HashSet<int>();
        }

        private sealed class RemoteAccessoryDefault
        {
            public GameObject gameObject;
            public bool active;
        }

        private sealed class RemoteEngineAudioDefault
        {
            public Component controller;
            public string enumTypeName;
            public string enumName;
            public int enumRawValue;
        }

        private sealed class RemoteMutationState
        {
            public int rootId;
            public string lastAppliedSignature;
            public bool engineAudioRestorePending;
            public RemoteEngineAudioDefault engineAudio;
            public RemoteHeadlightDefaults headlights;
            public RemoteAccessoryDefaults accessories;
        }
    }
}
