using System;

namespace AlpineLib.Editor {
    /// <summary>
    /// Carries a gate's exit code back to <see cref="SmokeGateRunner"/> in place of ending the editor.
    /// Gates must not catch it.
    /// </summary>
    public sealed class SmokeGateExitException : Exception {
        public int Code { get; }

        public SmokeGateExitException(int code) : base($"smoke gate exited with code {code}") {
            Code = code;
        }
    }
}
