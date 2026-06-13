using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;

namespace AlpineTuning
{
    internal enum AlpinePeerTransportMode
    {
        Disabled,
        SteamP2P,
        SleddersInternal,
        DiagnosticsOnly
    }

    internal sealed class AlpineSleddersTransport
    {
        private const uint PacketMagic = 0x324C5041; // ALP2, little-endian.
        private const byte PacketKindFull = 1;
        private const byte PacketKindChunk = 2;
        private const int MaxChunkCount = 16;
        private const float ChunkTimeoutSeconds = 20f;

        private readonly Action<ulong, string, string> _onPayload;
        private readonly Dictionary<string, IncomingChunkSet> _incomingChunks =
            new Dictionary<string, IncomingChunkSet>();

        private Type _handlerDelegateType;
        private Type _deliveryType;
        private object _deliveryReliableFragmented;

        private object _clientInterface;
        private object _serverInterface;
        private object _serverReceiveInterface;
        private MethodInfo _clientNewWriterMethod;
        private MethodInfo _clientSendMethod;
        private MethodInfo _serverNewWriterMethod;
        private MethodInfo _serverSendMethod;
        private MethodInfo _serverSendListMethod;
        private MethodInfo _registerClientMethod;
        private MethodInfo _registerServerMethod;
        private MethodInfo _unregisterClientMethod;
        private MethodInfo _unregisterServerMethod;
        private Delegate _clientHandler;
        private Delegate _serverHandler;
        private bool _clientRegistered;
        private bool _serverRegistered;
        private bool _serverReceiveRegistered;
        private uint _nextSequence = 1;

        public AlpineSleddersTransport(Action<ulong, string, string> onPayload)
        {
            _onPayload = onPayload;
        }

        public bool IsReady => _clientRegistered || _serverRegistered;
        public bool ClientReady => _clientRegistered;
        public bool ServerReady => _serverRegistered;
        public bool CanSend => _serverRegistered;
        public bool HostRelayReady => _serverRegistered && _serverReceiveRegistered;
        public string BindingStatus { get; private set; } = "not bound";
        public string LastSendStatus { get; private set; } = "not sent";
        public string LastReceiveStatus { get; private set; } = "not received";

        public void Update()
        {
            EnsureBound();
            ExpireChunks();
        }

        public void Shutdown()
        {
            Unregister(_clientInterface, _unregisterClientMethod, ref _clientRegistered, "client");
            Unregister(_serverReceiveInterface, _unregisterServerMethod, ref _serverReceiveRegistered, "server");
            _serverRegistered = false;
            _clientInterface = null;
            _serverInterface = null;
            _serverReceiveInterface = null;
            _incomingChunks.Clear();
            BindingStatus = "shut down";
        }

        public bool Send(AlpineShareMessage message, ulong targetClientId, bool broadcast)
        {
            if (message == null)
            {
                LastSendStatus = "blocked: message missing";
                return false;
            }

            EnsureBound();
            if (!IsReady)
            {
                LastSendStatus = "blocked: Sledders internal transport is not bound";
                return false;
            }

            string json = SerializeForInternal(message, targetClientId);
            if (string.IsNullOrWhiteSpace(json))
            {
                LastSendStatus = "blocked: message serialization failed";
                return false;
            }

            if (_serverRegistered)
            {
                var targets = ResolveServerTargets(targetClientId, broadcast);
                if (targets.Count == 0)
                {
                    LastSendStatus = "blocked: no Sledders internal relay targets";
                    return false;
                }

                return SendJsonFromServer(json, targets);
            }

            if (_clientRegistered)
            {
                LastSendStatus =
                    "blocked: internal client-to-host send disabled until a safe Alpine server relay is bound";
                return false;
            }

            LastSendStatus = "blocked: no internal send path";
            return false;
        }

