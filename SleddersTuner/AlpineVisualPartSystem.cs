using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AlpineTuning
{
    /// <summary>
    /// Resolves length siblings from Sledders' native vehicle definitions and
    /// stages their Addressables chassis prefab for the game's normal recreate
    /// path. No renderer hierarchy is cloned or detached from native animation.
    /// </summary>
    internal sealed class AlpineVisualPartSystem
    {
        private const float SwapTimeoutSeconds = 12f;
        private static readonly Regex LengthPattern = new Regex(
            @"(?<!\d)(1[0-9]{2}|200)(?:\s*(?:in|inch|inches|""))?(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MetricLengthPattern = new Regex(
            @"(?<!\d)([34][0-9]{3})(?:\s*(?:mm|millimeter|millimeters))?(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FamilyNoise = new Regex(
            @"(?i)(?<!\d)(?:1[0-9]{2}|200|[34][0-9]{3})(?!\d)|\b(650|800|850|900|9r|boost|mod|stock|base|prefab|bwb|my\d{2})\b",
            RegexOptions.CultureInvariant);

        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<string, NativeChassisVariant> _variants =
            new Dictionary<string, NativeChassisVariant>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _rejectedVariantIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly AlpineTrackGraft _graft;
        private int _lastTargetId = int.MinValue;
        private VehicleScriptableObject _lastTarget;
        private bool _compatibilityScanPending;

        internal sealed class NativeChassisVariant
        {
            public string id;
            public string family;
            public float lengthInches;
            public NativeTrackLength length;
            public VehicleScriptableObject donor;
            public string displayName;
            public int rank;
            public string donorFamily;
            public TrackGraftMode? confirmedMode;
            public List<NativeChassisVariant> candidates;
            public float compatibilityScore;
        }

        internal enum NativeTrackLengthUnit
        {
            Inches,
            Millimeters
        }

        internal sealed class NativeTrackLength
        {
            public float nativeValue;
            public NativeTrackLengthUnit unit;
            public float canonicalInches;

            public string IdToken => nativeValue.ToString("0", CultureInfo.InvariantCulture);
        }

        internal sealed class PlatformFamilyMetadata
        {
            public string key;
            public string displayName;
            public string modelLine;
        }

        internal AlpineVisualPartSystem(AlpineTuningMod mod)
        {
            _mod = mod;
            _graft = new AlpineTrackGraft(mod);
        }

        internal bool HasPendingChassisSwap => _graft.HasPendingSwap;

        internal bool NeedsSourceReload => _graft.NeedsSourceReload;

        internal bool IsCompatibilityScanPending => _compatibilityScanPending;

        internal bool HasInstalledChassis(VehicleScriptableObject target)
        {
            return _graft.HasInstalledSwap(target);
        }

        internal bool IsInstalledVariant(string variantId, VehicleScriptableObject target)
        {
            return _graft.IsInstalledVariant(variantId, target);
        }

        internal bool TryGetRecipeMetadata(
            string variantId,
            VehicleScriptableObject target,
            out string donorIdentity,
            out TrackGraftMode mode)
        {
            donorIdentity = null;
            mode = TrackGraftMode.DirectGraft;
            Refresh(target);
            if (string.IsNullOrWhiteSpace(variantId) ||
                !_variants.TryGetValue(variantId, out NativeChassisVariant variant) ||
                !IsUsableVariant(target, variant))
                return false;
            donorIdentity = StableDonorAssetKey(variant.donor);
            mode = variant.confirmedMode ?? TrackGraftMode.DirectGraft;
            return !string.IsNullOrWhiteSpace(donorIdentity);
        }

        internal void Refresh(VehicleScriptableObject target)
        {
            if (target == null)
                return;
            int targetId = target.GetInstanceID();
            if (_lastTargetId == targetId)
                return;

            _graft.CancelCompatibilityScan();
            _variants.Clear();
            _rejectedVariantIds.Clear();
            _mod.Catalog.ClearDetectedTrackLengths();
            _lastTargetId = targetId;
            _lastTarget = target;
            PlatformFamilyMetadata targetPlatform = ResolvePlatformFamily(target);
            if (targetPlatform == null || string.IsNullOrWhiteSpace(targetPlatform.key))
                return;

            TryDetectLength(target, targetPlatform, out NativeTrackLength targetLength);
            IEnumerable<VehicleScriptableObject> candidates = (_mod?.SelectableSleds ??
                Array.Empty<VehicleScriptableObject>())
                .Concat(Resources.FindObjectsOfTypeAll<VehicleScriptableObject>() ??
                        Array.Empty<VehicleScriptableObject>())
                .Where(candidate => candidate != null && candidate != target)
                .GroupBy(AlpineTuningMod.GetVehicleId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());

            var discovered = new Dictionary<string, List<NativeChassisVariant>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (VehicleScriptableObject donor in candidates)
            {
                PlatformFamilyMetadata donorPlatform = ResolvePlatformFamily(donor);
                if (donorPlatform == null ||
                    !TryDetectLength(donor, donorPlatform, out NativeTrackLength length) ||
                    (targetLength != null &&
                     Mathf.Abs(length.canonicalInches - targetLength.canonicalInches) < 0.25f) ||
                    donor.assetReference == null || !donor.assetReference.RuntimeKeyIsValid())
                    continue;

                string id = SafeId(targetPlatform.key) + "." + length.IdToken;
                var variant = new NativeChassisVariant
                {
                    id = id,
                    family = targetPlatform.key,
                    lengthInches = length.canonicalInches,
                    length = length,
                    donor = donor,
                    displayName = FormatVariantDisplayName(targetPlatform, length),
                    rank = RankDonor(target, donor, targetPlatform, donorPlatform),
                    donorFamily = donorPlatform.key
                };

                if (!discovered.TryGetValue(id, out List<NativeChassisVariant> lengthCandidates))
                {
                    lengthCandidates = new List<NativeChassisVariant>();
                    discovered[id] = lengthCandidates;
                }
                string donorKey = StableDonorAssetKey(donor);
                if (!lengthCandidates.Any(item =>
                        string.Equals(StableDonorAssetKey(item.donor), donorKey,
                            StringComparison.OrdinalIgnoreCase)))
                    lengthCandidates.Add(variant);
            }

            foreach (KeyValuePair<string, List<NativeChassisVariant>> pair in discovered)
            {
                List<NativeChassisVariant> ordered = pair.Value
                    .OrderByDescending(item => item.rank)
                    .ThenBy(item => StableDonorKey(item.donor), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ordered.Count == 0) continue;
                NativeChassisVariant primary = ordered[0];
                primary.candidates = ordered;
                _variants[pair.Key] = primary;
            }
            List<NativeChassisVariant> unverified = _variants.Values.ToList();
            _variants.Clear();
            _compatibilityScanPending = unverified.Count > 0;
            if (!_compatibilityScanPending)
                return;
            _graft.BeginCompatibilityScan(target, unverified, verified =>
            {
                if (target == null || target.GetInstanceID() != _lastTargetId)
                    return;
                _variants.Clear();
                foreach (NativeChassisVariant variant in verified ??
                         Array.Empty<NativeChassisVariant>())
                {
                    _variants[variant.id] = variant;
                    ConfirmVariantMode(variant,
                        variant.confirmedMode ?? TrackGraftMode.DirectGraft, false);
                }
                _compatibilityScanPending = false;
                AlpineNativeUi.RefreshAttachedGarage();
                _mod.OnTrackCompatibilityScanCompleted(target);
            });
        }

        internal bool IsCompatible(string variantId, VehicleScriptableObject target)
        {
            Refresh(target);
            return !string.IsNullOrWhiteSpace(variantId) &&
                   _variants.TryGetValue(variantId, out NativeChassisVariant variant) &&
                   IsUsableVariant(target, variant);
        }

        internal bool HasAlternateNativeLength(VehicleScriptableObject target)
        {
            Refresh(target);
            return _variants.Values.Any(variant => IsUsableVariant(target, variant));
        }

        internal bool Apply(PartEffect effect, out string reason)
        {
            reason = null;
            string requested = effect?.visualTrackVariantId;
            if (string.IsNullOrWhiteSpace(requested))
            {
                RestoreTrackVisual();
                return true;
            }

            VehicleScriptableObject target = AlpineTuningMod.ActiveSO;
            Refresh(target);
            if (target == null || !_variants.TryGetValue(requested, out NativeChassisVariant variant) ||
                !IsUsableVariant(target, variant))
            {
                reason = "Native chassis donor is unavailable for this sled family.";
                return false;
            }

            return _graft.Stage(variant, target, out reason);
        }

        internal void OnControllerInitialized(
            SnowmobileController controller,
            VehicleScriptableObject sled)
        {
            _graft.OnControllerInitialized(controller, sled);
        }

        internal void Update()
        {
            _graft.Update();
        }

        internal void RollbackPendingSwap()
        {
            _graft.RollbackPendingSwap();
        }

        internal void RestoreTrackVisual()
        {
            _graft.RestoreRuntime();
        }

        internal bool RequestGaragePreview(
            VehicleScriptableObject target,
            string variantId,
            Action<bool, string> completed)
        {
            NativeChassisVariant variant = null;
            if (!string.IsNullOrWhiteSpace(variantId))
            {
                Refresh(target);
                if (!_variants.TryGetValue(variantId, out variant))
                {
                    completed?.Invoke(false, "Compatible track donor is unavailable.");
                    return false;
                }
            }
            return _graft.RequestGaragePreview(target, variant, completed);
        }

        internal void RestoreGaragePreview()
        {
            _graft.RestoreGaragePreview();
        }

        internal void InvalidateCatalog()
        {
            _graft.CancelCompatibilityScan();
            _lastTargetId = int.MinValue;
            _compatibilityScanPending = false;
        }

        internal void ConfirmVariantMode(
            NativeChassisVariant variant,
            TrackGraftMode mode,
            bool refreshUi = true)
        {
            if (variant == null) return;
            variant.confirmedMode = mode;
            string directName = FormatVariantDisplayName(null, variant.length);
            bool crossPlatform = _lastTarget != null && !string.Equals(
                ResolvePlatformFamily(_lastTarget)?.key,
                variant.donorFamily,
                StringComparison.OrdinalIgnoreCase);
            bool experimental = mode == TrackGraftMode.ScaledFallback || crossPlatform;
            variant.displayName = mode == TrackGraftMode.ScaledFallback
                ? directName.Replace("Track + Rear Chassis", "Track — Scaled fallback")
                : directName;
            _mod.Catalog.RegisterDetectedTrackLength(
                variant.id, variant.displayName, variant.lengthInches, 1f, 1f, 1f,
                mode == TrackGraftMode.ScaledFallback, experimental);
            if (refreshUi)
                AlpineNativeUi.RefreshAttachedGarage();
        }

        internal void Shutdown()
        {
            _graft.Shutdown();
            _variants.Clear();
            _rejectedVariantIds.Clear();
            _lastTargetId = int.MinValue;
            _lastTarget = null;
            _compatibilityScanPending = false;
        }

        private bool IsUsableVariant(
            VehicleScriptableObject target,
            NativeChassisVariant variant)
        {
            if (target == null || variant?.donor == null ||
                _rejectedVariantIds.Contains(variant.id) ||
                variant.donor.assetReference == null ||
                !variant.donor.assetReference.RuntimeKeyIsValid())
                return false;

            bool samePlatform = string.Equals(
                ResolvePlatformFamily(target)?.key,
                variant.donorFamily,
                StringComparison.OrdinalIgnoreCase);
            bool reducedFidelity = variant.confirmedMode == TrackGraftMode.ScaledFallback;
            return samePlatform && !reducedFidelity ||
                   (_mod.Settings.experimentalTrackCompatibility && variant.confirmedMode.HasValue);
        }

        internal static string ResolveFamily(VehicleScriptableObject vehicle)
        {
            return ResolvePlatformFamily(vehicle)?.key;
        }

        internal static string ResolveFamilyMetadata(
            string group,
            string prefabName,
            string displayName,
            string assetName)
        {
            return ResolvePlatformFamilyMetadata(group, prefabName, displayName, assetName)?.key;
        }

        internal static PlatformFamilyMetadata ResolvePlatformFamily(VehicleScriptableObject vehicle)
        {
            if (vehicle == null)
                return null;
            return ResolvePlatformFamilyMetadata(
                vehicle.group, vehicle.prefabName, vehicle.displayName, vehicle.name);
        }

        internal static PlatformFamilyMetadata ResolvePlatformFamilyMetadata(
            string group,
            string prefabName,
            string displayName,
            string assetName)
        {
            string combined = string.Join(" ", new[]
            {
                group, prefabName, displayName, assetName
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
            string modelLine = NormalizeFamily(prefabName ?? displayName ?? assetName);

            if (ContainsAny(combined, "matryx", "rmk", "khaos", "indy vr1", "switchback assault"))
                return Platform("rmk", "Polaris Matryx", modelLine);
            if (ContainsAny(combined, "ski-doo g5", "skidoo g5", "summit", "freeride", "mxz x-rs", "mxz x rs",
                            "backcountry x-rs", "backcountry x rs", "backcountry") ||
                Regex.IsMatch(combined, @"(?i)(^|\W)g5(\W|$)"))
                return Platform("g5", "Ski-Doo G5", modelLine);
            if (ContainsAny(combined, "articcat", "artic cat", "arcticcat", "arctic cat", "pollux"))
                return Platform("arctic-cat", "Arctic Cat", modelLine);
            if (ContainsAny(combined, "radien", "lynx rave", "shredder", "brutal re", "lynx brutal"))
                return Platform("lynx-radien", "Lynx Radien", modelLine);

            string fallback = modelLine;
            if (string.IsNullOrWhiteSpace(fallback))
                fallback = NormalizeFamily(group);
            return string.IsNullOrWhiteSpace(fallback)
                ? null
                : Platform(fallback, FamilyDisplayName(fallback), fallback);
        }

        internal static bool TryDetectLength(VehicleScriptableObject vehicle, out float inches)
        {
            inches = 0f;
            if (vehicle == null)
                return false;
            PlatformFamilyMetadata platform = ResolvePlatformFamily(vehicle);
            if (!TryDetectLength(vehicle, platform, out NativeTrackLength result))
                return false;
            inches = result.canonicalInches;
            return true;
        }

        internal static bool TryDetectLength(
            VehicleScriptableObject vehicle,
            PlatformFamilyMetadata platform,
            out NativeTrackLength result)
        {
            result = null;
            if (vehicle == null)
                return false;
            return TryDetectLengthMetadata(
                vehicle.lengthName, vehicle.prefabName, vehicle.displayName, vehicle.name,
                platform?.key, out result);
        }

        internal static bool TryDetectLengthMetadata(
            string lengthName,
            string prefabName,
            string displayName,
            string assetName,
            out float inches)
        {
            inches = 0f;
            if (!TryDetectLengthMetadata(
                    lengthName, prefabName, displayName, assetName, null,
                    out NativeTrackLength result))
                return false;
            inches = result.canonicalInches;
            return true;
        }

        internal static bool TryDetectLengthMetadata(
            string lengthName,
            string prefabName,
            string displayName,
            string assetName,
            string platformKey,
            out NativeTrackLength result)
        {
            result = null;
            foreach (string source in new[] { lengthName, prefabName, displayName, assetName })
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;
                float[] values = LengthPattern.Matches(source).Cast<Match>()
                    .Select(match => float.TryParse(match.Groups[1].Value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out float parsed)
                        ? parsed : 0f)
                    .Where(value => value >= 100f && value <= 200f)
                    .Distinct()
                    .ToArray();
                if (values.Length == 1)
                {
                    result = new NativeTrackLength
                    {
                        nativeValue = values[0],
                        unit = NativeTrackLengthUnit.Inches,
                        canonicalInches = values[0]
                    };
                    return true;
                }

                if (!string.Equals(platformKey, "lynx-radien", StringComparison.OrdinalIgnoreCase))
                    continue;
                float[] millimeterValues = MetricLengthPattern.Matches(source).Cast<Match>()
                    .Select(match => float.TryParse(match.Groups[1].Value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out float parsed)
                        ? parsed : 0f)
                    .Where(value => value >= 3000f && value <= 4500f)
                    .Distinct()
                    .ToArray();
                if (millimeterValues.Length == 1)
                {
                    result = new NativeTrackLength
                    {
                        nativeValue = millimeterValues[0],
                        unit = NativeTrackLengthUnit.Millimeters,
                        canonicalInches = millimeterValues[0] / 25.4f
                    };
                    return true;
                }
            }
            return false;
        }

        private static int RankDonor(
            VehicleScriptableObject target,
            VehicleScriptableObject donor,
            PlatformFamilyMetadata targetPlatform,
            PlatformFamilyMetadata donorPlatform)
        {
            bool sameCategory = target != null && donor != null && target.category.Equals(donor.category);
            int rank = RankDonorMetadata(
                targetPlatform?.modelLine,
                donorPlatform?.modelLine,
                target?.group,
                donor?.group,
                sameCategory,
                donor?.prefabName);
            if (string.Equals(targetPlatform?.key, donorPlatform?.key, StringComparison.OrdinalIgnoreCase))
                rank += 800;
            return rank;
        }

        internal static int RankDonorMetadata(
            string targetModelLine,
            string donorModelLine,
            string targetGroup,
            string donorGroup,
            bool sameCategory,
            string donorPrefabName)
        {
            int rank = 0;
            if (!string.IsNullOrWhiteSpace(targetModelLine) &&
                string.Equals(targetModelLine, donorModelLine, StringComparison.OrdinalIgnoreCase))
                rank += 400;
            if (!string.IsNullOrWhiteSpace(targetGroup) &&
                string.Equals(targetGroup, donorGroup, StringComparison.OrdinalIgnoreCase))
                rank += 200;
            if (sameCategory)
                rank += 100;
            string prefab = donorPrefabName ?? string.Empty;
            bool isMod = Regex.IsMatch(prefab, @"(?i)\bmod\b");
            if (Regex.IsMatch(prefab, @"(?i)\b(base|standard)\b") || !isMod)
                rank += 40;
            if (!isMod)
                rank += 20;
            if (!Regex.IsMatch(prefab, @"(?i)\b(650|800|850|900|9r|boost)\b"))
                rank += 10;
            return rank;
        }

        private static bool ShouldReplaceDonor(
            NativeChassisVariant existing,
            NativeChassisVariant candidate)
        {
            if (candidate.rank != existing.rank)
                return candidate.rank > existing.rank;
            return string.Compare(
                       StableDonorKey(candidate.donor),
                       StableDonorKey(existing.donor),
                       StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string StableDonorKey(VehicleScriptableObject donor)
        {
            return string.Join("|", new[]
            {
                donor?.prefabName ?? string.Empty,
                donor?.displayName ?? string.Empty,
                donor?.name ?? string.Empty,
                donor == null ? string.Empty : AlpineTuningMod.GetVehicleId(donor)
            });
        }

        private static string StableDonorAssetKey(VehicleScriptableObject donor)
        {
            if (donor?.assetReference != null && donor.assetReference.RuntimeKeyIsValid())
                return Convert.ToString(donor.assetReference.RuntimeKey, CultureInfo.InvariantCulture) ??
                       StableDonorKey(donor);
            return StableDonorKey(donor);
        }

        private static string FormatVariantDisplayName(
            PlatformFamilyMetadata platform,
            NativeTrackLength length)
        {
            if (length.unit == NativeTrackLengthUnit.Millimeters)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0} mm ({1:0.#}\") Track + Rear Chassis",
                    length.nativeValue,
                    length.canonicalInches);
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0}\" Track + Rear Chassis",
                length.nativeValue);
        }

        private static PlatformFamilyMetadata Platform(
            string key,
            string displayName,
            string modelLine)
        {
            return new PlatformFamilyMetadata
            {
                key = key,
                displayName = displayName,
                modelLine = modelLine
            };
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;
            return values.Any(value =>
                source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizeFamily(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string cleaned = FamilyNoise.Replace(value, " ");
            string normalized = new string(cleaned.ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray());
            while (normalized.Contains("--"))
                normalized = normalized.Replace("--", "-");
            return normalized.Trim('-');
        }

        private static string FamilyDisplayName(string family)
        {
            if (string.Equals(family, "rmk", StringComparison.OrdinalIgnoreCase))
                return "RMK";
            if (string.IsNullOrWhiteSpace(family))
                return "Native";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(family.Replace('-', ' '));
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "native";
            return new string(value.ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray()).Trim('-');
        }
    }
}
