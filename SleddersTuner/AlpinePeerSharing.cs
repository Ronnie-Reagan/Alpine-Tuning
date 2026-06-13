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
        private const float PeerDiagIntervalSeconds = 30f;
        private const float PeerHelloIntervalSeconds = 30f;

        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<string, RemoteTuneSummary> _remoteSummaries = new Dictionary<string, RemoteTuneSummary>();
        private readonly Dictionary<string, TuneProfile> _remotePayloads = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, TuneProfile> _publishedProfiles = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, float> _pendingRequestDeadlines = new Dictionary<string, float>();
        private readonly Dictionary<ulong, RemoteActiveTuneState> _remoteActiveStates = new Dictionary<ulong, RemoteActiveTuneState>();
        private readonly Dictionary<ulong, RemotePeerState> _remotePeers = new Dictionary<ulong, RemotePeerState>();
        private readonly Dictionary<string, TuneProfile> _remoteActivePayloads = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, float> _pendingActiveRequestDeadlines = new Dictionary<string, float>();
        private readonly Dictionary<ulong, string> _lastRemoteApplyStatus = new Dictionary<ulong, string>();
        private readonly AlpineSleddersTransport _internalTransport;
        private TuneProfile _activeProfile;
        private bool _initialized;
        private bool _loggedUnavailable;
        private float _nextPeerDiagTime;
        private float _nextHelloTime;
        private float _nextRemoteApplyTime;
        private float _nextSteamP2PBlockWarningTime;
        private AlpinePeerTransportMode _selectedTransportMode = AlpinePeerTransportMode.Disabled;
        private string _lastSteamP2PBlockReason = "none";

        public AlpinePeerSharing(AlpineTuningMod mod)
        {
            _mod = mod;
            _internalTransport = new AlpineSleddersTransport(ProcessInternalPayload);
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
        public IEnumerable<RemoteActiveTuneState> RemoteActiveTunes => _remoteActiveStates.Values;
        public IEnumerable<RemotePeerState> RemotePeers => _remotePeers.Values;
        public bool IsAvailable => !AlpineConstants.PeerSharingTemporarilyDisabled && _initialized && IsSteamValid();
        public string StatusMessage { get; private set; }
        public AlpinePeerTransportMode SelectedTransportMode => _selectedTransportMode;

        public void Initialize()
        {
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                _selectedTransportMode = AlpinePeerTransportMode.Disabled;
                return;
            }

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
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                _selectedTransportMode = AlpinePeerTransportMode.Disabled;
                return;
            }

            if (!_initialized)
                Initialize();

            if (!_initialized)
                return;

            _internalTransport.Update();
            UpdateSelectedTransportMode();

            if (UnityEngine.Time.unscaledTime >= _nextPeerDiagTime)
            {
                _nextPeerDiagTime = UnityEngine.Time.unscaledTime + PeerDiagIntervalSeconds;
                LogPeerTransportDiagnostics();
            }
            PollPackets();
            CheckRequestTimeouts();

            if (UnityEngine.Time.unscaledTime >= _nextRemoteApplyTime)
            {
                _nextRemoteApplyTime = UnityEngine.Time.unscaledTime + 2f;
                TryApplyRemoteActiveTunes(false);
            }

            if (UnityEngine.Time.unscaledTime >= _nextHelloTime)
            {
                _nextHelloTime = UnityEngine.Time.unscaledTime + PeerHelloIntervalSeconds;
                BroadcastHello();
            }
        }

        public bool BroadcastHello()
        {
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

            if (!_initialized)
            {
                StatusMessage = "Peer sharing unavailable.";
                return false;
            }

            var peers = DiscoverPeers().ToArray();
            if (!HasReachablePeer(peers))
            {
                StatusMessage = "Peer sharing waiting for a reachable Alpine transport.";
                if (peers.Any(p => p != null && p.hasInternalClientId))
                {
                    _lastSteamP2PBlockReason =
                        "Sledders internal peers discovered, but Alpine internal host relay is unavailable.";
                }
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

            if (_activeProfile != null)
                sent |= SendActiveSummary(_activeProfile);

            StatusMessage = sent ? "Peer discovery hello sent." : "No lobby peers discovered for sharing.";
            return sent;
        }

        public bool PublishProfile(TuneProfile profile)
        {
            if (profile == null)
                return false;

            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

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

        public bool BroadcastActiveTune(TuneProfile profile)
        {
            if (profile == null)
                return false;

            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

            if (!_initialized)
            {
                StatusMessage = "Peer sharing unavailable.";
                return false;
            }

            var clone = TuneStore.Clone(profile);
            clone.checksum = null;
            if (!TuneStore.TryValidateProfileForCatalog(clone, _mod.Catalog, false, false, out var reason))
            {
                StatusMessage = $"Active tune not broadcast: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return false;
            }

            clone.checksum = TuneStore.ComputeChecksum(clone);
            if (!TuneStore.TryValidateProfileForCatalog(clone, _mod.Catalog, true, true, out reason))
            {
                StatusMessage = $"Active tune not broadcast: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return false;
            }

            _activeProfile = TuneStore.Clone(clone);
            bool sent = SendActiveSummary(_activeProfile);
            StatusMessage = sent ? "Broadcast active Alpine tune." : "Active tune is ready, but no peers were discovered.";

            if (sent)
                MelonLogger.Msg($"Alpine active tune broadcast sent: {clone.name} ({clone.profileId}).");

            return sent;
        }

        public bool BroadcastActiveTuneClear(string sledKey, string vehicleId)
        {
            _activeProfile = null;
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

            if (!_initialized)
            {
                StatusMessage = "Peer sharing unavailable.";
                return false;
            }

            var state = new RemoteActiveTuneState
            {
                senderId = LocalSteamId(),
                senderName = LocalName,
                targetSledKey = sledKey,
                targetVehicleId = vehicleId,
                lastSeenUnixTime = NowUnix(),
                applyStatus = "cleared"
            };

            bool sent = SendToPeers(new AlpineShareMessage
            {
                type = "activeTuneClear",
                senderId = state.senderId,
                senderName = state.senderName,
                activeState = state
            });

            StatusMessage = sent ? "Broadcast active Alpine tune clear." : "No lobby peers discovered for active tune clear.";
            if (sent)
                MelonLogger.Msg("Alpine active tune clear broadcast sent.");

            return sent;
        }

        public bool RequestProfile(ulong peerId, string profileId)
        {
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

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

        public bool RequestActiveTune(ulong peerId, string profileId, string checksum = null)
        {
            if (AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                StatusMessage = AlpineConstants.PeerSharingPausedNotice;
                return false;
            }

            if (!_initialized || peerId == 0 || string.IsNullOrWhiteSpace(profileId))
            {
                StatusMessage = "Active tune request failed: sharing unavailable or invalid peer.";
                return false;
            }

            bool sent = SendToPeer(peerId, new AlpineShareMessage
            {
                type = "activeTuneRequest",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = profileId,
                checksum = checksum
            });

            string key = ActivePayloadKey(peerId, profileId, checksum);
            if (sent)
            {
                _pendingActiveRequestDeadlines[key] = UnityEngine.Time.unscaledTime + 10f;
                if (_remoteActiveStates.TryGetValue(peerId, out var state))
                    state.payloadRequested = true;

                StatusMessage = "Remote active tune payload requested.";
                MelonLogger.Msg($"Requested Alpine active tune payload from {peerId}.");
            }
            else
            {
                StatusMessage = "Remote active tune payload request failed.";
            }

            return sent;
        }

        public bool TryGetRemoteActivePayload(ulong peerId, out TuneProfile profile)
        {
            profile = null;
            if (peerId == 0 || !_remoteActiveStates.TryGetValue(peerId, out var state) || state == null)
                return false;

            string key = ActivePayloadKey(peerId, state.profileId, state.checksum);
            if (!_remoteActivePayloads.TryGetValue(key, out var cached) || cached == null)
                return false;

            profile = TuneStore.Clone(cached);
            return true;
        }

        public bool TryGetRemoteActiveState(ulong peerId, out RemoteActiveTuneState state)
        {
            state = null;
            return peerId != 0 && _remoteActiveStates.TryGetValue(peerId, out state) && state != null;
        }

        private void AttachSourceMetadata(TuneProfile profile, ulong senderId, string sourceProfileId)
        {
            if (profile == null)
                return;

            profile.sourceProfileId = sourceProfileId;
            profile.sourceSenderId = senderId;
            if (senderId != 0 &&
                _remoteSummaries.TryGetValue(RemoteKey(senderId, sourceProfileId), out var summary) &&
                summary != null)
            {
                profile.sourceSenderName = summary.senderName;
            }
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
            if (profile == null)
                return null;

            var clone = TuneStore.Clone(profile);
            AttachSourceMetadata(clone, ParseRemoteKeySender(match.Key), profile.profileId);
            return clone;
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

            var clone = TuneStore.Clone(profile);
            AttachSourceMetadata(clone, senderId, profile.profileId);
            return clone;
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

        private bool SendActiveSummary(TuneProfile profile)
        {
            if (!_initialized || profile == null)
                return false;

            var state = new RemoteActiveTuneState
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
                shareSetup = _mod.Settings.shareMySetup || _mod.Settings.alwaysShareMySetup,
                shareLighting = _mod.Settings.shareLighting,
                shareAudio = _mod.Settings.shareAudio,
                shareVisualEquipment = _mod.Settings.shareVisualEquipment,
                lastSeenUnixTime = NowUnix(),
                applyStatus = "broadcast"
            };

            return SendToPeers(new AlpineShareMessage
            {
                type = "activeTuneSummary",
                senderId = state.senderId,
                senderName = state.senderName,
                profileId = profile.profileId,
                checksum = profile.checksum,
                activeState = state
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
                if (!LooksLikeSteam64(packetSender))
                {
                    MelonLogger.Warning($"[AlpinePeerDiag] Ignored Steam P2P packet from non-Steam64 sender {packetSender}.");
                    return;
                }

                if (message.senderId != 0 && message.senderId != packetSender)
                {
                    StatusMessage = "Ignored peer tune packet with mismatched sender identity.";
                    MelonLogger.Warning(StatusMessage);
                    return;
                }

                message.senderId = packetSender;
                message.senderSteamId = packetSender;
                ProcessShareMessage(message, packetSender, "steamP2P");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine peer packet ignored: {ex.Message}");
            }
        }

        private void ProcessInternalPayload(ulong transportSenderId, string json, string source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json) ||
                    Encoding.UTF8.GetByteCount(json) > AlpineConstants.MaxPeerMessageBytes)
                {
                    StatusMessage = "Ignored internal Alpine packet with invalid size.";
                    MelonLogger.Warning(StatusMessage);
                    return;
                }

                var message = JsonConvert.DeserializeObject<AlpineShareMessage>(json);
                if (message == null || message.magic != "ALPINE_TUNE")
                    return;

                ulong senderId = message.senderSleddersClientId != 0
                    ? message.senderSleddersClientId
                    : (message.senderId != 0 ? message.senderId : transportSenderId);

                if (senderId == 0)
                    return;

                message.senderId = senderId;
                message.senderSleddersClientId = senderId;
                ProcessShareMessage(message, senderId, "sleddersInternal:" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Alpine internal peer packet ignored: {ex.Message}");
            }
        }

        private void ProcessShareMessage(AlpineShareMessage message, ulong senderId, string source)
        {
            if (message == null)
                return;

            ulong localSleddersId = SleddersGameBindings.GetLocalSleddersClientId();
            if (message.targetSleddersClientId != 0 &&
                localSleddersId != 0 &&
                message.targetSleddersClientId != localSleddersId)
            {
                return;
            }

            if (message.schemaVersion != AlpineConstants.SchemaVersion)
            {
                StatusMessage = $"Ignored peer tune packet with schema {message.schemaVersion}.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            message.senderId = senderId;
            if (message.summary != null)
                message.summary.senderId = senderId;
            if (message.activeState != null)
                message.activeState.senderId = senderId;

            TouchPeer(senderId, message.senderName, message.type);
            MelonLogger.Msg($"[AlpinePeerDiag] Alpine message received: source={source}, senderSleddersClientId={message.senderSleddersClientId}, senderSteamId={message.senderSteamId}, type={message.type ?? "NULL"}");

            switch (message.type)
            {
                case "hello":
                    TouchPeer(message.senderId, message.senderName, "hello");
                    foreach (var profile in _publishedProfiles.Values.ToList())
                        SendSummary(profile);
                    if (_activeProfile != null)
                        SendActiveSummary(_activeProfile);
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

                case "activeTuneSummary":
                    ReceiveActiveSummary(message);
                    break;

                case "activeTuneRequest":
                    SendRequestedActivePayload(message.senderId, message.profileId, message.checksum);
                    break;

                case "activeTunePayload":
                    ReceiveActivePayload(message);
                    break;

                case "activeTuneClear":
                    ReceiveActiveClear(message);
                    break;

                case "profileAck":
                    break;
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
            TouchPeer(summary.senderId, summary.senderName, "shared setup seen");
        }

        private void TouchPeer(ulong senderId, string senderName, string status)
        {
            ulong localSleddersId = SleddersGameBindings.GetLocalSleddersClientId();
            if (senderId == 0 || senderId == LocalSteamId() || (localSleddersId != 0 && senderId == localSleddersId))
                return;

            long now = NowUnix();
            if (!_remotePeers.TryGetValue(senderId, out var peer) || peer == null)
            {
                peer = new RemotePeerState
                {
                    senderId = senderId,
                    firstSeenUnixTime = now,
                    modDetected = true
                };
            }

            if (LooksLikeSteam64(senderId))
                peer.steamId = senderId;
            else
                peer.sleddersClientId = senderId;

            peer.senderName = string.IsNullOrWhiteSpace(senderName) ? peer.senderName : senderName;
            peer.lastSeenUnixTime = now;
            peer.status = status;
            peer.sharingEnabled = _remoteActiveStates.ContainsKey(senderId) ||
                                  _remoteSummaries.Keys.Any(k => k.StartsWith(senderId + "|", StringComparison.OrdinalIgnoreCase));

            if (_remoteActiveStates.TryGetValue(senderId, out var active) && active != null)
                peer.activeSetupName = active.profileName;

            _remotePeers[senderId] = peer;
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

        private void ReceiveActiveSummary(AlpineShareMessage message)
        {
            if (!TryValidateActiveState(message, out var reason))
            {
                StatusMessage = $"Ignored remote active tune summary: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            var incoming = message.activeState;
            bool changed = !_remoteActiveStates.TryGetValue(message.senderId, out var existing) ||
                           existing == null ||
                           !string.Equals(existing.checksum, incoming.checksum, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(existing.profileId, incoming.profileId, StringComparison.OrdinalIgnoreCase);

            incoming.senderId = message.senderId;
            incoming.senderName = string.IsNullOrWhiteSpace(message.senderName) ? incoming.senderName : message.senderName;
            incoming.lastSeenUnixTime = NowUnix();

            string payloadKey = ActivePayloadKey(incoming.senderId, incoming.profileId, incoming.checksum);
            incoming.hasPayload = _remoteActivePayloads.ContainsKey(payloadKey);
            incoming.payloadRequested = _pendingActiveRequestDeadlines.ContainsKey(payloadKey);

            if (!incoming.hasPayload)
            {
                incoming.applyStatus = incoming.payloadRequested
                    ? "payload requested"
                    : "summary received";
            }
            else if (string.IsNullOrWhiteSpace(incoming.applyStatus))
            {
                incoming.applyStatus = existing != null ? existing.applyStatus : "payload received";
            }

            _remoteActiveStates[incoming.senderId] = incoming;
            TouchPeer(incoming.senderId, incoming.senderName, "active setup seen");

            if (changed)
                MelonLogger.Msg($"Remote Alpine active tune summary received from {incoming.senderName ?? incoming.senderId.ToString()}: {incoming.profileName}.");

            if (!incoming.hasPayload && !incoming.payloadRequested)
                RequestActiveTune(incoming.senderId, incoming.profileId, incoming.checksum);
            else if (incoming.hasPayload && _remoteActivePayloads.TryGetValue(payloadKey, out var profile))
                TryApplyRemoteActiveTune(incoming, profile, true);
        }

        private void SendRequestedActivePayload(ulong peerId, string profileId, string checksum)
        {
            if (peerId == 0)
                return;

            if (_activeProfile == null)
            {
                SendToPeer(peerId, new AlpineShareMessage
                {
                    type = "activeTuneClear",
                    senderId = LocalSteamId(),
                    senderName = LocalName
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(profileId) &&
                !string.Equals(profileId, _activeProfile.profileId, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Requested active tune payload does not match local active tune.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(checksum) &&
                !string.Equals(checksum, _activeProfile.checksum, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Requested active tune checksum does not match local active tune.";
                return;
            }

            SendToPeer(peerId, new AlpineShareMessage
            {
                type = "activeTunePayload",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = _activeProfile.profileId,
                checksum = _activeProfile.checksum,
                profile = TuneStore.Clone(_activeProfile)
            });
        }

        private void ReceiveActivePayload(AlpineShareMessage message)
        {
            if (!TryValidateActivePayload(message, out var reason))
            {
                StatusMessage = $"Ignored remote active tune payload: {reason}.";
                MelonLogger.Warning(StatusMessage);
                return;
            }

            var clone = TuneStore.Clone(message.profile);

            string payloadKey = ActivePayloadKey(message.senderId, clone.profileId, clone.checksum);
            _remoteActivePayloads[payloadKey] = clone;
            _pendingActiveRequestDeadlines.Remove(ActivePayloadKey(message.senderId, clone.profileId, clone.checksum));

            if (!_remoteActiveStates.TryGetValue(message.senderId, out var state) || state == null)
            {
                state = new RemoteActiveTuneState
                {
                    senderId = message.senderId,
                    senderName = message.senderName,
                    profileId = clone.profileId,
                    profileName = clone.name,
                    targetSledKey = clone.targetSledKey,
                    targetVehicleId = clone.targetVehicleId,
                    catalogVersion = clone.catalogVersion,
                    checksum = clone.checksum,
                    shareSetup = true,
                    shareLighting = true,
                    shareAudio = true
                };
            }

            state.senderId = message.senderId;
            state.senderName = message.senderName;
            state.profileId = clone.profileId;
            state.profileName = clone.name;
            state.targetSledKey = clone.targetSledKey;
            state.targetVehicleId = clone.targetVehicleId;
            state.catalogVersion = clone.catalogVersion;
            state.checksum = clone.checksum;
            state.hasPayload = true;
            state.payloadRequested = false;
            state.lastSeenUnixTime = NowUnix();
            state.applyStatus = "payload received";
            _remoteActiveStates[message.senderId] = state;
            TouchPeer(message.senderId, message.senderName, "active setup received");

            StatusMessage = $"Received remote active tune payload '{clone.name}'.";
            MelonLogger.Msg($"Remote Alpine active tune payload received from {message.senderName ?? message.senderId.ToString()}: {clone.name}.");
            TryApplyRemoteActiveTune(state, clone, true);
        }

        private void ReceiveActiveClear(AlpineShareMessage message)
        {
            if (message.senderId == 0)
                return;

            _remoteActiveStates.Remove(message.senderId);
            foreach (string key in _remoteActivePayloads.Keys.Where(k => k.StartsWith(message.senderId + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                _remoteActivePayloads.Remove(key);

            foreach (string key in _pendingActiveRequestDeadlines.Keys.Where(k => k.StartsWith(message.senderId + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                _pendingActiveRequestDeadlines.Remove(key);

            _lastRemoteApplyStatus.Remove(message.senderId);
            _mod.RemoteReplication?.ClearSender(message.senderId);
            if (_remotePeers.TryGetValue(message.senderId, out var peer) && peer != null)
            {
                peer.sharingEnabled = false;
                peer.activeSetupName = null;
                peer.status = "sharing off";
                peer.lastSeenUnixTime = NowUnix();
            }
            StatusMessage = "Remote active Alpine tune cleared.";
            MelonLogger.Msg($"Remote Alpine active tune cleared by {message.senderId}.");
        }

        private bool TryApplyRemoteActiveTune(RemoteActiveTuneState state, TuneProfile profile, bool logStatus)
        {
            if (state == null || profile == null)
                return false;

            bool applied = _mod.TryApplyRemoteRuntimeTune(state.senderId, profile, out var status);
            state.applyStatus = string.IsNullOrWhiteSpace(status)
                ? (applied ? "applied" : "waiting for remote sled")
                : status;

            if (applied)
                state.lastAppliedUnixTime = NowUnix();

            _remoteActiveStates[state.senderId] = state;

            if (logStatus &&
                (!_lastRemoteApplyStatus.TryGetValue(state.senderId, out var last) ||
                 !string.Equals(last, state.applyStatus, StringComparison.OrdinalIgnoreCase)))
            {
                _lastRemoteApplyStatus[state.senderId] = state.applyStatus;
                if (applied)
                    MelonLogger.Msg($"Remote Alpine tune applied for {state.senderName ?? state.senderId.ToString()}: {state.applyStatus}");
                else
                    MelonLogger.Msg($"Remote Alpine tune waiting for {state.senderName ?? state.senderId.ToString()}: {state.applyStatus}");
            }

            StatusMessage = state.applyStatus;
            return applied;
        }

        private void TryApplyRemoteActiveTunes(bool logStatus)
        {
            foreach (var state in _remoteActiveStates.Values.ToList())
            {
                if (state == null || !state.hasPayload)
                    continue;

                string key = ActivePayloadKey(state.senderId, state.profileId, state.checksum);
                if (_remoteActivePayloads.TryGetValue(key, out var profile))
                    TryApplyRemoteActiveTune(state, profile, logStatus);
            }
        }

        private bool SendToPeers(AlpineShareMessage message)
        {
            var peers = DiscoverPeers().ToArray();
            bool hasInternalPeers = peers.Any(p => p != null && p.hasInternalClientId);
            bool hasSteamPeers = peers.Any(p => p != null && p.hasSteamId);

            if (_internalTransport.CanSend && hasInternalPeers)
                return _internalTransport.Send(message, 0, true);

            if (hasInternalPeers && !_internalTransport.CanSend && !hasSteamPeers)
            {
                _lastSteamP2PBlockReason =
                    $"Sledders internal peers discovered, but Alpine internal host relay is unavailable; type={message?.type ?? "NULL"}";
                return false;
            }

            bool sent = false;
            foreach (var peer in peers)
            {
                if (peer == null)
                    continue;

                if (peer.hasSteamId)
                    sent |= SendToSteamPeer(peer.steamId, message);
                else if (peer.hasInternalClientId)
                    BlockSteamForInternalId(peer.sleddersClientId, message?.type, false);
            }

            return sent;
        }

        private bool HasReachablePeer(AlpineDiscoveredPeer[] peers)
        {
            if (peers == null || peers.Length == 0)
                return false;

            return peers.Any(p =>
                p != null &&
                (p.hasSteamId || (p.hasInternalClientId && _internalTransport.CanSend)));
        }

        private bool SendToPeer(ulong peerId, AlpineShareMessage message)
        {
            ulong localSteamId = LocalSteamId();
            ulong localSleddersId = SleddersGameBindings.GetLocalSleddersClientId();

            if (!_initialized)
            {
                MelonLogger.Warning($"[AlpinePeerDiag] SendToPeer blocked: not initialized. peerId={peerId}, type={message?.type ?? "NULL"}");
                return false;
            }

            if (peerId == 0)
            {
                MelonLogger.Warning($"[AlpinePeerDiag] SendToPeer blocked: peerId is zero. type={message?.type ?? "NULL"}");
                return false;
            }

            if (peerId == localSteamId || (localSleddersId != 0 && peerId == localSleddersId))
            {
                MelonLogger.Msg($"[AlpinePeerDiag] SendToPeer blocked: peerId is local player. peerId={peerId}, type={message?.type ?? "NULL"}");
                return false;
            }

            if (_internalTransport.CanSend && !LooksLikeSteam64(peerId))
                return _internalTransport.Send(message, peerId, false);

            if (LooksLikeSteam64(peerId))
                return SendToSteamPeer(peerId, message);

            BlockSteamForInternalId(peerId, message?.type, true);
            return false;
        }

        private bool SendToSteamPeer(ulong steamId, AlpineShareMessage message)
        {
            ulong localId = LocalSteamId();

            if (!LooksLikeSteam64(steamId))
            {
                BlockSteamForInternalId(steamId, message?.type, true);
                return false;
            }

            if (steamId == localId)
            {
                MelonLogger.Msg($"[AlpinePeerDiag] Steam send blocked: peerId is local Steam ID. peer={steamId}, type={message?.type ?? "NULL"}");
                return false;
            }

            try
            {
                var id = new SteamId { Value = steamId };
                string json = JsonConvert.SerializeObject(message, Formatting.None);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                if (bytes.Length > AlpineConstants.MaxPeerMessageBytes)
                {
                    StatusMessage = "Alpine tune packet not sent because it exceeds the size limit.";
                    MelonLogger.Warning($"[AlpinePeerDiag] Steam send blocked: packet too large. bytes={bytes.Length}, max={AlpineConstants.MaxPeerMessageBytes}");
                    return false;
                }

                bool accepted = SteamNetworking.SendP2PPacket(
                    id,
                    bytes,
                    bytes.Length,
                    AlpineConstants.SteamP2PChannel,
                    P2PSend.Reliable);

                MelonLogger.Msg(
                    $"[AlpinePeerDiag] Steam SendP2PPacket result: accepted={accepted}, type={message?.type ?? "NULL"}, steamPeer={steamId}");

                if (!accepted)
                {
                    StatusMessage = "Alpine tune packet send rejected by Steam.";
                    MelonLogger.Warning($"[AlpinePeerDiag] Steam rejected packet. steamPeer={steamId}, type={message?.type ?? "NULL"}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not send Alpine tune packet to Steam peer {steamId}: {ex.GetType().Name}: {ex.Message}");
                StatusMessage = "Alpine tune packet send failed.";
                return false;
            }
        }

        private void BlockSteamForInternalId(ulong sleddersClientId, string messageType, bool warn)
        {
            _lastSteamP2PBlockReason =
                $"Steam P2P disabled for Sledders internal client id {sleddersClientId}; type={messageType ?? "NULL"}";

            if (!warn)
                return;

            if (UnityEngine.Time.unscaledTime < _nextSteamP2PBlockWarningTime)
                return;

            _nextSteamP2PBlockWarningTime = UnityEngine.Time.unscaledTime + 30f;
            MelonLogger.Warning("[AlpinePeerDiag] " + _lastSteamP2PBlockReason);
        }

        private IEnumerable<ulong> DiscoverPeerIds()
        {
            return SleddersGameBindings.DiscoverPeerIds(LocalSteamId());
        }

        private IEnumerable<AlpineDiscoveredPeer> DiscoverPeers()
        {
            return SleddersGameBindings.DiscoverPeers(LocalSteamId(), false);
        }

        private void UpdateSelectedTransportMode()
        {
            if (!_initialized)
            {
                _selectedTransportMode = AlpinePeerTransportMode.Disabled;
                return;
            }

            var peers = DiscoverPeers().ToArray();
            if (_internalTransport.CanSend && peers.Any(p => p != null && p.hasInternalClientId))
            {
                _selectedTransportMode = AlpinePeerTransportMode.SleddersInternal;
                return;
            }

            if (peers.Any(p => p != null && p.hasSteamId))
            {
                _selectedTransportMode = AlpinePeerTransportMode.SteamP2P;
                return;
            }

            _selectedTransportMode = AlpinePeerTransportMode.DiagnosticsOnly;
        }

        private void LogPeerTransportDiagnostics()
        {
            try
            {
                bool steamValid = false;
                ulong localId = 0;
                string localName = null;
                bool packetAvailable = false;

                try
                {
                    steamValid = SteamClient.IsValid;
                    if (steamValid)
                    {
                        localId = SteamClient.SteamId.Value;
                        localName = SteamClient.Name;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AlpinePeerDiag] SteamClient read failed: {ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    packetAvailable = SteamNetworking.IsP2PPacketAvailable(AlpineConstants.SteamP2PChannel);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AlpinePeerDiag] IsP2PPacketAvailable failed: {ex.GetType().Name}: {ex.Message}");
                }

                MelonLogger.Msg("========== ALPINE PEER TRANSPORT DIAG ==========");
                MelonLogger.Msg($"[AlpinePeerDiag] initialized={_initialized}");
                MelonLogger.Msg($"[AlpinePeerDiag] steamValid={steamValid}");
                MelonLogger.Msg($"[AlpinePeerDiag] localSteamId={localId}");
                MelonLogger.Msg($"[AlpinePeerDiag] localName={localName ?? "NULL"}");
                MelonLogger.Msg($"[AlpinePeerDiag] localSleddersClientId={SleddersGameBindings.GetLocalSleddersClientId()}");
                MelonLogger.Msg($"[AlpinePeerDiag] localSleddersName={SleddersGameBindings.GetNetClientNickname(SleddersGameBindings.GetLocalSleddersClientId()) ?? "NULL"}");
                MelonLogger.Msg($"[AlpinePeerDiag] p2pChannel={AlpineConstants.SteamP2PChannel}");
                MelonLogger.Msg($"[AlpinePeerDiag] incomingPacketAvailableOnChannel={packetAvailable}");
                MelonLogger.Msg($"[AlpinePeerDiag] bindingCapability={SleddersGameBindings.CapabilitySummary}");
                MelonLogger.Msg($"[AlpinePeerDiag] selectedTransportMode={_selectedTransportMode}");
                MelonLogger.Msg($"[AlpinePeerDiag] netClient.netInterface.ready={_internalTransport.ClientReady}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.ready={_internalTransport.IsReady}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.canSend={_internalTransport.CanSend}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.hostRelayReady={_internalTransport.HostRelayReady}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.binding={_internalTransport.BindingStatus}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.lastSend={_internalTransport.LastSendStatus}");
                MelonLogger.Msg($"[AlpinePeerDiag] internalTransport.lastReceive={_internalTransport.LastReceiveStatus}");
                MelonLogger.Msg($"[AlpinePeerDiag] steamP2PBlock={_lastSteamP2PBlockReason}");

                var peers = SleddersGameBindings.DiscoverPeers(localId, false);
                MelonLogger.Msg($"[AlpinePeerDiag] DiscoverPeers returned {peers.Length} peer(s).");

                foreach (var peer in peers)
                {
                    if (peer == null)
                        continue;

                    MelonLogger.Msg(
                        $"[AlpinePeerDiag] remotePeer sleddersClientId={(peer.hasInternalClientId ? peer.sleddersClientId.ToString() : "none")}, " +
                        $"steamId={(peer.hasSteamId ? peer.steamId.ToString() : "none")}, " +
                        $"nick={peer.name ?? "NULL"}");
                }

                if (peers.Length == 0)
                    MelonLogger.Warning("[AlpinePeerDiag] ZERO remote peer IDs found. HELLO cannot be sent until NetClient reports remote Sledders client IDs.");

                MelonLogger.Msg("========== ALPINE PEER TRANSPORT END ==========");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AlpinePeerDiag] LogPeerTransportDiagnostics failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        public void Shutdown()
        {
            try
            {
                if (_initialized)
                    SteamNetworking.OnP2PSessionRequest -= OnP2PSessionRequest;
                _internalTransport.Shutdown();
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
            _remoteActiveStates.Clear();
            _remotePeers.Clear();
            _remoteActivePayloads.Clear();
            _pendingActiveRequestDeadlines.Clear();
            _lastRemoteApplyStatus.Clear();
            _activeProfile = null;
            StatusMessage = "Peer sharing shut down.";
        }

        private void CheckRequestTimeouts()
        {
            if (_pendingRequestDeadlines.Count == 0 && _pendingActiveRequestDeadlines.Count == 0)
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

            var expiredActive = _pendingActiveRequestDeadlines
                .Where(pair => now >= pair.Value)
                .Select(pair => pair.Key)
                .ToList();

            foreach (string key in expiredActive)
                _pendingActiveRequestDeadlines.Remove(key);

            foreach (string key in expiredActive)
            {
                ulong senderId = ParseRemoteKeySender(key);
                if (senderId != 0 && _remoteActiveStates.TryGetValue(senderId, out var state))
                {
                    state.payloadRequested = false;
                    state.applyStatus = "active tune payload request timed out";
                }
            }

            if (expiredActive.Count > 0)
            {
                StatusMessage = "Remote active tune payload request timed out.";
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

            if (!HasUsableSledIdentity(summary.targetSledKey, summary.targetVehicleId))
            {
                reason = "target sled identity invalid";
                return false;
            }

            if (!_mod.CanResolveSledTarget(summary.targetSledKey, summary.targetVehicleId))
            {
                reason = "target sled incompatible";
                return false;
            }

            return true;
        }

        private bool TryValidateActiveState(AlpineShareMessage message, out string reason)
        {
            reason = null;

            if (message == null || message.activeState == null)
            {
                reason = "active summary missing";
                return false;
            }

            var state = message.activeState;
            if (message.senderId == 0)
            {
                reason = "sender missing";
                return false;
            }

            if (state.senderId != 0 && state.senderId != message.senderId)
            {
                reason = "sender mismatch";
                return false;
            }

            if (!IsSafePeerProfileId(state.profileId) ||
                !string.Equals(state.profileId, message.profileId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "profile id invalid";
                return false;
            }

            if (!string.Equals(state.catalogVersion, AlpineConstants.CatalogVersion, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"incompatible catalog {state.catalogVersion ?? "(missing)"}";
                return false;
            }

            if (!IsChecksumShape(state.checksum) ||
                !string.Equals(state.checksum, message.checksum, StringComparison.OrdinalIgnoreCase))
            {
                reason = "checksum invalid";
                return false;
            }

            if (!IsSafePeerText(state.profileName, AlpineConstants.MaxProfileNameLength) ||
                !IsSafePeerText(state.senderName, AlpineConstants.MaxProfileNameLength) ||
                !IsSafePeerText(message.senderName, AlpineConstants.MaxProfileNameLength))
            {
                reason = "active summary text invalid";
                return false;
            }

            if (!HasUsableSledIdentity(state.targetSledKey, state.targetVehicleId))
            {
                reason = "target sled identity invalid";
                return false;
            }

            if (!_mod.CanResolveSledTarget(state.targetSledKey, state.targetVehicleId))
            {
                reason = "target sled incompatible";
                return false;
            }

            return true;
        }

        private bool TryValidateActivePayload(AlpineShareMessage message, out string reason)
        {
            reason = null;

            if (message == null || message.profile == null || string.IsNullOrWhiteSpace(message.profile.profileId))
            {
                reason = "payload missing";
                return false;
            }

            if (message.senderId == 0)
            {
                reason = "sender missing";
                return false;
            }

            int profileBytes = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(message.profile, Formatting.None));
            if (profileBytes > AlpineConstants.MaxPeerProfileBytes)
            {
                reason = "profile size limit exceeded";
                return false;
            }

            if (!IsSafePeerProfileId(message.profile.profileId) ||
                !string.Equals(message.profileId, message.profile.profileId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "profile id mismatch";
                return false;
            }

            if (!IsChecksumShape(message.checksum) ||
                !string.Equals(message.checksum, message.profile.checksum, StringComparison.OrdinalIgnoreCase))
            {
                reason = "checksum header invalid";
                return false;
            }

            if (!TuneStore.TryValidateProfileForCatalog(message.profile, _mod.Catalog, true, true, out reason))
                return false;

            if (!IsSafePeerText(message.senderName, AlpineConstants.MaxProfileNameLength))
            {
                reason = "sender name invalid";
                return false;
            }

            if (!HasUsableSledIdentity(message.profile.targetSledKey, message.profile.targetVehicleId))
            {
                reason = "target sled identity invalid";
                return false;
            }

            if (!_mod.CanResolveSledTarget(message.profile.targetSledKey, message.profile.targetVehicleId))
            {
                reason = "target sled incompatible";
                return false;
            }

            if (_remoteActiveStates.TryGetValue(message.senderId, out var state) && state != null)
            {
                if (!string.Equals(state.profileId, message.profile.profileId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "profile id did not match active summary";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(state.checksum) &&
                    !string.Equals(state.checksum, message.profile.checksum, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "checksum did not match active summary";
                    return false;
                }
            }

            return true;
        }

        private static string RemoteKey(ulong senderId, string profileId)
        {
            return senderId.ToString() + "|" + (profileId ?? string.Empty);
        }

        private static string ActivePayloadKey(ulong senderId, string profileId, string checksum)
        {
            return senderId.ToString() + "|" + (profileId ?? string.Empty) + "|" + (checksum ?? string.Empty);
        }

        private static ulong ParseRemoteKeySender(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            int separator = key.IndexOf('|');
            string sender = separator >= 0 ? key.Substring(0, separator) : key;
            return ulong.TryParse(sender, out var senderId) ? senderId : 0;
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

        private static bool IsSafeSledIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Length > AlpineConstants.MaxSledIdentityLength)
                return false;

            foreach (char c in value)
            {
                if (char.IsControl(c) || c == '/' || c == '\\')
                    return false;
            }

            return true;
        }

        private static bool HasUsableSledIdentity(string sledKey, string vehicleId)
        {
            bool hasSledKey = !string.IsNullOrWhiteSpace(sledKey);
            bool hasVehicleId = !string.IsNullOrWhiteSpace(vehicleId);

            if (!hasSledKey && !hasVehicleId)
                return false;

            if (hasSledKey && !IsSafeSledIdentity(sledKey))
                return false;

            if (hasVehicleId && !IsSafeSledIdentity(vehicleId))
                return false;

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

        private static bool LooksLikeSteam64(ulong value)
        {
            return value >= 76561190000000000UL && value <= 76561210000000000UL;
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
