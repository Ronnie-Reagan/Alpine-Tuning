using MelonLoader;
using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AlpineTuning
{
    internal class AlpinePeerSharing
    {
        private readonly AlpineTuningMod _mod;
        private readonly Dictionary<string, RemoteTuneSummary> _remoteSummaries = new Dictionary<string, RemoteTuneSummary>();
        private readonly Dictionary<string, TuneProfile> _remotePayloads = new Dictionary<string, TuneProfile>();
        private readonly Dictionary<string, TuneProfile> _publishedProfiles = new Dictionary<string, TuneProfile>();
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

            if (UnityEngine.Time.unscaledTime >= _nextHelloTime)
            {
                _nextHelloTime = UnityEngine.Time.unscaledTime + 15f;
                BroadcastHello();
            }
        }

        public void BroadcastHello()
        {
            if (!_initialized)
                return;

            SendToPeers(new AlpineShareMessage
            {
                type = "hello",
                senderId = LocalSteamId(),
                senderName = LocalName
            });

            foreach (var profile in _publishedProfiles.Values.ToList())
                SendSummary(profile);
        }

        public void PublishProfile(TuneProfile profile)
        {
            if (profile == null)
                return;

            profile.checksum = TuneStore.ComputeChecksum(profile);
            _publishedProfiles[profile.profileId] = TuneStore.Clone(profile);
            SendSummary(profile);
        }

        public void RequestProfile(ulong peerId, string profileId)
        {
            if (!_initialized || peerId == 0 || string.IsNullOrWhiteSpace(profileId))
                return;

            SendToPeer(peerId, new AlpineShareMessage
            {
                type = "profileRequest",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = profileId
            });
        }

        public TuneProfile GetPayload(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return null;

            _remotePayloads.TryGetValue(profileId, out var profile);
            return profile != null ? TuneStore.Clone(profile) : null;
        }

        private void SendSummary(TuneProfile profile)
        {
            if (!_initialized || profile == null)
                return;

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

            SendToPeers(new AlpineShareMessage
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
                string json = Encoding.UTF8.GetString(packet.Data);
                var message = JsonConvert.DeserializeObject<AlpineShareMessage>(json);
                if (message == null || message.magic != "ALPINE_TUNE")
                    return;

                if (message.senderId == 0)
                    message.senderId = packet.SteamId.Value;

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
            if (message.summary == null || string.IsNullOrWhiteSpace(message.summary.profileId))
                return;

            var summary = message.summary;
            summary.senderId = message.senderId;
            summary.senderName = string.IsNullOrWhiteSpace(message.senderName) ? summary.senderName : message.senderName;
            summary.receivedUnixTime = NowUnix();
            summary.hasPayload = _remotePayloads.ContainsKey(summary.profileId);
            _remoteSummaries[summary.profileId] = summary;
        }

        private void SendRequestedPayload(ulong peerId, string profileId)
        {
            if (peerId == 0 || string.IsNullOrWhiteSpace(profileId))
                return;

            if (!_publishedProfiles.TryGetValue(profileId, out var profile))
                return;

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

            bool checksumOk = TuneStore.ChecksumMatches(message.profile);
            if (!checksumOk)
            {
                MelonLogger.Warning($"Received shared tune '{message.profile.name}' with checksum mismatch; ignored.");
                return;
            }

            _remotePayloads[message.profile.profileId] = TuneStore.Clone(message.profile);

            if (!_remoteSummaries.TryGetValue(message.profile.profileId, out var summary))
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
            _remoteSummaries[summary.profileId] = summary;

            SendToPeer(message.senderId, new AlpineShareMessage
            {
                type = "profileAck",
                senderId = LocalSteamId(),
                senderName = LocalName,
                profileId = message.profile.profileId,
                checksum = message.profile.checksum
            });
        }

        private void SendToPeers(AlpineShareMessage message)
        {
            foreach (ulong peerId in DiscoverPeerIds())
                SendToPeer(peerId, message);
        }

        private void SendToPeer(ulong peerId, AlpineShareMessage message)
        {
            if (!_initialized || peerId == 0 || peerId == LocalSteamId())
                return;

            try
            {
                var id = new SteamId { Value = peerId };
                string json = JsonConvert.SerializeObject(message, Formatting.None);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                SteamNetworking.SendP2PPacket(id, bytes, bytes.Length, AlpineConstants.SteamP2PChannel, P2PSend.Reliable);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not send Alpine tune packet to {peerId}: {ex.Message}");
            }
        }

        private IEnumerable<ulong> DiscoverPeerIds()
        {
            var ids = new HashSet<ulong>();
            ulong local = LocalSteamId();

            try
            {
                Type netClientType = Type.GetType("NetClient, Assembly-CSharp");
                PropertyInfo instanceProp = netClientType?.GetProperty("PKMPAOKMHCB", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object netClient = instanceProp?.GetValue(null);
                MethodInfo method = netClient?.GetType().GetMethod("GetAllClientIdsIncludingLocalPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var result = method?.Invoke(netClient, Array.Empty<object>()) as ulong[];
                if (result != null)
                {
                    foreach (ulong id in result)
                    {
                        if (id != 0 && id != local)
                            ids.Add(id);
                    }
                }
            }
            catch
            {
                // Peer discovery is best-effort; Steam send failures are already non-fatal.
            }

            return ids;
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

        private void LogUnavailableOnce(string message)
        {
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
