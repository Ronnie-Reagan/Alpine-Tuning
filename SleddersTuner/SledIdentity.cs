using System;

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
