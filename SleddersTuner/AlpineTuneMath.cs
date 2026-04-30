using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineTuneMath
    {
        private const float HpMinMult = 0.60f;
        private const float HpMaxMult = 1.75f;
        private const float HpAbsoluteMin = 20f;
        private const float HpAbsoluteMax = 380f;

        private const float PfMinMult = 0.55f;
        private const float PfMaxMult = 1.40f;
        private const float PfAbsoluteMin = 0.20f;
        private const float PfAbsoluteMax = 2.50f;

        private const float PowerFactorPartGainScale = 0.85f;
        private const float PowerFactorFineTuneGainScale = 0.50f;
        private const float PowerFactorAirEffect = 0.35f;
        private const float AltitudeMaxMeters = 4500f;
        private const float PressureRatioMin = 0.50f;
        private const float PressureRatioMax = 1.00f;

        private const float LugMinMult = 0.50f;
        private const float LugMaxMult = 2.25f;
        private const float LugAbsoluteMin = 1f;
        private const float LugAbsoluteMax = 95f;

        private const float FrictionMinMult = 0.55f;
        private const float FrictionMaxMult = 1.65f;
        private const float FrictionAbsoluteMin = 0.05f;
        private const float FrictionAbsoluteMax = 3.00f;
        private const float SeaLevelPressureKpa = 101.325f;

        public static ResolvedStats ComputeStats(
            SledDefaults baseDefaults,
            SledDefaults engineDefaults,
            PartEffect effect,
            FineTuneSettings fine)
        {
            EngineSimulationResult ignored;
            return ComputeStats(baseDefaults, engineDefaults, null, effect, fine, null, out ignored);
        }

        public static ResolvedStats ComputeStats(
            SledDefaults baseDefaults,
            SledDefaults engineDefaults,
            IEnumerable<TunePart> parts,
            PartEffect effect,
            FineTuneSettings fine,
            EngineSimulationInput simulationInput,
            out EngineSimulationResult simulationResult)
        {
            if (baseDefaults == null || engineDefaults == null)
            {
                simulationResult = null;
                return null;
            }

            if (effect == null)
                effect = new PartEffect();

            if (fine == null)
                fine = new FineTuneSettings();

            ClampFineTune(fine);

            float tractionTrim = 1f + fine.tractionTrimPercent / 100f;
            float weightTrim = 1f + fine.weightTrimPercent / 100f;

            var gains = ComputePowerGainBreakdown(parts, effect, fine);
            float hpBeforeEnvironment = engineDefaults.horsePower * GainToMultiplier(gains.TotalHorsepowerGain);
            float pfBeforeEnvironment = engineDefaults.powerFactor * GainToMultiplier(gains.TotalPowerFactorGain);
            simulationResult = ComputeEngineSimulation(simulationInput, effect, gains, hpBeforeEnvironment, pfBeforeEnvironment);

            float hp = simulationResult.horsepowerAfterEnvironment;
            float pf = simulationResult.powerFactorAfterEnvironment;
            float lug = TrackSpecResolver.ResolveLugHeightMillimeters(baseDefaults, effect);
            float friction = baseDefaults.friction * effect.frictionMultiplier * tractionTrim;
            float weight = (baseDefaults.weight * effect.weightMultiplier + effect.weightOffset) * weightTrim;

            Vector3 baseCom = ToVector3(baseDefaults.centerOfMassOffset);
            Vector3 baseDriverCom = ToVector3(baseDefaults.driverCenterOfMassOffset);

            Vector3 com =
                baseCom +
                ToVector3(effect.centerOfMassDelta) +
                new Vector3(0f, fine.centerOfMassYTrim, fine.centerOfMassZTrim);

            Vector3 driverCom =
                baseDriverCom +
                ToVector3(effect.driverCenterOfMassDelta);

            float skiStance =
                baseDefaults.skiStance +
                effect.skiStanceOffset +
                fine.skiStanceTrim;

            float skisXDistanceOffset =
                baseDefaults.skisXDistanceOffset +
                effect.skisXDistanceOffset;

            hp = ClampRelative(hp, engineDefaults.horsePower, HpMinMult, HpMaxMult, HpAbsoluteMin, HpAbsoluteMax);
            pf = ClampRelative(pf, engineDefaults.powerFactor, PfMinMult, PfMaxMult, PfAbsoluteMin, PfAbsoluteMax);
            lug = ClampRelative(lug, baseDefaults.lugHeight, LugMinMult, LugMaxMult, LugAbsoluteMin, LugAbsoluteMax);
            friction = ClampRelative(friction, baseDefaults.friction, FrictionMinMult, FrictionMaxMult, FrictionAbsoluteMin, FrictionAbsoluteMax);
            if (baseDefaults.weight > 1f)
                weight = Mathf.Clamp(weight, baseDefaults.weight * 0.75f, baseDefaults.weight * 1.35f);
            else
                weight = Mathf.Max(1f, weight);

            com = ClampVectorOffset(com, baseCom, new Vector3(0.10f, 0.24f, 0.28f));
            driverCom = ClampVectorOffset(driverCom, baseDriverCom, new Vector3(0.10f, 0.16f, 0.16f));
            skiStance = ClampOffset(skiStance, baseDefaults.skiStance, 0.18f, 0f, 4f);
            skisXDistanceOffset = ClampOffset(skisXDistanceOffset, baseDefaults.skisXDistanceOffset, 0.12f, -1f, 1f);

            return new ResolvedStats
            {
                horsePower = hp,
                powerFactor = pf,
                lugHeight = lug,
                friction = friction,
                weight = weight,
                skiStance = skiStance,
                skisXDistanceOffset = skisXDistanceOffset,
                isTurboOn = baseDefaults.isTurboOn || effect.isTurbo,
                engineText = !string.IsNullOrWhiteSpace(effect.engineText)
                    ? effect.engineText
                    : baseDefaults.engineText,
                centerOfMassOffset = Vec3Data.From(com),
                driverCenterOfMassOffset = Vec3Data.From(driverCom),
                boostTargetPsi = simulationResult.boostTargetPsi,
                boostLimitPsi = simulationResult.boostLimitPsi,
                estimatedBoostPsi = simulationResult.estimatedBoostPsi,
                altitudeCompensationPercent = simulationResult.turboAltitudeCompensation * 100f,
                estimatedManifoldPressureKpa = simulationResult.estimatedManifoldPressureKpa
            };
        }

        public static void MergeEffect(PartEffect target, PartEffect source)
        {
            if (target == null || source == null)
                return;

            target.horsePowerMultiplier = ComposeAdditiveMultiplier(target.horsePowerMultiplier, source.horsePowerMultiplier);
            target.powerFactorMultiplier = ComposeAdditiveMultiplier(target.powerFactorMultiplier, source.powerFactorMultiplier);
            target.lugHeightMultiplier *= source.lugHeightMultiplier;
            if (source.lugHeightTargetMm > 0.01f)
                target.lugHeightTargetMm = source.lugHeightTargetMm;
            target.lugHeightOffset += source.lugHeightOffset;
            target.frictionMultiplier *= source.frictionMultiplier;
            target.weightMultiplier *= source.weightMultiplier;
            target.weightOffset += source.weightOffset;
            target.skiStanceOffset += source.skiStanceOffset;
            target.skisXDistanceOffset += source.skisXDistanceOffset;
            target.centerOfMassDelta = Vec3Data.From(ToVector3(target.centerOfMassDelta) + ToVector3(source.centerOfMassDelta));
            target.driverCenterOfMassDelta = Vec3Data.From(ToVector3(target.driverCenterOfMassDelta) + ToVector3(source.driverCenterOfMassDelta));
            target.isTurbo |= source.isTurbo;
            if (!string.IsNullOrWhiteSpace(source.engineText))
                target.engineText = source.engineText;
            target.throttleExponentDelta += source.throttleExponentDelta;
            target.rpmSensitivityMultiplier *= source.rpmSensitivityMultiplier;
            target.rpmSensitivityDownMultiplier *= source.rpmSensitivityDownMultiplier;
            target.turboAltitudeCompensation = Mathf.Max(
                Mathf.Clamp01(target.turboAltitudeCompensation),
                Mathf.Clamp01(source.turboAltitudeCompensation));
            target.boostResponseMultiplier = Mathf.Clamp(
                target.boostResponseMultiplier * SanitizePositive(source.boostResponseMultiplier, 1f),
                0.70f,
                1.35f);
            if (source.boostTargetPsi > 0.01f)
                target.boostTargetPsi = source.boostTargetPsi;
            if (source.boostLimitPsi > 0.01f)
                target.boostLimitPsi = source.boostLimitPsi;
            target.clutchRpmMinOffset += source.clutchRpmMinOffset;
            target.clutchRpmMaxOffset += source.clutchRpmMaxOffset;
            target.minThrottleOnClutchEngagementOffset += source.minThrottleOnClutchEngagementOffset;
            target.wheelieThresholdOffset += source.wheelieThresholdOffset;
            target.stabilizerDampingMultiplier *= source.stabilizerDampingMultiplier;
            target.trackSpeedDampingMultiplier *= source.trackSpeedDampingMultiplier;
            target.trackSpeedGyroMultiplier *= source.trackSpeedGyroMultiplier;
            if (source.hasHeadlightColor)
            {
                target.hasHeadlightColor = true;
                target.headlightColor = source.headlightColor;
            }
            target.headlightIntensityMultiplier *= SanitizePositive(source.headlightIntensityMultiplier, 1f);
            target.headlightRangeMultiplier *= SanitizePositive(source.headlightRangeMultiplier, 1f);
            target.headlightSpotAngleMultiplier *= SanitizePositive(source.headlightSpotAngleMultiplier, 1f);
            target.headlightPitchOffsetDegrees += source.headlightPitchOffsetDegrees;
            if (!string.IsNullOrWhiteSpace(source.accessoryMode))
                target.accessoryMode = source.accessoryMode;
        }

        public static float ClampRelative(float value, float baseline, float minMult, float maxMult, float absoluteMin, float absoluteMax)
        {
            if (baseline > 0.01f)
                return Mathf.Clamp(value, Mathf.Max(absoluteMin, baseline * minMult), Mathf.Min(absoluteMax, baseline * maxMult));

            return Mathf.Clamp(value, absoluteMin, absoluteMax);
        }

        public static float ClampOffset(float value, float baseline, float maxDelta, float absoluteMin, float absoluteMax)
        {
            return Mathf.Clamp(value, Mathf.Max(absoluteMin, baseline - maxDelta), Mathf.Min(absoluteMax, baseline + maxDelta));
        }

        public static Vector3 ClampVectorOffset(Vector3 value, Vector3 baseline, Vector3 maxDelta)
        {
            return new Vector3(
                ClampOffset(value.x, baseline.x, maxDelta.x, -10f, 10f),
                ClampOffset(value.y, baseline.y, maxDelta.y, -10f, 10f),
                ClampOffset(value.z, baseline.z, maxDelta.z, -10f, 10f));
        }

        public static Vector3 ClampVectorRelative(Vector3 value, Vector3 baseline, float minMult, float maxMult)
        {
            return new Vector3(
                ClampRelativeSigned(value.x, baseline.x, minMult, maxMult),
                ClampRelativeSigned(value.y, baseline.y, minMult, maxMult),
                ClampRelativeSigned(value.z, baseline.z, minMult, maxMult));
        }

        public static float SafeRatio(float value, float baseline)
        {
            return Mathf.Abs(baseline) > 0.001f ? value / baseline : 1f;
        }

        public static void ClampFineTune(FineTuneSettings fine)
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

        public static PowerGainBreakdown ComputePowerGainBreakdown(
            IEnumerable<TunePart> parts,
            PartEffect mergedEffect,
            FineTuneSettings fine)
        {
            var gains = new PowerGainBreakdown();

            bool foundParts = false;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part == null || part.effect == null)
                        continue;

                    foundParts = true;
                    AddPartGains(gains, part.category, part.effect);
                }
            }

            if (!foundParts && mergedEffect != null)
                AddPartGains(gains, null, mergedEffect);

            float fineGain = fine != null ? PercentToGain(fine.powerTrimPercent) : 0f;
            gains.fineTuneHorsepowerGain = fineGain;
            gains.fineTunePowerFactorGain = fineGain * PowerFactorFineTuneGainScale;
            return gains;
        }

        private static EngineSimulationResult ComputeEngineSimulation(
            EngineSimulationInput input,
            PartEffect effect,
            PowerGainBreakdown gains,
            float horsepowerBeforeEnvironment,
            float powerFactorBeforeEnvironment)
        {
            var result = new EngineSimulationResult
            {
                gains = gains ?? new PowerGainBreakdown(),
                horsepowerBeforeEnvironment = SanitizePositive(horsepowerBeforeEnvironment, 0f),
                powerFactorBeforeEnvironment = SanitizePositive(powerFactorBeforeEnvironment, 0f)
            };

            bool useAltitude = input != null && input.altitudeCompensationEnabled && input.hasAltitudeMeters;
            result.altitudeMeters = useAltitude
                ? Mathf.Clamp(Sanitize(input.altitudeMeters, 0f), 0f, AltitudeMaxMeters)
                : 0f;

            result.altitudePressureRatio = useAltitude
                ? ComputePressureRatio(result.altitudeMeters)
                : 1f;

            bool turbo = effect != null && effect.isTurbo;
            result.turboAltitudeCompensation = turbo && effect != null
                ? Mathf.Clamp01(effect.turboAltitudeCompensation)
                : 0f;

            result.effectiveAirRatio = Mathf.Lerp(
                result.altitudePressureRatio,
                1f,
                result.turboAltitudeCompensation);

            result.effectiveAirRatio = Mathf.Clamp(
                Sanitize(result.effectiveAirRatio, 1f),
                PressureRatioMin,
                1f);

            float powerFactorAirRatio = Mathf.Lerp(1f, result.effectiveAirRatio, PowerFactorAirEffect);
            result.loadFactor = ResolveLoadFactor(input);
            result.horsepowerAfterEnvironment = result.horsepowerBeforeEnvironment * result.effectiveAirRatio;
            result.powerFactorAfterEnvironment = result.powerFactorBeforeEnvironment * powerFactorAirRatio;
            result.boostTargetPsi = turbo && effect != null
                ? Mathf.Max(0f, Sanitize(effect.boostTargetPsi, 0f))
                : 0f;
            result.boostLimitPsi = turbo && effect != null
                ? Mathf.Max(result.boostTargetPsi, Sanitize(effect.boostLimitPsi, result.boostTargetPsi))
                : 0f;
            result.estimatedBoostPsi = result.boostTargetPsi * result.loadFactor;
            result.estimatedManifoldPressureKpa =
                SeaLevelPressureKpa * result.altitudePressureRatio +
                result.estimatedBoostPsi * UnitConversion.KilopascalsPerPsi;
            return result;
        }

        private static void AddPartGains(PowerGainBreakdown gains, string category, PartEffect effect)
        {
            float horsepowerGain = MultiplierToGain(effect.horsePowerMultiplier);
            float powerFactorGain = MultiplierToGain(effect.powerFactorMultiplier) * PowerFactorPartGainScale;

            if (string.Equals(category, PartCatalog.EngineCore, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.engineHorsepowerGain += horsepowerGain;
                gains.enginePowerFactorGain += powerFactorGain;
                return;
            }

            if (string.Equals(category, PartCatalog.EnginePiston, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.EngineCrank, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.engineHorsepowerGain += horsepowerGain;
                gains.enginePowerFactorGain += powerFactorGain;
                return;
            }

            if (string.Equals(category, PartCatalog.Turbo, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.turboHorsepowerGain += horsepowerGain;
                gains.turboPowerFactorGain += powerFactorGain;
                return;
            }

            if (string.Equals(category, PartCatalog.Intake, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.intakeHorsepowerGain += horsepowerGain;
                gains.intakePowerFactorGain += powerFactorGain;
                return;
            }

            gains.otherHorsepowerGain += horsepowerGain;
            gains.otherPowerFactorGain += powerFactorGain;
        }

        private static float ComputePressureRatio(float altitudeMeters)
        {
            float altitude = Mathf.Clamp(Sanitize(altitudeMeters, 0f), 0f, AltitudeMaxMeters);
            float baseTerm = Mathf.Max(0.10f, 1f - 2.25577e-5f * altitude);
            float ratio = Mathf.Pow(baseTerm, 5.25588f);
            return Mathf.Clamp(Sanitize(ratio, 1f), PressureRatioMin, PressureRatioMax);
        }

        private static float ResolveLoadFactor(EngineSimulationInput input)
        {
            if (input != null && input.hasLoad01)
                return Mathf.Clamp01(Sanitize(input.load01, 1f));

            return 1f;
        }

        private static float ComposeAdditiveMultiplier(float currentMultiplier, float addedMultiplier)
        {
            return GainToMultiplier(MultiplierToGain(currentMultiplier) + MultiplierToGain(addedMultiplier));
        }

        private static float GainToMultiplier(float gain)
        {
            return Mathf.Max(0.05f, 1f + Sanitize(gain, 0f));
        }

        private static float MultiplierToGain(float multiplier)
        {
            if (!IsFinite(multiplier))
                return 0f;

            return multiplier - 1f;
        }

        private static float PercentToGain(float percent)
        {
            if (!IsFinite(percent))
                return 0f;

            return percent / 100f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            value = Sanitize(value, fallback);
            return value > 0f ? value : fallback;
        }

        private static float Sanitize(float value, float fallback)
        {
            return IsFinite(value) ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float ClampRelativeSigned(float value, float baseline, float minMult, float maxMult)
        {
            if (Mathf.Abs(baseline) <= 0.001f)
                return Mathf.Clamp(value, -10f, 10f);

            float a = baseline * minMult;
            float b = baseline * maxMult;
            return Mathf.Clamp(value, Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private static Vector3 ToVector3(Vec3Data value)
        {
            return value != null ? value.ToVector3() : Vector3.zero;
        }
    }
}
