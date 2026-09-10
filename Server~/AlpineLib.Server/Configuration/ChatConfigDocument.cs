using System;
using AlpineLib.Chat;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of the Unity <c>ChatConfig</c> asset.
    /// </summary>
    /// <remarks>
    /// The profanity list is a <c>TextAsset</c> in the editor and a flat array here: the exporter splits
    /// the file into words so the server never has to know what a Unity asset is.
    /// </remarks>
    public sealed class ChatConfigDocument {
        public ChatProviderMode ProviderMode { get; set; } = ChatProviderMode.BuiltIn;

        public int MaxMessageLength { get; set; } = 200;

        public int RateLimitBurst { get; set; } = 4;

        public float RateLimitRefillSeconds { get; set; } = 1.5f;

        public int MuteAfterViolations { get; set; } = 5;

        public int MuteDurationSeconds { get; set; } = 30;

        public float DuplicateWindowSeconds { get; set; } = 5f;

        public int HistoryBufferSize { get; set; } = 64;

        public int HistoryOnJoinCount { get; set; } = 32;

        public int ClientViewBufferSize { get; set; } = 200;

        public string[] ProfanityWordList { get; set; } = Array.Empty<string>();

        /// <summary>Maps this document onto the settings the chat pipeline runs by.</summary>
        public ChatSettings ToSettings() {
            return new ChatSettings {
                ProviderMode = ProviderMode,
                MaxMessageLength = MaxMessageLength,
                RateLimitBurst = RateLimitBurst,
                RateLimitRefillSeconds = RateLimitRefillSeconds,
                MuteAfterViolations = MuteAfterViolations,
                MuteDurationSeconds = MuteDurationSeconds,
                DuplicateWindowSeconds = DuplicateWindowSeconds,
                HistoryBufferSize = HistoryBufferSize,
                HistoryOnJoinCount = HistoryOnJoinCount,
                ClientViewBufferSize = ClientViewBufferSize,
                ProfanityWordList = ProfanityWordList ?? Array.Empty<string>()
            };
        }
    }
}
