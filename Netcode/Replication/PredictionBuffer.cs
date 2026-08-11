using System;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The owning client's record of what it has done and not yet had confirmed: every input it sent,
    /// paired with the state it predicted that input would produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prediction is what hides the round trip. The client steps <see cref="PawnMotor"/> the instant it
    /// reads input rather than waiting for the server to answer, so the pawn moves on the frame the key
    /// went down. The cost is that the client is running ahead on a guess, and the server's answer —
    /// which arrives stamped with the last input it consumed — may not match.
    /// </para>
    /// <para>
    /// <see cref="Reconcile"/> is the repair. It throws away everything the correction has now settled,
    /// plants the authoritative state, and replays the inputs the server has not seen yet through the
    /// same motor. The player sees a snap only when the server genuinely disagreed; when it agreed, the
    /// replay lands on the same numbers and nothing visibly happens.
    /// </para>
    /// <para>
    /// Replay is stamped with real server ticks, not with a counter starting at zero. The motor's world
    /// contains movers whose poses are a function of the tick, so replaying the input the server will
    /// consume at tick <c>T</c> against tick <c>T − 40</c> would ride a platform that is somewhere else
    /// entirely and manufacture the very disagreement the replay exists to erase. The correction carries
    /// the tick its state belongs to, and the pending inputs after it were sent for the ticks that follow
    /// it one at a time — so input <c>k</c> of the replayed window is stepped at
    /// <c>correctionServerTick + k + 1</c>.
    /// </para>
    /// <para>
    /// Storage is a fixed ring. If a client ever gets more than <see cref="Capacity"/> ticks ahead of
    /// acknowledgement the connection is already unusable, so the oldest entry is dropped rather than
    /// growing without bound.
    /// </para>
    /// </remarks>
    public sealed class PredictionBuffer {
        /// <summary>Pending ticks held by default: four seconds at a 30 Hz send rate.</summary>
        public const int DefaultCapacity = 128;

        private readonly PendingStep[] steps;
        private int head;
        private int count;

        /// <summary>Creates a buffer holding <see cref="DefaultCapacity"/> pending steps.</summary>
        public PredictionBuffer() : this(DefaultCapacity) { }

        /// <summary>Creates a buffer holding a given number of pending steps.</summary>
        public PredictionBuffer(int capacity) {
            if (capacity <= 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Prediction capacity must be positive.");
            }

            steps = new PendingStep[capacity];
            head = 0;
            count = 0;
        }

        /// <summary>How many pending steps the ring can hold before it starts dropping the oldest.</summary>
        public int Capacity => steps.Length;

        /// <summary>Pending steps currently held — inputs sent but not yet confirmed.</summary>
        public int Count => count;

        /// <summary>The state produced by the most recent recorded step; default when nothing is pending.</summary>
        public PawnState LatestState => count == 0 ? default : steps[IndexOf(count - 1)].State;

        /// <summary>Sequence of the most recent recorded input, or zero when nothing is pending.</summary>
        public uint LatestSequence => count == 0 ? 0u : steps[IndexOf(count - 1)].Input.Sequence;

        /// <summary>Records an input and the state the local motor predicted for it.</summary>
        public void Record(in PawnInput input, in PawnState predictedState) {
            if (count == steps.Length) {
                head = Advance(head);
                count--;
            }

            steps[IndexOf(count)] = new PendingStep(input, predictedState);
            count++;
        }

        /// <summary>Finds the state this buffer predicted for an input sequence, if it is still pending.</summary>
        public bool TryGetPredictedState(uint sequence, out PawnState state) {
            for (int offset = 0; offset < count; offset++) {
                PendingStep step = steps[IndexOf(offset)];

                if (step.Input.Sequence != sequence) {
                    continue;
                }

                state = step.State;
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>
        /// Applies an authoritative correction and replays whatever it did not cover.
        /// </summary>
        /// <param name="acknowledgedSequence">The last input sequence the server had consumed when it sent this.</param>
        /// <param name="correctedState">The server's state after consuming that input.</param>
        /// <param name="correctionServerTick">The server tick the corrected state belongs to.</param>
        /// <param name="profile">Movement envelope for this pawn — must be the same one the server used.</param>
        /// <param name="world">Scene collision — must be built from the same geometry the server loaded.</param>
        /// <param name="deltaSeconds">The fixed step both ends simulate at.</param>
        /// <returns>The re-predicted present state, which the caller should adopt.</returns>
        public PawnState Reconcile(
            uint acknowledgedSequence,
            in PawnState correctedState,
            uint correctionServerTick,
            MovementProfile profile,
            CollisionWorld world,
            float deltaSeconds) {
            DropThrough(acknowledgedSequence);

            PawnState current = correctedState;

            for (int offset = 0; offset < count; offset++) {
                int index = IndexOf(offset);
                PawnInput replayed = steps[index].Input;
                uint simTick = correctionServerTick + (uint)offset + 1u;
                current = PawnMotor.Step(in replayed, in current, profile, world, simTick, deltaSeconds);
                steps[index] = new PendingStep(in replayed, in current);
            }

            return current;
        }

        /// <summary>
        /// Settles a sequence without replaying anything: pending steps at or before it are discarded and
        /// the rest are kept as they are. This is the whole of a clean correction — when the server's
        /// state matches what was predicted for that sequence, dropping the confirmed steps is the only
        /// work left to do, and rewinding would only re-derive the numbers already held.
        /// </summary>
        public void Acknowledge(uint sequence) {
            DropThrough(sequence);
        }

        /// <summary>Forgets everything pending. Used on respawn, on rejoin and on authority changes.</summary>
        public void Clear() {
            head = 0;
            count = 0;
        }

        /// <summary>Discards every pending step at or before a sequence the server has now settled.</summary>
        private void DropThrough(uint acknowledgedSequence) {
            while (count > 0 && IsAtOrBefore(steps[head].Input.Sequence, acknowledgedSequence)) {
                head = Advance(head);
                count--;
            }
        }

        /// <summary>
        /// Sequence comparison that survives the counter wrapping past uint.MaxValue: the difference,
        /// read as a signed offset, is what orders two sequences rather than their absolute values.
        /// </summary>
        private static bool IsAtOrBefore(uint sequence, uint reference) {
            return (int)(sequence - reference) <= 0;
        }

        private int IndexOf(int offset) {
            return (head + offset) % steps.Length;
        }

        private int Advance(int index) {
            return (index + 1) % steps.Length;
        }

        /// <summary>One sent-but-unconfirmed input and the state the client believes it produced.</summary>
        private readonly struct PendingStep {
            public PendingStep(in PawnInput input, in PawnState state) {
                Input = input;
                State = state;
            }

            /// <summary>The input as it went to the server.</summary>
            public PawnInput Input { get; }

            /// <summary>What the local motor predicted for it, updated in place on every replay.</summary>
            public PawnState State { get; }
        }
    }
}
