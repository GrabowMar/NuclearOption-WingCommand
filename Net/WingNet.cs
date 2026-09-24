using System;
using System.Collections.Generic;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand.Net
{
    /// <summary>Host → client: the bytes of one M6a message (spec M6 §7).</summary>
    public struct WcToClient
    {
        public byte[] Payload;
        public int Count;
    }

    /// <summary>Client → host: the bytes of one M6a message.</summary>
    public struct WcToHost
    {
        public byte[] Payload;
        public int Count;
    }
}

namespace WingCommand
{
    using WingCommand.Net;

    /// <summary>Wing Command's Mirage transport (spec M6 §7): two byte-payload message types with hand-set serializers,
    /// handlers registered every session, and the server-first handshake — the host greets each authenticated player once
    /// a second until it is greeted (its own local player too: single-player is a loopback), a client answers the hello
    /// and sends nothing before it, and a player silent for <see cref="SilentSeconds"/> after its hello is logged once.
    /// Commands and snapshots are M6c's.</summary>
    internal static class WingNet
    {
        public static float HelloSeconds = 1f, SilentSeconds = 10f, SnapshotSeconds = 0.5f;
        /// <summary>The owner id of the host's own wing (per-player wings and their ids are M6c-2).</summary>
        public const uint HostWing = 1u;

        public static int Greeted { get; private set; }
        public static int Replies { get; private set; }
        public static int In { get; private set; }
        public static int Out { get; private set; }
        public static int DecodeFailures { get; private set; }
        /// <summary>The host greeted this client (a client may send only then).</summary>
        public static bool HostGreeted { get; private set; }
        public static bool Disabled { get; private set; }
        public static int SnapshotsIn { get; private set; }
        /// <summary>This client's copy of its wing (null until the first snapshot).</summary>
        public static WingMirror Mirror { get; private set; }

        private static bool hooked;
        private static NetworkManagerNuclearOption manager;
        private static float helloClock, snapshotClock, findClock = float.MaxValue;
        private static uint snapshotTick;
        private static readonly SnapshotMember[] snapshotMembers = new SnapshotMember[WcSnapshot.MaxMembers];
        private static readonly byte[] buffer = new byte[Protocol.MaxMessage];
        private static readonly Dictionary<INetworkPlayer, float> hellos = new Dictionary<INetworkPlayer, float>();
        private static readonly HashSet<INetworkPlayer> answered = new HashSet<INetworkPlayer>();
        private static readonly HashSet<INetworkPlayer> silent = new HashSet<INetworkPlayer>();

        private static string Version => Plugin.PluginVersion + "-" + Plugin.PluginPrerelease;

        /// <summary>Once, at plugin start: the serializers, and the ids checked against the game's own messages.</summary>
        public static void Init()
        {
            Writer<WcToClient>.Write = (w, m) => w.WriteBytesAndSize(m.Payload, 0, m.Count);
            Reader<WcToClient>.Read = r => Payload<WcToClient>(r.ReadBytesAndSize(Protocol.MaxMessage), (b, n) => new WcToClient { Payload = b, Count = n });
            Writer<WcToHost>.Write = (w, m) => w.WriteBytesAndSize(m.Payload, 0, m.Count);
            Reader<WcToHost>.Read = r => Payload<WcToHost>(r.ReadBytesAndSize(Protocol.MaxMessage), (b, n) => new WcToHost { Payload = b, Count = n });
            int toClient = MessagePacker.GetId<WcToClient>(), toHost = MessagePacker.GetId<WcToHost>();
            if (toClient == toHost || Taken(toClient, typeof(WcToClient)) || Taken(toHost, typeof(WcToHost)))
            {
                Disabled = true;
                Plugin.Logger.LogError($"[Net] message id collision ({toClient}, {toHost}); Wing Command networking is off");
                return;
            }
            try
            {
                MessagePacker.RegisterMessage<WcToClient>();
                MessagePacker.RegisterMessage<WcToHost>();
            }
            catch (ArgumentException e)
            {
                Disabled = true;
                Plugin.Logger.LogError($"[Net] {e.Message}; Wing Command networking is off");
            }
        }

        private static T Payload<T>(byte[] bytes, Func<byte[], int, T> make) => make(bytes ?? Array.Empty<byte>(), bytes?.Length ?? 0);

        private static bool Taken(int id, Type ours) =>
            MessagePacker.MessageTypes.TryGetValue(id, out Type other) && other != ours;

        /// <summary>Every frame, in or out of a mission: hook the network manager once it exists, greet players.</summary>
        public static void Tick(float dt)
        {
            if (Disabled) return;
            NetworkManagerNuclearOption nm = Manager(dt);
            if (nm == null || nm.Server == null || nm.Client == null) return;
            if (!hooked)
            {
                hooked = true;
                nm.Server.Started.AddListener(OnServerStarted);
                nm.Server.Stopped.AddListener(ClearPlayers);
                nm.Server.Disconnected.AddListener(Forget);
                nm.Client.Started.AddListener(OnClientStarted);
                if (nm.Server.Active) OnServerStarted();
                if (nm.Client.Active) OnClientStarted();
            }
            if (!nm.Server.Active) return;
            if ((snapshotClock += dt) >= SnapshotSeconds)
            {
                snapshotClock = 0f;
                SendSnapshots(nm.Server);
            }
            if ((helloClock += dt) < HelloSeconds) return;
            helloClock = 0f;
            Greet(nm.Server, Time.unscaledTime);
        }

