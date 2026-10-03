using AlpineTuning;
using System;
using UnityEngine;

namespace AlpineTuning.ReleaseTests
{
    internal static class ForgeSessionRegression
    {
        private sealed class Client
        {
            public object netInterface;
            public ulong? id;
            private Action _started, _stopped;
            public ulong? get_LocalClientId() => id;
            public void RegisterMultiplayerStartedSync(Action action) { _started += action; if (netInterface != null) action(); }
            public void UnRegisterMultiplayerStartedSync(Action action) { _started -= action; }
            public void add_OnMultiplayerStopped(Action action) { _stopped += action; }
            public void remove_OnMultiplayerStopped(Action action) { _stopped -= action; }
            internal void Start() { _started?.Invoke(); }
            internal void Stop() { _stopped?.Invoke(); }
            internal int Subscribers => (_started?.GetInvocationList().Length ?? 0) + (_stopped?.GetInvocationList().Length ?? 0);
        }

        private enum Platform { Steam, PS }
        private sealed class PlayerRecord
        {
            public Platform OAFNDMNIPBM;
            public string PFCCHGNNJEB;
        }

        internal static void Run()
        {
            var client = new Client();
            object current = client;
            var session = new AlpineMultiplayerSession(() => current);
            Program.Require(session.Update() && !session.IsActive && client.Subscribers == 2, "session-offline-attaches-once");
            Program.Require(!session.Update() && client.Subscribers == 2, "session-stable-no-duplicate-hooks");
            client.netInterface = new object();
            Program.Require(session.Update() && !session.IsActive, "session-joining-waits-for-identity");
            client.id = 0;
            Program.Require(session.Update() && session.IsActive, "session-zero-host-is-live");
            client.Stop();
            client.Start();
            Program.Require(session.Update() && session.IsActive, "session-reconnect-same-client-and-interface");
            client.id = null;
            client.netInterface = null;
            Program.Require(session.Update() && !session.IsActive, "session-leave-detected");
            var replacement = new Client { netInterface = new object(), id = 17 };
            current = replacement;
            Program.Require(session.Update() && session.IsActive && client.Subscribers == 0 && replacement.Subscribers == 2,
                "session-client-replacement-detaches-old-hooks");
            session.Detach();
            Program.Require(!session.IsActive && replacement.Subscribers == 0, "session-shutdown-detaches");
            Program.Require(AlpineMultiplayerSession.PeerId(0) != 0 &&
                AlpineMultiplayerSession.NativeId(AlpineMultiplayerSession.PeerId(0)) == 0 &&
                AlpineMultiplayerSession.PeerId(17) == 17, "session-host-alias-roundtrip");

            foreach (string name in new[] { "Running Boards", "RMK_850_Running_Board_LOD2", "Footrests", "Astinlauta" })
                Program.Require(AlpineForgeGeometry.Matches(name, SledForgeSlot.RunningBoards), "forge-board-semantic:" + name);
            foreach (string name in new[] { "DashBoard", "Handlebar", "Tunnel", "Skid", "LowerSidePanels" })
                Program.Require(!AlpineForgeGeometry.Matches(name, SledForgeSlot.RunningBoards), "forge-board-not-unrelated-paint:" + name);
            Program.Require(AlpineForgeGeometry.Matches("MY25_MatryxRMKCutAndSewSeat_low", SledForgeSlot.Seat) &&
                !AlpineForgeGeometry.Matches("Gastank", SledForgeSlot.Seat), "forge-seat-not-gas-tank-paint");
            Program.Require(AlpineForgeGeometry.LowerDetail("RMK_850_Running_Board_LOD2") &&
                AlpineForgeGeometry.LowerDetail("BodyL1") &&
                !AlpineForgeGeometry.LowerDetail("MY25_MatryxRMKCutAndSewSeat_low"), "forge-lod-semantic");
            var source = new Bounds(new Vector3(0, 0.5f, -0.6f), new Vector3(0.8f, 0.1f, 1.6f));
            var donor = new Bounds(new Vector3(0.2f, 0.8f, -1f), new Vector3(1f, 0.1f, 2f));
            Program.Require(AlpineForgeGeometry.TryScale(source, donor, SledForgeSlot.RunningBoards, out Vector3 scale),
                "forge-board-footprint-valid");
            Vector3 sourceMount = AlpineForgeGeometry.Mount(source, SledForgeSlot.RunningBoards);
            Vector3 donorMount = AlpineForgeGeometry.Mount(donor, SledForgeSlot.RunningBoards);
            Vector3 translation = sourceMount - Vector3.Scale(donorMount, scale);
            Vector3 fittedMount = Vector3.Scale(donorMount, scale) + translation;
            Program.Require((fittedMount - sourceMount).sqrMagnitude < 0.000001f &&
                Math.Abs(donor.size.x * scale.x - source.size.x) < 0.00001f &&
                Math.Abs(donor.size.z * scale.z - source.size.z) < 0.00001f && scale.y == 1f,
                "forge-donor-offset-cancelled-and-recipient-footprint-matched");
            Program.Require(!AlpineForgeGeometry.TryScale(source, new Bounds(Vector3.zero, new Vector3(0, 1, 1)),
                SledForgeSlot.Hood, out _) && !AlpineForgeGeometry.TryScale(source,
                new Bounds(Vector3.zero, new Vector3(float.NaN, 1, 1)), SledForgeSlot.Hood, out _),
                "forge-invalid-geometry-native-fallback");
            var player = new PlayerRecord { OAFNDMNIPBM = Platform.Steam, PFCCHGNNJEB = "76561198000000123" };
            Program.Require(SleddersGameBindings.TryGetPlayerSteamId(player, out ulong steamId) && steamId == 76561198000000123UL,
                "session-steam-id-from-native-platform-record");
            player.OAFNDMNIPBM = Platform.PS;
            Program.Require(!SleddersGameBindings.TryGetPlayerSteamId(player, out _), "session-console-id-not-steam");
            player.OAFNDMNIPBM = Platform.Steam;
            foreach (string invalid in new[] { "17", "0", "fixture-player", " 76561198000000123", "+76561198000000123" })
            {
                player.PFCCHGNNJEB = invalid;
                Program.Require(!SleddersGameBindings.TryGetPlayerSteamId(player, out _), "session-rejects-guessed-steam-id");
            }
            var owner = new object();
            var visibility = new AlpineVisibilityState(true, true);
            visibility.Acquire(owner);
            visibility.Release(owner);
            Program.Require(visibility.Enabled && visibility.ForceRenderingOff, "forge-preserves-original-force-off");
            visibility = new AlpineVisibilityState(true);
            visibility.Acquire(owner);
            Program.Require(visibility.ForceRenderingOff, "forge-hide-survives-lod-enable");
            visibility.Release(owner);
            Program.Require(!visibility.ForceRenderingOff, "forge-restore-releases-lod-hide");
        }
    }
}
