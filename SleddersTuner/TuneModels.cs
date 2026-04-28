using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineConstants
    {
        public const int SchemaVersion = 1;
        public const string ModVersion = "2026.04.28";
        public const string CatalogVersion = "2026.04.v1";
        public const int SteamP2PChannel = 7264;
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
        public bool requiresReload;
        public long createdUnixTime;
        public long updatedUnixTime;
        public string checksum;

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
        public float clutchRpmMinOffset;
        public float clutchRpmMaxOffset;
        public float minThrottleOnClutchEngagementOffset;
        public float wheelieThresholdOffset;
        public float stabilizerDampingMultiplier = 1f;
        public float trackSpeedDampingMultiplier = 1f;
        public float trackSpeedGyroMultiplier = 1f;
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
    }

    [Serializable]
    internal class AlpineShareMessage
    {
        public string magic = "ALPINE_TUNE";
        public int schemaVersion = AlpineConstants.SchemaVersion;
        public string type;
        public ulong senderId;
        public string senderName;
        public string profileId;
        public string checksum;
        public RemoteTuneSummary summary;
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
}
