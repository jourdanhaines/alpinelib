using System.IO;
using AlpineLib.Server.Hosting;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Covers the precedence between the two things that fill the host options: the deployed
    /// configuration file, and the command line the process was launched with.
    /// </summary>
    public sealed class ServerRuntimeOptionsTests {
        [Fact]
        public void AnAbsentFlagLeavesTheConfiguredValueStanding() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { MaxSessions = 3, IdleExitSeconds = 45 };

            options.ApplyArguments(ServerArguments.Parse(new[] { "--port", "9051" }));

            Assert.Equal(3, options.MaxSessions);
            Assert.Equal(45, options.IdleExitSeconds);
            Assert.Equal(9051, options.PortOverride);
        }

        [Fact]
        public void TheCommandLineWinsOverTheConfiguredValue() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { MaxSessions = 8, IdleExitSeconds = 0 };

            options.ApplyArguments(ServerArguments.Parse(new[] { "--max-sessions", "1", "--idle-exit-seconds", "30" }));

            Assert.Equal(1, options.MaxSessions);
            Assert.Equal(30, options.IdleExitSeconds);
        }

        [Fact]
        public void OneConfigDirectoryNamesBothExports() {
            // A launcher passes one path; the two exports must not be able to drift apart.
            ServerRuntimeOptions options = new ServerRuntimeOptions();

            options.ApplyArguments(ServerArguments.Parse(new[] { "--config", "/srv/alpine/cfg" }));

            Assert.Equal(Path.Combine("/srv/alpine/cfg", "session-config.json"), options.SessionConfigPath);
            Assert.Equal(Path.Combine("/srv/alpine/cfg", "geometry"), options.GeometryDirectory);
        }

        [Fact]
        public void ARelativePathIsResolvedAgainstTheContentRoot() {
            ServerRuntimeOptions options = new ServerRuntimeOptions();

            string resolved = options.ResolveSessionConfigPath("/opt/server");

            Assert.Equal(Path.Combine("/opt/server", "config", "session-config.json"), resolved);
        }

        [Fact]
        public void ARootedPathIsLeftExactlyAsItWasGiven() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { GeometryDirectory = "/var/lib/alpine/geometry" };

            Assert.Equal("/var/lib/alpine/geometry", options.ResolveGeometryDirectory("/opt/server"));
        }

        [Fact]
        public void AnUnsetPathStaysEmptyRatherThanBecomingTheContentRoot() {
            // The loader has a far better message for "nobody configured one" than File.Exists has for a
            // directory that happens to be the content root.
            ServerRuntimeOptions options = new ServerRuntimeOptions { SessionConfigPath = string.Empty };

            Assert.Equal(string.Empty, options.ResolveSessionConfigPath("/opt/server"));
        }

        /// <summary>
        /// A path that walks out of the content root and back in again resolves to where it actually
        /// points, so the log line an operator reads names a real place.
        /// </summary>
        [Fact]
        public void ATraversingPathIsCollapsedRatherThanPassedOnAsWritten() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { GeometryDirectory = Path.Combine("..", "shared", "geometry") };

            Assert.Equal(
                Path.Combine("/opt", "shared", "geometry"),
                options.ResolveGeometryDirectory("/opt/server"));
        }

        /// <summary>
        /// With no content root there is nothing to anchor a relative path to, and the working directory
        /// is not an answer: it would name a different file for a server launched from somewhere else.
        /// </summary>
        [Fact]
        public void ARelativePathWithNoContentRootIsLeftAloneRatherThanTiedToTheWorkingDirectory() {
            ServerRuntimeOptions options = new ServerRuntimeOptions {
                GeometryDirectory = Path.Combine("config", "geometry")
            };

            Assert.Equal(Path.Combine("config", "geometry"), options.ResolveGeometryDirectory(string.Empty));
        }

        /// <summary>A rooted path is normalised too — it is still the thing that goes in the log line.</summary>
        [Fact]
        public void ARootedPathIsNormalisedEvenThoughTheContentRootIsIgnored() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { GeometryDirectory = "/var/lib/alpine/./cfg/../geometry" };

            Assert.Equal("/var/lib/alpine/geometry", options.ResolveGeometryDirectory("/opt/server"));
        }

        [Fact]
        public void TheShippedDefaultsAreADedicatedServerThatNeverStopsItself() {
            ServerRuntimeOptions options = new ServerRuntimeOptions();

            Assert.Equal(8, options.MaxSessions);
            Assert.Equal(0, options.IdleExitSeconds);
            Assert.Null(options.PortOverride);
        }
    }
}
