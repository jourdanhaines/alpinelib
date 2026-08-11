using System;
using AlpineLib.DI;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Owns the process's connection to the network: the transport sockets, the client facade and,
    /// when listen-hosting, the server facade — and pumps all of them once a frame.
    /// </summary>
    /// <remarks>
    /// Everything above this service asks it for a <see cref="NetClient"/> or a <see cref="NetServer"/>
    /// rather than building one, because the two must share a single pump and a single lifetime. In
    /// <see cref="NetworkMode.Offline"/> both are null and every method here does nothing, which is what
    /// lets a scene opened directly in the editor run with no session at all.
    /// </remarks>
    public interface INetworkService : IDependencyProvider {
        /// <summary>What the process is doing on the network right now.</summary>
        NetworkMode Mode { get; }

        /// <summary>Tuning the facades were built from, or null before <see cref="Configure"/>.</summary>
        NetConfig Config { get; }

        /// <summary>The client facade, or null while offline.</summary>
        NetClient Client { get; }

        /// <summary>The server facade, or null unless listen-hosting.</summary>
        NetServer Server { get; }

        /// <summary>True once the local client has a live connection.</summary>
        bool IsConnected { get; }

        /// <summary>True while a server is running in this process.</summary>
        bool IsListenServer { get; }

        /// <summary>
        /// Round trip to the server in milliseconds, or zero when there is no connection to measure.
        /// </summary>
        /// <remarks>
        /// Zero rather than a sentinel on purpose: this number exists to be shown to a player, and a HUD
        /// that has to know what a negative reading means is a HUD that will one day print it. A listen
        /// host reads zero honestly — its round trip really is nothing.
        /// </remarks>
        int PingMs { get; }

        /// <summary>Raised after <see cref="Mode"/> changes, with the new mode.</summary>
        event Action<NetworkMode> OnModeChanged;

        /// <summary>Raised on the frame the local client's connection comes up.</summary>
        event Action OnConnected;

        /// <summary>Raised on the frame the local client's connection goes away, with the cause.</summary>
        event Action<DisconnectReason> OnDisconnected;

        /// <summary>Installs the tuning every facade built afterwards is created with.</summary>
        void Configure(NetConfig config);

        /// <summary>Brings up a client facade in <see cref="NetworkMode.Client"/>, without dialling yet.</summary>
        NetClient StartClient();

        /// <summary>
        /// Brings up a server in this process plus a client facade for the local player, leaving the
        /// caller to dial loopback.
        /// </summary>
        NetServer StartListenServer();

        /// <summary>Dials an endpoint with the current client facade. No-op while offline.</summary>
        void Connect(NetEndpoint endpoint);

        /// <summary>Disconnects the client but keeps the current mode and facades alive.</summary>
        void Disconnect();

        /// <summary>Tears everything down and returns to <see cref="NetworkMode.Offline"/>.</summary>
        void Shutdown();
    }

    /// <summary>
    /// App-root resident implementation of <see cref="INetworkService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Client and server each get their own <see cref="LiteNetTransport"/>: a listen host binds one
    /// socket to the listen port and dials it from a second, so the two ends are as separate as they
    /// would be across a real network and nothing in the stack has to special-case loopback.
    /// </para>
    /// <para>
    /// The pump order in <c>Update</c> is server first, then client. A listen host that polled the
    /// client first would deliver the client's messages to a server that had not yet read the frame
    /// they were answering, adding a frame of latency to every local round trip.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.NetworkService)]
    public class NetworkService : MonoBehaviour, INetworkService {
        /// <inheritdoc />
        public NetworkMode Mode { get; private set; } = NetworkMode.Offline;

        /// <inheritdoc />
        public NetConfig Config { get; private set; }

        /// <inheritdoc />
        public NetClient Client { get; private set; }

        /// <inheritdoc />
        public NetServer Server { get; private set; }

        /// <inheritdoc />
        public bool IsConnected => Client != null && Client.IsConnected;

        /// <inheritdoc />
        public bool IsListenServer => Mode == NetworkMode.ListenServer && Server != null;

        /// <inheritdoc />
        /// <remarks>
        /// The transport reports a negative reading for a peer it has no measurement for yet, which is
        /// flattened to zero here so nothing above this has to know that.
        /// </remarks>
        public int PingMs => IsConnected ? Mathf.Max(0, Client.PingMs) : 0;

        /// <inheritdoc />
        public event Action<NetworkMode> OnModeChanged;

        /// <inheritdoc />
        public event Action OnConnected;

        /// <inheritdoc />
        public event Action<DisconnectReason> OnDisconnected;

        private LiteNetTransport _clientTransport;
        private LiteNetTransport _serverTransport;

        /// <remarks>
        /// Declared on the concrete type rather than the interface, matching the library's other
        /// services: the injector reflects over the concrete type when registering a provider.
        /// </remarks>
        [Provide]
        public INetworkService ProvideNetworkService() {
            return this;
        }

        /// <inheritdoc />
        public void Configure(NetConfig config) {
            if (config == null) {
                Debug.LogError("NetworkService::Configure->No config supplied; staying offline.");
                return;
            }

            Config = config;
        }

        /// <inheritdoc />
        public NetClient StartClient() {
            if (Config == null) {
                Debug.LogError("NetworkService::StartClient->Configure must run before a client is started.");
                return null;
            }

            if (Client != null) return Client;

            _clientTransport = new LiteNetTransport(Config.DisconnectTimeoutMs);
            Client = new NetClient(_clientTransport, Config);
            Client.OnConnected += HandleClientConnected;
            Client.OnDisconnected += HandleClientDisconnected;

            if (Mode != NetworkMode.ListenServer) {
                SetMode(NetworkMode.Client);
            }

            return Client;
        }

        /// <inheritdoc />
        public NetServer StartListenServer() {
            if (Config == null) {
                Debug.LogError("NetworkService::StartListenServer->Configure must run before a server is started.");
                return null;
            }

            if (Server != null) return Server;

            _serverTransport = new LiteNetTransport(Config.DisconnectTimeoutMs);
            Server = new NetServer(_serverTransport, Config);
            Server.Start();

            SetMode(NetworkMode.ListenServer);
            StartClient();

            return Server;
        }

        /// <inheritdoc />
        public void Connect(NetEndpoint endpoint) {
            if (Client == null) {
                Debug.LogWarning("NetworkService::Connect->No client facade; call StartClient first.");
                return;
            }

            if (Client.State != ConnectionState.Disconnected) return;

            Client.Connect(endpoint);
        }

        /// <inheritdoc />
        public void Disconnect() {
            if (Client == null) return;

            Client.Disconnect();
        }

        /// <inheritdoc />
        public void Shutdown() {
            DisposeClient();
            DisposeServer();
            SetMode(NetworkMode.Offline);
        }

        /// <remarks>
        /// One pump per frame for each live facade. Both are null while offline, so this costs two
        /// reference comparisons in a scene that never touches the network.
        /// </remarks>
        private void Update() {
            float deltaSeconds = Time.deltaTime;

            Server?.Update(deltaSeconds);
            Client?.Update(deltaSeconds);
        }

        private void Awake() {
            // Application-shutdown guard, not a race guard: this service is installed on the app root,
            // so an absent injector means the game is already tearing down.
            if (!Injector.HasInstance) return;

            Injector.Instance.RegisterProvider(this);
        }

        private void OnDestroy() {
            Shutdown();

            if (!Injector.HasInstance) return;

            Injector.Instance.UnregisterProvider(this);
        }

        private void HandleClientConnected() {
            OnConnected?.Invoke();
        }

        private void HandleClientDisconnected(DisconnectReason reason) {
            OnDisconnected?.Invoke(reason);
        }

        private void DisposeClient() {
            if (Client == null) return;

            Client.OnConnected -= HandleClientConnected;
            Client.OnDisconnected -= HandleClientDisconnected;
            Client.Dispose();
            Client = null;

            _clientTransport?.Dispose();
            _clientTransport = null;
        }

        private void DisposeServer() {
            if (Server == null) return;

            Server.Dispose();
            Server = null;

            _serverTransport?.Dispose();
            _serverTransport = null;
        }

        private void SetMode(NetworkMode mode) {
            if (Mode == mode) return;

            Mode = mode;
            OnModeChanged?.Invoke(mode);
        }
    }
}
