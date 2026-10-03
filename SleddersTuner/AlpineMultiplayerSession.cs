using System;
using System.Reflection;

namespace AlpineTuning
{
    internal sealed class AlpineMultiplayerSession
    {
        // Alias native host zero only in Alpine metadata, never native sends.
        internal const ulong HostPeerId = 0x1000000000000000UL;
        internal static ulong PeerId(ulong nativeId) => nativeId == 0 ? HostPeerId : nativeId;
        internal static ulong NativeId(ulong peerId) => peerId == HostPeerId ? 0 : peerId;
        private readonly Func<object> _getClient;
        internal AlpineMultiplayerSession(Func<object> getClient = null)
        {
            _getClient = getClient ?? (() => { SleddersGameBindings.TryGetNetClient(out object client, out _); return client; });
        }
        private object _client;
        private object _interface;
        private ulong? _localId;
        private bool _lifecycleChanged;
        internal bool IsActive { get; private set; }
        internal string Status => IsActive ? "Multiplayer session detected." : "Waiting for a live multiplayer session.";
        private void LifecycleChanged() { _lifecycleChanged = true; }

        internal bool Update()
        {
            object client = _getClient();
            bool changed = false;
            if (!ReferenceEquals(client, _client))
            {
                Detach();
                _client = client;
                if (_client != null)
                {
                    InvokeLifecycle("RegisterMultiplayerStartedSync");
                    InvokeLifecycle("add_OnMultiplayerStopped");
                }
                changed = true;
            }
            object connection = client != null ? SleddersGameBindings.GetFieldValue<object>(client, "netInterface") : null;
            object identity = client?.GetType().GetMethod("get_LocalClientId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(client, null);
            ulong? local = identity is ulong id ? id : (ulong?)null;
            bool active = connection != null && local.HasValue;
            changed |= _lifecycleChanged || !ReferenceEquals(connection, _interface) || _localId != local || IsActive != active;
            _lifecycleChanged = false;
            _interface = connection;
            _localId = local;
            IsActive = active;
            return changed;
        }

        private void InvokeLifecycle(string name)
        {
            _client?.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(Action) }, null)?.Invoke(_client, new object[] { (Action)LifecycleChanged });
        }

        internal void Detach()
        {
            if (_client != null)
            {
                InvokeLifecycle("UnRegisterMultiplayerStartedSync");
                InvokeLifecycle("remove_OnMultiplayerStopped");
            }
            _client = null;
            _interface = null;
            _localId = null;
            IsActive = false;
        }
    }
}
