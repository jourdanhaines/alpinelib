using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// The one exit every batchmode gate reports through. Alone, a gate ends the editor with its code;
    /// under <see cref="SmokeGateRunner"/> the code unwinds back to the runner so the next gate can run
    /// in the same process.
    /// </summary>
    public static class SmokeGate {
        /// <summary>True while <see cref="SmokeGateRunner"/> is driving gates in this process.</summary>
        public static bool IsRunnerActive { get; internal set; }

        /// <summary>
        /// Ends the gate with <paramref name="code"/>. Throws <see cref="SmokeGateExitException"/> under
        /// the runner; otherwise exits the editor in batchmode and does nothing in an interactive editor.
        /// </summary>
        public static void Exit(int code) {
            if (IsRunnerActive) throw new SmokeGateExitException(code);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
