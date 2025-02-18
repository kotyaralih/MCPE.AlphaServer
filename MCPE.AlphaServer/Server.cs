﻿using MCPE.AlphaServer.Packets;
using MCPE.AlphaServer.Utils;
using MCPE.AlphaServer.Game;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Status = MCPE.AlphaServer.Packets.LoginResponsePacket.LoginStatus;

namespace MCPE.AlphaServer {
    public class Server {
        public static Server The;
        public World World => World.The;

        public IPEndPoint ListenEndpoints { get; private set; }
        public UdpClient UdpServer { get; private set; }
        public DateTime StartTime { get; private set; }
        public Dictionary<IPEndPoint, UdpConnection> Clients { get; private set; }

        public ulong Guid { get; private set; } = 0x1122334455667788;
        public bool IsRunning = true;

        public Server(int port) : this(new IPEndPoint(IPAddress.Any, port)) { }
        public Server(IPEndPoint endpoints) {
            ListenEndpoints = endpoints;
            UdpServer = new UdpClient(endpoints.Port);
            Clients = new Dictionary<IPEndPoint, UdpConnection>();
            StartTime = DateTime.Now;
            IsRunning = true;
            World.The = new World();
        }
        public async Task Update() {
            var result = await UdpServer.ReceiveAsync();
            var parsed = Packet.Parse(result.Buffer);
            var endPoint = result.RemoteEndPoint;

//            Console.WriteLine($"{parsed}");

            switch (parsed.Type) {
            case PacketType.UnconnectedPing: { await SendRaw(endPoint, UnconnectedPongPacket.FromPing(parsed.Get<UnconnectedPingPacket>(), Guid, "MCPE.AlphaServer")); break; }
            case PacketType.OpenConnectionRequest1: { await SendRaw(endPoint, OpenConnectionReplyPacket.FromRequest(parsed.Get<OpenConnectionRequestPacket>(), Guid, endPoint)); break; }
            case PacketType.OpenConnectionRequest2: {
                var packet = parsed.Get<OpenConnectionRequestPacket>();
                var Client = new UdpConnection(endPoint);
                Clients.Add(endPoint, Client);
                await SendRaw(endPoint, OpenConnectionReplyPacket.FromRequest(packet, Guid, endPoint));
                break;
            }
            case PacketType.RakNetPacket: { await HandleRakNetPacket(parsed.Get<RakNetPacket>(), endPoint); break; }
            default:
                break;
            }
        }

        async Task HandleRakNetPacket(RakNetPacket rakPacket, IPEndPoint endpoint) {
            if (!Clients.ContainsKey(endpoint))
                return;

            var Client = Clients[endpoint];
            Client.LastUpdate = DateTime.Now;
            if (!rakPacket.IsACKorNAK)
                Client.Sequence = rakPacket.SequenceNumber;

            foreach (var enclosing in rakPacket.Enclosing) {
                switch (enclosing.MessageID) {
                case RakPacketType.ConnectedPong: {
                    Console.WriteLine("[<=] PONG!");
                    break;
                }
                case RakPacketType.ConnectedPing: { await Send(Client, ConnectedPongPacket.FromPing(enclosing.Get<ConnectedPingPacket>(), StartTime)); break; }
                case RakPacketType.ConnectionRequest: {
                    var request = enclosing.Get<ConnectionRequestPacket>();
                    Client.Player = new Player();
                    Client.Player.CID = request.ClientGuid;
                    await Send(Client, ConnectionRequestAcceptedPacket.FromRequest(request, endpoint));
                    break;
                }
                case RakPacketType.LoginRequest: {
                    var request = enclosing.Get<LoginRequestPacket>();
                    var status = request.StatusFor(14);

                    Client.Player.Username = request.Username;
                    if (Client.Player.Username == "server" || // Don't impersonate the server.
                        World.GetPlayerByName(request.Username) != null) { // Don't log in if there's a player already in game.
                        await Send(Client, LoginResponsePacket.FromRequest(request, Status.ClientOutdated));
                        break;
                    }

                    // StartGame is when the Client gets it's own EntityID.
                    Client.Player.EID = World.LastEID++;

                    //TODO(atipls): More checks, check if a the player name is already logged in.
                    await Send(Client, LoginResponsePacket.FromRequest(request, status),
                        status == Status.VersionsMatch ? new StartGamePacket(request.ReliableNum.IntValue, Client.Player.EID) : null
                    );

                    await World.AddPlayer(Client);

                    Console.WriteLine("Players:");
                    foreach (var player in World.Players) {
                        Console.WriteLine($"{{ EID: {player.Player.EID} CID: {player.Player.CID} Name: {player.Player.Username} }}");
                    }

                    break;
                }
                case RakPacketType.Ready: {
                    // Notify the new client about existing players.
                    foreach (var P in World.Players) {
                        if (P == Client)
                            continue;
                        await Send(Client, new AddPlayerPacket(P.Player));
                    }
                    break;
                }
                case RakPacketType.NewIncomingConnection: {
                    Console.WriteLine($"[ +] {endpoint}");
                    break;
                }
                case RakPacketType.Message: {
                    await SendToEveryone(enclosing.Get<MessagePacket>());
                    break;
                }
                case RakPacketType.MovePlayer: {
                    var packet = enclosing.Get<MovePlayerPacket>();
                    Console.WriteLine($"MovePlayer: {{ ID: {packet.ID} (== {Client.Player.EID} OR == {Client.Player.CID}) }}");
                    await World.MovePlayer(Client, packet.Position, packet.Pitch, packet.Yaw);
                    break;
                }
                case RakPacketType.PlayerDisconnect: {
                    // Let the client updater thread disconnect the player.
                    Client.ForceInvalidate = true;
                    break;
                }
                default:
                    Console.WriteLine($"Unhandled RakPacket: {enclosing.MessageID}");
                    break;
                }
            }

            if (!rakPacket.IsACKorNAK) {
                var ackPacket = rakPacket.CreateACK();
                var ackData = ackPacket.Serialize();
                await UdpServer.SendAsync(ackData, ackData.Length, endpoint);
            }
        }

        public async Task Send(UdpConnection Client, params RakPacket[] packets) {
            var rakPacket = RakNetPacket.Create(Client.Sequence);
            Client.Sequence = Client.Sequence.Add(1);
            rakPacket.Enclosing.AddRange(packets);
            var data = rakPacket.Serialize();
            await UdpServer.SendAsync(data, data.Length, Client.EndPoint);
        }

        public async Task SendRaw(IPEndPoint endPoint, Packet packet) {
            var data = packet.Serialize();
            await UdpServer.SendAsync(data, data.Length, endPoint);
        }

        public async Task SendToEveryone(params RakPacket[] packets) {
            foreach (var Client in Clients.Values) {
                await Send(Client, packets);
            }
        }

        public async Task BroadcastMessage(string message) => await SendToEveryone(new MessagePacket("server", message));

        public void ListenerThread() { while (true) { Task.Run(Update).GetAwaiter().GetResult(); } }
        public void ClientUpdaterThread() {
            while (true) {
                lock (Clients) {
                    var disconnected = Clients.Where(x => !x.Value.Valid || x.Value.ForceInvalidate);
                    foreach (var client in disconnected) {
                        //TODO(atipls): Events??
                        Console.WriteLine($"[ -] {client.Key}");

                        // Setting up the async task is fine here, Player isn't used in the packet.
                        _ = SendToEveryone(new RemovePlayerPacket(client.Value.Player));

                        Clients.Remove(client.Key);
                        World.Players.Remove(client.Value);
                    }
                    Thread.Sleep(100);
                }
            }
        }
    }
}
