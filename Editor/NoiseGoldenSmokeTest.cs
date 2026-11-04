using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Noise;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Batchmode gate: evaluates <see cref="NoiseGoldenTable"/>, <see cref="MeshGoldenTable"/> and <see cref="WindGoldenTable"/> under the
    /// editor's Mono runtime, so the bits the .NET tests pin on CoreCLR are proven on Mono too.
    /// </summary>
    public static class NoiseGoldenSmokeTest {
        private const string LogPrefix = "NoiseGoldenSmokeTest";

        /// <summary>Logs <c>NoiseGoldenSmokeTest: OK</c> and exits 0 when every value matches; logs the mismatches and exits 1 otherwise.</summary>
        public static void Verify() {
            List<string> failures = NoiseGoldenTable.FindMismatches();
            failures.AddRange(MeshGoldenTable.FindMismatches());
            failures.AddRange(WindGoldenTable.FindMismatches());
            if (failures.Count > 0) {
                Debug.LogError($"{LogPrefix}: FAIL: {string.Join("; ", failures)}");
                SmokeGate.Exit(1);
                return;
            }
            Debug.Log($"{LogPrefix}: OK");
            SmokeGate.Exit(0);
        }
    }
}
