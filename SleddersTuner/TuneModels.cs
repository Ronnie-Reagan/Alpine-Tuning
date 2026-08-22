using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineConstants
    {
        public const int SchemaVersion = 3;
        public const string ModVersion = "2026.08.21";
        public const string CatalogVersion = "2026.08.fuel-v1";
        public const string DefaultProfileAuthor = "Alpine Rider";
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
        public string assemblyFileName;
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
        private const int CurrentHeadlightBindingRevision = 2;
        private const string DefaultHeadlightControllerBinding = "JoystickButton9";

        public int schemaVersion = AlpineConstants.SchemaVersion;
        public AlpineDisplayUnits units = AlpineDisplayUnits.Metric;

        // Master runtime switch. The tuning UI and saved profiles remain available
        // while disabled, but Alpine does not mutate sled runtime or VSO data.
        public bool alpineTuningEnabled = true;
        public bool idleFuelConsumptionEnabled = true;
        public bool persistentFuelLevelsEnabled = true;

        public bool headlightToggleEnabled;
        public string headlightKeyboardKey;
        public string headlightControllerButton;
        public bool headlightBindingConfigured;
        public int headlightBindingRevision;

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
            if (!Enum.IsDefined(typeof(AlpineDisplayUnits), units))
                units = AlpineDisplayUnits.Metric;

            if (headlightBindingRevision < CurrentHeadlightBindingRevision)
            {
                bool noBinding = string.IsNullOrWhiteSpace(headlightKeyboardKey) &&
                                 string.IsNullOrWhiteSpace(headlightControllerButton);
                bool oldLeftStickDefault =
                    string.Equals(headlightControllerButton, "JoystickButton7", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(headlightControllerButton, "JoystickButton8", StringComparison.OrdinalIgnoreCase);
                if (noBinding)
                {
                    headlightKeyboardKey = null;
                    headlightControllerButton = DefaultHeadlightControllerBinding;
                    headlightToggleEnabled = true;
                    headlightBindingConfigured = true;
                }
                else if (oldLeftStickDefault)
                {
                    // Migrate only the superseded controller default. A rider may
                    // also have an intentional keyboard binding, which must not be
                    // erased as part of the right-stick default migration.
                    headlightControllerButton = DefaultHeadlightControllerBinding;
                    headlightToggleEnabled = true;
                    headlightBindingConfigured = true;
                }
                headlightBindingRevision = CurrentHeadlightBindingRevision;
            }

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
                   string.Equals(value, "JoystickButton7", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "JoystickButton8", StringComparison.OrdinalIgnoreCase);
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
        public bool hasMaxRpm;
        public float maxRpm;
        public float lugHeight;
        public float friction;
        public float weight;
        public float fuelCapacity;
        public float fuelConsumption;
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
        public NativePhysicsDefaults nativePhysics = new NativePhysicsDefaults();

        public static SledDefaults FromSled(VehicleScriptableObject so, string key)
        {
            return new SledDefaults
            {
                sledKey = key,
                displayName = !string.IsNullOrWhiteSpace(so.displayName) ? so.displayName : so.name,
                vehicleId = AlpineTuningMod.GetVehicleId(so),
                horsePower = so.horsePower,
                powerFactor = so.powerFactor,
                hasMaxRpm = !float.IsNaN(so.maxRpm) && !float.IsInfinity(so.maxRpm) && so.maxRpm > 1000f,
                maxRpm = so.maxRpm,
                lugHeight = so.lugHeight,
                friction = so.coefficientOfFriction,
                weight = so.weight,
                fuelCapacity = Mathf.Max(0.01f, so.fuelCapacity),
                fuelConsumption = Mathf.Max(0f, so.fuelConsumption),
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
        public bool hasStabilizerDamping;
        public Vec3Data stabilizerDamping = new Vec3Data();
        public bool hasTrackSpeedDamping;
        public Vec3Data trackSpeedDamping = new Vec3Data();
        public bool hasTrackSpeedGyroMultiplier;
        public float trackSpeedGyroMultiplier;
    }

    /// <summary>
    /// Optional values captured from a freshly initialized local sled. These are
    /// informational factory references for previews and native-model graphs;
    /// runtime mutation still uses per-component captures so asymmetric or
    /// duplicated game objects can always be restored exactly.
    /// </summary>
    [Serializable]
    internal class NativePhysicsDefaults
    {
        public bool hasPowerEfficiency;
        public float powerEfficiency;
        public bool hasDrivetrainMinSpeed;
        public float drivetrainMinSpeed;
        public bool hasDrivetrainMaxSpeed1;
        public float drivetrainMaxSpeed1;
        public bool hasDrivetrainMaxSpeed2;
        public float drivetrainMaxSpeed2;
        public bool hasTrackMass;
        public float trackMass;
        public bool hasBrakeForce;
        public float brakeForce;

        public bool hasAntiRollBar;
        public float antiRollBar;
        public bool hasTrackRigidityFront;
        public float trackRigidityFront;
        public bool hasTrackRigidityRear;
        public float trackRigidityRear;
        public bool hasFrontSpring;
        public float frontSpring;
        public bool hasFrontDamper;
        public float frontDamper;
        public bool hasFrontCompressionDamping;
        public float frontCompressionDamping;
        public bool hasFrontReboundDamping;
        public float frontReboundDamping;
        public bool hasRearSpring;
        public float rearSpring;
        public bool hasRearDamper;
        public float rearDamper;
        public bool hasRearCompressionDamping;
        public float rearCompressionDamping;
        public bool hasRearReboundDamping;
        public float rearReboundDamping;

        public bool hasSkisMaxAngle;
        public float skisMaxAngle;
        public bool hasToeAngle;
        public float toeAngle;
        public bool hasLeftCamberFactor;
        public float leftCamberFactor;
        public bool hasRightCamberFactor;
        public float rightCamberFactor;
        public bool hasSkiGrip;
        public float skiGrip;
        public bool hasTrackGrip;
        public float trackGrip;
    }

    [Serializable]
    internal class TuneProfile
    {
        public int schemaVersion = AlpineConstants.SchemaVersion;
        public string modVersion = AlpineConstants.ModVersion;
        public string catalogVersion = AlpineConstants.CatalogVersion;
        public string profileId;
        public string name;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool usesAutomaticName;
        public string author;
        public string targetSledKey;
        public string targetVehicleId;
        public string donorSledKey;
        public string donorVehicleId;
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
            if (selectedParts == null)
                return null;

            for (int i = 0; i < selectedParts.Count; i++)
            {
                PartSelection selection = selectedParts[i];
                if (selection != null &&
                    string.Equals(selection.category, category, StringComparison.OrdinalIgnoreCase))
                {
                    return selection.partId;
                }
            }

            return null;
        }

        public void SetPartId(string category, string partId)
        {
            if (selectedParts == null)
                selectedParts = new List<PartSelection>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                PartSelection selection = selectedParts[i];
                if (selection != null &&
                    string.Equals(selection.category, category, StringComparison.OrdinalIgnoreCase))
                {
                    selection.partId = partId;
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
        public bool hasMaxRpm;
        public float maxRpm;
        public float lugHeight;
        public float friction;
        public float weight;
        public float fuelCapacity;
        public float fuelConsumption;
        public float backpackFuelCapacityLiters;
        public float backpackPayloadMassKg;
        public bool requiresCosmeticBackpack;
        public float skiStance;
        public float skisXDistanceOffset;
        public bool isTurboOn;
        public string engineText;
        public Vec3Data centerOfMassOffset = new Vec3Data();
        public Vec3Data driverCenterOfMassOffset = new Vec3Data();
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
        public float lugHeightMultiplier = 1f;
        public float lugHeightTargetMm;
        public float lugHeightOffset;
        public float frictionMultiplier = 1f;
        public float weightMultiplier = 1f;
        public float weightOffset;
        public float fuelCapacityMultiplier = 1f;
        public float tankHardwareMassOffsetKg;
        public float backpackFuelCapacityLiters;
        public float backpackContainerMassKg;
        public bool requiresCosmeticBackpack;
        public float skiStanceOffset;
        public float skisXDistanceOffset;
        public Vec3Data centerOfMassDelta = new Vec3Data();
        public Vec3Data driverCenterOfMassDelta = new Vec3Data();
        public bool isTurbo;
        public string engineText;
        public float throttleExponentDelta;
        public float rpmSensitivityMultiplier = 1f;
        public float rpmSensitivityDownMultiplier = 1f;
        public float turboRpmResponseMultiplier = 1f;
        public float clutchRpmMinOffset;
        public float clutchRpmMaxOffset;
        public float minThrottleOnClutchEngagementOffset;
        public float stabilizerDampingMultiplier = 1f;
        public float trackSpeedDampingMultiplier = 1f;
        public float trackSpeedGyroMultiplier = 1f;
        public float nativePowerEfficiencyMultiplier = 1f;
        public float nativeDrivetrainSpeedMultiplier = 1f;
        public float nativeTrackMassMultiplier = 1f;
        public float nativeAntiRollBarMultiplier = 1f;
        public float nativeTrackRigidityFrontMultiplier = 1f;
        public float nativeTrackRigidityRearMultiplier = 1f;
        public float nativeFrontSpringMultiplier = 1f;
        public float nativeFrontDamperMultiplier = 1f;
        public float nativeFrontCompressionDampingMultiplier = 1f;
        public float nativeFrontReboundDampingMultiplier = 1f;
        public float nativeRearSpringMultiplier = 1f;
        public float nativeRearDamperMultiplier = 1f;
        public float nativeRearCompressionDampingMultiplier = 1f;
        public float nativeRearReboundDampingMultiplier = 1f;
        public float nativeBrakeForceMultiplier = 1f;
        public float nativeSkisMaxAngleMultiplier = 1f;
        public float nativeToeAngleMultiplier = 1f;
        public float nativeCamberFactorMultiplier = 1f;
        public float nativeSkiGripMultiplier = 1f;
        public float nativeTrackGripMultiplier = 1f;
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
        public string unavailableReason;
        public bool requiresReload;
        public SledDefaults baseDefaults;
        public SledDefaults engineDefaults;
        public SledDefaults audioDefaults;
        public VehicleScriptableObject audioSource;
        public List<TunePart> parts = new List<TunePart>();
        public PartEffect mergedEffect = new PartEffect();
    }

    internal class PowerGainBreakdown
    {
        public float engineHorsepowerGain;
        public float turboHorsepowerGain;
        public float intakeHorsepowerGain;
        public float otherHorsepowerGain;
        public float fineTuneHorsepowerGain;

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
