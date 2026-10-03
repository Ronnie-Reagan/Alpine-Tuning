using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AlpineTuning
{
    internal static class GarageBuildCodec
    {
        internal const string FormatName = "ALPINE_GARAGE_BUILD";
        internal const int FormatVersion = 1;

        internal static bool TryFromProfile(TuneProfile profile, out GarageBuildV1 build, out string reason)
        {
            build = null;
            reason = null;
            if (profile == null)
            {
                reason = "setup is empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(profile.targetSledKey) && string.IsNullOrWhiteSpace(profile.targetVehicleId))
            {
                reason = "target sled identity is missing";
                return false;
            }

            FineTuneSettings fine = profile.fineTune ?? new FineTuneSettings();
            ResolvedStats stats = profile.resolvedStats ?? new ResolvedStats();
            var result = new GarageBuildV1
            {
                alpine = new GarageBuildVersionInfo
                {
                    modVersion = AlpineConstants.ModVersion,
                    profileSchema = AlpineConstants.SchemaVersion,
                    catalogVersion = AlpineConstants.CatalogVersion
                },
                name = string.IsNullOrWhiteSpace(profile.name) ? "Alpine Setup" : profile.name.Trim(),
                configuration = new GarageBuildConfiguration
                {
                    target = new GarageSledIdentity
                    {
                        sledKey = NullIfBlank(profile.targetSledKey),
                        vehicleId = NullIfBlank(profile.targetVehicleId)
                    },
                    engineDonor = new GarageSledIdentity
                    {
                        sledKey = NullIfBlank(profile.donorSledKey),
                        vehicleId = NullIfBlank(profile.donorVehicleId)
                    },
                    baseline = profile.baseline.ToString(),
                    parts = (profile.selectedParts ?? new List<PartSelection>())
                        .Where(selection => selection != null &&
                                            !string.IsNullOrWhiteSpace(selection.category) &&
                                            !string.IsNullOrWhiteSpace(selection.partId))
                        .Select(selection => new GaragePartSelection
                        {
                            category = selection.category,
                            partId = selection.partId
                        })
                        .ToList(),
                    fineTune = new GarageFineTuneSettings
                    {
                        powerTrimPercent = WireNumber(fine.powerTrimPercent),
                        tractionTrimPercent = WireNumber(fine.tractionTrimPercent),
                        weightTrimPercent = WireNumber(fine.weightTrimPercent),
                        clutchTrimPercent = WireNumber(fine.clutchTrimPercent),
                        centerOfMassYTrim = WireNumber(fine.centerOfMassYTrim),
                        centerOfMassZTrim = WireNumber(fine.centerOfMassZTrim),
                        skiStanceTrim = WireNumber(fine.skiStanceTrim),
                        nitrousBoostPercent = WireNumber(fine.nitrousBoostPercent)
                    },
                    headlightEnabled = profile.headlightEnabled
                },
                snapshot = new GarageBuildSnapshot
                {
                    horsePower = stats.horsePower,
                    weight = stats.weight,
                    fuelCapacity = stats.fuelCapacity,
                    requiresReload = profile.requiresReload,
                    engineText = stats.engineText
                }
            };

            result.contentHash = ComputeHash(result);
            build = result;
            return true;
        }

        internal static bool TryToProfile(GarageBuildV1 build, out TuneProfile profile, out string reason)
        {
            profile = null;
            reason = null;
            if (build == null || !string.Equals(build.format, FormatName, StringComparison.Ordinal) || build.formatVersion != FormatVersion)
            {
                reason = "unsupported Garage build format";
                return false;
            }
            if (build.alpine == null || build.alpine.profileSchema != AlpineConstants.SchemaVersion)
            {
                reason = $"incompatible schema {build.alpine?.profileSchema ?? 0}";
                return false;
            }
            if (!string.Equals(build.alpine.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"incompatible catalog {build.alpine.catalogVersion ?? "(missing)"}";
                return false;
            }
            if (build.configuration == null || build.configuration.target == null ||
                (string.IsNullOrWhiteSpace(build.configuration.target.sledKey) && string.IsNullOrWhiteSpace(build.configuration.target.vehicleId)))
            {
                reason = "target sled identity is missing";
                return false;
            }
            string computedHash;
            try
            {
                computedHash = ComputeHash(build);
            }
            catch (Exception)
            {
                reason = "Garage build contains invalid numeric data";
                return false;
            }
            if (!string.Equals(computedHash, build.contentHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Garage content hash mismatch";
                return false;
            }

            AlpineSetupBaseline baseline;
            if (!Enum.TryParse(build.configuration.baseline, false, out baseline) || !Enum.IsDefined(typeof(AlpineSetupBaseline), baseline))
            {
                reason = "setup baseline is invalid";
                return false;
            }

            GarageFineTuneSettings fine = build.configuration.fineTune ?? new GarageFineTuneSettings();
            if (!ValidFineTune(fine))
            {
                reason = "Garage fine tuning is outside Alpine limits";
                return false;
            }
            var result = new TuneProfile
            {
                schemaVersion = AlpineConstants.SchemaVersion,
                modVersion = AlpineConstants.ModVersion,
                catalogVersion = AlpineConstants.CatalogVersion,
                profileId = Guid.NewGuid().ToString("N"),
                name = string.IsNullOrWhiteSpace(build.name) ? "Garage Setup" : build.name.Trim(),
                usesAutomaticName = false,
                author = AlpineConstants.DefaultProfileAuthor,
                targetSledKey = NullIfBlank(build.configuration.target.sledKey),
                targetVehicleId = NullIfBlank(build.configuration.target.vehicleId),
                donorSledKey = NullIfBlank(build.configuration.engineDonor?.sledKey),
                donorVehicleId = NullIfBlank(build.configuration.engineDonor?.vehicleId),
                baseline = baseline,
                selectedParts = (build.configuration.parts ?? new List<GaragePartSelection>())
                    .Where(selection => selection != null)
                    .Select(selection => new PartSelection
                    {
                        category = selection.category,
                        partId = selection.partId
                    })
                    .ToList(),
                fineTune = new FineTuneSettings
                {
                    powerTrimPercent = (float)fine.powerTrimPercent,
                    tractionTrimPercent = (float)fine.tractionTrimPercent,
                    weightTrimPercent = (float)fine.weightTrimPercent,
                    clutchTrimPercent = (float)fine.clutchTrimPercent,
                    centerOfMassYTrim = (float)fine.centerOfMassYTrim,
                    centerOfMassZTrim = (float)fine.centerOfMassZTrim,
                    skiStanceTrim = (float)fine.skiStanceTrim,
                    nitrousBoostPercent = (float)fine.nitrousBoostPercent
                },
                sledBuild = new SledBuildSpec(),
                resolvedStats = new ResolvedStats(),
                headlightEnabled = build.configuration.headlightEnabled,
                requiresReload = false
            };

            profile = result;
            return true;
        }

        internal static string ComputeHash(GarageBuildV1 build)
        {
            string canonical = CanonicalContent(build);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var text = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        internal static bool TryPrepareImport(TuneProfile profile, PartCatalog catalog, out string reason)
        {
            // Validate the received selections before normalization can discard
            // unknown parts, invalid categories or duplicates and replace them
            // with stock. An accepted import must represent the published setup.
            if (!TuneStore.TryValidateProfileForCatalog(profile, catalog, true, false, out reason))
                return false;
            catalog.EnsureProfileSelections(profile);
            return true;
        }

        internal static string CanonicalContent(GarageBuildV1 build)
        {
            GarageBuildConfiguration config = build?.configuration ?? new GarageBuildConfiguration();
            GarageSledIdentity target = config.target ?? new GarageSledIdentity();
            GarageSledIdentity donor = config.engineDonor ?? new GarageSledIdentity();
            GarageFineTuneSettings fine = config.fineTune ?? new GarageFineTuneSettings();
            var lines = new List<string>
            {
                "ALPINE_GARAGE_BUILD|1",
                "schema=" + (build?.alpine?.profileSchema ?? 0).ToString(CultureInfo.InvariantCulture),
                "catalog=" + Lower(build?.alpine?.catalogVersion),
                "targetSledKey=" + Lower(target.sledKey),
                "targetVehicleId=" + Lower(target.vehicleId),
                "donorSledKey=" + Lower(donor.sledKey),
                "donorVehicleId=" + Lower(donor.vehicleId),
                "baseline=" + (config.baseline ?? string.Empty).Trim(),
                "headlight=" + (!config.headlightEnabled.HasValue ? "null" : config.headlightEnabled.Value ? "1" : "0")
            };

            foreach (GaragePartSelection part in (config.parts ?? new List<GaragePartSelection>())
                         .Where(item => item != null)
                         .OrderBy(item => (item.category ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal)
                         .ThenBy(item => (item.partId ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal))
            {
                lines.Add("part=" + Lower(part.category) + "=" + Lower(part.partId));
            }

            lines.Add("powerTrimPercent=" + CanonicalNumber(fine.powerTrimPercent));
            lines.Add("tractionTrimPercent=" + CanonicalNumber(fine.tractionTrimPercent));
            lines.Add("weightTrimPercent=" + CanonicalNumber(fine.weightTrimPercent));
            lines.Add("clutchTrimPercent=" + CanonicalNumber(fine.clutchTrimPercent));
            lines.Add("centerOfMassYTrim=" + CanonicalNumber(fine.centerOfMassYTrim));
            lines.Add("centerOfMassZTrim=" + CanonicalNumber(fine.centerOfMassZTrim));
            lines.Add("skiStanceTrim=" + CanonicalNumber(fine.skiStanceTrim));
            lines.Add("nitrousBoostPercent=" + CanonicalNumber(fine.nitrousBoostPercent));
            return string.Join("\n", lines);
        }

        private static double WireNumber(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Garage build contains a non-finite number.");
            // Use the same decimal value as JSON's float representation, not
            // the extra binary precision introduced by a direct double cast.
            return double.Parse(JsonConvert.SerializeObject(value), CultureInfo.InvariantCulture);
        }

        private static string CanonicalNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("Garage build contains a non-finite number.");
            double scaled = value * 1000000;
            double rounded = (scaled >= 0 ? Math.Floor(scaled + 0.5) : Math.Ceiling(scaled - 0.5)) / 1000000;
            if (rounded == 0) return "0";
            return rounded.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool ValidFineTune(GarageFineTuneSettings fine)
        {
            return InRange(fine.powerTrimPercent, -10, 10) && InRange(fine.tractionTrimPercent, -10, 10) &&
                InRange(fine.weightTrimPercent, -8, 8) && InRange(fine.clutchTrimPercent, -10, 10) &&
                InRange(fine.centerOfMassYTrim, -0.08, 0.08) && InRange(fine.centerOfMassZTrim, -0.12, 0.12) &&
                InRange(fine.skiStanceTrim, -0.08, 0.08) && InRange(fine.nitrousBoostPercent, 25, 200);
        }

        private static bool InRange(double value, double minimum, double maximum)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
        }

        private static string Lower(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
