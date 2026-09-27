using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace AlpineLib.Editor {
    /// <summary>
    /// Runs several batchmode gates in one editor process, so a gate suite pays for editor startup once
    /// rather than once per gate. Each gate is a public static parameterless method that reports through
    /// <see cref="SmokeGate.Exit"/>:
    /// <c>-batchmode -quit -executeMethod AlpineLib.Editor.SmokeGateRunner.RunFromCommandLine
    /// -smokeGates Ns.TypeA.Verify,Ns.TypeB.Verify</c>.
    /// </summary>
    /// <remarks>
    /// Logs <c>SmokeGate: &lt;method&gt; PASS|FAIL &lt;seconds&gt;</c> per gate, stops at the first
    /// failure and exits the editor 0 only when every gate passed.
    /// </remarks>
    public static class SmokeGateRunner {
        private const string gatesArgument = "-smokeGates";
        private const string logPrefix = "SmokeGate";

        public static void RunFromCommandLine() {
            string[] gateNames = ReadGateNames();
            bool passed = gateNames.Length > 0 && RunAll(gateNames);
            if (gateNames.Length == 0) Debug.LogError($"{logPrefix}: no gates given; pass {gatesArgument} A.B.Method,...");

            EditorApplication.Exit(passed ? 0 : 1);
        }

        /// <summary>Runs <paramref name="gateNames"/> in order and answers whether every one passed.</summary>
        public static bool RunAll(IReadOnlyList<string> gateNames) {
            SmokeGate.IsRunnerActive = true;
            try {
                foreach (string gateName in gateNames) {
                    if (!RunGate(gateName)) return false;
                }

                return true;
            } finally {
                SmokeGate.IsRunnerActive = false;
            }
        }

        private static bool RunGate(string gateName) {
            Debug.Log($"{logPrefix}: begin {gateName}");
            Stopwatch stopwatch = Stopwatch.StartNew();
            int code = Invoke(gateName);
            stopwatch.Stop();

            string result = code == 0 ? "PASS" : "FAIL";
            Debug.Log($"{logPrefix}: {gateName} {result} {stopwatch.Elapsed.TotalSeconds:F1}");
            return code == 0;
        }

        // A gate that returns without calling SmokeGate.Exit passed.
        private static int Invoke(string gateName) {
            MethodInfo method = ResolveGate(gateName);
            if (method == null) {
                Debug.LogError($"{logPrefix}: no public static parameterless method '{gateName}'");
                return 1;
            }

            try {
                method.Invoke(null, null);
                return 0;
            } catch (TargetInvocationException exception) when (exception.InnerException is SmokeGateExitException exit) {
                return exit.Code;
            } catch (TargetInvocationException exception) {
                Debug.LogException(exception.InnerException ?? exception);
                return 1;
            }
        }

        private static MethodInfo ResolveGate(string gateName) {
            int split = gateName.LastIndexOf('.');
            if (split <= 0) return null;

            string typeName = gateName.Substring(0, split);
            string methodName = gateName.Substring(split + 1);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Type type = assembly.GetType(typeName);
                MethodInfo method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method != null) return method;
            }

            return null;
        }

        private static string[] ReadGateNames() {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, gatesArgument);
            if (index < 0 || index + 1 >= arguments.Length) return Array.Empty<string>();

            return arguments[index + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
