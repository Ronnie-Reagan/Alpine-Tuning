using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace AlpineTuning
{
    internal sealed class AlpineGarageClient
    {
        internal delegate bool ExportProfile(string profileId, out GarageBuildV1 build, out string status);
        internal delegate bool ImportBuild(GarageBuildV1 build, out string status, string actionKey);
        private readonly AlpineTuningMod _mod;
        private readonly Func<IEnumerable<TuneProfile>> _profiles;
        private readonly ExportProfile _export;
        private readonly ImportBuild _import;
        private readonly string _garageDirectory;
        private readonly Func<float> _clock;
        private readonly Action<string> _log;
        private readonly Action<string> _warning;
        private readonly Func<string, string, string, object, string, HttpResult> _http;
        internal Task PendingRequest { get; private set; }
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private GarageConnectionRecord _connection;
        private bool _requestInFlight;
        private bool _tokenValidated;
        private bool _stopped;
        private bool _lastPollWasAction;
        private int _requestEpoch;
        private string _pairingCode;
        private float _nextNetworkTime;
        private float _nextSyncTime;
        private float _nextActionTime;
        private string _pairingRequestId;
        private string _pairingRequestToken;
        private string _candidateInstallationToken;
        private DateTime _pairingExpiresUtc;
        private string _lastNetworkError;
        private string _lastPresenceDiagnostic;

        private string GarageDirectory => _garageDirectory ??
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning", "Garage");
        private string ConnectionPath => Path.Combine(GarageDirectory, "connection.json");
        private string PairingPath => Path.Combine(GarageDirectory, "pairing-code.txt");
        private float Now => _clock();

        internal AlpineGarageClient(AlpineTuningMod mod, string garageDirectory = null,
            Func<float> clock = null, Action<string> log = null, Action<string> warning = null,
            Func<string, string, string, object, string, HttpResult> http = null,
            Func<IEnumerable<TuneProfile>> profiles = null, ExportProfile export = null, ImportBuild import = null)
        {
            _mod = mod;
            _garageDirectory = garageDirectory;
            _clock = clock ?? (() => Time.unscaledTime);
            _log = log ?? (message => MelonLogger.Msg(message));
            _warning = warning ?? (message => MelonLogger.Warning(message));
            _http = http;
            _profiles = profiles ?? (() => _mod.Store.Profiles.Values);
            _export = export ?? ExportFromMod;
            _import = import ?? ImportIntoMod;
        }

        private bool ExportFromMod(string profileId, out GarageBuildV1 build, out string status) =>
            _mod.TryExportGarageProfile(profileId, out build, out status);
        private bool ImportIntoMod(GarageBuildV1 build, out string status, string actionKey) =>
            _mod.TryImportGarageBuild(build, out status, actionKey);

        internal bool IsPaired => _tokenValidated && !string.IsNullOrWhiteSpace(_connection?.installationToken);
        internal string LinkedUser => _connection?.linkedUser;
        internal string ApiBaseUrl => NormalizeBaseUrl(_connection?.apiBaseUrl);
        internal bool IsEnabled => _connection?.enabled == true && !_stopped;
        internal string PairingCode => !string.IsNullOrEmpty(_pairingRequestId) && DateTime.UtcNow < _pairingExpiresUtc ? _pairingCode : null;
        internal string StatusText => _lastNetworkError ?? (!IsEnabled ? "Disconnected. Choose Connect to resume." :
            IsPaired ? "Connected to " + (LinkedUser ?? "Garage") :
            PairingCode != null ? "Enter pairing code " + PairingCode + " in Alpine Garage." :
            _requestInFlight ? "Connecting to Garage..." : "Waiting for Garage...");

        internal void Initialize()
        {
            ConfigureTls();
            Directory.CreateDirectory(GarageDirectory);
            _connection = GarageConnectionStorage.Load(ConnectionPath, out _lastNetworkError);
            if (string.IsNullOrWhiteSpace(_connection.apiBaseUrl))
                _connection.apiBaseUrl = GarageConnectionRecord.ProductionApiBaseUrl;
            if (!SaveConnection()) _connection.enabled = false;
            _nextNetworkTime = Now + 1f;
            _nextSyncTime = Now + 2f;
            _nextActionTime = Now + 2f;
            _log($"Alpine Garage client ready. API={ApiBaseUrl}; enabled={IsEnabled}");
        }

        internal bool Connect(string endpoint, out string status)
        {
            if (_stopped) { status = "Garage client is shut down."; return false; }
            if (!GarageConnectionStorage.TryNormalizeEndpoint(endpoint, out string normalized, out status))
                return false;
            var next = JsonConvert.DeserializeObject<GarageConnectionRecord>(JsonConvert.SerializeObject(_connection));
            if (!string.Equals(normalized, ApiBaseUrl, StringComparison.Ordinal))
            {
                next.installationId = next.installationToken = next.linkedUser = null;
                next.processedImportActionIds.Clear();
                next.pendingCompletion = null;
            }
            next.apiBaseUrl = normalized;
            next.enabled = true;
            if (!GarageConnectionStorage.TrySave(ConnectionPath, next, out status)) return false;
            InvalidateRequests();
            ClearPendingPairing();
            _connection = next;
            _lastNetworkError = null;
            _nextNetworkTime = _nextActionTime = _nextSyncTime = Now;
            status = "Connecting to Alpine Garage.";
            return true;
        }

        internal bool Disconnect(out string status)
        {
            InvalidateRequests();
            ClearPendingPairing();
            _connection.enabled = false;
            bool saved = SaveConnection();
            status = saved ? "Garage disconnected; saved setups are available locally." :
                "Disconnected for this session; connection settings could not be saved.";
            if (saved) _lastNetworkError = null;
            return saved;
        }

        private void InvalidateRequests()
        {
            System.Threading.Interlocked.Increment(ref _requestEpoch);
            _requestInFlight = false;
            _tokenValidated = false;
        }

        internal void Update()
        {
            PumpCallbacks();

            if (!IsEnabled || _requestInFlight || Now < _nextNetworkTime)
                return;

            if (string.IsNullOrWhiteSpace(_connection.installationToken))
            {
                if (!string.IsNullOrWhiteSpace(_pairingRequestId) && DateTime.UtcNow >= _pairingExpiresUtc)
                    PairingExpired();
                else if (string.IsNullOrWhiteSpace(_pairingRequestId))
                    StartPairing();
                else
                    PollPairing();
                return;
            }

            if (!_tokenValidated)
            {
                ValidateInstallation();
                return;
            }

            if (_connection.pendingCompletion != null)
            {
                SendPendingCompletion();
                return;
            }

            // Alternate when both queues are due. A slow action poll must not
            // permanently starve inventory sync, or vice versa.
            if (Now >= _nextSyncTime && (Now < _nextActionTime || _lastPollWasAction))
            {
                _lastPollWasAction = false;
                SyncProfiles();
            }
            else if (Now >= _nextActionTime)
            {
                _lastPollWasAction = true;
                PollAction();
            }
        }

        internal void PumpCallbacks()
        {
            while (_mainThread.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { _warning($"Alpine Garage callback failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        internal void Shutdown()
        {
            _stopped = true;
            InvalidateRequests();
            ClearPendingPairing();
            SaveConnection();
        }

        private bool SaveConnection()
        {
            if (GarageConnectionStorage.TrySave(ConnectionPath, _connection, out string reason)) return true;
            _lastNetworkError = reason;
            _warning(reason);
            return false;
        }

        private void PairingExpired()
        {
            ClearPendingPairing();
            _connection.enabled = false;
            SaveConnection();
            _lastNetworkError = "Pairing expired. Choose Connect for a new code.";
        }

        private void InstallationRejected()
        {
            InvalidateRequests();
            _connection.enabled = false;
            _connection.installationId = _connection.installationToken = _connection.linkedUser = null;
            _connection.pendingCompletion = null;
            SaveConnection();
            _lastNetworkError = "Garage connection is no longer valid. Choose Connect to pair again.";
        }

        private void StartPairing()
        {
            var request = new
            {
                installationName = "Alpine Tuning / Sledders",
                modVersion = AlpineConstants.ModVersion,
                schemaVersion = AlpineConstants.SchemaVersion,
                catalogVersion = AlpineConstants.CatalogVersion
            };

            RunRequest(
                baseUrl => SendJson(baseUrl, "POST", "/api/alpine/pairing/start", request, null),
                result =>
                {
                    if (!result.IsSuccess)
                    {
                        ScheduleNetworkRetry(result, 8f);
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<PairingStartResponse>(result.Body);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.pairingRequestId) ||
                        string.IsNullOrWhiteSpace(payload.pairingCode) ||
                        string.IsNullOrWhiteSpace(payload.requestToken) ||
                        string.IsNullOrWhiteSpace(payload.installationToken))
                    {
                        ScheduleNetworkRetry("pairing response was incomplete", 8f);
                        return;
                    }

                    _pairingRequestId = payload.pairingRequestId;
                    _pairingCode = payload.pairingCode;
                    _pairingRequestToken = payload.requestToken;
                    _candidateInstallationToken = payload.installationToken;
                    DateTime parsed;
                    _pairingExpiresUtc = DateTime.TryParse(payload.expiresAt, out parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow.AddMinutes(10);
                    _nextNetworkTime = Now + 2f;
                    _lastNetworkError = null;

                    try
                    {
                        File.WriteAllText(PairingPath, payload.pairingCode + Environment.NewLine);
                    }
                    catch { }

                    _log("============================================================");
                    _log($"ALPINE GARAGE PAIRING CODE: {payload.pairingCode}");
                    _log("Open Alpine Garage in Discord and choose Connect Alpine. Copy the code from Garage Connection settings.");
                    _log("Pairing expires in about 10 minutes.");
                    _log("============================================================");
                });
        }

        private void PollPairing()
        {
            string requestId = _pairingRequestId;
            string requestToken = _pairingRequestToken;
            RunRequest(
                baseUrl => SendJson(baseUrl, "GET", "/api/alpine/pairing/" + Uri.EscapeDataString(requestId), null, requestToken),
                result =>
                {
                    if (result.StatusCode == 404)
                    {
                        PairingExpired();
                        return;
                    }
                    if (!result.IsSuccess)
                    {
                        ScheduleNetworkRetry(result, 4f);
                        return;
                    }
                    var payload = JsonConvert.DeserializeObject<PairingPollResponse>(result.Body);
                    if (payload == null)
                    {
                        ScheduleNetworkRetry("pairing status was unreadable", 4f);
                        return;
                    }
                    if (payload.expired && !payload.paired)
                    {
                        PairingExpired();
                        return;
                    }
                    if (!payload.paired)
                    {
                        _nextNetworkTime = Now + 2f;
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(payload.installationId))
                    {
                        ScheduleNetworkRetry("paired installation identity was missing", 4f);
                        return;
                    }
                    _connection.installationId = payload.installationId;
                    _connection.installationToken = _candidateInstallationToken;
                    _connection.linkedUser = payload.user != null
                        ? (!string.IsNullOrWhiteSpace(payload.user.globalName) ? payload.user.globalName : payload.user.username)
                        : "Discord user";
                    if (!SaveConnection())
                    {
                        _connection.enabled = false;
                        ClearPendingPairing();
                        return;
                    }
                    _tokenValidated = true;
                    _lastNetworkError = null;
                    ClearPendingPairing();
                    _nextSyncTime = Now;
                    _nextActionTime = Now;
                    _log($"Alpine Garage connected to {_connection.linkedUser}. Installation {_connection.installationId}.");
                });
        }

        private void ValidateInstallation()
        {
            string token = _connection.installationToken;
            RunRequest(
                baseUrl => SendJson(baseUrl, "GET", "/api/alpine/status", null, token),
                result =>
                {
                    if (result.StatusCode == 401)
                    {
                        InstallationRejected();
                        return;
                    }
                    if (!result.IsSuccess)
                    {
                        ScheduleNetworkRetry(result, 8f);
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<InstallationStatusResponse>(result.Body);
                    if (payload == null) { ScheduleNetworkRetry("installation status was unreadable", 8f); return; }
                    if (!payload.connected) { InstallationRejected(); return; }
                    _tokenValidated = true;
                    _lastNetworkError = null;
                    if (payload?.user != null)
                        _connection.linkedUser = !string.IsNullOrWhiteSpace(payload.user.globalName) ? payload.user.globalName : payload.user.username;
                    SaveConnection();
                    _nextNetworkTime = Now + 1f;
                    _nextSyncTime = Now;
                    _nextActionTime = Now;
                });
        }

        private void SyncProfiles()
        {
            List<GarageProfileSummary> profiles = _profiles()
                .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.profileId))
                .OrderByDescending(profile => profile.updatedUnixTime)
                .Take(200)
                .Select(profile => new GarageProfileSummary
                {
                    profileId = profile.profileId,
                    name = string.IsNullOrWhiteSpace(profile.name) ? "Alpine Setup" : profile.name,
                    targetSledKey = profile.targetSledKey,
                    targetVehicleId = profile.targetVehicleId,
                    updatedUnixTime = profile.updatedUnixTime,
                    horsePower = profile.resolvedStats != null ? (float?)profile.resolvedStats.horsePower : null,
                    weight = profile.resolvedStats != null ? (float?)profile.resolvedStats.weight : null,
                    engineText = profile.resolvedStats?.engineText
                })
                .ToList();

            GarageServerPresence serverPresence = BuildServerPresence();
            string token = _connection.installationToken;
            RunRequest(
                baseUrl => SendJson(baseUrl, "POST", "/api/alpine/sync", new { profiles = profiles, serverPresence = serverPresence }, token),
                result =>
                {
                    if (result.StatusCode == 401)
                    {
                        InstallationRejected();
                        return;
                    }
                    if (!result.IsSuccess)
                    {
                        ScheduleNetworkRetry(result, 8f);
                        return;
                    }
                    _nextSyncTime = Now + 5f;
                    _nextNetworkTime = Now + 0.25f;
                    _lastNetworkError = null;

                    if (serverPresence != null)
                    {
                        bool accepted = false;
                        try
                        {
                            JObject payload = JObject.Parse(result.Body ?? "{}");
                            accepted = payload["serverConnected"]?.Value<bool>() == true;
                        }
                        catch { }
                        SetPresenceDiagnostic($"Alpine Garage presence reported: members={serverPresence.memberCount}, backendAccepted={accepted}.");
                    }
                });
        }

        private GarageServerPresence BuildServerPresence()
        {
            try
            {
                object client;
                string reason;
                if (!SleddersGameBindings.TryGetNetClient(out client, out reason) || client == null)
                {
                    SetPresenceDiagnostic("Alpine Garage presence idle: NetClient is unavailable.");
                    return null;
                }

                object connection = SleddersGameBindings.GetFieldValue<object>(client, "netInterface");
                if (connection == null)
                {
                    SetPresenceDiagnostic("Alpine Garage presence idle: NetClient exists but no multiplayer interface is active.");
                    return null;
                }

                var ids = new HashSet<ulong>();
                object identity = client.GetType()
                    .GetMethod("get_LocalClientId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(client, null);
                if (identity is ulong localId)
                    ids.Add(localId);

                System.Reflection.MethodInfo getIds = client.GetType().GetMethod(
                    "GetAllClientIdsIncludingLocalPlayer",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (getIds != null)
                {
                    object result = getIds.Invoke(client, null);
                    if (result is System.Collections.IEnumerable enumerable)
                    {
                        foreach (object item in enumerable)
                        {
                            if (item is ulong id) ids.Add(id);
                            else if (item != null && ulong.TryParse(item.ToString(), out ulong parsed)) ids.Add(parsed);
                        }
                    }
                }

                // Keep the existing Alpine discovery path as a fallback for game builds
                // where GetAllClientIdsIncludingLocalPlayer is unavailable or incomplete.
                AlpineDiscoveredPeer[] peers = SleddersGameBindings.DiscoverPeers(0, false);
                if (peers != null)
                {
                    foreach (AlpineDiscoveredPeer peer in peers)
                    {
                        if (peer == null) continue;
                        if (peer.hasInternalClientId) ids.Add(peer.sleddersClientId);
                        else if (peer.hasSteamId) ids.Add(peer.steamId);
                    }
                }

                if (ids.Count == 0)
                {
                    SetPresenceDiagnostic("Alpine Garage presence idle: multiplayer is active but no client IDs were discovered.");
                    return null;
                }

                List<ulong> orderedIds = ids.OrderBy(id => id).ToList();
                string canonical = "alpine-garage-sledders-room-v2\n" +
                    string.Join("\n", orderedIds.Select(id => id.ToString()));

                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                    var text = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        text.Append(hash[i].ToString("x2"));

                    SetPresenceDiagnostic($"Alpine Garage presence detected: {orderedIds.Count} Sledders member(s).");
                    return new GarageServerPresence
                    {
                        roomFingerprint = text.ToString(),
                        memberCount = orderedIds.Count
                    };
                }
            }
            catch (Exception ex)
            {
                SetPresenceDiagnostic("Alpine Garage presence failed: " + ex.GetType().Name + ": " + ex.Message, true);
                return null;
            }
        }

        private void SetPresenceDiagnostic(string message, bool warning = false)
        {
            if (string.Equals(message, _lastPresenceDiagnostic, StringComparison.Ordinal))
                return;
            _lastPresenceDiagnostic = message;
            if (warning) _warning(message); else _log(message);
        }

        private static string PresenceMemberToken(ulong id, string nickname)
        {
            return id.ToString() + "|" + NormalizePresenceName(nickname);
        }

        private static string NormalizePresenceName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string[] parts = value.Trim().ToLowerInvariant()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private void PollAction()
        {
            string token = _connection.installationToken;
            RunRequest(
                baseUrl => SendJson(baseUrl, "GET", "/api/alpine/actions/next", null, token),
                result =>
                {
                    if (result.StatusCode == 401)
                    {
                        InstallationRejected();
                        return;
                    }
                    if (!result.IsSuccess)
                    {
                        ScheduleNetworkRetry(result, 6f);
                        return;
                    }

                    var envelope = JsonConvert.DeserializeObject<GarageActionEnvelope>(result.Body);
                    _lastNetworkError = null;
                    if (envelope?.action == null)
                    {
                        _nextActionTime = Now + 1.5f;
                        _nextNetworkTime = Now + 0.2f;
                        return;
                    }

                    try { HandleAction(envelope.action); }
                    catch (Exception ex) { CompleteAction(envelope.action.id, false, "Invalid Garage action: " + ex.GetType().Name, null); }
                });
        }

        private void HandleAction(GarageAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.id))
            {
                _nextActionTime = Now + 1.5f;
                return;
            }

            if (string.Equals(action.type, "publish_profile", StringComparison.OrdinalIgnoreCase))
            {
                string profileId = action.payload?["profileId"]?.ToString();
                GarageBuildV1 build;
                string status;
                if (!_export(profileId, out build, out status))
                {
                    CompleteAction(action.id, false, status, null);
                    return;
                }
                CompleteAction(action.id, true, null, build);
                return;
            }

            if (string.Equals(action.type, "import_build", StringComparison.OrdinalIgnoreCase))
            {
                if (_connection.processedImportActionIds.Contains(action.id))
                {
                    CompleteAction(action.id, true, null, null);
                    return;
                }

                GarageBuildV1 build = action.payload?["build"]?.ToObject<GarageBuildV1>();
                string status;
                string importKey = JsonConvert.SerializeObject(new[] { ApiBaseUrl, _connection.installationId, action.id });
                if (!_import(build, out status, importKey))
                {
                    CompleteAction(action.id, false, status, null);
                    return;
                }

                _connection.processedImportActionIds.Add(action.id);
                while (_connection.processedImportActionIds.Count > 100)
                    _connection.processedImportActionIds.RemoveAt(0);
                SaveConnection();
                CompleteAction(action.id, true, null, null);
                return;
            }

            CompleteAction(action.id, false, "Unsupported Garage action: " + action.type, null);
        }

        private void CompleteAction(string actionId, bool ok, string error, GarageBuildV1 build)
        {
            _connection.pendingCompletion = new GarageActionCompletion { actionId = actionId, ok = ok, error = error, build = build };
            SendPendingCompletion();
        }

        private void SendPendingCompletion()
        {
            // Persist the exact publish payload before acknowledging. A dropped
            // response or restart retries the same action, not a changed setup.
            if (!SaveConnection()) { _nextNetworkTime = Now + 4f; return; }
            GarageActionCompletion pending = _connection.pendingCompletion;
            string token = _connection.installationToken;
            var body = new { ok = pending.ok, error = pending.error, build = pending.build };
            RunRequest(
                baseUrl => SendJson(baseUrl, "POST", "/api/alpine/actions/" + Uri.EscapeDataString(pending.actionId) + "/complete", body, token),
                result =>
                {
                    if (result.StatusCode == 401)
                        InstallationRejected();
                    else if (!result.IsSuccess)
                        ScheduleNetworkRetry(result, 4f);
                    else
                    {
                        _connection.pendingCompletion = null;
                        if (!SaveConnection())
                        {
                            _connection.pendingCompletion = pending;
                            _nextNetworkTime = Now + 4f;
                            return;
                        }
                        _nextActionTime = Now + 0.5f;
                        _nextSyncTime = Now + 0.5f;
                        _nextNetworkTime = Now + 0.2f;
                        _lastNetworkError = null;
                    }
                });
        }

        internal Task RunRequest(Func<string, HttpResult> request, Action<HttpResult> complete)
        {
            if (!IsEnabled || _requestInFlight)
                return Task.FromResult(0);
            string baseUrl = ApiBaseUrl;
            int epoch = _requestEpoch;
            _requestInFlight = true;
            return PendingRequest = Task.Run(() =>
            {
                HttpResult result;
                if (epoch != System.Threading.Volatile.Read(ref _requestEpoch)) return;
                try { result = request(baseUrl); }
                catch (Exception ex) { result = new HttpResult { StatusCode = 0, Body = ex.Message }; }
                _mainThread.Enqueue(() =>
                {
                    // A response from a previous endpoint or connection cannot
                    // change the new connection or process a queued import.
                    if (_stopped || epoch != _requestEpoch || !IsEnabled) return;
                    _requestInFlight = false;
                    try { complete(result); }
                    catch (Exception ex)
                    {
                        // A malformed successful response must not trigger the
                        // same request every frame or strand the action queue.
                        ScheduleNetworkRetry("response processing failed: " + ex.GetType().Name, 8f);
                    }
                });
            });
        }

        internal HttpResult SendJson(string baseUrl, string method, string relativePath, object body, string bearer)
        {
            if (_http != null) return _http(baseUrl, method, relativePath, body, bearer);
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Alpine Garage API URL is empty.");

            var request = (HttpWebRequest)WebRequest.Create(baseUrl + relativePath);
            request.Method = method;
            request.Accept = "application/json";
            request.ContentType = "application/json";
            request.UserAgent = "AlpineTuning/" + AlpineConstants.ModVersion;
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.KeepAlive = false;
            request.AllowAutoRedirect = false;
            if (!string.IsNullOrWhiteSpace(bearer))
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;

            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, _jsonSettings));
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);
            }
            else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                request.ContentLength = 0;
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return new HttpResult { StatusCode = (int)response.StatusCode, Body = GarageConnectionStorage.ReadResponse(response.GetResponseStream()) };
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    string detail = ex.Status + ": " + ex.Message;
                    Exception inner = ex.InnerException;
                    int depth = 0;
                    while (inner != null && depth++ < 4)
                    {
                        detail += " | " + inner.GetType().Name + ": " + inner.Message;
                        inner = inner.InnerException;
                    }
                    return new HttpResult { StatusCode = 0, Body = detail };
                }
                using (response)
                    return new HttpResult { StatusCode = (int)response.StatusCode, Body = GarageConnectionStorage.ReadResponse(response.GetResponseStream()) };
            }
        }

        private static void ConfigureTls()
        {
            try
            {
                // Sledders runs Alpine through Unity/Mono. Explicitly make TLS 1.2
                // available to HttpWebRequest without replacing or weakening the
                // runtime's normal certificate validation.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch
            {
                // Older runtimes may reject an unsupported flag. The request path
                // will still run and now reports the real transport error below.
            }
        }

        private void ScheduleNetworkRetry(HttpResult result, float seconds)
        {
            string detail = result?.Body;
            string error;
            if (result == null)
                error = "Garage request failed without a result; retrying.";
            else if (result.StatusCode == 0)
                error = string.IsNullOrWhiteSpace(detail)
                    ? "Garage transport failed with no response; retrying."
                    : "Garage transport failed: " + detail + "; retrying.";
            else
                error = "Garage returned HTTP " + result.StatusCode +
                    (string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail) + "; retrying.";

            ScheduleNetworkRetry(error, seconds);
        }

        private void ScheduleNetworkRetry(string error, float seconds)
        {
            if (!string.Equals(error, _lastNetworkError, StringComparison.Ordinal))
            {
                _lastNetworkError = error;
                _warning("Alpine Garage: " + error);
            }
            _nextNetworkTime = Now + seconds;
            _nextActionTime = Now + seconds;
            _nextSyncTime = Now + seconds;
        }

        private void ClearPendingPairing()
        {
            _pairingRequestId = null;
            _pairingCode = null;
            _pairingRequestToken = null;
            _candidateInstallationToken = null;
            _pairingExpiresUtc = DateTime.MinValue;
            try { if (File.Exists(PairingPath)) File.Delete(PairingPath); } catch { }
        }

        private static string NormalizeBaseUrl(string value)
        {
            string result = (value ?? string.Empty).Trim();
            while (result.EndsWith("/", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 1);
            return result;
        }

        // Json.NET populates these response properties through their setters.
        // Keep the property names aligned with the Garage JSON contract.
        [Serializable]
        private sealed class PairingStartResponse
        {
            public string pairingRequestId { get; set; }
            public string pairingCode { get; set; }
            public string requestToken { get; set; }
            public string installationToken { get; set; }
            public string expiresAt { get; set; }
        }

        [Serializable]
        private sealed class GarageUser
        {
            public string id { get; set; }
            public string username { get; set; }
            public string globalName { get; set; }
        }

        [Serializable]
        private sealed class PairingPollResponse
        {
            public bool paired { get; set; }
            public bool expired { get; set; }
            public string installationId { get; set; }
            public GarageUser user { get; set; }
        }

        [Serializable]
        private sealed class InstallationStatusResponse
        {
            public bool connected { get; set; }
            public GarageUser user { get; set; }
        }

        [Serializable]
        private sealed class GarageActionEnvelope
        {
            public GarageAction action { get; set; }
        }

        [Serializable]
        private sealed class GarageAction
        {
            public string id { get; set; }
            public string type { get; set; }
            public JObject payload { get; set; }
        }

        internal sealed class HttpResult
        {
            public int StatusCode;
            public string Body;
            public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
        }
    }
}
