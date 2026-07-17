using System;
using System.Globalization;

namespace AlpineTuning
{
    internal sealed class SledIdentity
    {
        public string sledKey;
        public string vehicleId;
        public string displayName;
        public string source;
        public bool fromGarageSelection;
        public bool fromRuntime;

        public string StableKey
        {
            get { return StableIdentityKey(sledKey, vehicleId); }
        }

        public static SledIdentity FromSled(
            VehicleScriptableObject sled,
            string source,
            bool fromGarageSelection,
            bool fromRuntime)
        {
            if (sled == null)
                return null;

            return new SledIdentity
            {
                sledKey = AlpineTuningMod.GetSledKey(sled),
                vehicleId = AlpineTuningMod.GetVehicleId(sled),
                displayName = AlpineTuningMod.GetSledDisplayName(sled),
                source = source,
                fromGarageSelection = fromGarageSelection,
                fromRuntime = fromRuntime
            };
        }

        public static string StableIdentityKey(VehicleScriptableObject sled)
        {
            if (sled == null)
                return null;

            return StableIdentityKey(AlpineTuningMod.GetSledKey(sled), AlpineTuningMod.GetVehicleId(sled));
        }

        public static string StableIdentityKey(string sledKey, string vehicleId)
        {
            if (!string.IsNullOrWhiteSpace(vehicleId))
                return vehicleId;

            if (!string.IsNullOrWhiteSpace(sledKey))
                return sledKey;

            return null;
        }

        public static bool HasNativeVehicleIdentity(string sledKey, string vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) ||
                string.Equals(vehicleId, sledKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Sledders 1.1.6 ItemIdentifier.ToString() is the underlying Int32.
            // Older Alpine builds stored GUID-like vehicleId strings, which must
            // remain eligible for guarded sled-key migration.
            return int.TryParse(
                       vehicleId,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int nativeId) &&
                   nativeId != 0;
        }

        public bool Matches(VehicleScriptableObject sled)
        {
            if (sled == null)
                return false;

            return Matches(
                AlpineTuningMod.GetSledKey(sled),
                AlpineTuningMod.GetVehicleId(sled));
        }

        public bool Matches(string otherSledKey, string otherVehicleId)
        {
            if (!string.IsNullOrWhiteSpace(vehicleId) &&
                !string.IsNullOrWhiteSpace(otherVehicleId) &&
                string.Equals(vehicleId, otherVehicleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A different current ItemIdentifier is authoritative. Falling back
            // to a shared Unity asset name here can bind two mod sleds together.
            if (HasNativeVehicleIdentity(sledKey, vehicleId) ||
                HasNativeVehicleIdentity(otherSledKey, otherVehicleId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sledKey) &&
                !string.IsNullOrWhiteSpace(otherSledKey) &&
                string.Equals(sledKey, otherSledKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    internal sealed class ResolvedSledTarget
    {
        public VehicleScriptableObject sled;
        public SledIdentity identity;
        public bool hasRuntimeInstance;
        public string status;

        public bool HasSled
        {
            get { return sled != null && identity != null; }
        }
    }
}
