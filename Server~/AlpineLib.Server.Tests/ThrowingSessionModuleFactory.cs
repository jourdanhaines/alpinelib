using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A game plug-in that builds a fixed number of modules and then refuses, the way a real one refuses
    /// a consist it cannot read out of its own configuration.
    /// </summary>
    /// <remarks>
    /// The refusal is a plain throw from <see cref="Create"/>, which is the only way a game has of saying
    /// no: the interface returns a module or it does not return. What a test wants to see is that the
    /// throw costs that one create and nothing else, so the entry the refusal was handed is kept here for
    /// the test to inspect afterwards.
    /// </remarks>
    internal sealed class ThrowingSessionModuleFactory : ISessionModuleFactory {
        /// <summary>What the refusal says, so a test can recognise it in a log.</summary>
        public const string FaultMessage = "The game cannot build a module for this session.";

        private readonly int _sessionsBuiltBeforeTheFault;

        private int _createCount;

        /// <param name="sessionsBuiltBeforeTheFault">
        /// How many sessions are built normally before every later one is refused. Zero refuses the first.
        /// </param>
        public ThrowingSessionModuleFactory(int sessionsBuiltBeforeTheFault) {
            _sessionsBuiltBeforeTheFault = sessionsBuiltBeforeTheFault;
        }

        /// <summary>Modules this factory did build, in creation order.</summary>
        public List<RecordingSessionModule> Modules { get; } = new List<RecordingSessionModule>();

        /// <summary>How many times the library asked for a module, refusals included.</summary>
        public int CreateCount => _createCount;

        /// <summary>The entry handed to the last refusal, so a test can see what became of it.</summary>
        public SessionEntry RefusedEntry { get; private set; }

        /// <inheritdoc />
        public ISessionModule Create(SessionEntry entry) {
            _createCount++;

            if (_createCount > _sessionsBuiltBeforeTheFault) {
                RefusedEntry = entry;
                throw new InvalidOperationException(FaultMessage);
            }

            RecordingSessionModule module = new RecordingSessionModule(entry);
            Modules.Add(module);
            return module;
        }

        /// <inheritdoc />
        /// <remarks>Claims no ids: these tests are about creation, not about the game's own traffic.</remarks>
        public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
        }
    }
}
