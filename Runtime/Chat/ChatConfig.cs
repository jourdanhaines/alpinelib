using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Chat {
    /// <summary>
    /// Authored chat tuning: which provider backs it, what a player may say, how often, and how much of
    /// the conversation is remembered.
    /// </summary>
    /// <remarks>
    /// The same asset configures both ends. A server enforces the limits — rate, length, duplicates,
    /// profanity, mutes — and a client only reads the ones that shape its own view, so a client that
    /// edits its copy gains nothing but a wrong idea of what it is allowed to send.
    /// </remarks>
    [CreateAssetMenu(fileName = "ChatConfig", menuName = "AlpineLib/Networking/Chat Config")]
    public class ChatConfig : ScriptableObject {
        [Header("Provider")]
        [Tooltip("Which implementation backs chat. BuiltIn rides the game connection; other modes are the external-service seam.")]
        public ChatProviderMode providerMode = ChatProviderMode.BuiltIn;

        [Header("Message Limits")]
        [Tooltip("Longest message a player may send, in characters.")]
        public int maxMessageLength = 200;
        [Tooltip("Messages a player may send back to back before the rate limit bites.")]
        public int rateLimitBurst = 4;
        [Tooltip("Seconds to earn back one message of burst allowance.")]
        public float rateLimitRefillSeconds = 1.5f;
        [Tooltip("Seconds within which repeating yourself counts as a duplicate.")]
        public float duplicateWindowSeconds = 5f;

        [Header("Moderation")]
        [Tooltip("Rejected messages a player may rack up before being muted automatically. Zero disables auto-mute.")]
        public int muteAfterViolations = 5;
        [Tooltip("Seconds an automatic mute lasts.")]
        public int muteDurationSeconds = 30;
        [Tooltip("One blocked word per line. Leave empty to disable the profanity filter.")]
        public TextAsset profanityWordList;

        [Header("History")]
        [Tooltip("Messages the server keeps per channel for replay to newcomers.")]
        public int historyBufferSize = 64;
        [Tooltip("Messages a joining player is sent from that history.")]
        public int historyOnJoinCount = 32;
        [Tooltip("Messages a client keeps for its own chat window.")]
        public int clientViewBufferSize = 200;

        /// <summary>Builds the shared settings this asset describes.</summary>
        public ChatSettings ToSettings() {
            return new ChatSettings {
                ProviderMode = providerMode,
                MaxMessageLength = maxMessageLength,
                RateLimitBurst = rateLimitBurst,
                RateLimitRefillSeconds = rateLimitRefillSeconds,
                DuplicateWindowSeconds = duplicateWindowSeconds,
                MuteAfterViolations = muteAfterViolations,
                MuteDurationSeconds = muteDurationSeconds,
                HistoryBufferSize = historyBufferSize,
                HistoryOnJoinCount = historyOnJoinCount,
                ClientViewBufferSize = clientViewBufferSize,
                ProfanityWordList = ParseWordList()
            };
        }

        /// <summary>
        /// Splits the authored word list into entries, dropping blank lines and surrounding whitespace.
        /// </summary>
        /// <remarks>
        /// A text asset rather than a string array so the list can be edited as a file — profanity lists
        /// are long, are often shared between projects, and do not belong in a serialized inspector
        /// array.
        /// </remarks>
        public string[] ParseWordList() {
            if (profanityWordList == null) return Array.Empty<string>();

            string[] lines = profanityWordList.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var words = new List<string>(lines.Length);

            foreach (string line in lines) {
                string word = line.Trim();

                if (word.Length == 0) continue;

                words.Add(word);
            }

            return words.ToArray();
        }
    }
}