        private bool EnsureBound()
        {
            ResolveStaticTypes();

            string clientReason;
            object clientInterface;
            if (SleddersGameBindings.TryGetNetClientInterface(out clientInterface, out clientReason) &&
                clientInterface != null)
            {
                if (!ReferenceEquals(_clientInterface, clientInterface))
                {
                    _clientInterface = clientInterface;
                    _clientRegistered = false;
                    _clientNewWriterMethod = FindNoArgMethod(_clientInterface.GetType(), "MDNBFANMMHH");
                    _clientSendMethod = FindSendMethod(_clientInterface.GetType());
                    _registerClientMethod = FindRegisterMethod(_clientInterface.GetType());
                    _unregisterClientMethod = FindUnregisterMethod(_clientInterface.GetType());
                }

                if (!_clientRegistered)
                    _clientRegistered = TryRegister(_clientInterface, _registerClientMethod, false, out clientReason);
            }
            else
            {
                _clientRegistered = false;
            }

            string serverReason;
            object netServer;
            if (SleddersGameBindings.TryGetNetServer(out netServer, out serverReason) &&
                netServer != null)
            {
                if (!ReferenceEquals(_serverInterface, netServer))
                {
                    _serverInterface = netServer;
                    _serverRegistered = false;
                    _serverNewWriterMethod = FindNoArgMethod(_serverInterface.GetType(), "MDNBFANMMHH");
                    _serverSendMethod = FindServerSendMethod(_serverInterface.GetType());
                    _serverSendListMethod = FindServerSendListMethod(_serverInterface.GetType());
                }

                _serverRegistered =
                    _serverNewWriterMethod != null &&
                    (_serverSendMethod != null || _serverSendListMethod != null);

                serverReason = _serverRegistered
                    ? "ready"
                    : "NetServer.PIDJHAOLBJM send path missing";
            }
            else
            {
                _serverRegistered = false;
            }

            string serverReceiveReason;
            object serverReceiveInterface;
            if (SleddersGameBindings.TryGetNetServerInterface(out serverReceiveInterface, out serverReceiveReason) &&
                serverReceiveInterface != null)
            {
                if (!ReferenceEquals(_serverReceiveInterface, serverReceiveInterface))
                {
                    _serverReceiveInterface = serverReceiveInterface;
                    _serverReceiveRegistered = false;
                    _registerServerMethod = FindRegisterMethod(_serverReceiveInterface.GetType());
                    _unregisterServerMethod = FindUnregisterMethod(_serverReceiveInterface.GetType());
                }

                if (!_serverReceiveRegistered)
                    _serverReceiveRegistered = TryRegister(_serverReceiveInterface, _registerServerMethod, true, out serverReceiveReason);
            }
            else
            {
                _serverReceiveRegistered = false;
            }

            BindingStatus =
                $"client={(_clientRegistered ? "ready" : clientReason ?? "missing")}, " +
                $"server={(_serverRegistered ? "ready" : serverReason ?? "missing")}, " +
                $"serverRx={(_serverReceiveRegistered ? "ready" : serverReceiveReason ?? "missing")}, " +
                $"messageId={AlpineConstants.SleddersInternalMessageId}";

            return IsReady;
        }

        private void ResolveStaticTypes()
        {
            if (_handlerDelegateType == null)
                _handlerDelegateType = Type.GetType("HDIGLPKCIDC+BJIFGFGGNFP, Assembly-CSharp");

            if (_deliveryType == null)
                _deliveryType = Type.GetType("BIMHPJPECDH, Assembly-CSharp");

            if (_deliveryReliableFragmented == null && _deliveryType != null)
            {
                try
                {
                    _deliveryReliableFragmented = Enum.Parse(_deliveryType, "ReliableFragmentedSequenced");
                }
                catch
                {
                    _deliveryReliableFragmented = Enum.ToObject(_deliveryType, 4);
                }
            }
        }

        private bool TryRegister(object netInterface, MethodInfo registerMethod, bool serverSide, out string reason)
        {
            reason = null;

            if (netInterface == null)
            {
                reason = "netInterface null";
                return false;
            }

            if (_handlerDelegateType == null)
            {
                reason = "handler delegate type missing";
                return false;
            }

            if (registerMethod == null)
            {
                reason = "message register method missing";
                return false;
            }

            if (ContainsMessageId(netInterface))
            {
                reason = $"message id {AlpineConstants.SleddersInternalMessageId} already registered";
                return false;
            }

            try
            {
                MethodInfo callback = GetType().GetMethod(
                    serverSide ? "OnServerMessage" : "OnClientMessage",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Delegate handler = Delegate.CreateDelegate(_handlerDelegateType, this, callback);
                registerMethod.Invoke(netInterface, new object[] { AlpineConstants.SleddersInternalMessageId, handler });

                if (serverSide)
                    _serverHandler = handler;
                else
                    _clientHandler = handler;

                MelonLogger.Msg(
                    $"[AlpineInternalTransport] Registered Sledders message id {AlpineConstants.SleddersInternalMessageId} on {(serverSide ? "server" : "client")} netInterface.");
                reason = "ready";
                return true;
            }
            catch (Exception ex)
            {
                reason = "register failed: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[AlpineInternalTransport] " + reason);
                return false;
            }
        }

