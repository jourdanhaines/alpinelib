using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A game factory that answers <see cref="ISessionModuleFactory.CreatePlacement"/> — with a
    /// recording placement, with nothing, or with a throw — and records what it was shown.
    /// </summary>
    internal sealed class PlacementSessionModuleFactory : ISessionModuleFactory {
        private readonly Func<SessionEntry, ISpawnPlacement> _answer;
        private readonly Dictionary<SessionEntry, ISessionModule> _moduleByEntry = new Dictionary<SessionEntry, ISessionModule>();

        public PlacementSessionModuleFactory(Func<SessionEntry, ISpawnPlacement> answer) {
            _answer = answer;
        }

        /// <summary>Every entry a module was built for, in creation order.</summary>
        public List<SessionEntry> Entries { get; } = new List<SessionEntry>();

        /// <summary>Whether the module handed to <c>CreatePlacement</c> was the one <c>Create</c> built.</summary>
        public List<bool> SawOwnModule { get; } = new List<bool>();

        /// <summary>The params each entry exposed when its placement was asked for.</summary>
        public List<byte[]> ParamsSeen { get; } = new List<byte[]>();

        /// <inheritdoc />
        public ISessionModule Create(SessionEntry entry) {
            RecordingSessionModule module = new RecordingSessionModule(entry);
            Entries.Add(entry);
            _moduleByEntry[entry] = module;
            return module;
        }

        /// <inheritdoc />
        public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
        }

        /// <inheritdoc />
        public ISpawnPlacement CreatePlacement(SessionEntry entry, ISessionModule module) {
            SawOwnModule.Add(_moduleByEntry.TryGetValue(entry, out ISessionModule built) && ReferenceEquals(built, module));
            ParamsSeen.Add(entry.CreateParams);
            return _answer(entry);
        }
    }
}
