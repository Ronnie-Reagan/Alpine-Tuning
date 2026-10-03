using System;
using System.Collections.Generic;

namespace AlpineTuning
{
    [Serializable]
    internal sealed class GarageBuildVersionInfo
    {
        public string modVersion;
        public int profileSchema;
        public string catalogVersion;
    }

    [Serializable]
    internal sealed class GarageSledIdentity
    {
        public string sledKey;
        public string vehicleId;
    }

    [Serializable]
    internal sealed class GaragePartSelection
    {
        public string category;
        public string partId;
    }

    [Serializable]
    internal sealed class GarageFineTuneSettings
    {
        public double powerTrimPercent;
        public double tractionTrimPercent;
        public double weightTrimPercent;
        public double clutchTrimPercent;
        public double centerOfMassYTrim;
        public double centerOfMassZTrim;
        public double skiStanceTrim;
        public double nitrousBoostPercent = 100;
    }

    [Serializable]
    internal sealed class GarageBuildConfiguration
    {
        public GarageSledIdentity target = new GarageSledIdentity();
        public GarageSledIdentity engineDonor = new GarageSledIdentity();
        public string baseline;
        public List<GaragePartSelection> parts = new List<GaragePartSelection>();
        public GarageFineTuneSettings fineTune = new GarageFineTuneSettings();
        public bool? headlightEnabled;
    }

    [Serializable]
    internal sealed class GarageBuildSnapshot
    {
        public float horsePower;
        public float weight;
        public float fuelCapacity;
        public bool requiresReload;
        public string engineText;
    }

    [Serializable]
    internal sealed class GarageBuildV1
    {
        public string format = GarageBuildCodec.FormatName;
        public int formatVersion = GarageBuildCodec.FormatVersion;
        public GarageBuildVersionInfo alpine = new GarageBuildVersionInfo();
        public string name;
        public GarageBuildConfiguration configuration = new GarageBuildConfiguration();
        public GarageBuildSnapshot snapshot = new GarageBuildSnapshot();
        public string contentHash;
    }

    [Serializable]
    internal sealed class GarageProfileSummary
    {
        public string profileId;
        public string name;
        public string targetSledKey;
        public string targetVehicleId;
        public long updatedUnixTime;
        public float? horsePower;
        public float? weight;
        public string engineText;
    }

    [Serializable]
    internal sealed class GarageServerPresence
    {
        public string roomFingerprint;
        public int memberCount;
    }

    [Serializable]
    internal sealed class GarageConnectionRecord
    {
        internal const string ProductionApiBaseUrl = "https://garage.donreagan.ca";
        public string apiBaseUrl = ProductionApiBaseUrl;
        public bool? enabled;
        public string installationId;
        public string installationToken;
        public string linkedUser;
        public List<string> processedImportActionIds = new List<string>();
        public GarageActionCompletion pendingCompletion;
    }

    [Serializable]
    internal sealed class GarageActionCompletion
    {
        public string actionId;
        public bool ok;
        public string error;
        public GarageBuildV1 build;
    }
}