        private void Unregister(object netInterface, MethodInfo unregisterMethod, ref bool registered, string side)
        {
            if (!registered || netInterface == null || unregisterMethod == null)
                return;

            try
            {
                unregisterMethod.Invoke(netInterface, new object[] { AlpineConstants.SleddersInternalMessageId });
                MelonLogger.Msg($"[AlpineInternalTransport] Unregistered Sledders message id {AlpineConstants.SleddersInternalMessageId} from {side} netInterface.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AlpineInternalTransport] Unregister skipped on {side}: {ex.GetType().Name}: {ex.Message}");
            }

            registered = false;
        }

        private bool ContainsMessageId(object netInterface)
        {
            try
            {
                FieldInfo registryField = FindFieldInHierarchy(netInterface.GetType(), "MIHNCAHMKPF");
                object registry = registryField?.GetValue(netInterface);
                if (registry == null)
                    return false;

                MethodInfo contains = registry.GetType().GetMethod("ContainsKey", new[] { typeof(byte) });
                if (contains == null)
                    return false;

                return (bool)contains.Invoke(registry, new object[] { AlpineConstants.SleddersInternalMessageId });
            }
            catch
            {
                return false;
            }
        }

        private void OnClientMessage(ulong transportSenderId, ref DataStreamReader reader)
        {
            HandleIncoming(false, transportSenderId, ref reader);
        }

        private void OnServerMessage(ulong transportSenderId, ref DataStreamReader reader)
        {
            HandleIncoming(true, transportSenderId, ref reader);
        }

        private void HandleIncoming(bool serverSide, ulong transportSenderId, ref DataStreamReader reader)
        {
            try
            {
                if (reader.Length - reader.GetBytesRead() < 21)
                {
                    LastReceiveStatus = "ignored: internal packet header truncated";
                    return;
                }

                uint magic = reader.ReadUInt();
                if (magic != PacketMagic)
                {
                    LastReceiveStatus = $"ignored: internal packet magic mismatch {magic}";
                    return;
                }

                byte kind = reader.ReadByte();
                uint sequence = reader.ReadUInt();
                ushort index = reader.ReadUShort();
                ushort count = reader.ReadUShort();
                int totalBytes = reader.ReadInt();
                int payloadBytes = reader.ReadInt();

                int remaining = reader.Length - reader.GetBytesRead();
                if (payloadBytes <= 0 || payloadBytes > remaining || count == 0 || count > MaxChunkCount || index >= count)
                {
                    LastReceiveStatus = "ignored: invalid internal packet chunk bounds";
                    return;
                }

                byte[] payload = ReadBytes(ref reader, payloadBytes);
                string json = null;

                if (kind == PacketKindFull)
                {
                    json = Encoding.UTF8.GetString(payload);
                }
                else if (kind == PacketKindChunk)
                {
                    json = AddChunk(transportSenderId, sequence, index, count, totalBytes, payload);
                    if (json == null)
                    {
                        LastReceiveStatus = $"chunk {index + 1}/{count} received from {transportSenderId}";
                        return;
                    }
                }
                else
                {
                    LastReceiveStatus = $"ignored: unknown internal packet kind {kind}";
                    return;
                }

                DispatchJson(serverSide, transportSenderId, json);
            }
            catch (Exception ex)
            {
                LastReceiveStatus = "receive failed: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[AlpineInternalTransport] " + LastReceiveStatus);
            }
        }

        private byte[] ReadBytes(ref DataStreamReader reader, int byteCount)
        {
            var bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
                bytes[i] = reader.ReadByte();

            return bytes;
        }

