using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Guards the rule the whole networking design rests on: the shared assemblies must compile and run
    /// outside Unity. A stray UnityEngine reference would only surface as a server crash at runtime, so
    /// it is asserted at the assembly-reference level instead.
    /// </summary>
    public sealed class SharedAssemblyIsolationTests {
        [Theory]
        [InlineData("AlpineLib.Netcode")]
        [InlineData("AlpineLib.Chat")]
        [InlineData("AlpineLib.Server")]
        public void SharedAssemblyLoadsOutsideUnity(string assemblyName) {
            var assembly = Assembly.Load(assemblyName);

            Assert.NotNull(assembly);
        }

        [Theory]
        [InlineData("AlpineLib.Netcode")]
        [InlineData("AlpineLib.Chat")]
        [InlineData("AlpineLib.Server")]
        public void SharedAssemblyReferencesNoEngineAssemblies(string assemblyName) {
            var assembly = Assembly.Load(assemblyName);

            var engineReferences = assembly
                .GetReferencedAssemblies()
                .Where(IsEngineAssembly)
                .Select(reference => reference.Name)
                .ToArray();

            Assert.Empty(engineReferences);
        }

        [Fact]
        public void VendoredTransportLibraryIsAvailable() {
            var netcode = Assembly.Load("AlpineLib.Netcode");

            var netManager = netcode.GetType("LiteNetLib.LiteNetManager", throwOnError: false)
                ?? netcode.GetType("LiteNetLib.NetManager", throwOnError: false);

            Assert.NotNull(netManager);
        }

        private static bool IsEngineAssembly(AssemblyName reference) {
            return reference.Name != null
                && reference.Name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase);
        }
    }
}
