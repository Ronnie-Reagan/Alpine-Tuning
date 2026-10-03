using AlpineTuning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AlpineTuning.ReleaseTests
{
    internal static class GarageCompatibility
    {
        internal static void RunGolden(string repoRoot)
        {
            var fixtures = JArray.Parse(File.ReadAllText(Path.Combine(repoRoot, "ReleaseTests", "Fixtures", "garage-wire.json")));
            Program.Require(fixtures.Count >= 10, "garage-wire-fixture-coverage");
            foreach (JToken fixture in fixtures)
            {
                var build = fixture["build"].ToObject<GarageBuildV1>();
                string name = fixture["name"].ToString();
                Program.Require(GarageBuildCodec.CanonicalContent(build) == fixture["canonical"].ToString(),
                    "garage-js-canonical:" + name);
                Program.Require(GarageBuildCodec.ComputeHash(build) == build.contentHash, "garage-js-hash:" + name);
                Program.Require(GarageBuildCodec.TryToProfile(build, out _, out _), "garage-js-decode:" + name);
            }
            var invalid = fixtures[0]["build"].ToObject<GarageBuildV1>();
            invalid.configuration.fineTune.powerTrimPercent = 10.0000001;
            invalid.contentHash = GarageBuildCodec.ComputeHash(invalid);
            Program.Require(!GarageBuildCodec.TryToProfile(invalid, out _, out _), "garage-wire-range-before-float-cast");
        }

        private static AlpineGarageClient Client()
        {
            return new AlpineGarageClient(null, clock: () => 0, log: message => { }, warning: message => { });
        }

        private static JObject Request(AlpineGarageClient client, string endpoint, string method,
            string route, object body = null, string token = null, int expected = 200)
        {
            AlpineGarageClient.HttpResult result = client.SendJson(endpoint, method, route, body, token);
            // Never print response bodies or tokens, including on a failed live test.
            Program.Require(result.StatusCode == expected, "garage-http:" + method + " " + route + ":" + result.StatusCode);
            return JObject.Parse(result.Body);
        }

        internal static void RunLive(string endpoint)
        {
            Program.Require(GarageConnectionStorage.TryNormalizeEndpoint(endpoint, out endpoint, out _), "garage-live-endpoint");
            var client = Client();
            JObject health = Request(client, endpoint, "GET", "/api/health");
            Program.Require((bool?)health["ok"] == true && (bool?)health["configured"] == true &&
                (string)health["service"] == "alpine-garage-api", "garage-live-health");
            JObject pair = Request(client, endpoint, "POST", "/api/alpine/pairing/start", new
            {
                installationName = "Alpine Tuning compatibility check",
                modVersion = AlpineConstants.ModVersion,
                schemaVersion = AlpineConstants.SchemaVersion,
                catalogVersion = AlpineConstants.CatalogVersion
            }, expected: 201);
            Program.Require(!string.IsNullOrWhiteSpace((string)pair["pairingRequestId"]) &&
                !string.IsNullOrWhiteSpace((string)pair["pairingCode"]) &&
                !string.IsNullOrWhiteSpace((string)pair["requestToken"]) &&
                !string.IsNullOrWhiteSpace((string)pair["installationToken"]) &&
                DateTimeOffset.Parse((string)pair["expiresAt"], CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow,
                "garage-live-pairing-shape");
            string route = "/api/alpine/pairing/" + Uri.EscapeDataString((string)pair["pairingRequestId"]);
            JObject poll = Request(client, endpoint, "GET", route, token: (string)pair["requestToken"]);
            Program.Require((bool?)poll["paired"] == false && (bool?)poll["expired"] == false &&
                !string.IsNullOrWhiteSpace((string)poll["installationId"]), "garage-live-unclaimed-pairing");
            string installationToken = (string)pair["installationToken"];
            Request(client, endpoint, "GET", "/api/alpine/status", token: installationToken, expected: 401);
            Request(client, endpoint, "POST", "/api/alpine/sync", new { profiles = new object[0] }, installationToken, 401);
            Request(client, endpoint, "GET", "/api/alpine/actions/next", token: installationToken, expected: 401);
            // Leave this short-lived compatibility code unclaimed. No Discord
            // account is linked and no user inventory/builds are changed.
        }

        internal static void RunClientLifecycle(string testRoot)
        {
            string directory = Path.Combine(testRoot, "garage-client-lifecycle");
            Directory.CreateDirectory(directory);
            float now = 10f;
            int exports = 0, imports = 0, syncs = 0, stage = 0, publishAttempts = 0, importAttempts = 0;
            bool rejectInstallation = false;
            bool pairingClaimed = false, expirePairing = false, installationConnected = true;
            var catalog = new PartCatalog();
            var profile = new TuneProfile { profileId = "fixture-profile", name = "Original publish", targetSledKey = "fixture-sled" };
            catalog.EnsureProfileSelections(profile);
            Program.Require(GarageBuildCodec.TryFromProfile(profile, out GarageBuildV1 wireBuild, out _), "garage-client-fixture");
            string originalName = wireBuild.name;
            Func<string, string, string, object, string, AlpineGarageClient.HttpResult> http = (url, method, route, body, token) =>
            {
                Program.Require(url == "https://fixture.invalid", "garage-client-endpoint");
                object payload = null;
                int code = 200;
                if (route == "/api/alpine/pairing/start")
                {
                    Program.Require(method == "POST" && token == null, "garage-client-pair-start-no-credentials");
                    payload = new { pairingRequestId = "fixture-pair", pairingCode = "FIXTURE", requestToken = "fixture-request",
                        installationToken = "fixture-installation-token", expiresAt = DateTime.UtcNow.AddMinutes(10).ToString("o") };
                }
                else if (route == "/api/alpine/pairing/fixture-pair")
                {
                    Program.Require(token == "fixture-request", "garage-client-pair-poll-request-token");
                    payload = new { paired = pairingClaimed, expired = expirePairing, installationId = "fixture-installation",
                        user = new { id = "fixture-user", username = "fixture-rider", globalName = "Fixture rider" } };
                }
                else
                {
                    Program.Require(token == "fixture-installation-token", "garage-client-installation-credential");
                    if (rejectInstallation) return new AlpineGarageClient.HttpResult { StatusCode = 401, Body = "{}" };
                    if (route == "/api/alpine/status") payload = new { connected = installationConnected,
                        user = new { id = "fixture-user", username = "fixture-rider", globalName = (string)null } };
                    else if (route == "/api/alpine/sync")
                    {
                        syncs++;
                        Program.Require(JObject.FromObject(body)["profiles"].Count() == 1, "garage-client-inventory-sent");
                        payload = new { ok = true };
                    }
                    else if (route == "/api/alpine/actions/next")
                    {
                        object action = stage == 0 ? new { id = "publish", type = "publish_profile", payload = (object)new { profileId = profile.profileId } } :
                            stage == 1 || stage == 2 ? new { id = "import", type = "import_build", payload = (object)new { build = wireBuild } } :
                            stage == 3 ? new { id = "bad-import", type = "import_build", payload = (object)new { build = "invalid object" } } : null;
                        payload = new { action };
                    }
                    else if (route == "/api/alpine/actions/publish/complete")
                    {
                        var completion = JObject.FromObject(body);
                        Program.Require((bool)completion["ok"] && (string)completion["build"]["name"] == originalName,
                            "garage-client-retry-publishes-original-payload");
                        if (++publishAttempts == 1) code = 503;
                        else stage = 1;
                        payload = new { ok = true };
                    }
                    else if (route == "/api/alpine/actions/import/complete")
                    {
                        if (++importAttempts == 1) code = 503;
                        else if (stage == 1) stage = 2;
                        else stage = 3;
                        payload = new { ok = true };
                    }
                    else if (route == "/api/alpine/actions/bad-import/complete")
                    {
                        Program.Require((bool)JObject.FromObject(body)["ok"] == false, "garage-client-malformed-action-acknowledged-failed");
                        stage = 4; payload = new { ok = true };
                    }
                    else throw new InvalidOperationException("Unexpected fixture route");
                }
                return new AlpineGarageClient.HttpResult { StatusCode = code, Body = JsonConvert.SerializeObject(payload) };
            };
            AlpineGarageClient.ExportProfile export = (string id, out GarageBuildV1 build, out string status) =>
            {
                Program.Require(id == profile.profileId, "garage-client-deserialized-publish-profile-id");
                exports++; build = wireBuild; status = null; return true;
            };
            AlpineGarageClient.ImportBuild import = (GarageBuildV1 build, out string status, string key) =>
            {
                imports++;
                Program.Require(GarageBuildCodec.TryToProfile(build, out _, out _) && key.Contains("fixture-installation"),
                    "garage-client-import-scoped-to-installation");
                status = "Imported"; return true;
            };
            Func<AlpineGarageClient> create = () => new AlpineGarageClient(null, directory, () => now,
                message => { }, message => { }, http, () => new[] { profile }, export, import);
            var client = create(); client.Initialize();
            Program.Require(client.Connect("https://fixture.invalid", out _), "garage-client-connect");
            Action tick = () =>
            {
                now += 10;
                client.Update();
                for (int i = 0; i < 5; i++)
                {
                    var pending = client.PendingRequest;
                    if (pending != null) Program.Require(pending.Wait(5000), "garage-client-request-finished");
                    client.PumpCallbacks();
                    if (ReferenceEquals(pending, client.PendingRequest)) break;
                }
            };
            tick();
            Program.Require(client.PairingCode == "FIXTURE" && !client.IsPaired,
                "garage-client-deserialized-pairing-code");
            tick();
            Program.Require(client.PairingCode == "FIXTURE" && !client.IsPaired && client.IsEnabled,
                "garage-client-unclaimed-pairing-remains-pending");
            pairingClaimed = true;
            tick();
            Program.Require(client.IsPaired && client.LinkedUser == "Fixture rider" && client.PairingCode == null,
                "garage-client-deserialized-pairing-identity");
            for (int i = 0; i < 20 && publishAttempts == 0; i++) tick();
            Program.Require(client.IsPaired && publishAttempts == 1 && exports == 1,
                "garage-client-pairs-syncs-and-queues-publish-retry");
            client.Shutdown();
            profile.name = "Edited after failed acknowledgement";
            wireBuild = JsonConvert.DeserializeObject<GarageBuildV1>(JsonConvert.SerializeObject(wireBuild));
            wireBuild.name = profile.name;
            client = create(); client.Initialize();
            tick();
            Program.Require(client.IsPaired && client.LinkedUser == "fixture-rider",
                "garage-client-deserialized-status-user-fallback");
            for (int i = 0; i < 30 && stage != 4; i++) tick();
            Program.Require(stage == 4 && exports == 1 && imports == 1 && syncs > 0 && publishAttempts == 2 && importAttempts == 3,
                "garage-client-restart-completion-and-import-redelivery-idempotent");
            Program.Require(GarageConnectionStorage.Load(Path.Combine(directory, "connection.json"), out _).pendingCompletion == null,
                "garage-client-completion-journal-cleared");
            int before = syncs;
            Program.Require(client.Disconnect(out _), "garage-client-disconnect");
            tick(); Program.Require(syncs == before && !client.IsEnabled, "garage-client-disconnect-stops-io");
            installationConnected = false;
            Program.Require(client.Connect("https://fixture.invalid", out _), "garage-client-status-reconnect");
            tick();
            Program.Require(!client.IsPaired && !client.IsEnabled && client.StatusText.Contains("no longer valid"),
                "garage-client-disconnected-status-requires-pairing");
            installationConnected = true;
            Program.Require(client.Connect("https://fixture.invalid", out _), "garage-client-pair-after-status-rejection");
            tick(); tick();
            Program.Require(client.IsPaired, "garage-client-repaired-after-status-rejection");
            Program.Require(client.Disconnect(out _), "garage-client-disconnect-before-revocation");
            rejectInstallation = true;
            Program.Require(client.Connect("https://fixture.invalid", out _), "garage-client-reconnect");
            tick(); Program.Require(!client.IsPaired && !client.IsEnabled && client.StatusText.Contains("no longer valid"),
                "garage-client-revocation-requires-pairing");
            pairingClaimed = false;
            expirePairing = true;
            Program.Require(client.Connect("https://fixture.invalid", out _), "garage-client-connect-before-expiry");
            tick(); tick();
            Program.Require(!client.IsPaired && !client.IsEnabled && client.PairingCode == null &&
                client.StatusText.Contains("Pairing expired"), "garage-client-deserialized-expired-pairing");
            client.Shutdown();
        }

        internal static void RunBackend(string backendSource, string testRoot)
        {
            string root = Path.Combine(testRoot, "garage-backend");
            Directory.CreateDirectory(root);
            int port;
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            string endpoint = "http://127.0.0.1:" + port;
            string source = File.ReadAllText(backendSource);
            Program.Require(source.Contains("const dataDir = path.join(rootDir, \"data\");") &&
                source.Contains("let store = loadStore();"), "garage-backend-supported-fixture-bootstrap");
            source = source.Replace("const dataDir = path.join(rootDir, \"data\");",
                "const dataDir = " + JsonConvert.SerializeObject(Path.Combine(root, "data")) + ";");
            // Seed only an isolated test server. The real backend and its users,
            // database and OAuth secrets are never loaded or changed.
            source = source.Replace("let store = loadStore();", @"
let store = emptyStore();
store.users['fixture-user'] = {id:'fixture-user',username:'fixture-rider',globalName:'Fixture rider'};
sessions.set(sha256('fixture-session'), {userId:'fixture-user',expiresAt:Date.now()+600000});
");
            string script = Path.Combine(root, "server.mjs");
            File.WriteAllText(script, source, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", "\"" + script + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["API_PORT"] = port.ToString(CultureInfo.InvariantCulture);
            start.EnvironmentVariables["DISCORD_APPLICATION_ID"] = "";
            start.EnvironmentVariables["DISCORD_CLIENT_SECRET"] = "";
            using (var process = Process.Start(start))
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    var client = Client();
                    var deadline = Stopwatch.StartNew();
                    while (client.SendJson(endpoint, "GET", "/api/health", null, null).StatusCode != 200)
                    {
                        Program.Require(!process.HasExited && deadline.ElapsedMilliseconds < 5000, "garage-backend-started");
                        Thread.Sleep(50);
                    }
                    JObject pair = Request(client, endpoint, "POST", "/api/alpine/pairing/start", new
                    {
                        installationName = "Fixture Alpine", modVersion = AlpineConstants.ModVersion,
                        schemaVersion = AlpineConstants.SchemaVersion, catalogVersion = AlpineConstants.CatalogVersion
                    }, expected: 201);
                    string token = (string)pair["installationToken"];
                    string pollRoute = "/api/alpine/pairing/" + Uri.EscapeDataString((string)pair["pairingRequestId"]);
                    Request(client, endpoint, "GET", pollRoute, token: (string)pair["requestToken"]);
                    Request(client, endpoint, "POST", "/api/pairing/claim", new { code = (string)pair["pairingCode"] }, "fixture-session");
                    JObject poll = Request(client, endpoint, "GET", pollRoute, token: (string)pair["requestToken"]);
                    Program.Require((bool?)poll["paired"] == true && (bool?)poll["expired"] == false &&
                        (string)poll["user"]?["globalName"] == "Fixture rider", "garage-backend-paired");
                    string installation = (string)poll["installationId"];
                    Program.Require((bool?)Request(client, endpoint, "GET", "/api/alpine/status", token: token)["connected"] == true,
                        "garage-backend-authenticated");
                    var catalog = new PartCatalog();
                    var profile = new TuneProfile { profileId = "fixture-profile", name = "Wire precision build",
                        targetSledKey = "fixture-sled", targetVehicleId = "1001", fineTune = new FineTuneSettings
                        {
                            powerTrimPercent = 3.1234567f, nitrousBoostPercent = 137.123456f,
                            centerOfMassYTrim = -0.000003f, centerOfMassZTrim = 0.000001f
                        } };
                    catalog.EnsureProfileSelections(profile);
                    Request(client, endpoint, "POST", "/api/alpine/sync", new { profiles = new[]
                    {
                        new GarageProfileSummary { profileId = profile.profileId, name = profile.name,
                            targetSledKey = profile.targetSledKey, targetVehicleId = profile.targetVehicleId }
                    } }, token);
                    JObject publish = Request(client, endpoint, "POST", "/api/builds/publish-request",
                        new { installationId = installation, profileId = profile.profileId, visibility = "private" },
                        "fixture-session", 202);
                    JObject action = Request(client, endpoint, "GET", "/api/alpine/actions/next", token: token);
                    Program.Require((string)action["action"]?["type"] == "publish_profile" &&
                        (string)action["action"]?["id"] == (string)publish["actionId"], "garage-backend-publish-action");
                    Program.Require(GarageBuildCodec.TryFromProfile(profile, out GarageBuildV1 build, out _), "garage-backend-export");
                    JObject completed = Request(client, endpoint, "POST", "/api/alpine/actions/" + (string)publish["actionId"] + "/complete",
                        new { ok = true, build }, token);
                    Program.Require(!string.IsNullOrEmpty((string)completed["buildId"]), "garage-backend-accepted-csharp-hash");
                    JObject repeated = Request(client, endpoint, "POST", "/api/alpine/actions/" + (string)publish["actionId"] + "/complete",
                        new { ok = true, build }, token);
                    Program.Require((string)repeated["buildId"] == (string)completed["buildId"] &&
                        Request(client, endpoint, "GET", "/api/builds", token: "fixture-session")["builds"].Count() == 1,
                        "garage-backend-publish-ack-retry-no-duplicate");
                    JObject queued = Request(client, endpoint, "POST", "/api/builds/" + (string)completed["buildId"] + "/import",
                        new { installationId = installation }, "fixture-session", 202);
                    action = Request(client, endpoint, "GET", "/api/alpine/actions/next", token: token);
                    Program.Require((string)action["action"]?["type"] == "import_build" &&
                        (string)action["action"]?["id"] == (string)queued["actionId"], "garage-backend-import-action");
                    build = action["action"]["payload"]["build"].ToObject<GarageBuildV1>();
                    Program.Require(GarageBuildCodec.TryToProfile(build, out TuneProfile imported, out _) &&
                        GarageBuildCodec.TryPrepareImport(imported, catalog, out _), "garage-backend-roundtrip-valid");
                    Program.Require(imported.fineTune.nitrousBoostPercent == profile.fineTune.nitrousBoostPercent &&
                        imported.fineTune.centerOfMassYTrim == profile.fineTune.centerOfMassYTrim, "garage-backend-roundtrip-values");
                    Request(client, endpoint, "POST", "/api/alpine/actions/" + (string)queued["actionId"] + "/complete",
                        new { ok = true }, token);
                    Request(client, endpoint, "POST", "/api/alpine/actions/" + (string)queued["actionId"] + "/complete",
                        new { ok = true }, token);
                    Program.Require((int)Request(client, endpoint, "GET", "/api/builds", token: "fixture-session")["builds"][0]["importCount"] == 1,
                        "garage-backend-import-ack-retry-counted-once");
                    Program.Require(Request(client, endpoint, "GET", "/api/alpine/actions/next", token: token)["action"].Type == JTokenType.Null,
                        "garage-backend-actions-completed");
                    JObject validationAction = Request(client, endpoint, "POST", "/api/builds/publish-request",
                        new { installationId = installation, profileId = profile.profileId, visibility = "private" },
                        "fixture-session", 202);
                    string validationRoute = "/api/alpine/actions/" + (string)validationAction["actionId"] + "/complete";
                    string validHash = build.contentHash;
                    build.contentHash = new string('0', 64);
                    Request(client, endpoint, "POST", validationRoute, new { ok = true, build }, token, 400);
                    build.contentHash = validHash;
                    Program.Require(!string.IsNullOrEmpty((string)Request(client, endpoint, "POST", validationRoute,
                        new { ok = true, build }, token)["buildId"]), "garage-backend-invalid-publish-does-not-complete-action");
                    Request(client, endpoint, "DELETE", "/api/installations/" + installation, token: "fixture-session");
                    Request(client, endpoint, "GET", "/api/alpine/status", token: token, expected: 401);
                }
                finally
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(5000);
                }
            }
        }
    }
}