        private string AddChunk(ulong transportSenderId, uint sequence, int index, int count, int totalBytes, byte[] payload)
        {
            string key = transportSenderId + ":" + sequence;
            if (!_incomingChunks.TryGetValue(key, out var set) || set == null)
            {
                set = new IncomingChunkSet
                {
                    createdAt = UnityEngine.Time.unscaledTime,
                    totalBytes = totalBytes,
                    chunks = new byte[count][],
                    receivedBytes = 0
                };
                _incomingChunks[key] = set;
            }

            if (set.chunks.Length != count || set.totalBytes != totalBytes)
            {
                _incomingChunks.Remove(key);
                return null;
            }

            if (set.chunks[index] == null)
            {
                set.chunks[index] = payload;
                set.receivedBytes += payload.Length;
            }

            for (int i = 0; i < set.chunks.Length; i++)
            {
                if (set.chunks[i] == null)
                    return null;
            }

            _incomingChunks.Remove(key);
            var combined = new byte[set.receivedBytes];
            int offset = 0;
            for (int i = 0; i < set.chunks.Length; i++)
            {
                Buffer.BlockCopy(set.chunks[i], 0, combined, offset, set.chunks[i].Length);
                offset += set.chunks[i].Length;
            }

            if (combined.Length != totalBytes)
                return null;

            return Encoding.UTF8.GetString(combined);
        }

        private void DispatchJson(bool serverSide, ulong transportSenderId, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            AlpineShareMessage message = null;
            ulong logicalSender = transportSenderId;
            try
            {
                message = JsonConvert.DeserializeObject<AlpineShareMessage>(json);
                if (message != null && message.senderSleddersClientId != 0)
                    logicalSender = message.senderSleddersClientId;
            }
            catch
            {
            }

            LastReceiveStatus =
                $"received {(serverSide ? "server" : "client")} packet from transport={transportSenderId}, sender={logicalSender}, bytes={Encoding.UTF8.GetByteCount(json)}";

            _onPayload?.Invoke(logicalSender, json, serverSide ? "server" : "client");

            if (serverSide && message != null)
                RelayFromServer(logicalSender, message, json);
        }

        private void RelayFromServer(ulong logicalSender, AlpineShareMessage message, string json)
        {
            if (!_serverRegistered || _serverInterface == null || message == null)
                return;

            var targets = ResolveServerTargets(message.targetSleddersClientId, message.targetSleddersClientId == 0);
            if (logicalSender != 0)
                targets.Remove(logicalSender);

            ulong local = SleddersGameBindings.GetLocalSleddersClientId();
            if (local != 0)
                targets.Remove(local);

            if (targets.Count == 0)
                return;

            SendJsonFromServer(json, targets);
        }

        private List<ulong> ResolveServerTargets(ulong targetClientId, bool broadcast)
        {
            var targets = new HashSet<ulong>();

            if (!broadcast && targetClientId != 0)
            {
                targets.Add(targetClientId);
            }
            else
            {
                foreach (var peer in SleddersGameBindings.DiscoverPeers(0, false))
                {
                    if (peer != null && peer.hasInternalClientId && peer.sleddersClientId != 0)
                        targets.Add(peer.sleddersClientId);
                }
            }

            ulong local = SleddersGameBindings.GetLocalSleddersClientId();
            if (local != 0)
                targets.Remove(local);

            return targets.ToList();
        }