        /// <summary>The game's manager, looked up once a second until found (review M6c I1: its <c>i</c> getter logs an
        /// error on every call before the main menu preloads it).</summary>
        private static NetworkManagerNuclearOption Manager(float dt)
        {
            if (manager != null) return manager;
            if ((findClock += dt) < 1f) return null;
            findClock = 0f;
            return manager = UnityEngine.Object.FindObjectOfType<NetworkManagerNuclearOption>();
        }

        /// <summary>A fault in the transport turns Wing Command networking off (review M6c I2): the AI keeps ticking.</summary>
        public static void Fail(Exception e)
        {
            Disabled = true;
            Plugin.Logger.LogError($"[Net] Wing Command networking failed and is off until restart: {e}");
        }

        /// <summary>A player gone from the server is forgotten (review M6c I4: the tables grew for the whole session).</summary>
        private static void Forget(INetworkPlayer p)
        {
            hellos.Remove(p);
            answered.Remove(p);
            silent.Remove(p);
        }

        private static void ClearPlayers()
        {
            hellos.Clear();
            answered.Clear();
            silent.Clear();
        }

        private static void OnServerStarted()
        {
            ClearPlayers();
            NetworkManagerNuclearOption.i.Server.MessageHandler.RegisterHandler<WcToHost>(FromClient, false);
            Plugin.Logger.LogInfo("[Net] host handlers registered");
        }

        private static void OnClientStarted()
        {
            HostGreeted = false;
            Mirror = null;
            // Only the host sends to a client; the client's connection to it is its only player.
            NetworkManagerNuclearOption.i.Client.MessageHandler.RegisterHandler<WcToClient>(FromHost, true);
            Plugin.Logger.LogInfo("[Net] client handlers registered");
        }

        private static void Greet(NetworkServer server, float now)
        {
            foreach (INetworkPlayer p in server.AuthenticatedPlayers)
            {
                if (!hellos.TryGetValue(p, out float at))
                {
                    var w = new ByteWriter(buffer);
                    new WcHello { ModVersion = Version, PerPlayer = 8, Total = 16 }.Encode(w);
                    p.Send(new WcToClient { Payload = buffer, Count = w.Length }, Channel.Reliable);
                    hellos[p] = now;
                    Greeted++;
                    Out++;
                    continue;
                }
                if (!answered.Contains(p) && now - at > SilentSeconds && silent.Add(p))
                    Plugin.Logger.LogWarning($"[Net] a player did not answer the hello in {SilentSeconds:0} s: Wing Command is missing there");
            }
        }

        private static void FromClient(INetworkPlayer player, WcToHost m)
        {
            In++;
            ByteReader r = WireCodec.Open(m.Payload, 0, m.Count, out MessageKind kind);
            if (kind == MessageKind.HelloReply && WcHelloReply.TryDecode(r, out WcHelloReply reply))
            {
                if (answered.Add(player))
                {
                    Replies++;
                    Plugin.Logger.LogInfo($"[Net] a player answered with Wing Command {reply.ModVersion}{(player.IsHost ? " (the host's own client)" : "")}");
                }
                return;
            }
            // Commands arrive in M6c; anything else (or a message before the reply) is refused.
            DecodeFailures++;
        }

        /// <summary>The host's own wing to its own local player, twice a second, unreliable (a snapshot supersedes the last).
        /// Remote players get nothing until their own wings exist (M6c-2) rather than the host's wing.</summary>
        private static void SendSnapshots(NetworkServer server)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return;
            foreach (INetworkPlayer p in answered)
            {
                if (!p.IsHost) continue;
                int n = wing.FillSnapshot(snapshotMembers);
                var members = new SnapshotMember[n];
                Array.Copy(snapshotMembers, members, n);
                var w = new ByteWriter(buffer);
                new WcSnapshot { Tick = ++snapshotTick, Owner = HostWing, Members = members }.Encode(w);
                p.Send(new WcToClient { Payload = buffer, Count = w.Length }, Channel.Unreliable);
                Out++;
            }
        }

        private static void FromHost(INetworkPlayer player, WcToClient m)
        {
            In++;
            ByteReader r = WireCodec.Open(m.Payload, 0, m.Count, out MessageKind kind);
            if (kind == MessageKind.Snapshot)
            {
                if (!HostGreeted || !WcSnapshot.TryDecode(r, out WcSnapshot snapshot))
                {
                    DecodeFailures++;
                    return;
                }
                if (Mirror == null) Mirror = new WingMirror(snapshot.Owner);
                if (Mirror.Apply(snapshot, Time.unscaledTime)) SnapshotsIn++;
                return;
            }
            if (kind != MessageKind.Hello || !WcHello.TryDecode(r, out WcHello hello))
            {
                DecodeFailures++;
                return;
            }
            HostGreeted = true;
            var w = new ByteWriter(buffer);
            new WcHelloReply { ModVersion = Version }.Encode(w);
            NetworkManagerNuclearOption.i.Client.Send(new WcToHost { Payload = buffer, Count = w.Length }, Channel.Reliable);
            Out++;
            Plugin.Logger.LogInfo($"[Net] greeted by a host with Wing Command {hello.ModVersion}");
        }
    }
}
