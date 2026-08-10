using System;
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

        /// <summary>Tick of the most recent recorded input, or zero when nothing is pending.</summary>
        public uint LatestTick => count == 0 ? 0u : steps[IndexOf(count - 1)].Input.Tick;

        /// <summary>Records an input and the state the local motor predicted for it.</summary>
        public void Record(in PawnInput input, in PawnState predictedState) {
            if (count == steps.Length) {
                head = Advance(head);
                count--;
            }

            steps[IndexOf(count)] = new PendingStep(input, predictedState);
            count++;
        }

        /// <summary>Finds the state this buffer predicted for a tick, if it is still pending.</summary>
        public bool TryGetPredictedState(uint tick, out PawnState state) {
            for (int offset = 0; offset < count; offset++) {
                PendingStep step = steps[IndexOf(offset)];

                if (step.Input.Tick != tick) {
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
        /// <param name="acknowledgedTick">The last input tick the server had consumed when it sent this.</param>
        /// <param name="correctedState">The server's state after consuming that input.</param>
        /// <param name="profile">Movement envelope for this pawn — must be the same one the server used.</param>
        /// <param name="groundProvider">Ground seam — must agree with the server's.</param>
        /// <param name="deltaSeconds">The fixed step both ends simulate at.</param>
        /// <returns>The re-predicted present state, which the caller should adopt.</returns>
        public PawnState Reconcile(
            uint acknowledgedTick,
            in PawnState correctedState,
            MovementProfile profile,
            IGroundProvider groundProvider,
            float deltaSeconds) {
            DropThrough(acknowledgedTick);

            PawnState current = correctedState;

            for (int offset = 0; offset < count; offset++) {
                int index = IndexOf(offset);
                PawnInput replayed = steps[index].Input;
                current = PawnMotor.Step(in replayed, in current, profile, groundProvider, deltaSeconds);
                steps[index] = new PendingStep(in replayed, in current);
            }

            return current;
        }

        /// <summary>Forgets everything pending. Used on respawn, on rejoin and on authority changes.</summary>
        public void Clear() {
            head = 0;
            count = 0;
        }

        /// <summary>Discards every pending step at or before a tick the server has now settled.</summary>
        private void DropThrough(uint acknowledgedTick) {
            while (count > 0 && IsAtOrBefore(steps[head].Input.Tick, acknowledgedTick)) {
                head = Advance(head);
                count--;
            }
        }

        /// <summary>
        /// Tick comparison that survives the counter wrapping past uint.MaxValue: the difference, read as
        /// a signed offset, is what orders two ticks rather than their absolute values.
        /// </summary>
        private static bool IsAtOrBefore(uint tick, uint reference) {
            return (int)(tick - reference) <= 0;
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