        private bool SendJsonToHost(string json, ulong targetClientId, bool broadcast)
        {
            if (_clientInterface == null || _clientSendMethod == null || _clientNewWriterMethod == null)
            {
                LastSendStatus = "blocked: client internal send method missing";
                return false;
            }

            try
            {
                int packetCount = 0;
                foreach (DataStreamWriter writer in BuildWriters(_clientInterface, _clientNewWriterMethod, json))
                {
                    _clientSendMethod.Invoke(_clientInterface, new[] { _deliveryReliableFragmented, (object)writer });
                    packetCount++;
                }

                LastSendStatus =
                    $"sent {packetCount} internal packet(s) client-to-host target={(broadcast ? "broadcast" : targetClientId.ToString())}";
                return packetCount > 0;
            }
            catch (Exception ex)
            {
                LastSendStatus = "client send failed: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[AlpineInternalTransport] " + LastSendStatus);
                return false;
            }
        }

        private bool SendJsonFromServer(string json, List<ulong> targets)
        {
            if (_serverInterface == null ||
                (_serverSendMethod == null && _serverSendListMethod == null) ||
                _serverNewWriterMethod == null)
            {
                LastSendStatus = "blocked: server internal send method missing";
                return false;
            }

            try
            {
                int packetCount = 0;
                if (_serverSendMethod != null)
                {
                    foreach (ulong target in targets.Distinct().Where(t => t != 0).ToArray())
                    {
                        foreach (DataStreamWriter writer in BuildWriters(_serverInterface, _serverNewWriterMethod, json))
                        {
                            _serverSendMethod.Invoke(
                                _serverInterface,
                                new object[] { target, _deliveryReliableFragmented, writer });
                            packetCount++;
                        }
                    }
                }
                else
                {
                    foreach (DataStreamWriter writer in BuildWriters(_serverInterface, _serverNewWriterMethod, json))
                    {
                        _serverSendListMethod.Invoke(
                            _serverInterface,
                            new object[] { targets, _deliveryReliableFragmented, writer });
                        packetCount++;
                    }
                }

                LastSendStatus =
                    $"sent {packetCount} internal packet(s) server-to-clients targets=[{string.Join(",", targets.Select(t => t.ToString()).ToArray())}]";
                return packetCount > 0;
            }
            catch (Exception ex)
            {
                LastSendStatus = "server send failed: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[AlpineInternalTransport] " + LastSendStatus);
                return false;
            }
        }

        private IEnumerable<DataStreamWriter> BuildWriters(object netInterface, MethodInfo newWriterMethod, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > AlpineConstants.MaxPeerMessageBytes)
                throw new InvalidOperationException($"message too large ({bytes.Length} bytes)");

            uint sequence = _nextSequence++;
            int maxChunkBytes = AlpineConstants.SleddersInternalMaxChunkBytes;
            int count = Math.Max(1, (bytes.Length + maxChunkBytes - 1) / maxChunkBytes);
            if (count > MaxChunkCount)
                throw new InvalidOperationException($"message needs {count} chunks");

            for (int index = 0; index < count; index++)
            {
                int offset = index * maxChunkBytes;
                int length = Math.Min(maxChunkBytes, bytes.Length - offset);
                byte[] chunk = new byte[length];
                Buffer.BlockCopy(bytes, offset, chunk, 0, length);

                DataStreamWriter writer = CreateWriter(netInterface, newWriterMethod);
                writer.WriteByte(AlpineConstants.SleddersInternalMessageId);
                writer.WriteUInt(PacketMagic);
                writer.WriteByte(count == 1 ? PacketKindFull : PacketKindChunk);
                writer.WriteUInt(sequence);
                writer.WriteUShort((ushort)index);
                writer.WriteUShort((ushort)count);
                writer.WriteInt(bytes.Length);
                writer.WriteInt(length);

                for (int i = 0; i < chunk.Length; i++)
                    writer.WriteByte(chunk[i]);

                if (writer.HasFailedWrites)
                    throw new InvalidOperationException("DataStreamWriter failed while writing internal packet");

                yield return writer;
            }
        }

        private DataStreamWriter CreateWriter(object netInterface, MethodInfo newWriterMethod)
        {
            object raw = newWriterMethod.Invoke(netInterface, Array.Empty<object>());
            if (!(raw is DataStreamWriter))
                throw new InvalidOperationException("MDNBFANMMHH did not return DataStreamWriter");

            return (DataStreamWriter)raw;
        }

        private string SerializeForInternal(AlpineShareMessage message, ulong targetClientId)
        {
            ulong localClientId = SleddersGameBindings.GetLocalSleddersClientId();
            ulong oldSenderId = message.senderId;
            ulong oldSteamId = message.senderSteamId;
            ulong oldSleddersId = message.senderSleddersClientId;
            ulong oldTargetId = message.targetSleddersClientId;
            string oldTransport = message.transport;
            ulong oldSummarySender = message.summary != null ? message.summary.senderId : 0;
            ulong oldActiveSender = message.activeState != null ? message.activeState.senderId : 0;

            try
            {
                if (localClientId != 0)
                {
                    message.senderId = localClientId;
                    message.senderSleddersClientId = localClientId;
                    if (message.summary != null)
                        message.summary.senderId = localClientId;
                    if (message.activeState != null)
                        message.activeState.senderId = localClientId;
                }

                if (message.senderSteamId == 0 && LooksLikeSteam64(oldSenderId))
                    message.senderSteamId = oldSenderId;

                message.targetSleddersClientId = targetClientId;
                message.transport = "sleddersInternal";
                return JsonConvert.SerializeObject(message, Formatting.None);
            }
            catch (Exception ex)
            {
                LastSendStatus = "serialize failed: " + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
            finally
            {
                message.senderId = oldSenderId;
                message.senderSteamId = oldSteamId;
                message.senderSleddersClientId = oldSleddersId;
                message.targetSleddersClientId = oldTargetId;
                message.transport = oldTransport;
                if (message.summary != null)
                    message.summary.senderId = oldSummarySender;
                if (message.activeState != null)
                    message.activeState.senderId = oldActiveSender;
            }
        }

        private void ExpireChunks()
        {
            if (_incomingChunks.Count == 0)
                return;

            float now = UnityEngine.Time.unscaledTime;
            foreach (string key in _incomingChunks
                         .Where(pair => now - pair.Value.createdAt > ChunkTimeoutSeconds)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _incomingChunks.Remove(key);
            }
        }

        private MethodInfo FindRegisterMethod(Type type)
        {
            return FindMethodInHierarchy(type, m =>
            {
                if (m.Name != "LIOJOEAICOP" || m.IsGenericMethod)
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(byte) &&
                       _handlerDelegateType != null &&
                       parameters[1].ParameterType == _handlerDelegateType;
            });
        }

        private MethodInfo FindUnregisterMethod(Type type)
        {
            return FindMethodInHierarchy(type, m =>
            {
                if (m.Name != "BHIBDALLNOD")
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(byte);
            });
        }

        private MethodInfo FindNoArgMethod(Type type, string name)
        {
            return FindMethodInHierarchy(type, m => m.Name == name && m.GetParameters().Length == 0);
        }

        private MethodInfo FindSendMethod(Type type)
        {
            return FindMethodInHierarchy(type, m =>
            {
                if (m.Name != "FGFICFGOCDL" || _deliveryType == null)
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == _deliveryType &&
                       parameters[1].ParameterType == typeof(DataStreamWriter);
            });
        }

        private MethodInfo FindServerSendMethod(Type type)
        {
            return FindMethodInHierarchy(type, m =>
            {
                if (m.Name != "PIDJHAOLBJM" || _deliveryType == null)
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == typeof(ulong) &&
                       parameters[1].ParameterType == _deliveryType &&
                       parameters[2].ParameterType == typeof(DataStreamWriter);
            });
        }

        private MethodInfo FindServerSendListMethod(Type type)
        {
            return FindMethodInHierarchy(type, m =>
            {
                if (m.Name != "EAGNHKAFODD" || _deliveryType == null)
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 3 &&
                       typeof(IReadOnlyList<ulong>).IsAssignableFrom(parameters[0].ParameterType) &&
                       parameters[1].ParameterType == _deliveryType &&
                       parameters[2].ParameterType == typeof(DataStreamWriter);
            });
        }

        private MethodInfo FindMethodInHierarchy(Type type, Func<MethodInfo, bool> predicate)
        {
            while (type != null)
            {
                MethodInfo match = type
                    .GetMethods(SleddersGameBindings.All)
                    .FirstOrDefault(predicate);
                if (match != null)
                    return match;

                type = type.BaseType;
            }

            return null;
        }

        private FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, SleddersGameBindings.All);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        private static bool LooksLikeSteam64(ulong value)
        {
            return value >= 76561190000000000UL && value <= 76561210000000000UL;
        }

        private sealed class IncomingChunkSet
        {
            public float createdAt;
            public int totalBytes;
            public int receivedBytes;
            public byte[][] chunks;
        }
    }
}
