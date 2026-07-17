using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AlpineTuning.ReleaseTests
{
    internal static class TuneStoreRegression
    {
        public static void Run(string repoRoot, string testRoot)
        {
            Program.Require(!string.IsNullOrWhiteSpace(testRoot), "tune-test-root");
            Directory.CreateDirectory(testRoot);

            TestRoundTripAndSafety(repoRoot, Path.Combine(testRoot, "roundtrip"));
            TestCorruptedPrimaryRecovery(repoRoot, Path.Combine(testRoot, "corrupt-primary"));
            TestTamperedPrimaryRecovery(repoRoot, Path.Combine(testRoot, "tampered-primary"));
            TestLegacyCatalogNormalization(repoRoot, Path.Combine(testRoot, "legacy-catalog"));
            TestSelectionNormalization(repoRoot, Path.Combine(testRoot, "selection-normalization"));
            TestCurrentSetupIdentityNormalization(repoRoot, Path.Combine(testRoot, "current-identity"));
            TestCurrentSetupCollisionResolution(repoRoot, Path.Combine(testRoot, "current-collisions"));
            TestProfileWriteOutcome(repoRoot, Path.Combine(testRoot, "profile-write-outcome"));
            TestDefaultsAndSettingsBackupRecovery(Path.Combine(testRoot, "store-backups"));
            TestHeadlightBindingMigrationMatrix();
        }

        private static void TestRoundTripAndSafety(string repoRoot, string root)
        {
            var expectedModifiedParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (TuneStore.UseTestStorageRoot(root))
            {
                var catalog = new PartCatalog();
                var store = new TuneStore(catalog);
                store.Initialize();

                TuneProfile stock = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
                Program.Require(store.SaveProfile(stock, false), "tune-stock-save");
                TuneProfile savedStock = store.GetProfile(stock.profileId);
                Program.Require(savedStock != null, "tune-stock-load");
                Program.Require(savedStock.name == "Stock Setup" && savedStock.usesAutomaticName, "tune-automatic-name");
                Program.Require(savedStock.author == AlpineConstants.DefaultProfileAuthor, "tune-author-sanitized");
                Program.Require(TuneStore.ChecksumMatches(savedStock), "tune-stock-checksum");
                Program.Require(store.BuildProfilePartSummary(savedStock) == "Stock", "tune-stock-summary");

                TuneProfile donorSummary = TuneStore.Clone(savedStock);
                donorSummary.donorSledKey = "fixture_donor";
                donorSummary.donorVehicleId = "4001";
                Program.Require(
                    store.BuildProfilePartSummary(donorSummary).Contains("Unavailable Engine"),
                    "tune-unavailable-donor-summary");
                store.PutDefaults(new SledDefaults
                {
                    sledKey = donorSummary.donorSledKey,
                    vehicleId = donorSummary.donorVehicleId,
                    displayName = "Synthetic Donor",
                    horsePower = 165f,
                    powerFactor = 1f,
                    lugHeight = 50f,
                    friction = 1f,
                    weight = 250f,
                    skiStance = 939.8f,
                    skisXDistanceOffset = 0f
                });
                Program.Require(
                    store.BuildProfilePartSummary(donorSummary).Contains("Engine Swap"),
                    "tune-available-donor-summary");

                TuneProfile modified = CopyAndLoadFixture(repoRoot, root, "tune-modified.json");
                foreach (PartSelection selection in modified.selectedParts)
                    expectedModifiedParts[selection.category] = selection.partId;
                Program.Require(store.SaveProfile(modified, false), "tune-modified-save");
                TuneProfile savedModified = store.GetProfile(modified.profileId);
                Program.Require(savedModified != null, "tune-modified-load");
                Program.Require(savedModified.name == "Summit Deep Snow" && !savedModified.usesAutomaticName, "tune-manual-name");
                Program.Require(store.CountModifiedParts(savedModified) >= 18, "tune-modified-summary-count");
                Program.Require(store.BuildProfilePartSummary(savedModified, 3).Length > 0, "tune-modified-summary");

                Program.Require(store.SetCurrentSetup(
                    TuneStore.Clone(savedModified),
                    savedModified.targetSledKey,
                    savedModified.targetVehicleId,
                    "Synthetic Sled",
                    savedModified.profileId,
                    savedModified.name,
                    true,
                    true), "tune-dirty-current-save");

                TuneProfile overwritten = TuneStore.Clone(savedModified);
                overwritten.fineTune.powerTrimPercent = 5f;
                overwritten.name = "Summit Deep Snow Revised";
                Program.Require(store.SaveProfile(overwritten, false), "tune-overwrite");
                List<TuneHistoryEntry> history = store.GetProfileHistoryForSled(
                    overwritten.targetSledKey,
                    overwritten.targetVehicleId,
                    20);
                Program.Require(history.Any(entry => entry.sourceProfileId == overwritten.profileId), "tune-overwrite-history");

                TuneProfile crossSled = TuneStore.Clone(overwritten);
                crossSled.targetSledKey = "fixture_sled_beta";
                crossSled.targetVehicleId = "1002";
                Program.Require(!store.SaveProfile(crossSled, false), "tune-cross-sled-overwrite");
                Program.Require(store.GetProfile(overwritten.profileId).targetVehicleId == "1001", "tune-cross-sled-original");

                TuneProfile removable = TuneStore.Clone(savedStock);
                removable.profileId = "fixture-removed";
                removable.name = "Removable Trail Setup";
                removable.usesAutomaticName = false;
                removable.checksum = null;
                Program.Require(store.SaveProfile(removable, false), "tune-removable-save");
                Program.Require(store.DeleteProfile(removable.profileId), "tune-removal");
                Program.Require(store.GetProfile(removable.profileId) == null, "tune-removal-live-state");
                Program.Require(store.GetArchivedProfilesForSled("fixture_sled_alpha", "1001")
                    .Any(profile => profile.profileId == removable.profileId), "tune-removal-archive");
                Program.Require(store.RestoreLatestArchivedProfile(removable.profileId, out TuneProfile restored), "tune-removal-restore");
                Program.Require(restored != null && store.GetProfile(restored.profileId) != null, "tune-removal-restored-live");
            }

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();

                TuneProfile reloadedStock = store.GetProfile("fixture-stock");
                TuneProfile reloadedModified = store.GetProfile("fixture-modified");
                Program.Require(reloadedStock != null && reloadedModified != null, "tune-restart-load");
                Program.Require(reloadedStock.name == "Stock Setup", "tune-restart-auto-name");
                Program.Require(reloadedModified.name == "Summit Deep Snow Revised", "tune-restart-manual-name");
                Program.Require(TuneStore.ChecksumMatches(reloadedModified), "tune-restart-checksum");

                foreach (KeyValuePair<string, string> expected in expectedModifiedParts)
                    Program.Require(reloadedModified.GetPartId(expected.Key) == expected.Value, "tune-selection-loss");

                TuneProfile dirtyDraft = store.GetCurrentSetupForSled("fixture_sled_alpha", "1001");
                Program.Require(dirtyDraft != null && dirtyDraft.setupEdited, "tune-dirty-load-roundtrip");
                Program.Require(dirtyDraft.setupSlotId == "fixture-modified", "tune-dirty-slot-roundtrip");
                Program.Require(store.GetProfileHistoryForSled("fixture_sled_alpha", "1001", 20).Count > 0, "tune-history-restart");
            }
        }

        private static void TestCorruptedPrimaryRecovery(string repoRoot, string root)
        {
            const string profileId = "fixture-corrupt";
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile profile = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
                profile.profileId = profileId;
                profile.name = "Recovery Baseline";
                profile.usesAutomaticName = false;
                Program.Require(store.SaveProfile(profile, false), "tune-recovery-first-save");

                TuneProfile revised = TuneStore.Clone(profile);
                revised.SetPartId(PartCatalog.Track, "track.trail");
                revised.name = "Recovery Revised";
                Program.Require(store.SaveProfile(revised, false), "tune-recovery-second-save");
            }

            string primary = Path.Combine(root, "Profiles", profileId + ".json");
            Program.Require(File.Exists(primary) && File.Exists(primary + ".bak"), "tune-recovery-backup-created");
            string validBackup = File.ReadAllText(primary + ".bak");
            File.WriteAllText(primary, "{ deliberately corrupted synthetic primary");

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile recovered = store.GetProfile(profileId);
                Program.Require(recovered != null, "tune-recovery-loaded");
                Program.Require(TuneStore.ChecksumMatches(recovered), "tune-recovery-checksum");
                Program.Require(recovered.GetPartId(PartCatalog.Track) == "track.stock", "tune-recovery-backup-content");
                Program.Require(Directory.GetFiles(Path.Combine(root, "Recovery"), "*.json", SearchOption.AllDirectories).Length > 0, "tune-recovery-snapshot");
                Program.Require(File.ReadAllText(primary + ".bak") == validBackup, "tune-recovery-backup-preserved");
                Program.Require(JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(primary)) != null, "tune-recovery-primary-restored");
            }
        }

        private static void TestLegacyCatalogNormalization(string repoRoot, string root)
        {
            string profiles = Path.Combine(root, "Profiles");
            Directory.CreateDirectory(profiles);
            string copiedFixture = CopyFixture(repoRoot, root, "tune-legacy.json");
            File.Copy(copiedFixture, Path.Combine(profiles, "fixture-legacy.json"), true);

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile migrated = store.GetProfile("fixture-legacy");
                Program.Require(migrated != null, "tune-legacy-load");
                Program.Require(migrated.schemaVersion == AlpineConstants.SchemaVersion, "tune-legacy-schema");
                Program.Require(migrated.catalogVersion == AlpineConstants.CatalogVersion, "tune-legacy-catalog");
                Program.Require(migrated.targetVehicleId == null, "tune-legacy-identity-preserved");
                Program.Require(store.GetProfilesForSled("fixture_sled_legacy", "2001").Any(profile => profile.profileId == migrated.profileId), "tune-legacy-key-fallback");
                Program.Require(migrated.GetPartId(PartCatalog.EngineCore) == "engine.stage1", "tune-legacy-engine-preserved");
                Program.Require(migrated.GetPartId(PartCatalog.Track) == "track.trail", "tune-legacy-track-preserved");
                Program.Require(migrated.GetPartId(PartCatalog.Skis) == "skis.wide", "tune-legacy-skis-preserved");
                Program.Require(migrated.GetPartId(PartCatalog.BrakeCalibration) == "brake.stock", "tune-new-brake-normalized");
                Program.Require(migrated.GetPartId(PartCatalog.SteeringGeometry) == "geometry.stock", "tune-new-geometry-normalized");
                Program.Require(PartCatalog.OrderedCategories.All(category => !string.IsNullOrWhiteSpace(migrated.GetPartId(category))), "tune-catalog-selection-completeness");
                Program.Require(TuneStore.ChecksumMatches(migrated), "tune-legacy-checksum");
                Program.Require(Directory.GetFiles(Path.Combine(root, "Recovery"), "*.json", SearchOption.AllDirectories).Length > 0, "tune-pre-migration-recovery");
            }
        }

        private static void TestTamperedPrimaryRecovery(string repoRoot, string root)
        {
            const string profileId = "fixture-tampered";
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile profile = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
                profile.profileId = profileId;
                profile.name = "Integrity Baseline";
                profile.usesAutomaticName = false;
                Program.Require(store.SaveProfile(profile, false), "tune-tamper-first-save");
                Program.Require(store.SaveProfile(TuneStore.Clone(profile), false), "tune-tamper-second-save");
            }

            string primaryPath = Path.Combine(root, "Profiles", profileId + ".json");
            string backupPath = primaryPath + ".bak";
            Program.Require(File.Exists(primaryPath) && File.Exists(backupPath), "tune-tamper-backup-created");
            string validBackup = File.ReadAllText(backupPath);
            TuneProfile tampered = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(primaryPath));
            tampered.SetPartId(PartCatalog.Track, "track.trail");
            tampered.author = "Private Synthetic Rider";
            // Deliberately retain the old checksum; privacy sanitization must not
            // turn this unrelated content change into a trusted primary.
            File.WriteAllText(primaryPath, JsonConvert.SerializeObject(tampered, Formatting.Indented));

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile loaded = store.GetProfile(profileId);
                Program.Require(loaded != null, "tune-tamper-backup-loaded");
                Program.Require(loaded.GetPartId(PartCatalog.Track) == "track.stock", "tune-tamper-primary-not-blessed");
                Program.Require(TuneStore.ChecksumMatches(loaded), "tune-tamper-restored-checksum");
            }

            Program.Require(File.ReadAllText(backupPath) == validBackup, "tune-tamper-valid-backup-preserved");
            TuneProfile restoredPrimary = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(primaryPath));
            Program.Require(restoredPrimary.GetPartId(PartCatalog.Track) == "track.stock", "tune-tamper-primary-restored-from-backup");
            string[] recoveryFiles = Directory.GetFiles(
                Path.Combine(root, "Recovery"),
                "*.json",
                SearchOption.AllDirectories);
            Program.Require(recoveryFiles.Length > 0, "tune-tamper-recovery-created");
            Program.Require(
                recoveryFiles.Select(File.ReadAllText).Any(json =>
                {
                    TuneProfile recovered = JsonConvert.DeserializeObject<TuneProfile>(json);
                    return recovered != null &&
                           recovered.GetPartId(PartCatalog.Track) == "track.trail" &&
                           recovered.author == AlpineConstants.DefaultProfileAuthor;
                }),
                "tune-tamper-sanitized-rejected-primary-preserved");
        }

        private static void TestSelectionNormalization(string repoRoot, string root)
        {
            var catalog = new PartCatalog();
            var nullSafe = new TuneProfile { selectedParts = null };
            Program.Require(nullSafe.GetPartId(PartCatalog.EngineCore) == null, "tune-null-selection-read");
            nullSafe.SetPartId(PartCatalog.EngineCore, "engine.stage1");
            Program.Require(nullSafe.GetPartId(PartCatalog.EngineCore) == "engine.stage1", "tune-null-selection-write");

            TuneProfile normalized = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
            normalized.selectedParts = new List<PartSelection>
            {
                null,
                new PartSelection { category = PartCatalog.EngineCore, partId = "track.trail" },
                new PartSelection { category = PartCatalog.EngineCore, partId = "engine.stage1" },
                new PartSelection { category = PartCatalog.EngineCore, partId = "engine.stage2" },
                new PartSelection { category = PartCatalog.Track, partId = "track.trail" },
                new PartSelection { category = PartCatalog.Track, partId = "engine.stage2" }
            };
            catalog.EnsureProfileSelections(normalized);
            Program.Require(normalized.selectedParts.Count == PartCatalog.OrderedCategories.Length, "tune-normalized-selection-count");
            Program.Require(
                normalized.selectedParts.Select(selection => selection.category)
                    .SequenceEqual(PartCatalog.OrderedCategories, StringComparer.OrdinalIgnoreCase),
                "tune-normalized-selection-order");
            Program.Require(normalized.GetPartId(PartCatalog.EngineCore) == "engine.stage1", "tune-normalized-first-valid-selection");
            Program.Require(normalized.GetPartId(PartCatalog.Track) == "track.trail", "tune-normalized-category-selection");
            Program.Require(
                normalized.selectedParts.All(selection =>
                {
                    TunePart part = catalog.Find(selection.partId);
                    return part != null && string.Equals(part.category, selection.category, StringComparison.OrdinalIgnoreCase);
                }),
                "tune-normalized-cross-category-removed");

            TuneProfile duplicate = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
            duplicate.selectedParts.Add(new PartSelection
            {
                category = PartCatalog.EngineCore,
                partId = "engine.stage2"
            });
            duplicate.checksum = null;
            Program.Require(
                !TuneStore.TryValidateProfileForCatalog(duplicate, catalog, true, false, out string duplicateReason) &&
                duplicateReason.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0,
                "tune-strict-duplicate-rejected");

            TuneProfile crossCategory = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
            PartSelection engine = crossCategory.selectedParts.First(
                selection => string.Equals(selection.category, PartCatalog.EngineCore, StringComparison.OrdinalIgnoreCase));
            engine.partId = "track.trail";
            crossCategory.checksum = null;
            Program.Require(
                !TuneStore.TryValidateProfileForCatalog(crossCategory, catalog, true, false, out string categoryReason) &&
                categoryReason.IndexOf("does not belong", StringComparison.OrdinalIgnoreCase) >= 0,
                "tune-strict-cross-category-rejected");

            string profiles = Path.Combine(root, "Profiles");
            Directory.CreateDirectory(profiles);
            TuneProfile localRepair = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
            localRepair.profileId = "fixture-selection-repair";
            localRepair.author = AlpineConstants.DefaultProfileAuthor;
            localRepair.selectedParts = new List<PartSelection>
            {
                null,
                new PartSelection { category = "obsoleteCategory", partId = "track.trail" },
                new PartSelection { category = PartCatalog.EngineCore, partId = "track.trail" },
                new PartSelection { category = PartCatalog.EngineCore, partId = "engine.stage1" },
                new PartSelection { category = PartCatalog.EngineCore, partId = "engine.stage2" },
                new PartSelection { category = PartCatalog.Track, partId = "track.trail" }
            };
            localRepair.checksum = null;
            localRepair.checksum = TuneStore.ComputeChecksum(localRepair);
            File.WriteAllText(
                Path.Combine(profiles, localRepair.profileId + ".json"),
                JsonConvert.SerializeObject(localRepair, Formatting.Indented));
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(catalog);
                store.Initialize();
                TuneProfile repaired = store.GetProfile(localRepair.profileId);
                Program.Require(repaired != null, "tune-local-malformed-selection-repaired");
                Program.Require(repaired.GetPartId(PartCatalog.EngineCore) == "engine.stage1", "tune-local-first-valid-selection-preserved");
                Program.Require(repaired.GetPartId(PartCatalog.Track) == "track.trail", "tune-local-valid-track-preserved");
                Program.Require(repaired.selectedParts.Count == PartCatalog.OrderedCategories.Length, "tune-local-selection-count-repaired");
                Program.Require(TuneStore.ChecksumMatches(repaired), "tune-local-selection-repair-checksum");
            }
            Program.Require(
                Directory.GetFiles(Path.Combine(root, "Recovery"), "*.json", SearchOption.AllDirectories).Length > 0,
                "tune-local-selection-pre-repair-recovery");
        }

        private static void TestCurrentSetupIdentityNormalization(string repoRoot, string root)
        {
            string currentSetups = Path.Combine(root, "CurrentSetups");
            Directory.CreateDirectory(currentSetups);
            TuneProfile profile = CopyAndLoadFixture(repoRoot, root, "tune-modified.json");
            profile.author = AlpineConstants.DefaultProfileAuthor;
            profile.checksum = null;
            var record = new CurrentSetupRecord
            {
                sledKey = "fixture_sled_wrong",
                vehicleId = "9999",
                displayName = "Synthetic Wrapper",
                setupSlotId = profile.profileId,
                setupSlotName = profile.name,
                setupEdited = true,
                updatedUnixTime = 1700000000L,
                profile = profile
            };
            string mismatchedPath = Path.Combine(currentSetups, "9999.json");
            string serialized = JsonConvert.SerializeObject(record, Formatting.Indented);
            File.WriteAllText(mismatchedPath, serialized);
            File.WriteAllText(mismatchedPath + ".bak", serialized);

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile authoritative = store.GetCurrentSetupForSled(
                    profile.targetSledKey,
                    profile.targetVehicleId);
                Program.Require(authoritative != null, "tune-current-authoritative-identity-load");
                Program.Require(authoritative.targetSledKey == profile.targetSledKey, "tune-current-profile-key-preserved");
                Program.Require(authoritative.targetVehicleId == profile.targetVehicleId, "tune-current-profile-id-preserved");
                Program.Require(
                    store.GetCurrentSetupForSled("fixture_sled_wrong", "9999") == null,
                    "tune-current-wrapper-identity-not-indexed");
            }

            string canonicalPath = Path.Combine(currentSetups, profile.targetVehicleId + ".json");
            Program.Require(File.Exists(canonicalPath), "tune-current-canonical-record-written");
            Program.Require(!File.Exists(mismatchedPath), "tune-current-mismatched-primary-removed");
            Program.Require(!File.Exists(mismatchedPath + ".bak"), "tune-current-mismatched-backup-removed");
            CurrentSetupRecord canonical = JsonConvert.DeserializeObject<CurrentSetupRecord>(File.ReadAllText(canonicalPath));
            Program.Require(canonical != null && canonical.profile != null, "tune-current-canonical-record-readable");
            Program.Require(canonical.sledKey == profile.targetSledKey, "tune-current-wrapper-key-normalized");
            Program.Require(canonical.vehicleId == profile.targetVehicleId, "tune-current-wrapper-id-normalized");
        }

        private static void TestCurrentSetupCollisionResolution(string repoRoot, string root)
        {
            RunCurrentSetupCollisionCase(
                repoRoot,
                Path.Combine(root, "canonical-newer"),
                3000L,
                2000L,
                false,
                false,
                "tune-current-collision-canonical-newer");
            RunCurrentSetupCollisionCase(
                repoRoot,
                Path.Combine(root, "mismatch-newer"),
                2000L,
                3000L,
                true,
                false,
                "tune-current-collision-mismatch-newer");
            RunCurrentSetupCollisionCase(
                repoRoot,
                Path.Combine(root, "equal-time"),
                3000L,
                3000L,
                false,
                false,
                "tune-current-collision-canonical-tie");
            RunCurrentSetupCollisionCase(
                repoRoot,
                Path.Combine(root, "corrupt-canonical"),
                2000L,
                3000L,
                true,
                true,
                "tune-current-collision-corrupt-canonical");

            string failureRoot = Path.Combine(root, "write-failure");
            string currentSetups = Path.Combine(failureRoot, "CurrentSetups");
            Directory.CreateDirectory(currentSetups);
            CurrentSetupRecord failureRecord = CreateCurrentSetupRecord(
                repoRoot,
                failureRoot,
                "failure-winner",
                "track.trail",
                4000L,
                "fixture_sled_wrong",
                "9999");
            string stalePath = Path.Combine(currentSetups, "9999.json");
            File.WriteAllText(stalePath, JsonConvert.SerializeObject(failureRecord, Formatting.Indented));
            Directory.CreateDirectory(Path.Combine(currentSetups, "1001.json"));

            using (TuneStore.UseTestStorageRoot(failureRoot))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                Program.Require(
                    store.GetCurrentSetupForSled("fixture_sled_alpha", "1001") == null,
                    "tune-current-write-failure-not-indexed");
            }
            Program.Require(File.Exists(stalePath), "tune-current-write-failure-source-preserved");
        }

        private static void RunCurrentSetupCollisionCase(
            string repoRoot,
            string root,
            long canonicalProfileTime,
            long mismatchedProfileTime,
            bool expectMismatched,
            bool corruptCanonical,
            string assertionPrefix)
        {
            string currentSetups = Path.Combine(root, "CurrentSetups");
            Directory.CreateDirectory(currentSetups);
            CurrentSetupRecord canonical = CreateCurrentSetupRecord(
                repoRoot,
                root,
                "collision-canonical",
                "track.stock",
                canonicalProfileTime,
                "fixture_sled_alpha",
                "1001");
            CurrentSetupRecord mismatched = CreateCurrentSetupRecord(
                repoRoot,
                root,
                "collision-mismatched",
                "track.trail",
                mismatchedProfileTime,
                "fixture_sled_wrong",
                "9999");

            string canonicalPath = Path.Combine(currentSetups, "1001.json");
            string mismatchedPath = Path.Combine(currentSetups, "9999.json");
            File.WriteAllText(
                canonicalPath,
                corruptCanonical
                    ? "{ corrupt synthetic canonical current setup"
                    : JsonConvert.SerializeObject(canonical, Formatting.Indented));
            File.WriteAllText(mismatchedPath, JsonConvert.SerializeObject(mismatched, Formatting.Indented));

            string expectedTrack = expectMismatched ? "track.trail" : "track.stock";
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                TuneProfile selected = store.GetCurrentSetupForSled("fixture_sled_alpha", "1001");
                Program.Require(selected != null, assertionPrefix + "-loaded");
                Program.Require(selected.GetPartId(PartCatalog.Track) == expectedTrack, assertionPrefix + "-winner");
                Program.Require(
                    store.GetCurrentSetupForSled("fixture_sled_wrong", "9999") == null,
                    assertionPrefix + "-stale-identity-not-indexed");
            }

            Program.Require(!File.Exists(mismatchedPath), assertionPrefix + "-stale-file-removed");
            Program.Require(JsonConvert.DeserializeObject<CurrentSetupRecord>(File.ReadAllText(canonicalPath)) != null, assertionPrefix + "-canonical-readable");
            if (!corruptCanonical)
            {
                Program.Require(
                    Directory.GetFiles(Path.Combine(root, "Recovery"), "*.json", SearchOption.AllDirectories).Length > 0,
                    assertionPrefix + "-loser-recovered");
            }

            using (TuneStore.UseTestStorageRoot(root))
            {
                var restarted = new TuneStore(new PartCatalog());
                restarted.Initialize();
                TuneProfile selected = restarted.GetCurrentSetupForSled("fixture_sled_alpha", "1001");
                Program.Require(selected != null, assertionPrefix + "-restart-loaded");
                Program.Require(selected.GetPartId(PartCatalog.Track) == expectedTrack, assertionPrefix + "-restart-winner");
            }
        }

        private static CurrentSetupRecord CreateCurrentSetupRecord(
            string repoRoot,
            string root,
            string profileId,
            string trackPartId,
            long profileUpdatedTime,
            string wrapperSledKey,
            string wrapperVehicleId)
        {
            TuneProfile profile = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
            profile.profileId = profileId;
            profile.name = profileId;
            profile.author = AlpineConstants.DefaultProfileAuthor;
            profile.updatedUnixTime = profileUpdatedTime;
            profile.SetPartId(PartCatalog.Track, trackPartId);
            new PartCatalog().EnsureProfileSelections(profile);
            profile.checksum = null;
            profile.checksum = TuneStore.ComputeChecksum(profile);
            return new CurrentSetupRecord
            {
                sledKey = wrapperSledKey,
                vehicleId = wrapperVehicleId,
                setupSlotId = profile.profileId,
                setupSlotName = profile.name,
                setupEdited = false,
                // Deliberately opposite/untrusted wrapper time: winner selection
                // must use the checksummed profile time instead.
                updatedUnixTime = long.MaxValue - profileUpdatedTime,
                profile = profile
            };
        }

        private static void TestDefaultsAndSettingsBackupRecovery(string root)
        {
            const string defaultVehicleId = "3001";
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();

                var defaults = new SledDefaults
                {
                    sledKey = "fixture_defaults",
                    vehicleId = defaultVehicleId,
                    displayName = "Synthetic Defaults",
                    horsePower = 150f,
                    powerFactor = 1f,
                    lugHeight = 50f,
                    friction = 1f,
                    weight = 250f,
                    skiStance = 939.8f,
                    skisXDistanceOffset = 0f
                };
                store.PutDefaults(defaults);
                defaults.horsePower = 155f;
                store.PutDefaults(defaults);

                AlpineUserSettings settings = store.Settings;
                settings.headlightKeyboardKey = "F7";
                settings.headlightControllerButton = "JoystickButton4";
                settings.headlightToggleEnabled = true;
                settings.headlightBindingConfigured = true;
                settings.headlightBindingRevision = 2;
                settings.units = AlpineDisplayUnits.Metric;
                Program.Require(store.SaveSettings(), "tune-settings-custom-first-save");
                settings.units = AlpineDisplayUnits.Imperial;
                Program.Require(store.SaveSettings(), "tune-settings-custom-second-save");
            }

            string defaultsPath = Path.Combine(root, "Defaults", defaultVehicleId + ".json");
            string settingsPath = Path.Combine(root, "user-settings.json");
            Program.Require(File.Exists(defaultsPath + ".bak"), "tune-defaults-backup-created");
            Program.Require(File.Exists(settingsPath + ".bak"), "tune-settings-backup-created");
            string defaultsBackup = File.ReadAllText(defaultsPath + ".bak");
            string settingsBackup = File.ReadAllText(settingsPath + ".bak");
            File.WriteAllText(defaultsPath, "{ corrupt synthetic defaults primary");
            File.WriteAllText(settingsPath, "{ corrupt synthetic settings primary");

            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                SledDefaults defaults = store.GetDefaults("fixture_defaults", defaultVehicleId);
                Program.Require(defaults != null && Math.Abs(defaults.horsePower - 150f) < 0.001f, "tune-defaults-backup-recovered");
                Program.Require(store.Settings.headlightKeyboardKey == "F7", "tune-settings-keyboard-backup-recovered");
                Program.Require(store.Settings.headlightControllerButton == "JoystickButton4", "tune-settings-controller-backup-recovered");
                Program.Require(store.Settings.headlightToggleEnabled, "tune-settings-enabled-backup-recovered");
            }

            Program.Require(File.ReadAllText(defaultsPath + ".bak") == defaultsBackup, "tune-defaults-valid-backup-preserved");
            Program.Require(File.ReadAllText(settingsPath + ".bak") == settingsBackup, "tune-settings-valid-backup-preserved");
            Program.Require(JsonConvert.DeserializeObject<SledDefaults>(File.ReadAllText(defaultsPath)) != null, "tune-defaults-primary-restored");
            Program.Require(JsonConvert.DeserializeObject<AlpineUserSettings>(File.ReadAllText(settingsPath)) != null, "tune-settings-primary-restored");
        }

        private static void TestProfileWriteOutcome(string repoRoot, string root)
        {
            using (TuneStore.UseTestStorageRoot(root))
            {
                var store = new TuneStore(new PartCatalog());
                store.Initialize();
                Directory.CreateDirectory(Path.Combine(root, "active-profiles.json"));
                TuneProfile profile = CopyAndLoadFixture(repoRoot, root, "tune-stock.json");
                profile.profileId = "fixture-written-not-active";
                profile.checksum = null;

                bool overall = store.SaveProfile(
                    profile,
                    true,
                    out bool profileWritten,
                    out bool madeActive);
                Program.Require(!overall, "tune-save-outcome-overall-failure");
                Program.Require(profileWritten, "tune-save-outcome-profile-written");
                Program.Require(!madeActive, "tune-save-outcome-not-active");
                Program.Require(store.GetProfile(profile.profileId) != null, "tune-save-outcome-profile-indexed");
                Program.Require(
                    File.Exists(Path.Combine(root, "Profiles", profile.profileId + ".json")),
                    "tune-save-outcome-profile-durable");
            }
        }

        private static void TestHeadlightBindingMigrationMatrix()
        {
            var emptyLegacy = new AlpineUserSettings { headlightBindingRevision = 0 };
            emptyLegacy.Normalize();
            Program.Require(emptyLegacy.headlightKeyboardKey == null, "tune-binding-empty-keyboard-remains-empty");
            Program.Require(emptyLegacy.headlightControllerButton == "JoystickButton9", "tune-binding-empty-controller-migrated");
            Program.Require(emptyLegacy.headlightToggleEnabled && emptyLegacy.headlightBindingConfigured, "tune-binding-empty-default-enabled");
            Program.Require(emptyLegacy.headlightBindingRevision == 2, "tune-binding-empty-revision");

            var legacyControllerWithKeyboard = new AlpineUserSettings
            {
                headlightKeyboardKey = "F7",
                headlightControllerButton = "JoystickButton7",
                headlightBindingRevision = 1
            };
            legacyControllerWithKeyboard.Normalize();
            Program.Require(legacyControllerWithKeyboard.headlightKeyboardKey == "F7", "tune-binding-custom-keyboard-preserved");
            Program.Require(legacyControllerWithKeyboard.headlightControllerButton == "JoystickButton9", "tune-binding-legacy-controller-replaced");

            var customLegacyRevision = new AlpineUserSettings
            {
                headlightKeyboardKey = "K",
                headlightControllerButton = "JoystickButton4",
                headlightBindingConfigured = true,
                headlightToggleEnabled = false,
                headlightBindingRevision = 1
            };
            customLegacyRevision.Normalize();
            Program.Require(customLegacyRevision.headlightKeyboardKey == "K", "tune-binding-nonlegacy-keyboard-unchanged");
            Program.Require(customLegacyRevision.headlightControllerButton == "JoystickButton4", "tune-binding-nonlegacy-controller-unchanged");
            Program.Require(!customLegacyRevision.headlightToggleEnabled, "tune-binding-nonlegacy-enabled-unchanged");

            var currentRevision = new AlpineUserSettings
            {
                headlightKeyboardKey = "P",
                headlightControllerButton = "JoystickButton8",
                headlightBindingConfigured = true,
                headlightToggleEnabled = true,
                headlightBindingRevision = 2
            };
            currentRevision.Normalize();
            Program.Require(currentRevision.headlightKeyboardKey == "P", "tune-binding-current-keyboard-unchanged");
            Program.Require(currentRevision.headlightControllerButton == "JoystickButton8", "tune-binding-current-controller-unchanged");
            Program.Require(currentRevision.headlightBindingRevision == 2, "tune-binding-current-revision-unchanged");

            var invalidUnits = new AlpineUserSettings
            {
                units = (AlpineDisplayUnits)999,
                headlightBindingRevision = 2
            };
            invalidUnits.Normalize();
            Program.Require(invalidUnits.units == AlpineDisplayUnits.Metric, "tune-settings-invalid-units-normalized");
        }

        private static TuneProfile CopyAndLoadFixture(string repoRoot, string root, string fileName)
        {
            string path = CopyFixture(repoRoot, root, fileName);
            TuneProfile profile = JsonConvert.DeserializeObject<TuneProfile>(File.ReadAllText(path));
            Program.Require(profile != null, "tune-fixture-deserialize");
            return profile;
        }

        private static string CopyFixture(string repoRoot, string root, string fileName)
        {
            string source = Path.Combine(repoRoot, "ReleaseTests", "Fixtures", fileName);
            Program.Require(File.Exists(source), "tune-fixture-missing");
            string inputDirectory = Path.Combine(root, "FixtureInputs");
            Directory.CreateDirectory(inputDirectory);
            string destination = Path.Combine(inputDirectory, fileName);
            File.Copy(source, destination, true);
            return destination;
        }
    }
}
