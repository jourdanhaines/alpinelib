using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A stand-in for a game's own server plug-in: one module per session, and one handler for the whole
    /// process that demultiplexes by sender.
    /// </summary>
    /// <remarks>
    /// The demux is the part worth pinning. A game that registered its handler and then acted on the
    /// message without asking which session the sender is in would be letting one session's traffic
    /// change another's world.
    /// </remarks>
    internal sealed class RecordingSessionModuleFactory : ISessionModuleFactory {
        private readonly Dictionary<SessionEntry, RecordingSessionModule> _modulesByEntry =
            new Dictionary<SessionEntry, RecordingSessionModule>();

        private Func<PeerHandle, SessionEntry> _resolveEntry;

        /// <summary>Every module this factory built, in creation order.</summary>
        public List<RecordingSessionModule> Modules { get; } = new List<RecordingSessionModule>();

        /// <summary>How many times the library asked for handlers to be registered.</summary>
        public int RegisterHandlerCallCount { get; private set; }

        /// <summary>Payloads that arrived from a peer in no session at all.</summary>
        public List<byte> UnroutedPayloads { get; } = new List<byte>();

        /// <inheritdoc />
        public ISessionModule Create(SessionEntry entry) {
            RecordingSessionModule module = new RecordingSessionModule(entry);
            _modulesByEntry[entry] = module;
            Modules.Add(module);
            return module;
        }

        /// <inheritdoc />
        public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            RegisterHandlerCallCount++;
            _resolveEntry = resolveEntry;
            router.Register<GameModuleTestMessage>(GameModuleTestMessage.MessageId, ReceiveTestMessage);
        }

        /// <summary>The module built for one session, or null when none was.</summary>
        public RecordingSessionModule ModuleFor(SessionEntry entry) {
            return entry != null && _modulesByEntry.TryGetValue(entry, out RecordingSessionModule module) ? module : null;
        }

        private void ReceiveTestMessage(in GameModuleTestMessage message, PeerHandle sender) {
            SessionEntry entry = _resolveEntry(sender);

            if (entry == null) {
                UnroutedPayloads.Add(message.Payload);
                return;
            }

            ModuleFor(entry)?.Receive(message.Payload);
        }
    }
}
