using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineConstants
    {
        public const int SchemaVersion = 2;
        public const string ModVersion = "2026.06.13";
        public const string CatalogVersion = "2026.06.v1";
        public static readonly bool PeerSharingTemporarilyDisabled = true;
        public const string PeerSharingPausedNotice =
            "Networked setup sharing is paused for this build. It may return if new P2P sharing methods are found.";
        public const int SteamP2PChannel = 7264;
        public const byte SleddersInternalMessageId = 252;
        public const int SleddersInternalMaxChunkBytes = 5600;
        public const int MaxPeerMessageBytes = 65536;
        public const int MaxPeerProfileBytes = 32768;
        public const int MaxProfileIdLength = 64;
        public const int MaxProfileNameLength = 96;
        public const int MaxSledIdentityLength = 128;
    }

    internal enum AlpineDisplayUnits
    {
        Metric,
        Imperial
    }

    [Serializable]
    internal class AlpineCapabilityStatus
    {
        public string id;
        public string label;
        public string state;
        public bool required;
        public string detail;

        [JsonIgnore]
        public bool IsReady => string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    internal class AlpineCompatibilityReport
    {
        public string overallStatus;
        public string assemblyPath;
        public string assemblyLastWriteUtc;
        public long assemblyLengthBytes;
        public string assemblyLightHash;
        public List<AlpineCapabilityStatus> capabilities = new List<AlpineCapabilityStatus>();

        [JsonIgnore]
        public string SummaryLine =>
            $"Compatibility {DisplayStatus(overallStatus)} | Assembly {DisplayOrUnknown(assemblyLightHash)} | " +
            $"{ReadyCapabilityCount}/{capabilities.Count} capabilities ready";

        [JsonIgnore]
        public int ReadyCapabilityCount
        {
            get
            {
                int count = 0;
                foreach (var capability in capabilities)
                {
                    if (capability != null && capability.IsReady)
                        count++;
                }

                return count;
            }
        }

        private static string DisplayStatus(string status)
        {
            return string.IsNullOrWhiteSpace(status) ? "unknown" : status;
        }

        private static string DisplayOrUnknown(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
    }

    [Serializable]
    internal class AlpineUserSettings
    {
        public int schemaVersion = AlpineConstants.SchemaVersion;
        public AlpineDisplayUnits units = AlpineDisplayUnits.Metric;
        public bool advancedDetails;
        public bool diagnosticSteamIdScanEnabled;

        public bool headlightToggleEnabled;
        public string headlightKeyboardKey;
        public string headlightControllerButton;
        public bool headlightBindingConfigured;

        public bool shareMySetup = false;
        public bool alwaysShareMySetup = false;
        public bool shareLighting = true;
        public bool shareAudio = true;
        public bool shareVisualEquipment = false;
        public bool receivePeerSetups = true;
        public bool receivePeerLighting = true;
        public bool receivePeerAudio = true;
        public bool receivePeerVisualEquipment;

        public void Normalize()
        {
            schemaVersion = AlpineConstants.SchemaVersion;

            if (!headlightBindingConfigured)
            {
                bool hasKeyboard = !string.IsNullOrWhiteSpace(headlightKeyboardKey);
                bool hasController = !string.IsNullOrWhiteSpace(headlightControllerButton);
                bool onlyLegacyDefaults =
                    (!hasKeyboard || IsLegacyDefaultHeadlightBinding(headlightKeyboardKey)) &&
                    (!hasController || IsLegacyDefaultHeadlightBinding(headlightControllerButton));

                if (onlyLegacyDefaults)
                {
                    headlightKeyboardKey = null;
                    headlightControllerButton = null;
                    headlightToggleEnabled = false;
                }
                else if (hasKeyboard || hasController)
                {
                    headlightBindingConfigured = true;
                }
            }

            if (string.IsNullOrWhiteSpace(headlightKeyboardKey))
                headlightKeyboardKey = null;

            if (string.IsNullOrWhiteSpace(headlightControllerButton))
                headlightControllerButton = null;

            if (string.IsNullOrWhiteSpace(headlightKeyboardKey) &&
                string.IsNullOrWhiteSpace(headlightControllerButton))
            {
                headlightToggleEnabled = false;
            }
        }

        private static bool IsLegacyDefaultHeadlightBinding(string value)
        {
            return string.Equals(value, "H", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "JoystickButton7", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    internal class Vec3Data
    {
        public float x;
        public float y;
        public float z;

        public Vec3Data()
        {
        }

        public Vec3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vec3Data From(Vector3 value)
        {
            return new Vec3Data(value.x, value.y, value.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    internal class SledDefaults
    {
        public string sledKey;
        public string displayName;
        public string vehicleId;
        public float horsePower;
        public float powerFactor;
        public float lugHeight;
        public float friction;
        public float weight;
        public float skiStance;
        public float skisXDistanceOffset;
        public bool isTurboOn;
        public string engineText;
        public bool hasSnowmobileStats;
        public float statsPower;
        public float statsClimbing;
        public float statsAgility;
        public bool hasAccessoryDefaults;
        public bool hasWindshield;
        public bool hasSnowFlaps;
        public bool hasRemovableRearParts;
        public bool hasTunnelAccessories;
        public Vec3Data centerOfMassOffset = new Vec3Data();
        public Vec3Data driverCenterOfMassOffset = new Vec3Data();
        public string engineAudioEnumType;
        public string engineAudioEnumName;
        public int engineAudioEnumRawValue;
        public ControllerDefaults controller = new ControllerDefaults();

        public static SledDefaults FromSled(VehicleScriptableObject so, string key)
        {
            return new SledDefaults
            {
                sledKey = key,
                displayName = !string.IsNullOrWhiteSpace(so.displayName) ? so.displayName : so.name,
                vehicleId = AlpineTuningMod.GetVehicleId(so),
                horsePower = so.horsePower,
                powerFactor = so.powerFactor,
                lugHeight = so.lugHeight,
                friction = so.coefficientOfFriction,
                weight = so.weight,
                skiStance = so.skiStance,
                skisXDistanceOffset = so.skisXDistanceOffset,
                isTurboOn = so.isTurboOn,
                engineText = so.engineText,
                hasSnowmobileStats = so.snowmobileStats != null,
                statsPower = so.snowmobileStats != null ? so.snowmobileStats.power : 0f,
                statsClimbing = so.snowmobileStats != null ? so.snowmobileStats.climbing : 0f,
                statsAgility = so.snowmobileStats != null ? so.snowmobileStats.agility : 0f,
                hasAccessoryDefaults = true,
                hasWindshield = so.hasWindshield,
                hasSnowFlaps = so.hasSnowFlaps,
                hasRemovableRearParts = so.hasRemovableRearParts,
                hasTunnelAccessories = so.hasTunnelAccessories,
                centerOfMassOffset = Vec3Data.From(so.centerOfMassOffset),
                driverCenterOfMassOffset = Vec3Data.From(so.driverCenterOfMassOffset)
            };
        }
    }

    [Serializable]
    internal class ControllerDefaults
    {
        public bool hasThrottleExponent;
        public float throttleExponent;
        public bool hasRpmSensitivity;
        public float rpmSensitivity;
        public bool hasRpmSensitivityDown;
        public float rpmSensitivityDown;
        public bool hasClutchRpmMin;
        public float clutchRpmMin;
        public bool hasClutchRpmMax;
        public float clutchRpmMax;
        public bool hasMinThrottleOnClutchEngagement;
        public float minThrottleOnClutchEngagement;
        public bool hasWheelieThreshold;
        public float wheelieThreshold;
        public bool hasStabilizerDamping;
        public Vec3Data stabilizerDamping = new Vec3Data();
        public bool hasTrackSpeedDamping;
        public Vec3Data trackSpeedDamping = new Vec3Data();
        public bool hasTrackSpeedGyroMultiplier;
        public float trackSpeedGyroMultiplier;
    }

    [Serializable]
    internal class TuneProfile
    {
        public int schemaVersion = AlpineConstants.SchemaVersion;
        public string modVersion = AlpineConstants.ModVersion;
        public string catalogVersion = AlpineConstants.CatalogVersion;
        public string profileId;
        public string name;
        public string author;
        public string targetSledKey;
        public string targetVehicleId;
        public string donorSledKey;
        public List<PartSelection> selectedParts = new List<PartSelection>();
        public FineTuneSettings fineTune = new FineTuneSettings();
        public ResolvedStats resolvedStats = new ResolvedStats();
        public bool? headlightEnabled;
        public bool requiresReload;
        public long createdUnixTime;
        public long updatedUnixTime;
        public string sourceProfileId;
        public ulong sourceSenderId;
        public string sourceSenderName;
        public long importedUnixTime;
        public string checksum;
        [JsonIgnore] public string setupSlotId;
        [JsonIgnore] public string setupSlotName;
        [JsonIgnore] public bool setupEdited;
        [JsonIgnore] public bool isCurrentSetup;

        public string GetPartId(string category)
        {
            for (int i = 0; i < selectedParts.Count; i++)
            {
                if (string.Equals(selectedParts[i].category, category, StringComparison.OrdinalIgnoreCase))
                    return selectedParts[i].partId;
            }

            return null;
        }

        public void SetPartId(string category, string partId)
        {
            for (int i = 0; i < selectedParts.Count; i++)
            {
                if (string.Equals(selectedParts[i].category, category, StringComparison.OrdinalIgnoreCase))
                {
                    selectedParts[i].partId = partId;
                    return;
                }
            }

            selectedParts.Add(new PartSelection { category = category, partId = partId });
        }
    }

    [Serializable]
    internal class PartSelection
    {
        public string category;
        public string partId;
    }

    [Serializable]
    internal class FineTuneSettings
    {
        public float powerTrimPercent;
        public float tractionTrimPercent;
        public float weightTrimPercent;
        public float clutchTrimPercent;
        public float centerOfMassYTrim;
        public float centerOfMassZTrim;
        public float skiStanceTrim;
    }

    [Serializable]
    internal class ResolvedStats
    {
        public float horsePower;
        public float powerFactor;
        public float lugHeight;
        public float friction;
        public float weight;
        public float skiStance;
        public float skisXDistanceOffset;
        public bool isTurboOn;
        public string engineText;
        public Vec3Data centerOfMassOffset = new Vec3Data();
        public Vec3Data driverCenterOfMassOffset = new Vec3Data();
        public float boostTargetPsi;
        public float boostLimitPsi;
        public float estimatedBoostPsi;
        public float altitudeCompensationPercent;
        public float estimatedManifoldPressureKpa;
    }

    internal class TunePart
    {
        public string id;
        public string category;
        public string name;
        public string description;
        public bool requiresReload;
        public PartEffect effect = new PartEffect();
    }

    internal class PartEffect
    {
        public float horsePowerMultiplier = 1f;
        public float powerFactorMultiplier = 1f;
        public float lugHeightMultiplier = 1f;
        public float lugHeightTargetMm;
        public float lugHeightOffset;
        public float frictionMultiplier = 1f;
        public float weightMultiplier = 1f;
        public float weightOffset;
        public float skiStanceOffset;
        public float skisXDistanceOffset;
        public Vec3Data centerOfMassDelta = new Vec3Data();
        public Vec3Data driverCenterOfMassDelta = new Vec3Data();
        public bool isTurbo;
        public string engineText;
        public float throttleExponentDelta;
        public float rpmSensitivityMultiplier = 1f;
        public float rpmSensitivityDownMultiplier = 1f;
        public float turboAltitudeCompensation;
        public float boostResponseMultiplier = 1f;
        public float boostTargetPsi;
        public float boostLimitPsi;
        public float clutchRpmMinOffset;
        public float clutchRpmMaxOffset;
        public float minThrottleOnClutchEngagementOffset;
        public float wheelieThresholdOffset;
        public float stabilizerDampingMultiplier = 1f;
        public float trackSpeedDampingMultiplier = 1f;
        public float trackSpeedGyroMultiplier = 1f;
        public bool hasHeadlightColor;
        public Color headlightColor = Color.white;
        public float headlightIntensityMultiplier = 1f;
        public float headlightRangeMultiplier = 1f;
        public float headlightSpotAngleMultiplier = 1f;
        public float headlightPitchOffsetDegrees;
        public string accessoryMode;
    }

    internal class TuneComputation
    {
        public ResolvedStats stats = new ResolvedStats();
        public bool requiresReload;
        public SledDefaults baseDefaults;
        public SledDefaults engineDefaults;
        public SledDefaults audioDefaults;
        public VehicleScriptableObject audioSource;
        public List<TunePart> parts = new List<TunePart>();
        public PartEffect mergedEffect = new PartEffect();
        public EngineSimulationInput simulationInput;
        public EngineSimulationResult simulationResult;
    }

    internal class PowerGainBreakdown
    {
        public float engineHorsepowerGain;
        public float turboHorsepowerGain;
        public float intakeHorsepowerGain;
        public float otherHorsepowerGain;
        public float fineTuneHorsepowerGain;

        public float enginePowerFactorGain;
        public float turboPowerFactorGain;
        public float intakePowerFactorGain;
        public float otherPowerFactorGain;
        public float fineTunePowerFactorGain;

        public float TotalHorsepowerGain
        {
            get
            {
                return engineHorsepowerGain +
                       turboHorsepowerGain +
                       intakeHorsepowerGain +
                       otherHorsepowerGain +
                       fineTuneHorsepowerGain;
            }
        }

        public float TotalPowerFactorGain
        {
            get
            {
                return enginePowerFactorGain +
                       turboPowerFactorGain +
                       intakePowerFactorGain +
                       otherPowerFactorGain +
                       fineTunePowerFactorGain;
            }
        }
    }

    internal class EngineSimulationInput
    {
        public bool altitudeCompensationEnabled;
        public bool hasAltitudeMeters;
        public float altitudeMeters;
        public bool hasThrottle01;
        public float throttle01;
        public bool hasNormalizedRpm;
        public float normalizedRpm;
        public bool hasSpeedMetersPerSecond;
        public float speedMetersPerSecond;
        public bool hasLoad01;
        public float load01;
    }

    internal class EngineSimulationResult
    {
        public PowerGainBreakdown gains = new PowerGainBreakdown();
        public float altitudeMeters;
        public float altitudePressureRatio = 1f;
        public float turboAltitudeCompensation;
        public float effectiveAirRatio = 1f;
        public float loadFactor = 1f;
        public float horsepowerBeforeEnvironment;
        public float horsepowerAfterEnvironment;
        public float powerFactorBeforeEnvironment;
        public float powerFactorAfterEnvironment;
        public float boostTargetPsi;
        public float boostLimitPsi;
        public float estimatedBoostPsi;
        public float estimatedManifoldPressureKpa;
    }

    [Serializable]
    internal class AlpineShareMessage
    {
        public string magic = "ALPINE_TUNE";
        public int schemaVersion = AlpineConstants.SchemaVersion;
        public string type;
        public ulong senderId;
        public ulong senderSteamId;
        public ulong senderSleddersClientId;
        public ulong targetSleddersClientId;
        public string transport;
        public string senderName;
        public string profileId;
        public string checksum;
        public RemoteTuneSummary summary;
        public RemoteActiveTuneState activeState;
        public TuneProfile profile;
    }

    [Serializable]
    internal class RemoteTuneSummary
    {
        public ulong senderId;
        public string senderName;
        public string profileId;
        public string profileName;
        public string targetSledKey;
        public string targetVehicleId;
        public string catalogVersion;
        public string checksum;
        public bool hasPayload;
        public long receivedUnixTime;
    }

    [Serializable]
    internal class RemoteActiveTuneState
    {
        public ulong senderId;
        public string senderName;
        public string profileId;
        public string profileName;
        public string checksum;
        public string targetSledKey;
        public string targetVehicleId;
        public string catalogVersion;
        public bool hasPayload;
        public bool payloadRequested;
        public bool shareSetup;
        public bool shareLighting;
        public bool shareAudio;
        public bool shareVisualEquipment;
        public long lastSeenUnixTime;
        public long lastAppliedUnixTime;
        public string applyStatus;
    }

    [Serializable]
    internal class RemotePeerState
    {
        public ulong senderId;
        public ulong sleddersClientId;
        public ulong steamId;
        public string senderName;
        public bool modDetected;
        public bool sharingEnabled;
        public string activeSetupName;
        public long firstSeenUnixTime;
        public long lastSeenUnixTime;
        public string status;
    }

    internal sealed class AlpineDiscoveredPeer
    {
        public ulong sleddersClientId;
        public ulong steamId;
        public string name;
        public string source;
        public bool hasSteamId;
        public bool hasInternalClientId;
    }

    [Serializable]
    internal class CurrentSetupRecord
    {
        public int schemaVersion = 1;
        public string sledKey;
        public string vehicleId;
        public string displayName;
        public string setupSlotId;
        public string setupSlotName;
        public bool setupEdited;
        public long updatedUnixTime;
        public TuneProfile profile;
    }
}
