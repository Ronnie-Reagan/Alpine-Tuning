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
        public const string BrakeCalibration = "brakeCalibration";
        public const string Track = "track";
        public const string TrackLimiter = "trackLimiter";
        public const string RearShock = "rearShock";
        public const string RearSpring = "rearSpring";
        public const string Suspension = "suspension";
        public const string Chassis = "chassis";
        public const string Skis = "skis";
        public const string SteeringGeometry = "steeringGeometry";
        public const string HeadlightColor = "headlightColor";
        public const string HeadlightBrightness = "headlightBrightness";
        public const string HeadlightBeam = "headlightBeam";
        public const string HeadlightAim = "headlightAim";
        public const string Accessories = "accessories";
        public const string FuelTank = "fuelTank";
        public const string BackpackFuel = "backpackFuel";

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
            BrakeCalibration,
            Track,
            TrackLimiter,
            RearShock,
            RearSpring,
            Suspension,
            Chassis,
            Skis,
            SteeringGeometry,
            HeadlightColor,
            HeadlightBrightness,
            HeadlightBeam,
            HeadlightAim,
            Accessories,
            FuelTank,
            BackpackFuel
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
                    return "Intake";
                case Clutch:
                    return "Clutch Calibration";
                case ClutchWeights:
                    return "Clutch Weights";
                case RatioFeel:
                    return "Ratio Feel";
                case BrakeCalibration:
                    return "Brake Calibration";
                case Track:
                    return "Track";
                case TrackLimiter:
                    return "Weight Transfer Setup";
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
                case SteeringGeometry:
                    return "Steering Geometry";
                case HeadlightColor:
                    return "Headlight Color";
                case HeadlightBrightness:
                    return "Headlight Brightness";
                case HeadlightBeam:
                    return "Headlight Beam";
                case HeadlightAim:
                    return "Headlight Alignment";
                case Accessories:
                    return "Accessories";
                case FuelTank:
                    return "Fuel Tank";
                case BackpackFuel:
                    return "Backpack Fuel";
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
                case BrakeCalibration: return "brake.stock";
                case Track: return "track.stock";
                case TrackLimiter: return "limiter.stock";
                case RearShock: return "shock.stock";
                case RearSpring: return "spring.stock";
                case Suspension: return "suspension.stock";
                case Chassis: return "chassis.stock";
                case Skis: return "skis.stock";
                case SteeringGeometry: return "geometry.stock";
                case HeadlightColor: return "light.color.stock";
                case HeadlightBrightness: return "light.brightness.stock";
                case HeadlightBeam: return "light.beam.stock";
                case HeadlightAim: return "light.aim.stock";
                case Accessories: return "accessory.stock";
                case FuelTank: return "fuel.tank.stock";
                case BackpackFuel: return "fuel.backpack.none";
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
                usesAutomaticName = true,
                // Never persist a platform display name into setup JSON.
                author = AlpineConstants.DefaultProfileAuthor,
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
            profile.usesAutomaticName = false;
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

            var sourceSelections = profile.selectedParts ?? new List<PartSelection>();
            var normalizedSelections = new List<PartSelection>(OrderedCategories.Length);
            foreach (string category in OrderedCategories)
            {
                string partId = sourceSelections
                    .Where(selection => selection != null &&
                                        string.Equals(
                                            selection.category,
                                            category,
                                            StringComparison.OrdinalIgnoreCase))
                    .Select(selection => Find(selection.partId))
                    .Where(part => part != null &&
                                   string.Equals(
                                       part.category,
                                       category,
                                       StringComparison.OrdinalIgnoreCase))
                    .Select(part => part.id)
                    .FirstOrDefault();

                normalizedSelections.Add(new PartSelection
                {
                    category = category,
                    partId = partId ?? DefaultPartId(category)
                });
            }

            profile.selectedParts = normalizedSelections;
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
                e => { e.horsePowerMultiplier = 1.055f; });
            Add("engine.stage2", EngineCore, "Stage 2 Kit", "Stronger top-end calibration with a controlled output gain.", true,
                e => { e.horsePowerMultiplier = 1.10f; e.weightOffset = 2f; });
            Add("engine.bigbore", EngineCore, "Big Bore Build", "More displacement without full race volatility.", true,
                e => { e.horsePowerMultiplier = 1.17f; e.weightOffset = 5f; });
            Add("engine.race", EngineCore, "Race Engine", "High-output naturally aspirated package with sharper response.", true,
                e => { e.horsePowerMultiplier = 1.24f; e.weightOffset = 6f; e.throttleExponentDelta = -0.04f; });

            Add("piston.stock", EnginePiston, "Stock Pistons", "Factory rotating assembly behavior.", false,
                e => { });
            Add("piston.lightweight", EnginePiston, "Lightweight Pistons", "Small rotating-mass reduction for quicker response.", false,
                e => { e.throttleExponentDelta = -0.025f; e.rpmSensitivityMultiplier = 1.025f; e.weightOffset = -1f; });
            Add("piston.highcompression", EnginePiston, "High Compression Pistons", "Mild compression-oriented power transfer with sharper response.", false,
                e => { e.horsePowerMultiplier = 1.025f; e.throttleExponentDelta = -0.015f; e.weightOffset = 0.5f; });
            Add("piston.forgedturbo", EnginePiston, "Forged Turbo Pistons", "Stronger turbo-safe piston setup with stable RPM response.", false,
                e => { e.horsePowerMultiplier = 1.015f; e.rpmSensitivityDownMultiplier = 0.985f; e.weightOffset = 1.5f; });
            Add("piston.race", EnginePiston, "Race Pistons", "Response-focused race piston setup with a modest power transfer gain.", false,
                e => { e.horsePowerMultiplier = 1.035f; e.throttleExponentDelta = -0.035f; e.rpmSensitivityMultiplier = 1.035f; e.weightOffset = -0.5f; });

            Add("crank.stock", EngineCrank, "Stock Crank", "Factory crank behavior.", false,
                e => { });
            Add("crank.balanced", EngineCrank, "Balanced Crank", "Smoother RPM response without changing the power ceiling much.", false,
                e => { e.rpmSensitivityMultiplier = 1.018f; e.rpmSensitivityDownMultiplier = 0.99f; });
            Add("crank.lightened", EngineCrank, "Lightened Crank", "Quicker rev response with a small weight reduction.", false,
                e => { e.throttleExponentDelta = -0.025f; e.rpmSensitivityMultiplier = 1.035f; e.weightOffset = -1.5f; });
            Add("crank.heavyduty", EngineCrank, "Heavy Duty Crank", "More stable loaded RPM behavior with a slight weight penalty.", false,
                e => { e.rpmSensitivityDownMultiplier = 0.975f; e.weightOffset = 2f; });
            Add("crank.race", EngineCrank, "Race Crank", "Sharp race response with controlled RPM sensitivity.", false,
                e => { e.horsePowerMultiplier = 1.018f; e.throttleExponentDelta = -0.035f; e.rpmSensitivityMultiplier = 1.045f; e.weightOffset = -1f; });

            // Turbo / induction: configured output plus verified controller RPM response.
            Add("turbo.none", Turbo, "Stock Induction", "Preserves the selected sled's factory induction system without an Alpine boost kit.", false,
                e => { });
            Add("turbo.trail", Turbo, "Trail Turbo", "Moderate output with quicker RPM response.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.10f;
                    e.weightOffset = 4f;
                    e.isTurbo = true;
                    e.turboRpmResponseMultiplier = 1.02f;
                    e.engineText = "Trail Turbo";
                    e.rpmSensitivityMultiplier = 1.03f;
                });
            Add("turbo.mountain", Turbo, "Mountain Turbo", "Stronger output with controlled RPM response.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.15f;
                    e.weightOffset = 7f;
                    e.isTurbo = true;
                    e.turboRpmResponseMultiplier = 1.04f;
                    e.engineText = "Mountain Turbo";
                    e.rpmSensitivityMultiplier = 1.05f;
                });
            Add("turbo.bigboost", Turbo, "Big Boost Kit", "High-output induction package with sharper RPM response and extra weight.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.25f;
                    e.weightOffset = 11f;
                    e.isTurbo = true;
                    e.turboRpmResponseMultiplier = 1.07f;
                    e.engineText = "Big Boost Turbo";
                    e.rpmSensitivityMultiplier = 1.07f;
                });

            // Intake: small response and weight changes that should not dominate the build.
            Add("intake.stock", Intake, "Stock Intake", "Factory airbox, runners, and reed intake.", false,
                e => { });
            Add("intake.flow", Intake, "High Flow Intake", "Filtered high-flow runners for a small response and horsepower gain.", false,
                e => { e.horsePowerMultiplier = 1.03f; e.weightOffset = -1f; });
            Add("intake.pipe", Intake, "Race Velocity Intake", "Short velocity stacks and reed runners for sharper response and lower weight.", false,
                e => { e.horsePowerMultiplier = 1.05f; e.weightOffset = -2f; e.throttleExponentDelta = -0.03f; });

            // Clutch / gearing: runtime controller feel. Tune RPM offsets and throttleExponentDelta here.
            Add("clutch.stock", Clutch, "Stock Calibration", "Factory clutch calibration.", false,
                e => { });
            Add("clutch.trail", Clutch, "TrapLine Smooth", "Smoother engagement and forgiving backshift.", false,
                e => { e.clutchRpmMinOffset = -140f; e.clutchRpmMaxOffset = 110f; e.minThrottleOnClutchEngagementOffset = -0.02f; e.rpmSensitivityMultiplier = 1.04f; e.rpmSensitivityDownMultiplier = 1.03f; });
            Add("clutch.mountain", Clutch, "ClimberClub Mountain", "Holds RPM under load for better climb response.", false,
                e => { e.clutchRpmMinOffset = 180f; e.clutchRpmMaxOffset = 320f; e.minThrottleOnClutchEngagementOffset = 0.015f; e.rpmSensitivityMultiplier = 1.13f; e.rpmSensitivityDownMultiplier = 0.93f; });
            Add("clutch.race", Clutch, "IceRacer Aggressive", "Quick engagement and sharp throttle response.", false,
                e => { e.clutchRpmMinOffset = 320f; e.clutchRpmMaxOffset = 460f; e.minThrottleOnClutchEngagementOffset = 0.035f; e.rpmSensitivityMultiplier = 1.20f; e.rpmSensitivityDownMultiplier = 0.90f; e.throttleExponentDelta = -0.08f; });

            Add("weights.stock", ClutchWeights, "Stock Weights", "Factory clutch weight feel.", false,
                e => { });
            Add("weights.light", ClutchWeights, "Light Weights", "Lighter weight feel with quicker engagement response.", false,
                e => { e.clutchRpmMinOffset = 150f; e.clutchRpmMaxOffset = 120f; e.rpmSensitivityMultiplier = 1.055f; e.throttleExponentDelta = -0.02f; });
            Add("weights.heavy", ClutchWeights, "Heavy Weights", "Heavier weight feel with calmer shift response.", false,
                e => { e.clutchRpmMinOffset = -140f; e.clutchRpmMaxOffset = -90f; e.rpmSensitivityMultiplier = 0.955f; e.rpmSensitivityDownMultiplier = 1.045f; });
            Add("weights.mountain", ClutchWeights, "Adjustable Mountain Weights", "Mountain weight feel that keeps RPM responsive under load.", false,
                e => { e.clutchRpmMinOffset = 95f; e.clutchRpmMaxOffset = 230f; e.rpmSensitivityMultiplier = 1.085f; e.rpmSensitivityDownMultiplier = 0.955f; });
            Add("weights.race", ClutchWeights, "Race Weights", "Aggressive weight feel for fast RPM changes.", false,
                e => { e.clutchRpmMinOffset = 240f; e.clutchRpmMaxOffset = 300f; e.rpmSensitivityMultiplier = 1.12f; e.throttleExponentDelta = -0.035f; });

            Add("ratio.stock", RatioFeel, "Stock Ratio Feel", "Factory drive response feel.", false,
                e => { });
            Add("ratio.low", RatioFeel, "Low Ratio Feel", "Shorter drivetrain thresholds with stronger low-speed drive response.", false,
                e => { e.clutchRpmMinOffset = 60f; e.clutchRpmMaxOffset = 80f; e.rpmSensitivityMultiplier = 1.025f; e.nativePowerEfficiencyMultiplier = 1.025f; e.nativeDrivetrainSpeedMultiplier = 0.92f; });
            Add("ratio.highspeed", RatioFeel, "High Speed Ratio Feel", "Taller drivetrain thresholds with calmer low-speed response.", false,
                e => { e.clutchRpmMinOffset = -60f; e.clutchRpmMaxOffset = -40f; e.rpmSensitivityMultiplier = 0.985f; e.rpmSensitivityDownMultiplier = 1.015f; e.nativePowerEfficiencyMultiplier = 0.99f; e.nativeDrivetrainSpeedMultiplier = 1.08f; });

            Add("brake.stock", BrakeCalibration, "Factory Brake", "Brake-force calibration.", false,
                e => { });
            Add("brake.progressive", BrakeCalibration, "Progressive Brake", "Softer brake-force response for smoother modulation.", false,
                e => { e.nativeBrakeForceMultiplier = 0.90f; });
            Add("brake.trail", BrakeCalibration, "Trail Brake", "Moderately stronger brake-force calibration.", false,
                e => { e.nativeBrakeForceMultiplier = 1.08f; });
            Add("brake.aggressive", BrakeCalibration, "Aggressive Brake", "Strong brake-force calibration..", false,
                e => { e.nativeBrakeForceMultiplier = 1.18f; });

            // Track: traction, lug height, and weight. Most changes require reload because sled physics are rebuilt.
            Add("track.stock", Track, "Stock Track", "Factory track setup.", false,
                e => { });
            Add("track.trail", Track, "Trail Track", "Quick, light, and close to factory behavior.", true,
                e => { e.lugHeightMultiplier = 0.95f; e.frictionMultiplier = 0.95f; e.lugHeightOffset = -1f; e.weightOffset = -3f; e.nativeTrackMassMultiplier = 0.96f; });
            Add("track.mountain", Track, "Mountain Track", "More bite and lug for mixed climbs.", true,
                e => { e.lugHeightMultiplier = 1.08f; e.frictionMultiplier = 1.05f; e.lugHeightOffset = 4f; e.weightOffset = 5f; e.nativeTrackMassMultiplier = 1.04f; });
            Add("track.powder", Track, "Deep Powder Track", "Tall-lug deep-snow bite with added rotating weight.", true,
                e => { e.lugHeightMultiplier = 1.18f; e.frictionMultiplier = 1.08f; e.lugHeightOffset = 8f; e.weightOffset = 9f; e.centerOfMassDelta = new Vec3Data(0f, 0.01f, -0.02f); e.nativeTrackMassMultiplier = 1.08f; });
            Add("track.race", Track, "Race Track", "Lower-lug, lighter rotating setup with reduced snow bite.", true,
                e => { e.lugHeightMultiplier = 0.86f; e.frictionMultiplier = 0.90f; e.lugHeightOffset = -2f; e.weightOffset = -6f; e.nativeTrackMassMultiplier = 0.92f; });
            Add("track.ice", Track, "Ice Studded Track", "Eighteen percent more per-track hard-surface contact grip with added mass.", true,
                e => { e.lugHeightMultiplier = 1.00f; e.lugHeightOffset = 1f; e.weightOffset = 7f; e.nativeTrackMassMultiplier = 1.06f; e.nativeTrackGripMultiplier = 1.18f; });
            Add("track.long", Track, "Climbing Track Kit", "Tall-lug, higher-bite climbing setup with extra rotating mass.", true,
                e => { e.lugHeightMultiplier = 1.12f; e.frictionMultiplier = 1.10f; e.lugHeightOffset = 5f; e.weightOffset = 12f; e.centerOfMassDelta = new Vec3Data(0f, -0.01f, -0.06f); e.nativeTrackMassMultiplier = 1.12f; });

            AddPaddleTrack("track.paddle.2_25", 2.25f, "2.25\" Paddle Track", "Light trail paddle with direct lugHeight mapping.", 0.98f, -2f);
            AddPaddleTrack("track.paddle.2_50", 2.50f, "2.50\" Paddle Track", "Balanced paddle height with direct lugHeight mapping.", 1.00f, 0f);
            AddPaddleTrack("track.paddle.2_75", 2.75f, "2.75\" Paddle Track", "Mountain paddle with direct lugHeight mapping.", 1.04f, 3f);
            AddPaddleTrack("track.paddle.3_00", 3.00f, "3.00\" Paddle Track", "Deep-snow paddle with direct lugHeight mapping.", 1.07f, 6f);
            AddPaddleTrack("track.paddle.3_25", 3.25f, "3.25\" Paddle Track", "Tall paddle option with direct lugHeight mapping and extra rotating weight.", 1.09f, 9f);

            Add("limiter.stock", TrackLimiter, "Factory Transfer", "Factory weight-transfer setup.", false,
                e => { });
            Add("limiter.loose", TrackLimiter, "Playful Transfer", "Rearward COM and softer captured stabilizer/track response for easier ski lift.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, 0.005f, -0.055f); e.trackSpeedGyroMultiplier = 0.94f; e.stabilizerDampingMultiplier = 0.965f; e.nativeTrackRigidityFrontMultiplier = 0.90f; e.nativeTrackRigidityRearMultiplier = 0.96f; });
            Add("limiter.tight", TrackLimiter, "Controlled Transfer", "Forward COM and firmer captured stabilizer/track response to reduce ski lift.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.005f, 0.065f); e.trackSpeedGyroMultiplier = 1.055f; e.stabilizerDampingMultiplier = 1.055f; e.nativeTrackRigidityFrontMultiplier = 1.10f; e.nativeTrackRigidityRearMultiplier = 1.03f; });
            Add("limiter.hillclimb", TrackLimiter, "Hillclimb Transfer", "Strongest forward-COM and captured stabilizer/track anti-lift preset.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.025f, 0.090f); e.trackSpeedGyroMultiplier = 1.095f; e.stabilizerDampingMultiplier = 1.085f; e.trackSpeedDampingMultiplier = 1.065f; e.nativeTrackRigidityFrontMultiplier = 1.18f; e.nativeTrackRigidityRearMultiplier = 1.06f; });

            Add("shock.stock", RearShock, "Stock Shock", "Factory rear shock setup.", false,
                e => { });
            Add("shock.comfort", RearShock, "Comfort Shock", "Softer rear compression and rebound damping.", false,
                e => { e.stabilizerDampingMultiplier = 0.90f; e.trackSpeedDampingMultiplier = 0.92f; e.trackSpeedGyroMultiplier = 0.970f; e.nativeRearDamperMultiplier = 0.96f; e.nativeRearCompressionDampingMultiplier = 0.94f; e.nativeRearReboundDampingMultiplier = 0.95f; });
            Add("shock.mountain", RearShock, "Mountain Shock", "Controlled rear damping for uneven climbs.", false,
                e => { e.stabilizerDampingMultiplier = 1.085f; e.trackSpeedDampingMultiplier = 1.095f; e.trackSpeedGyroMultiplier = 1.035f; e.nativeRearDamperMultiplier = 1.035f; e.nativeRearCompressionDampingMultiplier = 1.05f; e.nativeRearReboundDampingMultiplier = 1.045f; });
            Add("shock.race", RearShock, "Race Shock", "Firm compression and rebound damping with fast response.", false,
                e => { e.stabilizerDampingMultiplier = 1.17f; e.trackSpeedDampingMultiplier = 1.15f; e.trackSpeedGyroMultiplier = 1.065f; e.nativeRearDamperMultiplier = 1.06f; e.nativeRearCompressionDampingMultiplier = 1.11f; e.nativeRearReboundDampingMultiplier = 1.09f; });
            Add("shock.heavyduty", RearShock, "Heavy Duty Shock", "Maximum controlled rear damping for heavier setups.", false,
                e => { e.stabilizerDampingMultiplier = 1.20f; e.trackSpeedDampingMultiplier = 1.18f; e.trackSpeedGyroMultiplier = 1.075f; e.weightOffset = 1.5f; e.nativeRearDamperMultiplier = 1.075f; e.nativeRearCompressionDampingMultiplier = 1.13f; e.nativeRearReboundDampingMultiplier = 1.11f; });

            Add("spring.light", RearSpring, "Light Rider Spring Setup", "Softer rear spring support for lighter riders.", false,
                e => { e.stabilizerDampingMultiplier = 0.965f; e.trackSpeedDampingMultiplier = 0.965f; e.centerOfMassDelta = new Vec3Data(0f, 0f, -0.015f); e.nativeRearSpringMultiplier = 0.90f; e.nativeTrackRigidityRearMultiplier = 0.96f; });
            Add("spring.stock", RearSpring, "Stock Spring Setup", "Factory rear support feel.", false,
                e => { });
            Add("spring.mountain", RearSpring, "Mountain Spring Setup", "Firmer rear spring support for climbing control.", false,
                e => { e.stabilizerDampingMultiplier = 1.045f; e.trackSpeedDampingMultiplier = 1.045f; e.centerOfMassDelta = new Vec3Data(0f, -0.005f, 0.015f); e.nativeRearSpringMultiplier = 1.08f; e.nativeTrackRigidityRearMultiplier = 1.04f; });
            Add("spring.utility", RearSpring, "Heavy Utility Spring Setup", "Heavy rear spring support for loaded setups.", false,
                e => { e.stabilizerDampingMultiplier = 1.09f; e.trackSpeedDampingMultiplier = 1.08f; e.centerOfMassDelta = new Vec3Data(0f, -0.01f, 0.025f); e.weightOffset = 1f; e.nativeRearSpringMultiplier = 1.16f; e.nativeTrackRigidityRearMultiplier = 1.08f; });
            Add("spring.race", RearSpring, "Race Spring Setup", "Firm rear spring support with quick chassis response.", false,
                e => { e.stabilizerDampingMultiplier = 1.11f; e.trackSpeedDampingMultiplier = 1.06f; e.trackSpeedGyroMultiplier = 1.04f; e.nativeRearSpringMultiplier = 1.13f; e.nativeTrackRigidityRearMultiplier = 1.06f; });

            // Suspension: shock, anti-roll and track-rigidity values plus conservative legacy fallbacks.
            Add("suspension.stock", Suspension, "Stock Suspension", "Factory suspension behavior.", false,
                e => { });
            Add("suspension.lowcg", Suspension, "Low CG Kit", "Lower center of mass and calmer rollover behavior.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.12f, 0f); e.stabilizerDampingMultiplier = 1.13f; e.trackSpeedDampingMultiplier = 1.06f; e.nativeAntiRollBarMultiplier = 1.15f; e.nativeTrackRigidityFrontMultiplier = 1.06f; e.nativeTrackRigidityRearMultiplier = 1.06f; e.nativeFrontSpringMultiplier = 1.04f; e.nativeRearSpringMultiplier = 1.04f; e.nativeFrontDamperMultiplier = 1.03f; e.nativeRearDamperMultiplier = 1.03f; });
            Add("suspension.frontbite", Suspension, "Front Bite Setup", "More front authority for technical lines.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.04f, 0.11f); e.trackSpeedGyroMultiplier = 0.93f; e.stabilizerDampingMultiplier = 1.04f; e.nativeAntiRollBarMultiplier = 1.04f; e.nativeTrackRigidityFrontMultiplier = 1.11f; e.nativeTrackRigidityRearMultiplier = 0.98f; e.nativeFrontSpringMultiplier = 1.09f; e.nativeFrontDamperMultiplier = 1.07f; e.nativeFrontCompressionDampingMultiplier = 1.03f; e.nativeFrontReboundDampingMultiplier = 1.03f; e.nativeRearSpringMultiplier = 0.98f; });
            Add("suspension.freeride", Suspension, "Freeride Setup", "Rear bias for playful lift.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.11f); e.trackSpeedGyroMultiplier = 0.96f; e.nativeAntiRollBarMultiplier = 0.90f; e.nativeTrackRigidityFrontMultiplier = 0.91f; e.nativeTrackRigidityRearMultiplier = 0.95f; e.nativeFrontSpringMultiplier = 0.96f; e.nativeFrontDamperMultiplier = 0.96f; e.nativeRearSpringMultiplier = 0.94f; });
            Add("suspension.precision", Suspension, "Precision Kit", "Stable, responsive all-round handling.", false,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.065f, 0.055f); e.trackSpeedDampingMultiplier = 1.10f; e.stabilizerDampingMultiplier = 1.08f; e.nativeAntiRollBarMultiplier = 1.10f; e.nativeTrackRigidityFrontMultiplier = 1.07f; e.nativeTrackRigidityRearMultiplier = 1.05f; e.nativeFrontSpringMultiplier = 1.03f; e.nativeRearSpringMultiplier = 1.03f; e.nativeFrontDamperMultiplier = 1.05f; e.nativeRearDamperMultiplier = 1.05f; e.nativeFrontCompressionDampingMultiplier = 1.03f; e.nativeFrontReboundDampingMultiplier = 1.03f; e.nativeRearCompressionDampingMultiplier = 1.03f; e.nativeRearReboundDampingMultiplier = 1.03f; });

            // Chassis: global weight and center-of-mass personality.
            Add("chassis.stock", Chassis, "Stock Chassis", "Factory chassis.", false,
                e => { });
            Add("chassis.light", Chassis, "Lightweight Chassis", "Quicker handling with less planted feel.", true,
                e => { e.weightMultiplier = 0.94f; e.centerOfMassDelta = new Vec3Data(0f, 0.01f, 0.01f); });
            Add("chassis.reinforced", Chassis, "Reinforced Tunnel", "More stability with extra weight.", true,
                e => { e.weightMultiplier = 1.05f; e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.02f); e.stabilizerDampingMultiplier = 1.05f; });
            Add("chassis.lowcg", Chassis, "Low CG Chassis", "Predictable carve-focused setup.", true,
                e => { e.weightMultiplier = 1.01f; e.centerOfMassDelta = new Vec3Data(0f, -0.08f, 0f); });
            Add("chassis.rear", Chassis, "Rear Bias Chassis", "Playful lift and easier hop timing.", true,
                e => { e.centerOfMassDelta = new Vec3Data(0f, -0.02f, -0.09f); });

            // Skis / stance: ski width and front-end grip personality.
            Add("skis.stock", Skis, "Stock Skis", "Factory ski stance.", false,
                e => { });
            Add("skis.narrow", Skis, "Narrow Technical Skis", "Quicker side-to-side motion.", true,
                e => { e.skiStanceOffset = -0.05f; e.skisXDistanceOffset = -0.04f; e.weightOffset = -1f; });
            Add("skis.wide", Skis, "Wide Mountain Skis", "More stable platform.", true,
                e => { e.skiStanceOffset = 0.06f; e.skisXDistanceOffset = 0.05f; e.weightOffset = 1f; });
            Add("skis.aggressive", Skis, "Aggressive Keel Skis", "Wider stance with ten percent more per-ski hard-surface contact grip.", true,
                e => { e.skiStanceOffset = 0.03f; e.skisXDistanceOffset = 0.02f; e.nativeSkiGripMultiplier = 1.10f; });

            Add("geometry.stock", SteeringGeometry, "Factory Geometry", "Factory steering angle, toe, and camber response.", false,
                e => { });
            Add("geometry.reduced_toe", SteeringGeometry, "Reduced Toe", "Reduces the toe angle while preserving its direction.", false,
                e => { e.nativeToeAngleMultiplier = 0.75f; });
            Add("geometry.increased_toe", SteeringGeometry, "Increased Toe", "Increases the toe angle while preserving its direction.", false,
                e => { e.nativeToeAngleMultiplier = 1.25f; });
            Add("geometry.responsive", SteeringGeometry, "Responsive Geometry", "A conservative increase in steering angle and camber response with reduced toe.", false,
                e => { e.nativeSkisMaxAngleMultiplier = 1.08f; e.nativeToeAngleMultiplier = 0.90f; e.nativeCamberFactorMultiplier = 1.12f; });

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
            Add("light.color.gold", HeadlightColor, "Golden Fog", "Deep gold headlight color for flat light and storms.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(1.00f, 0.72f, 0.32f, 1f); });
            Add("light.color.blue", HeadlightColor, "Blue Tint", "Subtle blue-tint headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(0.55f, 0.70f, 1.00f, 1f); });
            Add("light.color.ice", HeadlightColor, "Ice Blue", "Crisp ice-blue headlight color.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(0.50f, 0.88f, 1.00f, 1f); });
            Add("light.color.green", HeadlightColor, "Trail Green", "Green-tinted headlight color for a custom trail look.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(0.55f, 1.00f, 0.62f, 1f); });
            Add("light.color.red", HeadlightColor, "Red Lens", "Red-tinted headlight color for night visibility experiments.", false,
                e => { e.hasHeadlightColor = true; e.headlightColor = new Color(1.00f, 0.30f, 0.26f, 1f); });

            Add("light.brightness.stock", HeadlightBrightness, "Stock Brightness", "Factory headlight intensity.", false,
                e => { });
            Add("light.brightness.low", HeadlightBrightness, "Low Output", "Reduced runtime headlight intensity.", false,
                e => { e.headlightIntensityMultiplier = 0.70f; e.headlightRangeMultiplier = 0.88f; });
            Add("light.brightness.bright", HeadlightBrightness, "Bright", "Moderately brighter runtime headlight intensity.", false,
                e => { e.headlightIntensityMultiplier = 1.25f; e.headlightRangeMultiplier = 1.08f; });
            Add("light.brightness.rally", HeadlightBrightness, "Rally", "High-output runtime headlight intensity.", false,
                e => { e.headlightIntensityMultiplier = 1.55f; e.headlightRangeMultiplier = 1.18f; });
            Add("light.brightness.baja", HeadlightBrightness, "Baja", "Maximum runtime headlight intensity within Alpine's safety clamp.", false,
                e => { e.headlightIntensityMultiplier = 1.90f; e.headlightRangeMultiplier = 1.35f; });

            Add("light.beam.stock", HeadlightBeam, "Stock Beam", "Factory headlight beam.", false,
                e => { });
            Add("light.beam.spot", HeadlightBeam, "Narrow Spot", "Narrower runtime beam with longer reach.", false,
                e => { e.headlightSpotAngleMultiplier = 0.72f; e.headlightRangeMultiplier = 1.18f; });
            Add("light.beam.longrange", HeadlightBeam, "Long Range Pencil", "Tight long-range runtime beam.", false,
                e => { e.headlightSpotAngleMultiplier = 0.55f; e.headlightRangeMultiplier = 1.35f; });
            Add("light.beam.driving", HeadlightBeam, "Driving Beam", "Slightly focused runtime beam with extra reach.", false,
                e => { e.headlightSpotAngleMultiplier = 0.88f; e.headlightRangeMultiplier = 1.15f; });
            Add("light.beam.flood", HeadlightBeam, "Wide Flood", "Wider runtime beam with broader near-field coverage.", false,
                e => { e.headlightSpotAngleMultiplier = 1.22f; e.headlightRangeMultiplier = 1.0f; });
            Add("light.beam.fog", HeadlightBeam, "Low Fog Flood", "Very wide shorter runtime beam for near-field visibility.", false,
                e => { e.headlightSpotAngleMultiplier = 1.50f; e.headlightRangeMultiplier = 0.92f; e.headlightPitchOffsetDegrees = 2f; });
            Add("light.beam.combo", HeadlightBeam, "Combo Beam", "Mixed width and reach for general night riding.", false,
                e => { e.headlightSpotAngleMultiplier = 1.08f; e.headlightRangeMultiplier = 1.12f; });

            Add("light.aim.stock", HeadlightAim, "Stock Aim", "Factory vertical headlight alignment.", false,
                e => { });
            Add("light.aim.low", HeadlightAim, "Aim Down", "Small downward runtime headlight pitch.", false,
                e => { e.headlightPitchOffsetDegrees = 3f; });
            Add("light.aim.high", HeadlightAim, "Aim Up", "Small upward runtime headlight pitch.", false,
                e => { e.headlightPitchOffsetDegrees = -3f; });

            // Fuel system. Tank shell-mass offsets are conservative HDPE/aluminium
            // assembly estimates; gasoline payload itself is kept as actual liters
            // so capacity changes do not magically create mass.
            Add("fuel.tank.reduced", FuelTank, "Reduced Tank", "75% of the factory capacity. Retains existing liters; excess is discarded only after confirmation.", true,
                e => { e.fuelCapacityMultiplier = 0.75f; e.tankHardwareMassOffsetKg = -0.8f; });
            Add("fuel.tank.stock", FuelTank, "Stock Tank", "Factory fuel capacity and tank hardware mass.", true,
                e => { });
            Add("fuel.tank.increased", FuelTank, "Increased Tank", "125% of the factory capacity with a modest larger-tank mass penalty.", true,
                e => { e.fuelCapacityMultiplier = 1.25f; e.tankHardwareMassOffsetKg = 0.9f; });
            Add("fuel.tank.expedition", FuelTank, "Expedition Tank", "150% of the factory capacity for long-distance rides.", true,
                e => { e.fuelCapacityMultiplier = 1.50f; e.tankHardwareMassOffsetKg = 1.7f; });

            Add("fuel.backpack.none", BackpackFuel, "No Backpack Fuel", "Carry no reserve fuel in the rider backpack.", false,
                e => { });
            Add("fuel.backpack.bottles", BackpackFuel, "Water Bottles", "1 L reserve.", true,
                e => { e.backpackFuelCapacityLiters = 1f; e.backpackContainerMassKg = 0.15f; e.requiresCosmeticBackpack = true; });
            Add("fuel.backpack.jug", BackpackFuel, "Juice Jug", "4 L reserve.", true,
                e => { e.backpackFuelCapacityLiters = 4f; e.backpackContainerMassKg = 0.25f; e.requiresCosmeticBackpack = true; });
            Add("fuel.backpack.tinycan", BackpackFuel, "Tiny Gas Can", "6 L reserve.", true,
                e => { e.backpackFuelCapacityLiters = 6f; e.backpackContainerMassKg = 0.65f; e.requiresCosmeticBackpack = true; });
            Add("fuel.backpack.fillbag", BackpackFuel, "Just Fill the Bag", "22 L reserve. Ridiculous, heavy.", true,
                e => { e.backpackFuelCapacityLiters = 22f; e.backpackContainerMassKg = 1.10f; e.requiresCosmeticBackpack = true; });

            // accessories: toggles existing in-game accessory objects only; no custom meshes are spawned here.
            Add("accessory.stock", Accessories, "Factory Accessories", "Keep current accessory state.", false,
                e => { e.accessoryMode = "stock"; });
            Add("accessory.race_trim", Accessories, "Clean Race Trim", "Hide exposed removable trim where the model allows it.", false,
                e => { e.accessoryMode = "race_trim"; });
            Add("accessory.utility", Accessories, "Utility Kit", "Show windshield, flap, and rear accessory groups where present.", false,
                e => { e.accessoryMode = "utility"; });
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
            part.requiresReload |= EffectTouchesSpawnState(part.effect);
            _parts.Add(part);
            _byId[id] = part;
        }

        private static bool EffectTouchesSpawnState(PartEffect effect)
        {
            if (effect == null)
                return false;

            Vector3 center = effect.centerOfMassDelta?.ToVector3() ?? Vector3.zero;
            Vector3 driverCenter = effect.driverCenterOfMassDelta?.ToVector3() ?? Vector3.zero;
            return !Mathf.Approximately(effect.horsePowerMultiplier, 1f) ||
                   !Mathf.Approximately(effect.lugHeightMultiplier, 1f) ||
                   effect.lugHeightTargetMm > 0.01f ||
                   !Mathf.Approximately(effect.lugHeightOffset, 0f) ||
                   !Mathf.Approximately(effect.frictionMultiplier, 1f) ||
                   !Mathf.Approximately(effect.weightMultiplier, 1f) ||
                   !Mathf.Approximately(effect.weightOffset, 0f) ||
                   !Mathf.Approximately(effect.fuelCapacityMultiplier, 1f) ||
                   !Mathf.Approximately(effect.tankHardwareMassOffsetKg, 0f) ||
                   effect.backpackFuelCapacityLiters > 0.001f ||
                   !Mathf.Approximately(effect.skiStanceOffset, 0f) ||
                   !Mathf.Approximately(effect.skisXDistanceOffset, 0f) ||
                   center.sqrMagnitude > 0.0000001f ||
                   driverCenter.sqrMagnitude > 0.0000001f ||
                   effect.isTurbo ||
                   !string.IsNullOrWhiteSpace(effect.engineText);
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
                    e.nativeTrackMassMultiplier = Mathf.Clamp(1f + weightOffset / 100f, 0.92f, 1.12f);
                });
        }
    }
}
