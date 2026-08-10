using System;
using System.Text;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Mints the short codes friends type to join an igloo.
    /// </summary>
    /// <remarks>
    /// A join code is a pure selector: the server registry mints one per session and looks it up when
    /// a <c>JoinSessionRequest</c> arrives. It encodes nothing — no address, no session id — so codes
    /// stay short, stay reusable after a session dies, and never leak topology.
    /// <para>
    /// The alphabet drops the characters people mistype when reading a code aloud (I, L, O, U, 0, 1),
    /// leaving 30 symbols. Six characters give about 729 million codes, which is far beyond the
    /// live-session count any one deployment holds, so collisions are rare and the caller's predicate
    /// only has to reject the occasional clash.
    /// </para>
    /// </remarks>
    public sealed class JoinCodeGenerator {
        /// <summary>Unambiguous base32-style alphabet. Excludes I, L, O, U, 0 and 1.</summary>
        public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        /// <summary>Characters in every minted code.</summary>
        public const int CodeLength = 6;

        /// <summary>How many codes are tried before the generator gives up on collisions.</summary>
        public const int MaxAttempts = 64;

        private readonly Random _random;

        /// <summary>Creates a generator seeded from the clock.</summary>
        public JoinCodeGenerator() {
            _random = new Random();
        }

        /// <summary>Creates a generator with a fixed seed, so tests can reproduce a sequence.</summary>
        public JoinCodeGenerator(int seed) {
            _random = new Random(seed);
        }

        /// <summary>Mints a code with no collision checking.</summary>
        public string Generate() {
            StringBuilder builder = new StringBuilder(CodeLength);

            for (int characterIndex = 0; characterIndex < CodeLength; characterIndex++) {
                builder.Append(Alphabet[_random.Next(Alphabet.Length)]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Mints a code the caller's predicate does not already claim.
        /// </summary>
        /// <param name="isTaken">Returns true when the candidate is already registered.</param>
        public string Generate(Func<string, bool> isTaken) {
            if (isTaken == null) {
                throw new ArgumentNullException(nameof(isTaken));
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++) {
                string candidate = Generate();

                if (!isTaken(candidate)) {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not mint a free join code in " + MaxAttempts.ToString()
                + " attempts. The join-code space is effectively exhausted.");
        }

        /// <summary>
        /// Turns whatever a player typed into a canonical code: uppercased, with spaces and dashes
        /// stripped. Fails when a character is outside the alphabet or the length is wrong.
        /// </summary>
        public static bool TryNormalize(string input, out string code) {
            code = null;

            if (string.IsNullOrEmpty(input)) {
                return false;
            }

            StringBuilder builder = new StringBuilder(CodeLength);

            for (int characterIndex = 0; characterIndex < input.Length; characterIndex++) {
                char current = input[characterIndex];

                if (IsSeparator(current)) {
                    continue;
                }

                char upper = char.ToUpperInvariant(current);

                if (Alphabet.IndexOf(upper) < 0) {
                    return false;
                }

                if (builder.Length == CodeLength) {
                    return false;
                }

                builder.Append(upper);
            }

            if (builder.Length != CodeLength) {
                return false;
            }

            code = builder.ToString();
            return true;
        }

        /// <summary>True when the string is already a canonical join code.</summary>
        public static bool IsValid(string code) {
            if (code == null || code.Length != CodeLength) {
                return false;
            }

            for (int characterIndex = 0; characterIndex < CodeLength; characterIndex++) {
                if (Alphabet.IndexOf(code[characterIndex]) < 0) {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSeparator(char character) {
            return character == ' ' || character == '-' || character == '_' || character == '\t';
        }
    }
}
