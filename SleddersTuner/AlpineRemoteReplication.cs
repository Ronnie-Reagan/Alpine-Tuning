using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal sealed class AlpineRemoteReplication
    {
        private readonly Dictionary<int, RemoteHeadlightDefaults> _headlightDefaultsByRoot =
            new Dictionary<int, RemoteHeadlightDefaults>();

        private readonly Dictionary<int, string> _lastAppliedChecksumByRoot =
            new Dictionary<int, string>();

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

            if (!SleddersGameBindings.TryGetRemoteNetworkVehicle(senderId, out var remoteVehicle) ||
                remoteVehicle == null)
            {
                status = "Remote sled found, waiting for remote vehicle identity.";
                return false;
            }

            if (!TargetMatches(remoteVehicle, profile))
            {
                status = "Remote tune skipped: remote sled target does not match active tune.";
                return false;
            }

            int rootId = root.GetInstanceID();
            if (!string.IsNullOrWhiteSpace(profile.checksum) &&
                _lastAppliedChecksumByRoot.TryGetValue(rootId, out var lastChecksum) &&
                string.Equals(lastChecksum, profile.checksum, StringComparison.OrdinalIgnoreCase))
            {
                status = "Remote tune already applied to current sled instance.";
                return true;
            }

            var applied = new List<string>();
            var skipped = new List<string>();
            PartEffect effect = computation.mergedEffect ?? new PartEffect();
            settings = settings ?? new AlpineUserSettings();

            if (settings.receivePeerAudio)
                ApplyEngineAudio(root, computation.audioDefaults, applied, skipped);
            else
                skipped.Add("engine audio sharing off");

            if (settings.receivePeerLighting)
                ApplyHeadlights(root, effect, applied, skipped);
            else
                skipped.Add("headlight sharing off");

            if (settings.receivePeerVisualEquipment)
                ApplyAccessories(root, effect.accessoryMode, computation.baseDefaults, applied, skipped);
            else
                skipped.Add("visual equipment sharing off");

            if (!string.IsNullOrWhiteSpace(profile.checksum))
                _lastAppliedChecksumByRoot[rootId] = profile.checksum;

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

            if (!SleddersGameBindings.TryFindRemoteSnowmobileRoot(senderId, out var root, out _) ||
                root == null)
            {
                return;
            }

            int rootId = root.GetInstanceID();
            _lastAppliedChecksumByRoot.Remove(rootId);
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
            return !string.IsNullOrWhiteSpace(profile.targetSledKey) &&
                   string.Equals(remoteSledKey, profile.targetSledKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasEngineAudioToken(SledDefaults defaults)
        {
            return defaults != null &&
                   !string.IsNullOrWhiteSpace(defaults.engineAudioEnumType) &&
                   (!string.IsNullOrWhiteSpace(defaults.engineAudioEnumName) ||
                    defaults.engineAudioEnumRawValue != 0);
        }

        private static void ApplyEngineAudio(
            Component root,
            SledDefaults audioDefaults,
            List<string> applied,
            List<string> skipped)
        {
            if (!HasEngineAudioToken(audioDefaults))
            {
                skipped.Add("engine audio token missing");
                return;
            }

            Component audioController = SleddersGameBindings.FindEngineAudioController(root);
            if (audioController == null)
            {
                skipped.Add("engine audio controller");
                return;
            }

            if (SleddersGameBindings.TryApplyEngineAudioToken(
                    audioController,
                    audioDefaults.engineAudioEnumType,
                    audioDefaults.engineAudioEnumName,
                    audioDefaults.engineAudioEnumRawValue,
                    out var reason))
            {
                applied.Add("engine audio");
                return;
            }

            skipped.Add("engine audio: " + reason);
        }

        private void ApplyHeadlights(
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

            int rootId = root.GetInstanceID();
            if (!_headlightDefaultsByRoot.TryGetValue(rootId, out var defaults))
            {
                defaults = CaptureHeadlightDefaults(lights);
                _headlightDefaultsByRoot[rootId] = defaults;
            }

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

        private static RemoteHeadlightDefaults CaptureHeadlightDefaults(IEnumerable<Light> lights)
        {
            var captured = new RemoteHeadlightDefaults();
            foreach (var light in lights)
            {
                if (light == null)
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

            return captured;
        }

        private static void ApplyAccessories(
            Component root,
            string accessoryMode,
            SledDefaults defaults,
            List<string> applied,
            List<string> skipped)
        {
            if (string.IsNullOrWhiteSpace(accessoryMode))
            {
                skipped.Add("accessory mode");
                return;
            }

            if (string.Equals(accessoryMode, "stock", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add("stock accessory mode keeps local visual equipment");
                return;
            }

            var components = SleddersGameBindings.GetSnowmobileAccessories(root);
            if (components == null || components.Length == 0)
            {
                skipped.Add("accessories");
                return;
            }

            object accessories = components[0];
            bool utility = string.Equals(accessoryMode, "utility", StringComparison.OrdinalIgnoreCase);
            bool raceTrim = string.Equals(accessoryMode, "race_trim", StringComparison.OrdinalIgnoreCase);

            if (utility || raceTrim)
            {
                SetGameObjectListActive(accessories, "windshieldObjects", utility);
                SetGameObjectListActive(accessories, "snowFlapObjects", utility);
                SetGameObjectListActive(accessories, "rearPartObjects", utility);
                SetGameObjectListActive(accessories, "tunnelReflectors", utility);
                applied.Add("accessories");
                return;
            }

            skipped.Add("unknown accessory mode keeps local visual equipment");
        }

        private static void ApplyControllerFeel(
            ulong senderId,
            TuneComputation computation,
            TuneProfile profile,
            List<string> applied,
            List<string> skipped)
        {
            if (!SleddersGameBindings.TryFindRemoteSnowmobileController(senderId, out var controller) ||
                controller == null)
            {
                skipped.Add("controller feel");
                return;
            }

            if (computation.baseDefaults == null || computation.baseDefaults.controller == null)
            {
                skipped.Add("controller defaults");
                return;
            }

            var defaults = computation.baseDefaults.controller;
            var effect = computation.mergedEffect ?? new PartEffect();
            var fine = profile.fineTune ?? new FineTuneSettings();
            ClampFineTune(fine);

            float clutchTrim = 1f + fine.clutchTrimPercent / 100f;
            float boostResponse = Mathf.Clamp(effect.boostResponseMultiplier, 0.70f, 1.35f);

            if (defaults.hasThrottleExponent)
                SetFloatField(controller, "throttleExponent", ClampOffset(defaults.throttleExponent + effect.throttleExponentDelta, defaults.throttleExponent, 0.20f, 0.25f, 4f));

            if (defaults.hasRpmSensitivity)
                SetFloatField(controller, "rpmSensitivity", ClampRelative(defaults.rpmSensitivity * effect.rpmSensitivityMultiplier * boostResponse, defaults.rpmSensitivity, 0.50f, 1.70f, 0.05f, 10f));

            if (defaults.hasRpmSensitivityDown)
                SetFloatField(controller, "rpmSensitivityDown", ClampRelative(defaults.rpmSensitivityDown * effect.rpmSensitivityDownMultiplier, defaults.rpmSensitivityDown, 0.50f, 1.70f, 0.05f, 10f));

            float clutchMin = defaults.hasClutchRpmMin
                ? ClampRelative((defaults.clutchRpmMin + effect.clutchRpmMinOffset) * clutchTrim, defaults.clutchRpmMin, 0.75f, 1.35f, 0f, 14000f)
                : 0f;

            float clutchMax = defaults.hasClutchRpmMax
                ? ClampRelative((defaults.clutchRpmMax + effect.clutchRpmMaxOffset) * clutchTrim, defaults.clutchRpmMax, 0.75f, 1.35f, 0f, 14000f)
                : 0f;

            if (defaults.hasClutchRpmMin && defaults.hasClutchRpmMax && clutchMax < clutchMin + 100f)
                clutchMax = Mathf.Min(14000f, clutchMin + 100f);

            if (defaults.hasClutchRpmMin)
                SetFloatField(controller, "clutchRpmMin", clutchMin);

            if (defaults.hasClutchRpmMax)
                SetFloatField(controller, "clutchRpmMax", clutchMax);

            if (defaults.hasMinThrottleOnClutchEngagement)
                SetFloatField(controller, "minThrottleOnClutchEngagement", Mathf.Clamp01(defaults.minThrottleOnClutchEngagement + effect.minThrottleOnClutchEngagementOffset));

            if (defaults.hasWheelieThreshold)
                SetFloatField(controller, "wheelieThreshold", ClampOffset(defaults.wheelieThreshold + effect.wheelieThresholdOffset, defaults.wheelieThreshold, 0.25f, 0.05f, 3f));

            object stabilizer = SleddersGameBindings.GetStabilizer(controller);
            if (stabilizer != null)
            {
                if (defaults.hasStabilizerDamping)
                    SleddersGameBindings.SetFieldValue(stabilizer, "damping", ClampVectorRelative(defaults.stabilizerDamping.ToVector3() * effect.stabilizerDampingMultiplier, defaults.stabilizerDamping.ToVector3(), 0.50f, 1.80f));

                if (defaults.hasTrackSpeedDamping)
                    SleddersGameBindings.SetFieldValue(stabilizer, "trackSpeedDamping", ClampVectorRelative(defaults.trackSpeedDamping.ToVector3() * effect.trackSpeedDampingMultiplier, defaults.trackSpeedDamping.ToVector3(), 0.50f, 1.80f));

                if (defaults.hasTrackSpeedGyroMultiplier)
                    SleddersGameBindings.SetFieldValue(stabilizer, "trackSpeedGyroMultiplier", ClampRelative(defaults.trackSpeedGyroMultiplier * effect.trackSpeedGyroMultiplier, defaults.trackSpeedGyroMultiplier, 0.60f, 1.50f, 0.01f, 10f));
            }

            applied.Add("controller feel");
        }

        private static void SetGameObjectListActive(object owner, string fieldName, bool active)
        {
            SleddersGameBindings.SetGameObjectListActive(owner, fieldName, active);
        }

        private static void SetFloatField(object target, string fieldName, float value)
        {
            SleddersGameBindings.SetFloatField(target, fieldName, value);
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

        private sealed class RemoteHeadlightDefaults
        {
            public readonly List<RemoteHeadlightDefault> lights = new List<RemoteHeadlightDefault>();
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
    }
}
