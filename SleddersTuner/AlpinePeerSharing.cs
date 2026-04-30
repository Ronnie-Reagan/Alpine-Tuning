using MelonLoader;
using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AlpineTuning
{
    internal class AlpinePeerSharing
    {
        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<string, RemoteTuneSummary> _remoteSummaries = new Dictionary<string, RemoteTuneSummary>();
        private readonly Dictionary<string, TuneProfile> _remotePayloads = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, TuneProfile> _publishedProfiles = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, float> _pendingRequestDeadlines = new Dictionary<string, float>();
        private bool _initialized;
        private bool _loggedUnavailable;
        private float _nextHelloTime;

        public AlpinePeerSharing(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        public string LocalName
        {
            get
            {
                try
                {
                    return SteamClient.IsValid ? SteamClient.Name : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        public IEnumerable<RemoteTuneSummary> RemoteSummaries => _remoteSummaries.Values;
        public bool IsAvailable => _initialized && IsSteamValid();
        public string StatusMessage { get; private set; }

        public void Initialize()
        {
            if (_initialized)
                return;

            try
            {
                if (!SteamClient.IsValid)
                {
                    LogUnavailableOnce("Steam client is not valid yet; peer tune sharing will retry in-game.");
                    return;
                }

                SteamNetworking.AllowP2PPacketRelay(true);
                SteamNetworking.OnP2PSessionRequest -= OnP2PSessionRequest;
                SteamNetworking.OnP2PSessionRequest += OnP2PSessionRequest;
                _initialized = true;
                StatusMessage = "Peer sharing ready.";
                MelonLogger.Msg("Alpine peer tune sharing initialized.");
            }
            catch (Exception ex)
            {
                LogUnavailableOnce($"Peer tune sharing unavailable: {ex.Message}");
            }
        }

        public void Update()
        {
            if (!_initialized)
                Initialize();

            if (!_initialized)
                return;

            PollPackets();
            CheckRequestTimeouts();

            if (UnityEngine.Time.unscaledTime >= _nextHelloTime)
            {
                _nextHelloTime = UnityEngine.Time.unscaledTime + 15f;
                BroadcastHello();
            }
        }

        public bool BroadcastHello()
        {
            if (!_initialized)
            {
                StatusMessage = "Peer sharing unavailable.";
                return false;
            }

            bool sent = SendToPeers(new AlpineShareMessage
            {
                type = "hello",
                senderId = LocalSteamId(),
                senderName = LocalName
            });

            foreach (var profile in _publishedProfiles.Values.ToList())
                sent |= SendSummary(profile);

            StatusMessage = sent ? "Peer discovery hello sent." : "No lobby peers discovered for sharing.";
            return sent;
        }

        public bool PublishProfile(TuneProfile profile)
        {
            if (profile == null)
                return false;

            if (!_initialized)
            {
                StatusMessage = "Peer sharing unavailable.";
                return false;
            }

            profile.checksum = null;
            if (!TuneStore.TryValidateProfileForCatalog(profile, _mod.Catalog, false, false, out var reason))
            {
                StatusMessage = $"Tune not published: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return false;
            }

            profile.checksum = TuneStore.ComputeChecksum(profile);
            _publishedProfiles[profile.profileId] = TuneStore.Clone(profile);
            bool sent = SendSummary(profile);
            StatusMessage = sent ? "Published tune summary." : "Tune is ready, but no peers were discovered.";
            return sent;
        }

        public bool RequestProfile(ulong peerId, string profileId)
        {
            if (!_initialized || peerId == 0 || string.IsNullOrWhiteSpace(profileId))
            {
                StatusMessage = "Profile request failed: sharing unavailable or invalid peer.";
                return false;
            }

            bool sent = SendToPeer(peerId, new AlpineShareMessage
            {
                type = "profileRequest",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = profileId
            });

            if (sent)
            {
                _pendingRequestDeadlines[RemoteKey(peerId, profileId)] = UnityEngine.Time.unscaledTime + 10f;
                StatusMessage = "Shared tune payload requested.";
            }
            else
            {
                StatusMessage = "Shared tune payload request failed.";
            }

            return sent;
        }

        public TuneProfile GetPayload(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return null;

            var match = _remotePayloads
                .Where(pair => pair.Key.EndsWith("|" + profileId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(pair =>
                {
                    _remoteSummaries.TryGetValue(pair.Key, out var summary);
                    return summary != null ? summary.receivedUnixTime : 0;
                })
                .FirstOrDefault();

            var profile = match.Value;
            return profile != null ? TuneStore.Clone(profile) : null;
        }

        public TuneProfile GetPayload(ulong senderId, string profileId, out string status)
        {
            status = null;
            if (senderId == 0 || string.IsNullOrWhiteSpace(profileId))
            {
                status = "Shared payload request is invalid.";
                return null;
            }

            string key = RemoteKey(senderId, profileId);
            if (!_remotePayloads.TryGetValue(key, out var profile) || profile == null)
            {
                status = "Shared payload is missing. Request it first.";
                return null;
            }

            if (!TuneStore.TryValidateProfileForCatalog(profile, _mod.Catalog, true, true, out var reason))
            {
                status = $"Shared payload rejected: {reason}.";
                return null;
            }

            return TuneStore.Clone(profile);
        }

        private bool SendSummary(TuneProfile profile)
        {
            if (!_initialized || profile == null)
                return false;

            var summary = new RemoteTuneSummary
            {
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = profile.profileId,
                profileName = profile.name,
                targetSledKey = profile.targetSledKey,
                targetVehicleId = profile.targetVehicleId,
                catalogVersion = profile.catalogVersion,
                checksum = profile.checksum,
                hasPayload = false,
                receivedUnixTime = NowUnix()
            };

            return SendToPeers(new AlpineShareMessage
            {
                type = "profileSummary",
                senderId = summary.senderId,
                senderName = summary.senderName,
                profileId = profile.profileId,
                checksum = profile.checksum,
                summary = summary
            });
        }

        private void PollPackets()
        {
            try
            {
                int guard = 0;
                while (SteamNetworking.IsP2PPacketAvailable(AlpineConstants.SteamP2PChannel) && guard++ < 32)
                {
                    var packet = SteamNetworking.ReadP2PPacket(AlpineConstants.SteamP2PChannel);
                    if (!packet.HasValue)
                        break;

                    ProcessPacket(packet.Value);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine peer packet polling failed: {ex.Message}");
            }
        }

        private void ProcessPacket(P2Packet packet)
        {
            try
            {
                if (packet.Data == null || packet.Data.Length == 0 || packet.Data.Length > AlpineConstants.MaxPeerMessageBytes)
                {
                    StatusMessage = "Ignored peer tune packet with invalid size.";
                    MelonLogger.Warning(StatusMessage);
                    return;
                }

                string json = Encoding.UTF8.GetString(packet.Data);
                var message = JsonConvert.DeserializeObject<AlpineShareMessage>(json);
                if (message == null || message.magic != "ALPINE_TUNE")
                    return;

                ulong packetSender = packet.SteamId.Value;
                if (packetSender == 0)
                    return;

                if (message.senderId != 0 && message.senderId != packetSender)
                {
                    StatusMessage = "Ignored peer tune packet with mismatched sender identity.";
                    MelonLogger.Warning(StatusMessage);
                    return;
                }

                message.senderId = packetSender;

                if (message.schemaVersion != AlpineConstants.SchemaVersion)
                {
                    StatusMessage = $"Ignored peer tune packet with schema {message.schemaVersion}.";
                    MelonLogger.Warning(StatusMessage);
                    return;
                }

                switch (message.type)
                {
                    case "hello":
                        foreach (var profile in _publishedProfiles.Values.ToList())
                            SendSummary(profile);
                        break;

                    case "profileSummary":
                        ReceiveSummary(message);
                        break;

                    case "profileRequest":
                        SendRequestedPayload(message.senderId, message.profileId);
                        break;

                    case "profilePayload":
                        ReceivePayload(message);
                        break;

                    case "profileAck":
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine peer packet ignored: {ex.Message}");
            }
        }

        private void ReceiveSummary(AlpineShareMessage message)
        {
            if (!TryValidateSummary(message, out var reason))
            {
                StatusMessage = $"Ignored shared tune summary: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            var summary = message.summary;
            summary.senderId = message.senderId;
            summary.senderName = string.IsNullOrWhiteSpace(message.senderName) ? summary.senderName : message.senderName;
            summary.receivedUnixTime = NowUnix();
            string key = RemoteKey(summary.senderId, summary.profileId);
            summary.hasPayload = _remotePayloads.ContainsKey(key);
            _remoteSummaries[key] = summary;
        }

        private void SendRequestedPayload(ulong peerId, string profileId)
        {
            if (peerId == 0 || string.IsNullOrWhiteSpace(profileId))
                return;

            if (!_publishedProfiles.TryGetValue(profileId, out var profile))
            {
                StatusMessage = "Requested shared tune payload was not published locally.";
                return;
            }

            SendToPeer(peerId, new AlpineShareMessage
            {
                type = "profilePayload",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = profile.profileId,
                checksum = profile.checksum,
                profile = TuneStore.Clone(profile)
            });
        }

        private void ReceivePayload(AlpineShareMessage message)
        {
            if (message.profile == null || string.IsNullOrWhiteSpace(message.profile.profileId))
                return;

            int profileBytes = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(message.profile, Formatting.None));
            if (profileBytes > AlpineConstants.MaxPeerProfileBytes)
            {
                StatusMessage = "Received shared tune payload exceeds profile size limit; ignored.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            if (!string.Equals(message.profileId, message.profile.profileId, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Received shared tune payload with mismatched profile id; ignored.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.checksum) &&
                !string.Equals(message.checksum, message.profile.checksum, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Received shared tune payload with mismatched checksum header; ignored.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            if (!TuneStore.TryValidateProfileForCatalog(message.profile, _mod.Catalog, true, true, out var reason))
            {
                StatusMessage = $"Received shared tune '{message.profile.name}' rejected: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            if (!IsSafePeerText(message.senderName, AlpineConstants.MaxProfileNameLength))
            {
                StatusMessage = "Received shared tune rejected: sender name invalid.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            if (!_mod.CanResolveSledTarget(message.profile.targetSledKey, message.profile.targetVehicleId))
            {
                StatusMessage = "Received shared tune rejected: incompatible target sled.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            string key = RemoteKey(message.senderId, message.profile.profileId);
            if (_remoteSummaries.TryGetValue(key, out var knownSummary) &&
                !string.IsNullOrWhiteSpace(knownSummary.checksum) &&
                !string.Equals(knownSummary.checksum, message.profile.checksum, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Received shared tune payload rejected: checksum did not match summary.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            var clone = TuneStore.Clone(message.profile);
            clone.sourceProfileId = message.profile.profileId;
            clone.sourceSenderId = message.senderId;
            clone.sourceSenderName = message.senderName;
            _remotePayloads[key] = clone;
            _pendingRequestDeadlines.Remove(key);

            if (!_remoteSummaries.TryGetValue(key, out var summary))
            {
                summary = new RemoteTuneSummary
                {
                    senderId = message.senderId,
                    senderName = message.senderName,
                    profileId = message.profile.profileId,
                    profileName = message.profile.name,
                    targetSledKey = message.profile.targetSledKey,
                    targetVehicleId = message.profile.targetVehicleId,
                    catalogVersion = message.profile.catalogVersion,
                    checksum = message.profile.checksum
                };
            }

            summary.hasPayload = true;
            summary.receivedUnixTime = NowUnix();
            summary.senderId = message.senderId;
            summary.senderName = message.senderName;
            _remoteSummaries[key] = summary;
            StatusMessage = $"Received shared tune payload '{message.profile.name}'.";

            SendToPeer(message.senderId, new AlpineShareMessage
            {
                type = "profileAck",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = message.profile.profileId,
                checksum = message.profile.checksum
            });
        }

        private bool SendToPeers(AlpineShareMessage message)
        {
            bool sent = false;
            foreach (ulong peerId in DiscoverPeerIds())
                sent |= SendToPeer(peerId, message);

            return sent;
        }

        private bool SendToPeer(ulong peerId, AlpineShareMessage message)
        {
            if (!_initialized || peerId == 0 || peerId == LocalSteamId())
                return false;

            try
            {
                var id = new SteamId { Value = peerId };
                string json = JsonConvert.SerializeObject(message, Formatting.None);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                if (bytes.Length > AlpineConstants.MaxPeerMessageBytes)
                {
                    StatusMessage = "Alpine tune packet not sent because it exceeds the size limit.";
                    MelonLogger.Warning(StatusMessage);
                    return false;
                }

                SteamNetworking.SendP2PPacket(id, bytes, bytes.Length, AlpineConstants.SteamP2PChannel, P2PSend.Reliable);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not send Alpine tune packet to {peerId}: {ex.Message}");
                StatusMessage = "Alpine tune packet send failed.";
                return false;
            }
        }

        private IEnumerable<ulong> DiscoverPeerIds()
        {
            return SleddersGameBindings.DiscoverPeerIds(LocalSteamId());
        }

        public void Shutdown()
        {
            try
            {
                if (_initialized)
                    SteamNetworking.OnP2PSessionRequest -= OnP2PSessionRequest;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine peer sharing unsubscribe skipped: {ex.Message}");
            }

            _initialized = false;
            _remoteSummaries.Clear();
            _remotePayloads.Clear();
            _publishedProfiles.Clear();
            _pendingRequestDeadlines.Clear();
            StatusMessage = "Peer sharing shut down.";
        }

        private void CheckRequestTimeouts()
        {
            if (_pendingRequestDeadlines.Count == 0)
                return;

            float now = UnityEngine.Time.unscaledTime;
            var expired = _pendingRequestDeadlines
                .Where(pair => now >= pair.Value)
                .Select(pair => pair.Key)
                .ToList();

            foreach (string key in expired)
                _pendingRequestDeadlines.Remove(key);

            if (expired.Count > 0)
            {
                StatusMessage = "Shared tune payload request timed out.";
                MelonLogger.Warning(StatusMessage);
            }
        }

        private bool TryValidateSummary(AlpineShareMessage message, out string reason)
        {
            reason = null;

            if (message == null || message.summary == null)
            {
                reason = "summary missing";
                return false;
            }

            var summary = message.summary;
            if (message.senderId == 0)
            {
                reason = "sender missing";
                return false;
            }

            if (!IsSafePeerProfileId(summary.profileId) ||
                !string.Equals(summary.profileId, message.profileId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "profile id invalid";
                return false;
            }

            if (!string.Equals(summary.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"incompatible catalog {summary.catalogVersion ?? "(missing)"}";
                return false;
            }

            if (!IsChecksumShape(summary.checksum) ||
                !string.Equals(summary.checksum, message.checksum, StringComparison.OrdinalIgnoreCase))
            {
                reason = "checksum invalid";
                return false;
            }

            if (!IsSafePeerText(summary.profileName, AlpineConstants.MaxProfileNameLength) ||
                !IsSafePeerText(summary.senderName, AlpineConstants.MaxProfileNameLength) ||
                !IsSafePeerText(message.senderName, AlpineConstants.MaxProfileNameLength))
            {
                reason = "summary text invalid";
                return false;
            }

            if (!_mod.CanResolveSledTarget(summary.targetSledKey, summary.targetVehicleId))
            {
                reason = "target sled incompatible";
                return false;
            }

            return true;
        }

        private static string RemoteKey(ulong senderId, string profileId)
        {
            return senderId.ToString() + "|" + (profileId ?? string.Empty);
        }

        private static bool IsSafePeerProfileId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > AlpineConstants.MaxProfileIdLength)
                return false;

            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            }

            return true;
        }

        private static bool IsSafePeerText(string value, int maxLength)
        {
            if (value == null)
                return true;

            if (value.Length > maxLength)
                return false;

            foreach (char c in value)
            {
                if (char.IsControl(c))
                    return false;
            }

            return true;
        }

        private static bool IsChecksumShape(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            foreach (char c in value)
            {
                bool hex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');

                if (!hex)
                    return false;
            }

            return true;
        }

        private void OnP2PSessionRequest(SteamId steamId)
        {
            try
            {
                SteamNetworking.AcceptP2PSessionWithUser(steamId);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not accept Alpine P2P session: {ex.Message}");
            }
        }

        private ulong LocalSteamId()
        {
            try
            {
                return SteamClient.IsValid ? SteamClient.SteamId.Value : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsSteamValid()
        {
            try
            {
                return SteamClient.IsValid;
            }
            catch
            {
                return false;
            }
        }

        private void LogUnavailableOnce(string message)
        {
            StatusMessage = message;
            if (_loggedUnavailable)
                return;

            _loggedUnavailable = true;
            MelonLogger.Warning(message);
        }

        private static long NowUnix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
