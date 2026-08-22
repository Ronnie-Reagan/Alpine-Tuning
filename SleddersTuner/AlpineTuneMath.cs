using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineTuneMath
    {
        internal enum EstimatedEngineArchetype
        {
            Unknown,
            TwoStrokeNaturallyAspirated,
            TwoStrokeTurbo,
            FourStrokeNaturallyAspirated,
            FourStrokeTurbo
        }

        internal struct ResolvedClutchRange
        {
            public bool HasMinimum;
            public float Minimum;
            public bool HasMaximum;
            public float Maximum;
        }

        private const float HpMinMult = 0.60f;
        private const float HpMaxMult = 1.75f;
        private const float HpAbsoluteMin = 20f;
        private const float HpAbsoluteMax = 380f;

        private const float LugMinMult = 0.50f;
        private const float LugMaxMult = 2.25f;
        private const float LugAbsoluteMin = 1f;
        private const float LugAbsoluteMax = 95f;

        private const float FrictionMinMult = 0.55f;
        private const float FrictionMaxMult = 1.65f;
        private const float FrictionAbsoluteMin = 0.05f;
        private const float FrictionAbsoluteMax = 3.00f;
        public static ResolvedStats ComputeStats(
            SledDefaults baseDefaults,
            SledDefaults engineDefaults,
            PartEffect effect,
            FineTuneSettings fine)
        {
            return ComputeStats(baseDefaults, engineDefaults, null, effect, fine);
        }

        public static ResolvedStats ComputeStats(
            SledDefaults baseDefaults,
            SledDefaults engineDefaults,
            IEnumerable<TunePart> parts,
            PartEffect effect,
            FineTuneSettings fine)
        {
            if (baseDefaults == null || engineDefaults == null)
                return null;

            if (effect == null)
                effect = new PartEffect();

            if (fine == null)
                fine = new FineTuneSettings();

            ClampFineTune(fine);

            float tractionTrim = 1f + fine.tractionTrimPercent / 100f;
            float weightTrim = 1f + fine.weightTrimPercent / 100f;

            var gains = ComputePowerGainBreakdown(parts, effect, fine);
            float hp = engineDefaults.horsePower * GainToMultiplier(gains.TotalHorsepowerGain);
            // powerFactor is retained solely so old factory defaults can be
            // restored exactly. The current game physics never reads it, so
            // Alpine never tunes or presents it as a performance control.
            float pf = baseDefaults.powerFactor;
            // An engine swap carries the donor engine's native induction state.
            // Alpine turbo parts may add forced induction, but the recipient
            // chassis must not keep or erase the donor's factory turbo flag.
            bool resolvedTurbo = engineDefaults.isTurboOn || effect.isTurbo;
            float lug = TrackSpecResolver.ResolveLugHeightMillimeters(baseDefaults, effect);
            float friction = baseDefaults.friction * effect.frictionMultiplier * tractionTrim;

            // Fuel capacity belongs to the recipient chassis/tank. Engine swaps
            // intentionally inherit only the donor engine's nominal consumption.
            float fuelCapacity = Mathf.Max(0.01f, baseDefaults.fuelCapacity) *
                                 SanitizePositive(effect.fuelCapacityMultiplier, 1f);
            fuelCapacity = ClampRelative(
                fuelCapacity,
                Mathf.Max(0.01f, baseDefaults.fuelCapacity),
                0.50f, 1.75f, 1f, 100f);
            float fuelConsumption = Mathf.Max(0f, engineDefaults.fuelConsumption);

            const float GasolineDensityKgPerLiter = 0.74f;
            float backpackFuelCapacity = Mathf.Max(0f, effect.backpackFuelCapacityLiters);
            float backpackPayloadMass = backpackFuelCapacity > 0.001f
                ? Mathf.Max(0f, effect.backpackContainerMassKg) +
                  backpackFuelCapacity * GasolineDensityKgPerLiter
                : 0f;
            // Tank shell mass is part of the installed sled. Backpack fuel is a
            // live rider payload and is applied by AlpineFuelSystem using the
            // actual remaining reserve, so it is not baked into the VSO weight.
            float weight = (baseDefaults.weight * effect.weightMultiplier +
                            effect.weightOffset +
                            effect.tankHardwareMassOffsetKg) * weightTrim;

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
                (effect.skiStanceOffset + fine.skiStanceTrim) * 1000f;

            float skisXDistanceOffset =
                baseDefaults.skisXDistanceOffset +
                effect.skisXDistanceOffset +
                fine.skiStanceTrim;

            hp = ClampRelative(hp, engineDefaults.horsePower, HpMinMult, HpMaxMult, HpAbsoluteMin, HpAbsoluteMax);
            lug = ClampRelative(lug, baseDefaults.lugHeight, LugMinMult, LugMaxMult, LugAbsoluteMin, LugAbsoluteMax);
            friction = ClampRelative(friction, baseDefaults.friction, FrictionMinMult, FrictionMaxMult, FrictionAbsoluteMin, FrictionAbsoluteMax);
            if (baseDefaults.weight > 1f)
                weight = Mathf.Clamp(weight, baseDefaults.weight * 0.75f, baseDefaults.weight * 1.35f);
            else
                weight = Mathf.Max(1f, weight);

            com = ClampVectorOffset(com, baseCom, new Vector3(0.10f, 0.24f, 0.28f));
            driverCom = ClampVectorOffset(driverCom, baseDriverCom, new Vector3(0.10f, 0.16f, 0.16f));
            // VehicleScriptableObject.skiStance is millimetres. Persisted part
            // offsets and the schema-2 fine trim intentionally remain metres.
            skiStance = ClampOffset(skiStance, baseDefaults.skiStance, 180f, 0f, 4000f);
            skisXDistanceOffset = ClampOffset(skisXDistanceOffset, baseDefaults.skisXDistanceOffset, 0.12f, -1f, 1f);
            bool hasResolvedMaxRpm = engineDefaults.hasMaxRpm &&
                                     IsFinite(engineDefaults.maxRpm) && engineDefaults.maxRpm > 1000f;
            float resolvedMaxRpm = hasResolvedMaxRpm ? engineDefaults.maxRpm : 0f;
            if (!hasResolvedMaxRpm && baseDefaults.hasMaxRpm &&
                IsFinite(baseDefaults.maxRpm) && baseDefaults.maxRpm > 1000f)
            {
                hasResolvedMaxRpm = true;
                resolvedMaxRpm = baseDefaults.maxRpm;
            }

            return new ResolvedStats
            {
                horsePower = hp,
                powerFactor = pf,
                hasMaxRpm = hasResolvedMaxRpm,
                maxRpm = hasResolvedMaxRpm ? Mathf.Clamp(resolvedMaxRpm, 3000f, 14000f) : 0f,
                lugHeight = lug,
                friction = friction,
                weight = weight,
                fuelCapacity = fuelCapacity,
                fuelConsumption = fuelConsumption,
                backpackFuelCapacityLiters = backpackFuelCapacity,
                backpackPayloadMassKg = backpackPayloadMass,
                requiresCosmeticBackpack = effect.requiresCosmeticBackpack,
                skiStance = skiStance,
                skisXDistanceOffset = skisXDistanceOffset,
                isTurboOn = resolvedTurbo,
                engineText = !string.IsNullOrWhiteSpace(effect.engineText)
                    ? effect.engineText
                    : engineDefaults.engineText,
                centerOfMassOffset = Vec3Data.From(com),
                driverCenterOfMassOffset = Vec3Data.From(driverCom)
            };
        }

        internal static float NativeDeliveredTrackPower(
            float horsepower,
            float efficiency,
            float driveInput,
            float trackSpeed,
            float taperStart,
            float taperEnd)
        {
            if (!IsFinite(horsepower) || !IsFinite(efficiency) || !IsFinite(driveInput) ||
                !IsFinite(trackSpeed) || !IsFinite(taperStart) || !IsFinite(taperEnd) ||
                taperEnd <= taperStart)
            {
                return 0f;
            }

            float speed = Mathf.Abs(trackSpeed);
            float taper = speed <= taperStart
                ? 1f
                : speed >= taperEnd
                    ? 0f
                    : 1f - Mathf.InverseLerp(taperStart, taperEnd, speed);
            return Mathf.Max(0f, horsepower) * 782.7273f *
                   Mathf.Max(0f, efficiency) * Mathf.Clamp01(driveInput) * taper;
        }

        internal static float NativeTrackForce(float power, float trackSpeed, float minimumSpeed)
        {
            if (!IsFinite(power) || !IsFinite(trackSpeed) || !IsFinite(minimumSpeed) || minimumSpeed <= 0f)
                return 0f;
            return Mathf.Max(0f, power) / Mathf.Max(Mathf.Abs(trackSpeed), minimumSpeed);
        }

        internal static bool TryGetEstimatedEngineCurve(
            string engineName,
            bool turbo,
            out EstimatedEngineArchetype archetype,
            out Vector2[] anchors)
        {
            anchors = null;
            string token = (engineName ?? string.Empty).ToUpperInvariant();
            bool fourStroke = token.Contains("ACE");
            bool twoStroke = token.Contains("E-TEC") || token.Contains("ETEC") ||
                             token.Contains("PATRIOT") || token.Contains("KITTY") ||
                             token.Contains("LIBERTY") || token.Contains("TRIPLE");
            if (!fourStroke && !twoStroke)
            {
                archetype = EstimatedEngineArchetype.Unknown;
                return false;
            }

            if (fourStroke && turbo)
            {
                archetype = EstimatedEngineArchetype.FourStrokeTurbo;
                anchors = new[]
                {
                    new Vector2(0f, .50f), new Vector2(.30f, .80f),
                    new Vector2(.70f, 1f), new Vector2(1f, .94f)
                };
            }
            else if (fourStroke)
            {
                archetype = EstimatedEngineArchetype.FourStrokeNaturallyAspirated;
                anchors = new[]
                {
                    new Vector2(0f, .42f), new Vector2(.35f, .72f),
                    new Vector2(.76f, 1f), new Vector2(1f, .88f)
                };
            }
            else if (turbo)
            {
                archetype = EstimatedEngineArchetype.TwoStrokeTurbo;
                anchors = new[]
                {
                    new Vector2(0f, .38f), new Vector2(.40f, .70f),
                    new Vector2(.82f, 1f), new Vector2(1f, .92f)
                };
            }
            else
            {
                archetype = EstimatedEngineArchetype.TwoStrokeNaturallyAspirated;
                anchors = new[]
                {
                    new Vector2(0f, .30f), new Vector2(.45f, .62f),
                    new Vector2(.86f, 1f), new Vector2(1f, .88f)
                };
            }
            return true;
        }

        internal static float InterpolateEstimatedEngineCurve(Vector2[] anchors, float normalizedRpm)
        {
            if (anchors == null || anchors.Length == 0 || !IsFinite(normalizedRpm))
                return 0f;
            normalizedRpm = Mathf.Clamp01(normalizedRpm);
            for (int i = 1; i < anchors.Length; i++)
            {
                if (normalizedRpm <= anchors[i].x)
                {
                    float t = Mathf.InverseLerp(anchors[i - 1].x, anchors[i].x, normalizedRpm);
                    return Mathf.Lerp(anchors[i - 1].y, anchors[i].y, t);
                }
            }
            return anchors[anchors.Length - 1].y;
        }

        internal static float ResolveEstimatedRedline(ResolvedStats stats)
        {
            float value = stats != null && stats.hasMaxRpm ? stats.maxRpm : 8500f;
            if (!IsFinite(value) || value <= 1000f)
                value = 8500f;
            return Mathf.Clamp(value, 3000f, 14000f);
        }

        internal static ResolvedClutchRange ResolveClutchRange(
            ControllerDefaults defaults,
            PartEffect effect,
            FineTuneSettings fine)
        {
            var result = new ResolvedClutchRange();
            if (defaults == null)
                return result;

            float trimPercent = fine?.clutchTrimPercent ?? 0f;
            if (!IsFinite(trimPercent))
                trimPercent = 0f;
            float trim = 1f + Mathf.Clamp(trimPercent, -10f, 10f) / 100f;
            float minimumOffset = effect?.clutchRpmMinOffset ?? 0f;
            float maximumOffset = effect?.clutchRpmMaxOffset ?? 0f;
            if (!IsFinite(minimumOffset))
                minimumOffset = 0f;
            if (!IsFinite(maximumOffset))
                maximumOffset = 0f;

            result.HasMinimum = defaults.hasClutchRpmMin && IsFinite(defaults.clutchRpmMin);
            result.HasMaximum = defaults.hasClutchRpmMax && IsFinite(defaults.clutchRpmMax);
            bool modified = Mathf.Abs(trimPercent) > 0.0001f ||
                            Mathf.Abs(minimumOffset) > 0.0001f ||
                            Mathf.Abs(maximumOffset) > 0.0001f;
            if (!modified)
            {
                result.Minimum = result.HasMinimum ? defaults.clutchRpmMin : 0f;
                result.Maximum = result.HasMaximum ? defaults.clutchRpmMax : 0f;
                return result;
            }

            if (result.HasMinimum)
            {
                result.Minimum = ClampRelative(
                    (defaults.clutchRpmMin + minimumOffset) * trim,
                    defaults.clutchRpmMin,
                    0.75f,
                    1.35f,
                    0f,
                    14000f);
            }
            if (result.HasMaximum)
            {
                result.Maximum = ClampRelative(
                    (defaults.clutchRpmMax + maximumOffset) * trim,
                    defaults.clutchRpmMax,
                    0.75f,
                    1.35f,
                    0f,
                    14000f);
            }
            if (result.HasMinimum && result.HasMaximum && result.Maximum < result.Minimum + 100f)
                result.Maximum = Mathf.Min(14000f, result.Minimum + 100f);

            return result;
        }

        internal static float ResolveRpmSensitivity(float baseline, PartEffect effect)
        {
            float rpmMultiplier = SanitizePositive(effect?.rpmSensitivityMultiplier ?? 1f, 1f);
            float turboMultiplier = SanitizePositive(effect?.turboRpmResponseMultiplier ?? 1f, 1f);
            if (Mathf.Approximately(rpmMultiplier, 1f) && Mathf.Approximately(turboMultiplier, 1f))
                return baseline;
            return ClampRelative(
                baseline * rpmMultiplier * Mathf.Clamp(turboMultiplier, 0.70f, 1.35f),
                baseline,
                0.50f,
                1.70f,
                0.05f,
                10f);
        }

        internal static float ResolveRpmSensitivityDown(float baseline, PartEffect effect)
        {
            float multiplier = SanitizePositive(effect?.rpmSensitivityDownMultiplier ?? 1f, 1f);
            if (Mathf.Approximately(multiplier, 1f))
                return baseline;
            return ClampRelative(
                baseline * multiplier,
                baseline,
                0.50f,
                1.70f,
                0.05f,
                10f);
        }

        internal static float ResolveEstimatedCurveStartRpm(
            float redline,
            ControllerDefaults recipientController,
            PartEffect effect,
            FineTuneSettings fine)
        {
            redline = !IsFinite(redline) || redline <= 1000f
                ? 8500f
                : Mathf.Clamp(redline, 3000f, 14000f);
            ResolvedClutchRange clutch = ResolveClutchRange(recipientController, effect, fine);
            float start = clutch.HasMinimum && clutch.Minimum > 0f
                ? clutch.Minimum
                : redline * 0.45f;
            return Mathf.Clamp(start, 0f, redline);
        }

        public static void MergeEffect(PartEffect target, PartEffect source)
        {
            if (target == null || source == null)
                return;

            target.horsePowerMultiplier = ComposeAdditiveMultiplier(target.horsePowerMultiplier, source.horsePowerMultiplier);
            target.lugHeightMultiplier *= source.lugHeightMultiplier;
            if (source.lugHeightTargetMm > 0.01f)
                target.lugHeightTargetMm = source.lugHeightTargetMm;
            target.lugHeightOffset += source.lugHeightOffset;
            target.frictionMultiplier *= source.frictionMultiplier;
            target.weightMultiplier *= source.weightMultiplier;
            target.weightOffset += source.weightOffset;
            target.fuelCapacityMultiplier *= SanitizePositive(source.fuelCapacityMultiplier, 1f);
            target.tankHardwareMassOffsetKg += source.tankHardwareMassOffsetKg;
            if (source.backpackFuelCapacityLiters > 0.001f)
            {
                target.backpackFuelCapacityLiters = source.backpackFuelCapacityLiters;
                target.backpackContainerMassKg = source.backpackContainerMassKg;
            }
            target.requiresCosmeticBackpack |= source.requiresCosmeticBackpack;
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
            target.turboRpmResponseMultiplier = Mathf.Clamp(
                target.turboRpmResponseMultiplier * SanitizePositive(source.turboRpmResponseMultiplier, 1f),
                0.70f,
                1.35f);
            target.clutchRpmMinOffset += source.clutchRpmMinOffset;
            target.clutchRpmMaxOffset += source.clutchRpmMaxOffset;
            target.minThrottleOnClutchEngagementOffset += source.minThrottleOnClutchEngagementOffset;
            target.stabilizerDampingMultiplier *= source.stabilizerDampingMultiplier;
            target.trackSpeedDampingMultiplier *= source.trackSpeedDampingMultiplier;
            target.trackSpeedGyroMultiplier *= source.trackSpeedGyroMultiplier;
            target.nativePowerEfficiencyMultiplier *= source.nativePowerEfficiencyMultiplier;
            target.nativeDrivetrainSpeedMultiplier *= source.nativeDrivetrainSpeedMultiplier;
            target.nativeTrackMassMultiplier *= source.nativeTrackMassMultiplier;
            target.nativeAntiRollBarMultiplier *= source.nativeAntiRollBarMultiplier;
            target.nativeTrackRigidityFrontMultiplier *= source.nativeTrackRigidityFrontMultiplier;
            target.nativeTrackRigidityRearMultiplier *= source.nativeTrackRigidityRearMultiplier;
            target.nativeFrontSpringMultiplier *= source.nativeFrontSpringMultiplier;
            target.nativeFrontDamperMultiplier *= source.nativeFrontDamperMultiplier;
            target.nativeFrontCompressionDampingMultiplier *= source.nativeFrontCompressionDampingMultiplier;
            target.nativeFrontReboundDampingMultiplier *= source.nativeFrontReboundDampingMultiplier;
            target.nativeRearSpringMultiplier *= source.nativeRearSpringMultiplier;
            target.nativeRearDamperMultiplier *= source.nativeRearDamperMultiplier;
            target.nativeRearCompressionDampingMultiplier *= source.nativeRearCompressionDampingMultiplier;
            target.nativeRearReboundDampingMultiplier *= source.nativeRearReboundDampingMultiplier;
            target.nativeBrakeForceMultiplier *= source.nativeBrakeForceMultiplier;
            target.nativeSkisMaxAngleMultiplier *= source.nativeSkisMaxAngleMultiplier;
            target.nativeToeAngleMultiplier *= source.nativeToeAngleMultiplier;
            target.nativeCamberFactorMultiplier *= source.nativeCamberFactorMultiplier;
            target.nativeSkiGripMultiplier *= source.nativeSkiGripMultiplier;
            target.nativeTrackGripMultiplier *= source.nativeTrackGripMultiplier;
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
            return gains;
        }

        private static void AddPartGains(PowerGainBreakdown gains, string category, PartEffect effect)
        {
            float horsepowerGain = MultiplierToGain(effect.horsePowerMultiplier);

            if (string.Equals(category, PartCatalog.EngineCore, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.engineHorsepowerGain += horsepowerGain;
                return;
            }

            if (string.Equals(category, PartCatalog.EnginePiston, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.EngineCrank, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.engineHorsepowerGain += horsepowerGain;
                return;
            }

            if (string.Equals(category, PartCatalog.Turbo, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.turboHorsepowerGain += horsepowerGain;
                return;
            }

            if (string.Equals(category, PartCatalog.Intake, System.StringComparison.OrdinalIgnoreCase))
            {
                gains.intakeHorsepowerGain += horsepowerGain;
                return;
            }

            gains.otherHorsepowerGain += horsepowerGain;
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
