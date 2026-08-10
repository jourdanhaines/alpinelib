using System;
using AlpineLib.Chat.Filters;

namespace AlpineLib.Chat {
    /// <summary>
    /// Every knob the chat pipeline has, as a plain object with no Unity types in it.
    /// </summary>
    /// <remarks>
    /// The Unity-side <c>ChatConfig</c> asset converts into one of these, and the dedicated server reads
    /// the same values out of its exported JSON, so both ends enforce identical policy from a single
    /// authored source. Defaults here are the shipping Penguin values — a settings object built with
    /// <c>new</c> and nothing else is already a working configuration.
    /// </remarks>
    public sealed class ChatSettings {
        /// <summary>Which provider implementation to install.</summary>
        public ChatProviderMode ProviderMode { get; set; } = ChatProviderMode.BuiltIn;

        /// <summary>Longest line a player may send, after trimming.</summary>
        public int MaxMessageLength { get; set; } = 200;

        /// <summary>How many lines a player may send back to back before the limiter bites.</summary>
        public int RateLimitBurst { get; set; } = 4;

        /// <summary>Seconds to regain one line of rate-limit allowance.</summary>
        public float RateLimitRefillSeconds { get; set; } = 1.5f;

        /// <summary>Violations that earn an automatic mute. Zero disables automatic muting.</summary>
        public int MuteAfterViolations { get; set; } = 5;

        /// <summary>How long an automatic mute lasts, seconds.</summary>
        public int MuteDurationSeconds { get; set; } = 30;

        /// <summary>Window in which repeating yourself is treated as spam, seconds.</summary>
        public float DuplicateWindowSeconds { get; set; } = 5f;

        /// <summary>Messages the server keeps per channel for history pushes.</summary>
        public int HistoryBufferSize { get; set; } = 64;

        /// <summary>Messages pushed to a player when they join a channel.</summary>
        public int HistoryOnJoinCount { get; set; } = 32;

        /// <summary>Messages a client keeps in its own view buffer.</summary>
        public int ClientViewBufferSize { get; set; } = 200;

        /// <summary>Words the profanity filter masks. Authored as a text asset, exported as lines.</summary>
        public string[] ProfanityWordList { get; set; } = Array.Empty<string>();

        /// <summary>The automatic mute duration as a timespan.</summary>
        public TimeSpan MuteDuration => TimeSpan.FromSeconds(MuteDurationSeconds < 0 ? 0 : MuteDurationSeconds);

        /// <summary>True when the pipeline should mute repeat offenders on its own.</summary>
        public bool AutoMuteEnabled => MuteAfterViolations > 0;

        /// <summary>
        /// Builds the standard filter chain in the order the pipeline expects: shape first, then rate,
        /// then repetition, then content. Cheap rejections happen before expensive ones, and every later
        /// filter can assume the text is trimmed and within length.
        /// </summary>
        public IChatFilter[] BuildDefaultFilters() {
            return new IChatFilter[] {
                new LengthFilter(MaxMessageLength),
                new RateLimitFilter(RateLimitBurst, RateLimitRefillSeconds),
                new DuplicateFilter(DuplicateWindowSeconds),
                new ProfanityFilter(ProfanityWordList)
            };
        }

        /// <summary>Copies the settings, so a caller can hand a snapshot to a service that keeps it.</summary>
        public ChatSettings Clone() {
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
                ProfanityWordList = (string[])(ProfanityWordList ?? Array.Empty<string>()).Clone()
            };
        }
    }
}
