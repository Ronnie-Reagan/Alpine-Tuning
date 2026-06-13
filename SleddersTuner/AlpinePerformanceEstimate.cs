using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal sealed class AlpinePerformanceEstimate
    {
        public string sledName;
        public string setupName;
        public bool estimated = true;
        public float peakHorsepower;
        public float peakTorqueNm;
        public float estimatedWeightKg;
        public float engagementRpm;
        public readonly List<PerformanceStatEstimate> stats = new List<PerformanceStatEstimate>();
        public readonly List<PerformanceCurveSample> curve = new List<PerformanceCurveSample>();

        public static AlpinePerformanceEstimate Build(
            string sledName,
            string setupName,
            SledDefaults defaults,
            TuneComputation current)
        {
            var estimate = new AlpinePerformanceEstimate
            {
                sledName = sledName,
                setupName = setupName,
                estimatedWeightKg = current != null && current.stats != null ? current.stats.weight : 0f
            };

            if (defaults == null || current == null || current.stats == null)
                return estimate;

            PartEffect effect = current.mergedEffect ?? new PartEffect();
            float clutchMin = defaults.controller != null && defaults.controller.hasClutchRpmMin
                ? defaults.controller.clutchRpmMin
                : 4200f;

            if (current.parts != null)
            {
                // The computation already contains merged effects; parts are only used by tune math.
            }

            estimate.peakHorsepower = current.stats.horsePower;
            estimate.peakTorqueNm = EstimatePeakTorqueNm(current.stats.horsePower, current.stats.powerFactor);
            estimate.engagementRpm = Mathf.Clamp(clutchMin + effect.clutchRpmMinOffset, 2500f, 7000f);

            float powerRatio = Ratio(current.stats.horsePower, defaults.horsePower);
            float torqueRatio = Ratio(estimate.peakTorqueNm, EstimatePeakTorqueNm(defaults.horsePower, defaults.powerFactor));
            float weightDeltaKg = current.stats.weight - defaults.weight;
            float weightRatio = Ratio(current.stats.weight, defaults.weight);
            float lugRatio = Ratio(current.stats.lugHeight, defaults.lugHeight);
            float biteRatio = Ratio(current.stats.friction, defaults.friction);
            float stanceDelta = current.stats.skiStance - defaults.skiStance;
            float responseRatio = Mathf.Clamp(
                powerRatio * 0.25f +
                torqueRatio * 0.30f +
                effect.rpmSensitivityMultiplier * 0.25f +
                (1f + Mathf.Abs(effect.throttleExponentDelta) * 2.5f) * 0.20f,
                0.60f,
                1.55f);

            float trackBiteRatio = Mathf.Clamp((lugRatio * 0.45f + biteRatio * 0.55f), 0.55f, 1.80f);
            float powderFloatRatio = Mathf.Clamp(lugRatio * 0.60f + (1.05f / Mathf.Max(0.75f, weightRatio)) * 0.40f, 0.55f, 1.70f);
            float trailStabilityRatio = Mathf.Clamp(1f + stanceDelta * 2.2f + (weightRatio - 1f) * 0.35f + (effect.stabilizerDampingMultiplier - 1f) * 0.35f, 0.55f, 1.55f);
            float climbingRatio = Mathf.Clamp(powerRatio * 0.30f + torqueRatio * 0.25f + trackBiteRatio * 0.35f + (1.05f / Mathf.Max(0.75f, weightRatio)) * 0.10f, 0.55f, 1.80f);
            float agilityRatio = Mathf.Clamp((1.05f / Mathf.Max(0.75f, weightRatio)) * 0.45f + responseRatio * 0.30f + (1f - stanceDelta * 2.0f) * 0.25f, 0.50f, 1.60f);
            float heatRatio = Mathf.Clamp(1f + Mathf.Max(0f, powerRatio - 1f) * 1.7f + current.stats.estimatedBoostPsi / 24f, 0.70f, 2.00f);
            float beltRatio = Mathf.Clamp(1f + Mathf.Max(0f, torqueRatio - 1f) * 1.5f + Mathf.Abs(effect.clutchRpmMinOffset + effect.clutchRpmMaxOffset) / 1400f, 0.70f, 2.00f);
            float fuelRatio = Mathf.Clamp(1f + Mathf.Max(0f, powerRatio - 1f) * 1.2f + current.stats.estimatedBoostPsi / 30f, 0.75f, 1.85f);

            estimate.stats.Add(PercentStat("Power", powerRatio, "Estimated engine output compared to the stock setup."));
            estimate.stats.Add(PercentStat("Torque", torqueRatio, "Estimated pulling force from the engine. More torque helps launches and climbing."));
            estimate.stats.Add(PercentStat("Throttle Response", responseRatio, "How quickly the sled responds when you ask for power."));
            estimate.stats.Add(PercentStat("Track Bite", trackBiteRatio, "How strongly the track hooks into snow. Higher bite helps deep snow and climbing but can add drag."));
            estimate.stats.Add(PercentStat("Powder Float", powderFloatRatio, "How well the setup stays on top of soft snow."));
            estimate.stats.Add(PercentStat("Trail Stability", trailStabilityRatio, "How stable the sled feels at speed and on firmer trails."));
            estimate.stats.Add(PercentStat("Climbing", climbingRatio, "Estimated climbing strength from power, bite, flotation, and weight."));
            estimate.stats.Add(PercentStat("Agility", agilityRatio, "How quickly the sled changes direction."));
            estimate.stats.Add(AbsoluteStat("Weight", NormalizeLowerIsBetter(weightRatio), weightDeltaKg, "kg", "Total sled weight. Lower weight improves response and climbing."));
            estimate.stats.Add(RiskStat("Heat Risk", heatRatio, "Estimated heat load from power, boost, clutching, and sustained load."));
            estimate.stats.Add(RiskStat("Belt Stress", beltRatio, "Estimated load on the clutch and belt. Higher stress may feel stronger but less forgiving."));
            estimate.stats.Add(RiskStat("Fuel Use", fuelRatio, "Estimated fuel use from power and boost demand."));

            SampleCurves(defaults, current, estimate);
            return estimate;
        }

        private static void SampleCurves(SledDefaults defaults, TuneComputation current, AlpinePerformanceEstimate estimate)
        {
            const int sampleCount = 48;
            const float minRpm = 2500f;
            const float maxRpm = 9000f;

            float stockTorque = EstimatePeakTorqueNm(defaults.horsePower, defaults.powerFactor);
            float currentTorque = estimate.peakTorqueNm;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
                float rpm = Mathf.Lerp(minRpm, maxRpm, t);
                float shape = TorqueShape(t);
                float stockTorqueAtRpm = stockTorque * shape;
                float currentTorqueAtRpm = currentTorque * shape * CurrentCurveBias(t, current.mergedEffect);
                float stockHpAtRpm = stockTorqueAtRpm * rpm / 7127f;
                float currentHpAtRpm = currentTorqueAtRpm * rpm / 7127f;

                estimate.curve.Add(new PerformanceCurveSample
                {
                    rpm = rpm,
                    stockHorsepower = stockHpAtRpm,
                    currentHorsepower = currentHpAtRpm,
                    stockTorqueNm = stockTorqueAtRpm,
                    currentTorqueNm = currentTorqueAtRpm
                });
            }
        }

        private static float TorqueShape(float t)
        {
            float rise = Mathf.SmoothStep(0.52f, 1.0f, Mathf.Clamp01(t / 0.32f));
            float fall = Mathf.Lerp(1.0f, 0.72f, Mathf.Clamp01((t - 0.62f) / 0.38f));
            return Mathf.Clamp(rise * fall, 0.35f, 1.05f);
        }

        private static float CurrentCurveBias(float t, PartEffect effect)
        {
            if (effect == null)
                return 1f;

            float response = Mathf.Clamp(effect.boostResponseMultiplier, 0.85f, 1.20f);
            float lowEnd = Mathf.Lerp(1.02f, 0.98f, t);
            float turboTop = effect.isTurbo ? Mathf.Lerp(0.96f, 1.08f, t) : 1f;
            return Mathf.Clamp(response * lowEnd * turboTop, 0.80f, 1.25f);
        }

        private static PerformanceStatEstimate PercentStat(string label, float ratio, string tooltip)
        {
            float delta = (ratio - 1f) * 100f;
            return new PerformanceStatEstimate
            {
                label = label,
                normalized01 = RatioToBar(ratio),
                delta = delta,
                deltaLabel = delta.ToString("+0;-0;0") + "%",
                tooltip = tooltip
            };
        }

        private static PerformanceStatEstimate AbsoluteStat(string label, float normalized01, float delta, string unit, string tooltip)
        {
            return new PerformanceStatEstimate
            {
                label = label,
                normalized01 = normalized01,
                delta = delta,
                deltaLabel = delta.ToString("+0;-0;0") + " " + unit,
                tooltip = tooltip
            };
        }

        private static PerformanceStatEstimate RiskStat(string label, float ratio, string tooltip)
        {
            float delta = (ratio - 1f) * 100f;
            return new PerformanceStatEstimate
            {
                label = label,
                normalized01 = Mathf.Clamp01((ratio - 0.60f) / 1.40f),
                delta = delta,
                deltaLabel = ratio > 1.12f ? "Higher" : ratio < 0.92f ? "Lower" : "Stock",
                tooltip = tooltip
            };
        }

        private static float RatioToBar(float ratio)
        {
            return Mathf.Clamp01((ratio - 0.60f) / 0.90f);
        }

        private static float NormalizeLowerIsBetter(float ratio)
        {
            return Mathf.Clamp01((1.35f - ratio) / 0.60f);
        }

        private static float Ratio(float value, float baseline)
        {
            return Mathf.Abs(baseline) > 0.001f ? value / baseline : 1f;
        }

        private static float EstimatePeakTorqueNm(float horsepower, float powerFactor)
        {
            float normalizedPowerFactor = Mathf.Clamp(powerFactor, 0.25f, 2.5f);
            return Mathf.Clamp(horsepower * 7127f / 7400f * Mathf.Lerp(0.88f, 1.16f, Mathf.InverseLerp(0.45f, 1.40f, normalizedPowerFactor)), 40f, 520f);
        }
    }

    internal sealed class PerformanceStatEstimate
    {
        public string label;
        public float normalized01;
        public float delta;
        public string deltaLabel;
        public string tooltip;
    }

    internal sealed class PerformanceCurveSample
    {
        public float rpm;
        public float stockHorsepower;
        public float currentHorsepower;
        public float stockTorqueNm;
        public float currentTorqueNm;
    }
}
