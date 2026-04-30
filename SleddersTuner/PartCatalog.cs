using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlpineTuning
{
    internal class PartCatalog
    {
        public const string EngineCore = "engineCore";
        public const string EnginePiston = "enginePiston";
        public const string EngineCrank = "engineCrank";
        public const string Turbo = "turbo";
        public const string Intake = "intakeExhaust";
        public const string Clutch = "clutchGearing";
        public const string ClutchWeights = "clutchWeights";
        public const string RatioFeel = "ratioFeel";
        public const string Track = "track";
        public const string TrackLimiter = "trackLimiter";
        public const string RearShock = "rearShock";
        public const string RearSpring = "rearSpring";
        public const string Suspension = "suspension";
        public const string Chassis = "chassis";
        public const string Skis = "skis";
        public const string HeadlightColor = "headlightColor";
        public const string HeadlightBrightness = "headlightBrightness";
        public const string HeadlightBeam = "headlightBeam";
        public const string HeadlightAim = "headlightAim";
        public const string Accessories = "accessories";

        public static readonly string[] OrderedCategories =
        {
            EngineCore,
            EnginePiston,
            EngineCrank,
            Turbo,
            Intake,
            Clutch,
            ClutchWeights,
            RatioFeel,
            Track,
            TrackLimiter,
            RearShock,
            RearSpring,
            Suspension,
            Chassis,
            Skis,
            HeadlightColor,
            HeadlightBrightness,
            HeadlightBeam,
            HeadlightAim,
            Accessories
        };

        private readonly List<TunePart> _parts = new List<TunePart>();
        private readonly Dictionary<string, TunePart> _byId = new Dictionary<string, TunePart>();

        public PartCatalog()
        {
            Build();
        }

        public IReadOnlyList<TunePart> Parts => _parts;

        public IEnumerable<TunePart> PartsForCategory(string category)
        {
            return _parts.Where(p => p.category == category);
        }

        public TunePart Find(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId))
                return null;

            _byId.TryGetValue(partId, out var part);
            return part;
        }

        public string LabelForCategory(string category)
        {
            switch (category)
            {
                case EngineCore:
                    return "Engine Block";
                case EnginePiston:
                    return "Pistons";
                case EngineCrank:
                    return "Crank";
                case Turbo:
                    return "Turbo / Induction";
                case Intake:
                    return "Intake / Exhaust";
                case Clutch:
                    return "Clutch Calibration";
                case ClutchWeights:
                    return "Clutch Weights";
                case RatioFeel:
                    return "Ratio Feel";
                case Track:
                    return "Track";
                case TrackLimiter:
                    return "Limiter Strap Setup";
                case RearShock:
                    return "Rear Shock Setup";
                case RearSpring:
                    return "Rear Spring Setup";
                case Suspension:
                    return "Suspension";
                case Chassis:
                    return "Chassis";
                case Skis:
                    return "Skis / Stance";
                case HeadlightColor:
                    return "Headlight Color";
                case HeadlightBrightness:
                    return "Headlight Brightness";
                case HeadlightBeam:
                    return "Headlight Beam";
                case HeadlightAim:
                    return "Headlight Alignment";
                case Accessories:
                    return "Native Accessories";
                default:
                    return category;
            }
        }

        public string DefaultPartId(string category)
        {
            switch (category)
            {
                case EngineCore: return "engine.stock";
                case EnginePiston: return "piston.stock";
                case EngineCrank: return "crank.stock";
                case Turbo: return "turbo.none";
                case Intake: return "intake.stock";
                case Clutch: return "clutch.stock";
                case ClutchWeights: return "weights.stock";
                case RatioFeel: return "ratio.stock";
                case Track: return "track.trail";
                case TrackLimiter: return "limiter.stock";
                case RearShock: return "shock.stock";
                case RearSpring: return "spring.stock";
                case Suspension: return "suspension.stock";
                case Chassis: return "chassis.stock";
                case Skis: return "skis.stock";
                case HeadlightColor: return "light.color.stock";
                case HeadlightBrightness: return "light.brightness.stock";
                case HeadlightBeam: return "light.beam.stock";
                case HeadlightAim: return "light.aim.stock";
                case Accessories: return "accessory.stock";
            }

            var part = PartsForCategory(category).FirstOrDefault();
            return part != null ? part.id : null;
        }

        public TuneProfile CreateDefaultProfile(VehicleScriptableObject sled, string author)
        {
            string sledKey = AlpineTuningMod.GetSledKey(sled);
            var profile = new TuneProfile
            {
                profileId = Guid.NewGuid().ToString("N"),
                name = $"{GetSledDisplayName(sled)} Balanced Build",
                author = author,
                targetSledKey = sledKey,
                targetVehicleId = AlpineTuningMod.GetVehicleId(sled),
                createdUnixTime = NowUnix(),
                updatedUnixTime = NowUnix()
            };

            foreach (string category in OrderedCategories)
                profile.SetPartId(category, DefaultPartId(category));

            return profile;
        }

        public TuneProfile CreateLegacyProfile(
            VehicleScriptableObject sled,
            string author,
            string enginePartName,
            string trackPartName,
            string handlingPartName,
            string donorSledKey)
        {
            var profile = CreateDefaultProfile(sled, author);
            profile.name = $"{GetSledDisplayName(sled)} Migrated Tune";
            profile.donorSledKey = donorSledKey;
            profile.SetPartId(EngineCore, MapLegacyEngine(enginePartName));
            profile.SetPartId(Turbo, MapLegacyTurbo(enginePartName));
            profile.SetPartId(Track, MapLegacyTrack(trackPartName));
            profile.SetPartId(Suspension, MapLegacyHandling(handlingPartName));
            return profile;
        }

        public void EnsureProfileSelections(TuneProfile profile)
        {
            if (profile == null)
                return;

            foreach (string category in OrderedCategories)
            {
                string partId = profile.GetPartId(category);
                if (Find(partId) == null)
                    profile.SetPartId(category, DefaultPartId(category));
            }
        }

        private static string GetSledDisplayName(VehicleScriptableObject sled)
        {
            if (sled == null)
                return "Sled";

            if (!string.IsNullOrWhiteSpace(sled.displayName))
                return sled.displayName;

            return sled.name;
        }

        private static long NowUnix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private string MapLegacyEngine(string legacyName)
        {
            if (string.IsNullOrWhiteSpace(legacyName))
                return "engine.stock";

            string value = legacyName.ToLowerInvariant();
            if (value.Contains("performance"))
                return "engine.bigbore";
            if (value.Contains("stage 2"))
                return "engine.stage2";
            if (value.Contains("stage 1"))
                return "engine.stage1";
            if (value.Contains("extreme"))
                return "engine.race";
            return "engine.stock";
        }

        private string MapLegacyTurbo(string legacyName)
        {
            if (string.IsNullOrWhiteSpace(legacyName))
                return "turbo.none";

            string value = legacyName.ToLowerInvariant();
            if (value.Contains("extreme"))
                return "turbo.bigboost";
            if (value.Contains("turbo"))
                return "turbo.mountain";
            return "turbo.none";
        }

        private string MapLegacyTrack(string legacyName)
        {
            if (string.IsNullOrWhiteSpace(legacyName))
                return "track.trail";

            string value = legacyName.ToLowerInvariant();
            if (value.Contains("powder"))
                return "track.powder";
            if (value.Contains("mountain") || value.Contains("alpine"))
                return "track.mountain";
            if (value.Contains("racing"))
                return "track.race";
            if (value.Contains("ice"))
                return "track.ice";
            return "track.trail";
        }

        private string MapLegacyHandling(string legacyName)
        {
            if (string.IsNullOrWhiteSpace(legacyName))
                return "suspension.stock";

            string value = legacyName.ToLowerInvariant();
            if (value.Contains("low"))
                return "suspension.lowcg";
            if (value.Contains("front"))
                return "suspension.frontbite";
            if (value.Contains("rear"))
                return "suspension.freeride";
            if (value.Contains("precision"))
                return "suspension.precision";
            return "suspension.stock";
        }

        private void Build()
        {
            // Engine core: naturally aspirated power gains are composed additively at runtime.
            Add("engine.stock", EngineCore, "Stock Engine", "Factory engine mapping.", false,
                e => { });
            Add("engine.stage1", EngineCore, "Stage 1 Kit", "Mild porting and calibration with a small real-world power gain.", true,
                e => { e.horsePowerMultiplier = 1.055f; e.powerFactorMultiplier = 1.018f; });
            Add("engine.stage2", EngineCore, "Stage 2 Kit", "Stronger top-end calibration with a controlled torque bump.", true,
                e => { e.horsePowerMultiplier = 1.10f; e.powerFactorMultiplier = 1.04f; e.weightOffset = 2f; });
            Add("engine.bigbore", EngineCore, "Big Bore Build", "More displacement without full race volatility.", true,
                e => { e.horsePowerMultiplier = 1.17f; e.powerFactorMultiplier = 1.08f; e.weightOffset = 5f; });
            Add("engine.race", EngineCore, "Race Engine", "High-output naturally aspirated package with sharper response.", true,
                e => { e.horsePowerMultiplier = 1.24f; e.powerFactorMultiplier = 1.12f; e.weightOffset = 6f; e.throttleExponentDelta = -0.04f; });

            Add("piston.stock", EnginePiston, "Stock Pistons", "Factory rotating assembly behavior.", false,
                e => { });
            Add("piston.lightweight", EnginePiston, "Lightweight Pistons", "Small rotating-mass reduction for quicker response.", false,
                e => { e.throttleExponentDelta = -0.025f; e.rpmSensitivityMultiplier = 1.025f; e.weightOffset = -1f; });
            Add("piston.highcompression", EnginePiston, "High Compression Pistons", "Mild compression-oriented power transfer with sharper response.", false,
                e => { e.horsePowerMultiplier = 1.025f; e.powerFactorMultiplier = 1.015f; e.throttleExponentDelta = -0.015f; e.weightOffset = 0.5f; });
            Add("piston.forgedturbo", EnginePiston, "Forged Turbo Pistons", "Stronger turbo-safe piston setup with stable RPM response.", false,
                e => { e.horsePowerMultiplier = 1.015f; e.powerFactorMultiplier = 1.012f; e.rpmSensitivityDownMultiplier = 0.985f; e.weightOffset = 1.5f; });
            Add("piston.race", EnginePiston, "Race Pistons", "Response-focused race piston setup with a modest power transfer gain.", false,
                e => { e.horsePowerMultiplier = 1.035f; e.powerFactorMultiplier = 1.018f; e.throttleExponentDelta = -0.035f; e.rpmSensitivityMultiplier = 1.035f; e.weightOffset = -0.5f; });

            Add("crank.stock", EngineCrank, "Stock Crank", "Factory crank behavior.", false,
                e => { });
            Add("crank.balanced", EngineCrank, "Balanced Crank", "Smoother RPM response without changing the power ceiling much.", false,
                e => { e.rpmSensitivityMultiplier = 1.018f; e.rpmSensitivityDownMultiplier = 0.99f; });
            Add("crank.lightened", EngineCrank, "Lightened Crank", "Quicker rev response with a small weight reduction.", false,
                e => { e.throttleExponentDelta = -0.025f; e.rpmSensitivityMultiplier = 1.035f; e.weightOffset = -1.5f; });
            Add("crank.heavyduty", EngineCrank, "Heavy Duty Crank", "More stable loaded RPM behavior with a slight weight penalty.", false,
                e => { e.powerFactorMultiplier = 1.012f; e.rpmSensitivityDownMultiplier = 0.975f; e.weightOffset = 2f; });
            Add("crank.race", EngineCrank, "Race Crank", "Sharp race response with controlled RPM sensitivity.", false,
                e => { e.horsePowerMultiplier = 1.018f; e.powerFactorMultiplier = 1.012f; e.throttleExponentDelta = -0.035f; e.rpmSensitivityMultiplier = 1.045f; e.weightOffset = -1f; });

            // Turbo / induction: boosted gains add to engine gains and partially compensate for altitude.
            Add("turbo.none", Turbo, "Naturally Aspirated", "No forced induction.", false,
                e => { });
            Add("turbo.trail", Turbo, "Trail Turbo", "Fast-spooling boost for broad terrain.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.10f;
                    e.powerFactorMultiplier = 1.04f;
                    e.weightOffset = 4f;
                    e.isTurbo = true;
                    e.turboAltitudeCompensation = 0.40f;
                    e.boostResponseMultiplier = 1.02f;
                    e.boostTargetPsi = 5f;
                    e.boostLimitPsi = 7f;
                    e.engineText = "Trail Turbo";
                    e.rpmSensitivityMultiplier = 1.03f;
                });
            Add("turbo.mountain", Turbo, "Mountain Turbo", "Balanced boost with stronger altitude compensation.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.15f;
                    e.powerFactorMultiplier = 1.075f;
                    e.weightOffset = 7f;
                    e.isTurbo = true;
                    e.turboAltitudeCompensation = 0.65f;
                    e.boostResponseMultiplier = 1.04f;
                    e.boostTargetPsi = 7f;
                    e.boostLimitPsi = 9f;
                    e.engineText = "Mountain Turbo";
                    e.rpmSensitivityMultiplier = 1.05f;
                });
            Add("turbo.bigboost", Turbo, "Big Boost Kit", "High-output boost package with altitude-focused calibration and extra weight.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.25f;
                    e.powerFactorMultiplier = 1.10f;
                    e.weightOffset = 11f;
                    e.isTurbo = true;
                    e.turboAltitudeCompensation = 0.80f;
                    e.boostResponseMultiplier = 1.07f;
                    e.boostTargetPsi = 10f;
                    e.boostLimitPsi = 12f;
                    e.engineText = "Big Boost Turbo";
                    e.rpmSensitivityMultiplier = 1.07f;
                    e.wheelieThresholdOffset = -0.04f;
                });

            // Intake / exhaust: small response and weight changes that should not dominate the build.
            Add("intake.stock", Intake, "Stock Intake / Exhaust", "Factory breathing.", false,
                e => { });
            Add("intake.flow", Intake, "High Flow Intake", "Small response and horsepower gain.", false,
                e => { e.horsePowerMultiplier = 1.03f; e.powerFactorMultiplier = 1.01f; e.weightOffset = -1f; });
            Add("intake.pipe", Intake, "Race Pipe", "Sharper response with a modest breathing gain and weight drop.", false,
                e => { e.horsePowerMultiplier = 1.05f; e.powerFactorMultiplier = 1.02f; e.weightOffset = -2f; e.throttleExponentDelta = -0.03f; });

            // Clutch / gearing: runtime controller feel. Tune RPM offsets and throttleExponentDelta here.
            Add("clutch.stock", Clutch, "Stock Calibration", "Factory clutch calibration.", false,
                e => { });
            Add("clutch.trail", Clutch, "TrapLine Smooth", "Smoother engagement and forgiving backshift.", false,
                e => { e.clutchRpmMinOffset = -100f; e.clutchRpmMaxOffset = 70f; e.minThrottleOnClutchEngagementOffset = -0.015f; e.rpmSensitivityMultiplier = 1.025f; e.rpmSensitivityDownMultiplier = 1.02f; });
            Add("clutch.mountain", Clutch, "ClimberClub Mountain", "Holds RPM under load for better climb response.", false,
                e => { e.clutchRpmMinOffset = 120f; e.clutchRpmMaxOffset = 230f; e.minThrottleOnClutchEngagementOffset = 0.01f; e.rpmSensitivityMultiplier = 1.08f; e.rpmSensitivityDownMultiplier = 0.96f; });
            Add("clutch.race", Clutch, "IceRacer Aggressive", "Quick engagement and sharp throttle response.", false,
                e => { e.clutchRpmMinOffset = 240f; e.clutchRpmMaxOffset = 340f; e.minThrottleOnClutchEngagementOffset = 0.025f; e.rpmSensitivityMultiplier = 1.12f; e.rpmSensitivityDownMultiplier = 0.95f; e.throttleExponentDelta = -0.06f; });

            Add("weights.stock", ClutchWeights, "Stock Weights", "Factory clutch weight feel.", false,
                e => { });
            Add("weights.light", ClutchWeights, "Light Weights", "Lighter weight feel with quicker engagement response.", false,
                e => { e.clutchRpmMinOffset = 110f; e.clutchRpmMaxOffset = 80f; e.rpmSensitivityMultiplier = 1.035f; e.throttleExponentDelta = -0.015f; });
            Add("weights.heavy", ClutchWeights, "Heavy Weights", "Heavier weight feel with calmer shift response.", false,
                e => { e.clutchRpmMinOffset = -90f; e.clutchRpmMaxOffset = -50f; e.rpmSensitivityMultiplier = 0.98f; e.rpmSensitivityDownMultiplier = 1.02f; });
            Add("weights.mountain", ClutchWeights, "Adjustable Mountain Weights", "Mountain weight feel that keeps RPM responsive under load.", false,
                e => { e.clutchRpmMinOffset = 60f; e.clutchRpmMaxOffset = 160f; e.rpmSensitivityMultiplier = 1.055f; e.rpmSensitivityDownMultiplier = 0.975f; });
            Add("weights.race", ClutchWeights, "Race Weights", "Aggressive weight feel for fast RPM changes.", false,
                e => { e.clutchRpmMinOffset = 180f; e.clutchRpmMaxOffset = 220f; e.rpmSensitivityMultiplier = 1.085f; e.throttleExponentDelta = -0.025f; });

            Add("ratio.stock", RatioFeel, "Stock Ratio Feel", "Factory drive response feel.", false,
                e => { });
            Add("ratio.low", RatioFeel, "Low Ratio Feel", "Approximates shorter gearing through power delivery and clutch response.", false,
                e => { e.powerFactorMultiplier = 1.018f; e.clutchRpmMinOffset = 60f; e.clutchRpmMaxOffset = 80f; e.rpmSensitivityMultiplier = 1.025f; });
            Add("ratio.highspeed", RatioFeel, "High Speed Ratio Feel", "Approximates taller gearing with calmer low-speed response.", false,
                e => { e.powerFactorMultiplier = 0.992f; e.clutchRpmMinOffset = -60f; e.clutchRpmMaxOffset = -40f; e.rpmSensitivityMultiplier = 0.985f; e.rpmSensitivityDownMultiplier = 1.015f; });

            // Track: traction, lug height, and weight. Most changes require reload because sled physics are rebuilt.
            Add("track.trail", Track, "Trail Track", "Quick, light, and close to factory behavior.", true,
                e => { e.lugHeightMultiplier = 0.95f; e.frictionMultiplier = 0.95f; e.lugHeightOffset = -1f; e.weightOffset = -3f; });
            Add("track.mountain", Track, "Mountain Track", "More bite and lug for mixed climbs.", true,
                e => { e.lugHeightMultiplier = 1.08f; e.frictionMultiplier = 1.05f; e.lugHeightOffset = 4f; e.weightOffset = 5f; });
            Add("track.powder", Track, "Deep Powder Track", "Maximum flotation with a weight penalty.", true,
                e => { e.lugHeightMultiplier = 1.18f; e.frictionMultiplier = 1.08f; e.lugHeightOffset = 8f; e.weightOffset = 9f; e.centerOfMassDelta = new Vec3Data(0f, 0.01f, -0.02f); });
            Add("track.race", Track, "Race Track", "Lower drag and quicker rotation.", true,
                e => { e.lugHeightMultiplier = 0.86f; e.frictionMultiplier = 0.90f; e.lugHeightOffset = -2f; e.weightOffset = -6f; });
            Add("track.ice", Track, "Ice Studded Track", "Extra hard-surface bite.", true,
                e => { e.lugHeightMultiplier = 1.00f; e.frictionMultiplier = 1.18f; e.lugHeightOffset = 1f; e.weightOffset = 7f; });
            Add("track.long", Track, "Long Track Conversion", "Stable climbing with slower rotation.", true,
                e => { e.lugHeightMultiplier = 1.12f; e.frictionMultiplier = 1.10f; e.lugHeightOffset = 5f; e.weightOffset = 12f; e.centerOfMassDelta = new Vec3Data(0f, -0.01f, -0.06f); });

            AddPaddleTrack("track.paddle.2_25", 2.25f, "2.25\" Paddle Track", "Light trail paddle with direct lugHeight mapping.", 0.98f, -2f);
            AddPaddleTrack("track.paddle.2_50", 2.50f, "2.50\" Paddle Track", "Balanced paddle height with direct lugHeight mapping.", 1.00f, 0f);
            AddPaddleTrack("track.paddle.2_75", 2.75f, "2.75\" Paddle Track", "Mountain paddle with direct lugHeight mapping.", 1.04f, 3f);
            AddPaddleTrack("track.paddle.3_00", 3.00f, "3.00\" Paddle Track", "Deep-snow paddle with direct lugHeight mapping.", 1.07f, 6f);
            AddPaddleTrack("track.paddle.3_25", 3.25f, "3.25\" Paddle Track", "Tall paddle option with direct lugHeight mapping and extra rotating weight.", 1.09f, 9f);

            Add("limiter.stock", TrackLimiter, "Stock Limiter Strap", "Factory weight-transfer setup.", false,
                e => { });
            Add("limiter.loose", TrackLimiter, "Loose / Long Strap", "Playful weight-transfer setup that allows easier ski lift.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, 0f, -0.035f); e.wheelieThresholdOffset = -0.045f; e.trackSpeedGyroMultiplier = 0.965f; e.stabilizerDampingMultiplier = 0.985f; });
            Add("limiter.tight", TrackLimiter, "Tight / Short Strap", "Front-pressure setup that reduces lift and improves climbing control.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, 0f, 0.045f); e.wheelieThresholdOffset = 0.055f; e.trackSpeedGyroMultiplier = 1.035f; e.stabilizerDampingMultiplier = 1.035f; });
            Add("limiter.hillclimb", TrackLimiter, "Hillclimb Tight Strap", "Strongest anti-lift weight-transfer setup for controlled climbs.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.015f, 0.065f); e.wheelieThresholdOffset = 0.085f; e.trackSpeedGyroMultiplier = 1.065f; e.stabilizerDampingMultiplier = 1.055f; e.trackSpeedDampingMultiplier = 1.035f; });

            Add("shock.stock", RearShock, "Stock Shock", "Factory rear shock setup.", false,
                e => { });
            Add("shock.comfort", RearShock, "Comfort Shock", "Softer rear damping feel through available stabilizer fields.", false,
                e => { e.stabilizerDampingMultiplier = 0.94f; e.trackSpeedDampingMultiplier = 0.96f; e.trackSpeedGyroMultiplier = 0.985f; });
            Add("shock.mountain", RearShock, "Mountain Shock", "Controlled rear damping feel for uneven climbs.", false,
                e => { e.stabilizerDampingMultiplier = 1.05f; e.trackSpeedDampingMultiplier = 1.06f; e.trackSpeedGyroMultiplier = 1.02f; });
            Add("shock.race", RearShock, "Race Shock", "Firm, fast-response rear damping feel.", false,
                e => { e.stabilizerDampingMultiplier = 1.12f; e.trackSpeedDampingMultiplier = 1.10f; e.trackSpeedGyroMultiplier = 1.04f; });
            Add("shock.heavyduty", RearShock, "Heavy Duty Shock", "Extra controlled rear support feel for heavier setups.", false,
                e => { e.stabilizerDampingMultiplier = 1.16f; e.trackSpeedDampingMultiplier = 1.14f; e.trackSpeedGyroMultiplier = 1.055f; e.weightOffset = 1.5f; });

            Add("spring.light", RearSpring, "Light Rider Spring Setup", "Approximates lighter rear support through damping and transfer fields.", false,
                e => { e.stabilizerDampingMultiplier = 0.965f; e.trackSpeedDampingMultiplier = 0.965f; e.wheelieThresholdOffset = -0.025f; e.centerOfMassDelta = new Vec3Data(0f, 0f, -0.015f); });
            Add("spring.stock", RearSpring, "Stock Spring Setup", "Factory rear support feel.", false,
                e => { });
            Add("spring.mountain", RearSpring, "Mountain Spring Setup", "Approximates firmer rear support for climbing control.", false,
                e => { e.stabilizerDampingMultiplier = 1.045f; e.trackSpeedDampingMultiplier = 1.045f; e.wheelieThresholdOffset = 0.025f; e.centerOfMassDelta = new Vec3Data(0f, -0.005f, 0.015f); });
            Add("spring.utility", RearSpring, "Heavy Utility Spring Setup", "Approximates heavier rear support through available runtime fields.", false,
                e => { e.stabilizerDampingMultiplier = 1.09f; e.trackSpeedDampingMultiplier = 1.08f; e.wheelieThresholdOffset = 0.04f; e.centerOfMassDelta = new Vec3Data(0f, -0.01f, 0.025f); e.weightOffset = 1f; });
            Add("spring.race", RearSpring, "Race Spring Setup", "Approximates firm rear support with quicker chassis response.", false,
                e => { e.stabilizerDampingMultiplier = 1.11f; e.trackSpeedDampingMultiplier = 1.06f; e.trackSpeedGyroMultiplier = 1.04f; e.wheelieThresholdOffset = 0.03f; });

            // Suspension: handling feel through COM and stabilizer runtime fields.
            Add("suspension.stock", Suspension, "Stock Suspension", "Factory suspension behavior.", false,
                e => { });
            Add("suspension.lowcg", Suspension, "Low CG Kit", "Lower center of mass and calmer rollover behavior.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.10f, 0f); e.stabilizerDampingMultiplier = 1.08f; });
            Add("suspension.frontbite", Suspension, "Front Bite Setup", "More front authority for technical lines.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.03f, 0.08f); e.trackSpeedGyroMultiplier = 0.96f; });
            Add("suspension.freeride", Suspension, "Freeride Setup", "Rear bias for playful lift.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.08f); e.wheelieThresholdOffset = -0.05f; });
            Add("suspension.precision", Suspension, "Precision Kit", "Stable, responsive all-round handling.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.05f, 0.04f); e.trackSpeedDampingMultiplier = 1.06f; });

            // Chassis: global weight and center-of-mass personality.
            Add("chassis.stock", Chassis, "Stock Chassis", "Factory chassis.", true,
                e => { });
            Add("chassis.light", Chassis, "Lightweight Chassis", "Quicker handling with less planted feel.", true,
                e => { e.weightMultiplier = 0.94f; e.centerOfMassDelta = new Vec3Data(0f, 0.01f, 0.01f); });
            Add("chassis.reinforced", Chassis, "Reinforced Tunnel", "More stability with extra weight.", true,
                e => { e.weightMultiplier = 1.05f; e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.02f); e.stabilizerDampingMultiplier = 1.05f; });
            Add("chassis.lowcg", Chassis, "Low CG Chassis", "Predictable carve-focused setup.", true,
                e => { e.weightMultiplier = 1.01f; e.centerOfMassDelta = new Vec3Data(0f, -0.08f, 0f); });
            Add("chassis.rear", Chassis, "Rear Bias Chassis", "Playful lift and easier hop timing.", true,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.09f); e.wheelieThresholdOffset = -0.04f; });

            // Skis / stance: ski width and front-end grip personality.
            Add("skis.stock", Skis, "Stock Skis", "Factory ski stance.", true,
                e => { });
            Add("skis.narrow", Skis, "Narrow Technical Skis", "Quicker side-to-side motion.", true,
                e => { e.skiStanceOffset = -0.05f; e.skisXDistanceOffset = -0.04f; e.weightOffset = -1f; });
            Add("skis.wide", Skis, "Wide Mountain Skis", "More stable platform.", true,
                e => { e.skiStanceOffset = 0.06f; e.skisXDistanceOffset = 0.05f; e.weightOffset = 1f; });
            Add("skis.aggressive", Skis, "Aggressive Keel Skis", "Sharper bite with calmer high speed.", true,
                e => { e.skiStanceOffset = 0.03f; e.skisXDistanceOffset = 0.02f; e.frictionMultiplier = 1.03f; });

            Add("light.color.stock", HeadlightColor, "Stock Color", "Factory headlight color.", false,
                e => { });
            Add("light.color.warm", HeadlightColor, "Warm Halogen", "Warm halogen-style headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(1.00f, 0.82f, 0.55f, 1f); });
            Add("light.color.neutral", HeadlightColor, "Neutral White", "Neutral white headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(1.00f, 0.96f, 0.88f, 1f); });
            Add("light.color.cool", HeadlightColor, "Cool White", "Cool white headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(0.78f, 0.88f, 1.00f, 1f); });
            Add("light.color.amber", HeadlightColor, "Amber", "Amber headlight color for storm visibility.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(1.00f, 0.62f, 0.24f, 1f); });
            Add("light.color.blue", HeadlightColor, "Blue Tint", "Subtle blue-tint headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(0.55f, 0.70f, 1.00f, 1f); });

            Add("light.brightness.stock", HeadlightBrightness, "Stock Brightness", "Factory headlight intensity.", false,
                e => { });
            Add("light.brightness.bright", HeadlightBrightness, "Bright", "Moderately brighter runtime headlight intensity.", false,
                e => { e.headlightIntensityMultiplier = 1.25f; e.headlightRangeMultiplier = 1.08f; });
            Add("light.brightness.rally", HeadlightBrightness, "Rally", "High-output runtime headlight intensity.", false,
                e => { e.headlightIntensityMultiplier = 1.55f; e.headlightRangeMultiplier = 1.18f; });

            Add("light.beam.stock", HeadlightBeam, "Stock Beam", "Factory headlight beam.", false,
                e => { });
            Add("light.beam.spot", HeadlightBeam, "Narrow Spot", "Narrower runtime beam with longer reach.", false,
                e => { e.headlightSpotAngleMultiplier = 1.0f; e.headlightRangeMultiplier = 100.0f; });
            Add("light.beam.flood", HeadlightBeam, "Wide Flood", "Wider runtime beam with broader near-field coverage.", false,
                e => { e.headlightSpotAngleMultiplier = 1.22f; e.headlightRangeMultiplier = 1.0f; });

            Add("light.aim.stock", HeadlightAim, "Stock Aim", "Factory vertical headlight alignment.", false,
                e => { });
            Add("light.aim.low", HeadlightAim, "Aim Down", "Small downward runtime headlight pitch.", false,
                e => { e.headlightPitchOffsetDegrees = 3f; });
            Add("light.aim.high", HeadlightAim, "Aim Up", "Small upward runtime headlight pitch.", false,
                e => { e.headlightPitchOffsetDegrees = -3f; });

            // Native accessories: toggles existing in-game accessory objects only; no custom meshes are spawned here.
            Add("accessory.stock", Accessories, "Factory Accessories", "Keep current native accessory state.", false,
                e => { e.accessoryMode = "stock"; });
            Add("accessory.race_trim", Accessories, "Clean Race Trim", "Hide exposed removable trim where the native model allows it.", true,
                e => { e.accessoryMode = "race_trim"; e.weightOffset = -4f; });
            Add("accessory.utility", Accessories, "Utility Kit", "Show native windshield, flap, and rear accessory groups where present.", true,
                e => { e.accessoryMode = "utility"; e.weightOffset = 4f; });
        }

        private void Add(string id, string category, string name, string description, bool requiresReload, Action<PartEffect> configure)
        {
            var part = new TunePart
            {
                id = id,
                category = category,
                name = name,
                description = description,
                requiresReload = requiresReload,
                effect = new PartEffect()
            };

            configure(part.effect);
            _parts.Add(part);
            _byId[id] = part;
        }

        private void AddPaddleTrack(
            string id,
            float paddleHeightInches,
            string name,
            string description,
            float frictionMultiplier,
            float weightOffset)
        {
            float paddleHeightMillimeters = UnitConversion.InchesToMillimeters(paddleHeightInches);
            Add(id, Track, name, $"{description} ({UnitConversion.FormatInchesAndMillimeters(paddleHeightMillimeters)})", true,
                e =>
                {
                    e.lugHeightTargetMm = paddleHeightMillimeters;
                    e.frictionMultiplier = frictionMultiplier;
                    e.weightOffset = weightOffset;
                });
        }
    }
}
