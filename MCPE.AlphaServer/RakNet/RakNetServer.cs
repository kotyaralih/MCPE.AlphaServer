using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MCPE.AlphaServer.Utils;

namespace MCPE.AlphaServer.RakNet;

public class RakNetServer {
    private readonly Dictionary<IPEndPoint, RakNetClient> Connections;

    public RakNetServer(int port) {
        GUID = (ulong)Random.Shared.Next() & ((ulong)Random.Shared.Next() << 32);
        IP = new IPEndPoint(IPAddress.Any, port);
        UDP = new UdpClient(IP);
        Connections = new Dictionary<IPEndPoint, RakNetClient>();
        TaskCancellationToken = new CancellationTokenSource();
    }

    public ulong GUID { get; }
    public IPEndPoint IP { get; }
    internal UdpClient UDP { get; }
    public string ServerName { get; set; } = "gaming 2 real? ";

    private IConnectionHandler ConnectionHandler { get; set; }
    private CancellationTokenSource TaskCancellationToken { get; }
    private DateTime StartedOn { get; } = DateTime.Now;

    public ulong TimeSinceStart => (ulong)(DateTime.Now - StartedOn).TotalMilliseconds;

    public void Start(IConnectionHandler connectionHandler) {
        ConnectionHandler = connectionHandler;

        StartRepeatingTask(HandlePackets, TimeSpan.Zero);
        StartRepeatingTask(HandleConnections, TimeSpan.FromMilliseconds(1));
    }

    public void Stop() {
        TaskCancellationToken.Cancel();
        UDP.Close();
    }

    private async Task HandlePackets() {
        var receiveResult = await UDP.ReceiveAsync();

        // Try handling the connected packet, might fall through if the client reconnects?
        if (Connections.TryGetValue(receiveResult.RemoteEndPoint, out var existingConnection)) {
            // Logger.Debug($"Letting {existingConnection} handle packet");
            existingConnection.HandlePacket(receiveResult.Buffer);
            return;
        }

        // If we don't have a session for this IP yet.
        switch (UnconnectedPacket.Parse(receiveResult.Buffer)) {
            case UnconnectedPingPacket:
                await Send(receiveResult.RemoteEndPoint,
                    new UnconnectedPongPacket(TimeSinceStart, GUID, $"MCCPP;Demo;{ServerName}")
                );
                break;
            case OpenConnectionRequest1Packet openConnectionRequest1Packet:
                Logger.Debug($"Received OpenConnectionRequest1Packet from {receiveResult.RemoteEndPoint}");
                await Send(receiveResult.RemoteEndPoint,
                    new OpenConnectionReply1Packet(GUID, false, 1492) // TODO: MTU Is hardcoded.
                );
                break;
            case OpenConnectionRequest2Packet request:
                Logger.Debug($"Handling connection request from {receiveResult.RemoteEndPoint}");
                var newConnetion = new RakNetClient(receiveResult.RemoteEndPoint, this) {
                    ClientID = request.ClientID
                };

                Connections.Add(receiveResult.RemoteEndPoint, newConnetion);

                await Send(receiveResult.RemoteEndPoint,
                    new OpenConnectionReply2Packet(GUID, newConnetion.IP, 1492, false) // TODO: MTU Is hardcoded.
                );

                break;
            case { } packet:
                Logger.Warn($"Got unhandled unconnected packet {packet}? This is probably a bug.");
                break;
        }
    }

    private async Task HandleConnections() {
        foreach (var (_, connection) in Connections)
            await connection.HandleOutgoing();

        foreach (var (endpoint, client) in Connections.Where(x => !x.Value.IsConnected)) {
            ConnectionHandler?.OnClose(client, client.IsTimedOut ? "Timed out" : "Disconnected");
            Connections.Remove(endpoint);
        }

        ConnectionHandler?.OnUpdate();

        await Task.Delay(1);
    }

    private void StartRepeatingTask(Func<Task> action, TimeSpan interval) {
        Task.Run(async () => {
                while (!TaskCancellationToken.IsCancellationRequested) {
                    await action();
                    await Task.Delay(interval);
                }
            }, TaskCancellationToken.Token
        );
    }

    private async Task Send(IPEndPoint endPoint, UnconnectedPacket packet) {
        var writer = new DataWriter();
        packet.Encode(ref writer);
        var buffer = writer.GetBytes();
        await UDP.SendAsync(buffer, buffer.Length, endPoint);
    }

    internal void OnOpen(RakNetClient connection) => ConnectionHandler?.OnOpen(connection);
    internal void OnClose(RakNetClient connection, string reason) => ConnectionHandler?.OnClose(connection, reason);

    internal void OnData(RakNetClient connection, ReadOnlyMemory<byte> data) =>
        ConnectionHandler?.OnData(connection, data);
}
