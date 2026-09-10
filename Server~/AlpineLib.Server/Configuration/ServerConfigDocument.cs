namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// The whole editor-exported configuration file: objects mirroring the shared <c>NetConfig</c> and
    /// <c>SessionConfigData</c>, plus the chat policy and the spawn rules when the export carries them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only place the two worlds meet. Unity authors the assets and the session-config
    /// exporter writes this file straight from the same <c>ToNetConfig</c> / <c>ToData</c> calls the
    /// client runs by — so what the client plays with is exactly what the server is handed — and the
    /// server maps it back onto the same shared types here.
    /// </para>
    /// <para>
    /// <b>Chat and spawn are optional.</b> An older export is missing them rather than broken, and a
    /// server that refused to start over settings it has perfectly good defaults for would be refusing
    /// for no reason. When a section appears it wins; until then the shipped policy applies.
    /// </para>
    /// </remarks>
    public sealed class ServerConfigDocument {
        public NetConfigDocument Net { get; set; } = new NetConfigDocument();

        public SessionDataDocument Session { get; set; } = new SessionDataDocument();

        public ChatConfigDocument Chat { get; set; } = new ChatConfigDocument();

        public SpawnSettingsDocument Spawn { get; set; } = new SpawnSettingsDocument();

        /// <summary>Maps the document onto the runtime objects the host is built from.</summary>
        public ServerConfigBundle ToBundle(string sourcePath) {
            return new ServerConfigBundle(
                (Session ?? new SessionDataDocument()).ToData(),
                (Net ?? new NetConfigDocument()).ToConfig(),
                (Chat ?? new ChatConfigDocument()).ToSettings(),
                (Spawn ?? new SpawnSettingsDocument()).ToSettings(),
                sourcePath);
        }
    }
}
