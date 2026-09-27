using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    public sealed class LoopbackTransportTests {
        [Fact]
        public void AClientConnectsAndDataArrivesOnlyWhenPolled() {
            var network = new LoopbackNetwork();
            using LoopbackTransport server = network.CreateServerTransport();
            using LoopbackTransport client = network.CreateClientTransport();
            var connected = new List<PeerHandle>();
            var received = new List<byte>();
            server.OnPeerConnected += connected.Add;
            server.OnData += (peer, payload, delivery) => received.Add(payload.Array[payload.Offset]);

            server.StartServer(1, 4, "loopback");
            client.StartClient("loopback");
            client.Connect(NetEndpoint.Direct("loopback", 1));
            client.Send(LoopbackTransport.ServerPeer, new byte[] { 7 }, DeliveryClass.ReliableOrdered);

            Assert.Empty(connected);
            server.Poll();

            Assert.Equal(new[] { client.Handle }, connected);
            Assert.Equal(new byte[] { 7 }, received);
        }

        [Fact]
        public void ANetClientReachesANetServerOverTheLoopback() {
            var network = new LoopbackNetwork();
            var config = new NetConfig { GameProtocolName = "loopback-test", Port = 1, MaxPeers = 4 };
            using var server = new NetServer(network.CreateServerTransport(), config);
            using var client = new NetClient(network.CreateClientTransport(), config);
            server.Start();
            client.Connect(NetEndpoint.Direct("loopback", 1));

            for (int tick = 0; tick < 16 && !client.IsConnected; tick++) {
                server.Update(1f / 30f);
                client.Update(1f / 30f);
            }

            Assert.True(client.IsConnected);
            Assert.Single(server.Peers);
        }
    }
}
