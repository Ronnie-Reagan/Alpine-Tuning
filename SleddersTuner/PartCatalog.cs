using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlpineTuning
{
    internal class PartCatalog
    {
        public const string EngineCore = "engineCore";
        public const string Turbo = "turbo";
        public const string Intake = "intakeExhaust";
        public const string Clutch = "clutchGearing";
        public const string Track = "track";
        public const string Suspension = "suspension";
        public const string Chassis = "chassis";
        public const string Skis = "skis";
        public const string Accessories = "accessories";

        public static readonly string[] OrderedCategories =
        {
            EngineCore,
            Turbo,
            Intake,
            Clutch,
            Track,
            Suspension,
            Chassis,
            Skis,
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
                    return "Engine Core";
                case Turbo:
                    return "Turbo / Induction";
                case Intake:
                    return "Intake / Exhaust";
                case Clutch:
                    return "Clutch / Gearing";
                case Track:
                    return "Track";
                case Suspension:
                    return "Suspension";
                case Chassis:
                    return "Chassis";
                case Skis:
                    return "Skis / Stance";
                case Accessories:
                    return "Native Accessories";
                default:
                    return category;
            }
        }

        public string DefaultPartId(string category)
        {
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
            // Engine core: adjust naturally aspirated power and base torque feel.
            // Raise/lower horsePowerMultiplier and powerFactorMultiplier to balance straight-line speed.
            Add("engine.stock", EngineCore, "Stock Engine", "Factory engine mapping.", false,
                e => { });
            Add("engine.stage1", EngineCore, "Stage 1 Kit", "Mild porting and calibration.", true,
                e => { e.horsePowerMultiplier = 1.10f; e.powerFactorMultiplier = 1.05f; });
            Add("engine.stage2", EngineCore, "Stage 2 Kit", "Stronger top-end with a controlled torque bump.", true,
                e => { e.horsePowerMultiplier = 1.24f; e.powerFactorMultiplier = 1.12f; e.weightOffset = 2f; });
            Add("engine.bigbore", EngineCore, "Big Bore Build", "More displacement without full race volatility.", true,
                e => { e.horsePowerMultiplier = 1.38f; e.powerFactorMultiplier = 1.18f; e.weightOffset = 5f; });
            Add("engine.race", EngineCore, "Race Engine", "Highest naturally aspirated package in the safe catalog.", true,
                e => { e.horsePowerMultiplier = 1.52f; e.powerFactorMultiplier = 1.24f; e.weightOffset = 6f; e.throttleExponentDelta = -0.04f; });

            // Turbo / induction: adjust boosted builds. These stack with engine core, so keep multipliers conservative.
            Add("turbo.none", Turbo, "Naturally Aspirated", "No forced induction.", false,
                e => { });
            Add("turbo.trail", Turbo, "Trail Turbo", "Fast-spooling boost for broad terrain.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.18f;
                    e.powerFactorMultiplier = 1.08f;
                    e.weightOffset = 4f;
                    e.isTurbo = true;
                    e.engineText = "Trail Turbo";
                    e.rpmSensitivityMultiplier = 1.05f;
                });
            Add("turbo.mountain", Turbo, "Mountain Turbo", "Balanced boost for climbing and powder.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.34f;
                    e.powerFactorMultiplier = 1.15f;
                    e.weightOffset = 7f;
                    e.isTurbo = true;
                    e.engineText = "Mountain Turbo";
                    e.rpmSensitivityMultiplier = 1.08f;
                });
            Add("turbo.bigboost", Turbo, "Big Boost Kit", "Aggressive forced induction with extra weight.", true,
                e =>
                {
                    e.horsePowerMultiplier = 1.56f;
                    e.powerFactorMultiplier = 1.26f;
                    e.weightOffset = 11f;
                    e.isTurbo = true;
                    e.engineText = "Big Boost Turbo";
                    e.rpmSensitivityMultiplier = 1.12f;
                    e.wheelieThresholdOffset = -0.04f;
                });

            // Intake / exhaust: small response and weight changes that should not dominate the build.
            Add("intake.stock", Intake, "Stock Intake / Exhaust", "Factory breathing.", false,
                e => { });
            Add("intake.flow", Intake, "High Flow Intake", "Small response and horsepower gain.", false,
                e => { e.horsePowerMultiplier = 1.04f; e.powerFactorMultiplier = 1.02f; e.weightOffset = -1f; });
            Add("intake.pipe", Intake, "Race Pipe", "Sharper response with a modest weight drop.", false,
                e => { e.horsePowerMultiplier = 1.07f; e.powerFactorMultiplier = 1.03f; e.weightOffset = -2f; e.throttleExponentDelta = -0.03f; });

            // Clutch / gearing: runtime controller feel. Tune RPM offsets and throttleExponentDelta here.
            Add("clutch.stock", Clutch, "Stock Clutching", "Factory clutch and gearing.", false,
                e => { });
            Add("clutch.trail", Clutch, "Trail Clutch Kit", "Earlier response and smoother backshift.", false,
                e => { e.clutchRpmMinOffset = -80f; e.clutchRpmMaxOffset = 90f; e.rpmSensitivityMultiplier = 1.04f; });
            Add("clutch.mountain", Clutch, "Mountain Helix", "Keeps revs up under load.", false,
                e => { e.clutchRpmMinOffset = 120f; e.clutchRpmMaxOffset = 220f; e.rpmSensitivityMultiplier = 1.08f; e.rpmSensitivityDownMultiplier = 0.96f; });
            Add("clutch.race", Clutch, "Race Gearing", "Higher engagement and sharper throttle.", false,
                e => { e.clutchRpmMinOffset = 220f; e.clutchRpmMaxOffset = 320f; e.rpmSensitivityMultiplier = 1.12f; e.throttleExponentDelta = -0.06f; });

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
    }
}
