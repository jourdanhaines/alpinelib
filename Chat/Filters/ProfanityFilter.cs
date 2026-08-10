using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat.Filters {
    /// <summary>
    /// Masks — or optionally refuses — lines containing words from an injected block list.
    /// </summary>
    /// <remarks>
    /// Matching is whole-token, not substring. A substring match is what produces the Scunthorpe
    /// problem, and in a game whose players are children a filter that silently mangles innocent words
    /// is worse than one that misses a creative misspelling. The list itself is injected rather than
    /// compiled in: it is content, it changes without a build, and it differs per title.
    /// <para>
    /// The default action is to mask matched tokens with asterisks so the conversation still flows.
    /// Masking is not treated as a violation; only the reject mode counts towards an automatic mute.
    /// </para>
    /// </remarks>
    public sealed class ProfanityFilter : IChatFilter {
        private const char MaskCharacter = '*';

        private readonly HashSet<string> _blockedWords;
        private readonly bool _rejectMessages;

        /// <summary>Creates a filter that masks matched words.</summary>
        public ProfanityFilter(string[] blockedWords) : this(blockedWords, false) { }

        /// <summary>Creates a filter that either masks matched words or refuses the whole line.</summary>
        public ProfanityFilter(string[] blockedWords, bool rejectMessages) {
            _blockedWords = BuildWordSet(blockedWords);
            _rejectMessages = rejectMessages;
        }

        /// <summary>How many distinct words the filter is watching for.</summary>
        public int BlockedWordCount => _blockedWords.Count;

        /// <summary>True when a match refuses the line rather than masking it.</summary>
        public bool RejectsMessages => _rejectMessages;

        /// <inheritdoc />
        public string Name => "profanity";

        /// <inheritdoc />
        public ChatFilterResult Evaluate(in ChatFilterContext context) {
            if (_blockedWords.Count == 0) {
                return ChatFilterResult.Allow();
            }

            char[] masked = BuildMasked(context.Text);

            if (masked == null) {
                return ChatFilterResult.Allow();
            }

            if (_rejectMessages) {
                return ChatFilterResult.Reject(ChatSendStatus.Filtered, "Message contained blocked language.");
            }

            return ChatFilterResult.Sanitize(new string(masked));
        }

        /// <inheritdoc />
        public void Forget(PlayerId player) {
        }

        /// <summary>True when the text contains at least one blocked word.</summary>
        public bool ContainsBlockedWord(string text) {
            return BuildMasked(text ?? string.Empty) != null;
        }

        /// <summary>Returns the text with every blocked word masked, or the original when none matched.</summary>
        public string Mask(string text) {
            char[] masked = BuildMasked(text ?? string.Empty);
            return masked == null ? text : new string(masked);
        }

        private static HashSet<string> BuildWordSet(string[] blockedWords) {
            HashSet<string> words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (blockedWords == null) {
                return words;
            }

            for (int wordIndex = 0; wordIndex < blockedWords.Length; wordIndex++) {
                string candidate = blockedWords[wordIndex];

                if (string.IsNullOrWhiteSpace(candidate)) {
                    continue;
                }

                words.Add(candidate.Trim());
            }

            return words;
        }

        private static bool IsTokenCharacter(char character) {
            return char.IsLetterOrDigit(character) || character == '\'';
        }

        /// <summary>
        /// Walks the text token by token, masking matches. Returns null when nothing matched, so the
        /// common case allocates nothing at all.
        /// </summary>
        private char[] BuildMasked(string text) {
            char[] buffer = null;
            int tokenStart = -1;

            for (int index = 0; index <= text.Length; index++) {
                bool isTokenCharacter = index < text.Length && IsTokenCharacter(text[index]);

                if (isTokenCharacter) {
                    tokenStart = tokenStart < 0 ? index : tokenStart;
                    continue;
                }

                if (tokenStart < 0) {
                    continue;
                }

                buffer = MaskToken(text, tokenStart, index - tokenStart, buffer);
                tokenStart = -1;
            }

            return buffer;
        }

        private char[] MaskToken(string text, int start, int length, char[] buffer) {
            if (!_blockedWords.Contains(text.Substring(start, length))) {
                return buffer;
            }

            char[] target = buffer ?? text.ToCharArray();

            for (int offset = 0; offset < length; offset++) {
                target[start + offset] = MaskCharacter;
            }

            return target;
        }
    }
}
