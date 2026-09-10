using System;
using AlpineLib.Chat;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// Presents one <see cref="SessionHost"/> to the chat pipeline as the room it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of the chat-to-session dependency, and it points one way on purpose: chat leans
    /// on the session, the session has never heard of chat. Everything the pipeline needs — who is in
    /// the room, what they are called, whether they are reachable right now, what time it is — is
    /// answered from the roster the host already keeps.
    /// </para>
    /// <para>
    /// <b>A dropped link is not a departure.</b> When a member's connection dies while their seat is held
    /// for a rejoin, this reports <see cref="PlayerDisconnected"/> rather than
    /// <see cref="PlayerLeftRoom"/>: the pipeline keeps their subscription and their place in the history
    /// buffer, stops delivering while they are away, and the history push they get on rejoin fills the
    /// gap. Reporting a departure instead would drop them out of the room and lose the backlog they are
    /// about to come back for.
    /// </para>
    /// </remarks>
    public sealed class SessionHostChatAdapter : IChatServerHost {
        private readonly SessionHost _host;
        private readonly Func<long> _clock;

        private bool _isStarted;

        /// <summary>Wraps a session. Nothing is observed until <see cref="Start"/>.</summary>
        /// <param name="host">The session whose roster is the room.</param>
        /// <param name="clock">
        /// Reads the server's wall clock in Unix milliseconds. Injected rather than read directly so a
        /// test can move time by hand and watch a mute expire without waiting for it.
        /// </param>
        public SessionHostChatAdapter(SessionHost host, Func<long> clock) {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <inheritdoc />
        public event Action<PlayerId, string> PlayerJoinedRoom;

        /// <inheritdoc />
        public event Action<PlayerId, string> PlayerLeftRoom;

        /// <inheritdoc />
        public event Action<PlayerId> PlayerDisconnected;

        /// <inheritdoc />
        public long ServerTimeUnixMs => _clock();

        /// <summary>
        /// Key of the room channel this session's members talk in. The session id, because it is the one
        /// identifier both ends of the wire agree on for the whole life of the session — a join code can
        /// be recycled once the session dies.
        /// </summary>
        public string RoomKey => _host.SessionId;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsStarted => _isStarted;

        /// <summary>Subscribes to the session's roster events.</summary>
        public void Start() {
            if (_isStarted) {
                return;
            }

            _isStarted = true;
            _host.OnMemberJoined += HandleMemberJoined;
            _host.OnMemberLeft += HandleMemberLeft;
        }

        /// <summary>Stops observing the session. Safe to call when never started.</summary>
        public void Stop() {
            if (!_isStarted) {
                return;
            }

            _isStarted = false;
            _host.OnMemberJoined -= HandleMemberJoined;
            _host.OnMemberLeft -= HandleMemberLeft;
        }

        /// <inheritdoc />
        public string GetDisplayName(PlayerId player) {
            SessionMember member = _host.FindMember(player);
            return member == null ? string.Empty : member.DisplayName ?? string.Empty;
        }

        /// <inheritdoc />
        public bool IsOnline(PlayerId player) {
            SessionMember member = _host.FindMember(player);
            return member != null && member.IsConnected;
        }

        private void HandleMemberJoined(SessionMember member, bool isRejoin) {
            // A rejoin is announced exactly like a fresh arrival: the pipeline re-subscribes the player
            // and pushes the backlog, which is the entire chat side of coming back.
            PlayerJoinedRoom?.Invoke(member.PlayerId, RoomKey);
        }

        private void HandleMemberLeft(SessionMember member, LeaveReason reason) {
            if (IsSeatStillHeld(member)) {
                PlayerDisconnected?.Invoke(member.PlayerId);
                return;
            }

            PlayerLeftRoom?.Invoke(member.PlayerId, RoomKey);
        }

        private bool IsSeatStillHeld(SessionMember member) {
            // The host has already retired the member by the time this fires: still on the roster means a
            // rejoin reservation is being held, gone means the seat was given up for good.
            return _host.FindMember(member.PlayerId) != null;
        }
    }
}
