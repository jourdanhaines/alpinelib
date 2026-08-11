using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Client to server, thirty times a second: what the owner wants their pawn to do this step, bundled
    /// with the couple of steps before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole of the default authority model rests on this message. The client never says where it is
    /// — only what it is trying to do — so there is no position for it to lie about, and the fastest a
    /// cheat can make a pawn go is the fastest the server's own motor will carry it.
    /// </para>
    /// <para>
    /// <b>Why a bundle.</b> Inputs ride an unreliable channel, and a lost input is not retried — the
    /// server simulates a starved tick instead, which the owner never predicted and pays back as a
    /// correction. Carrying the previous two inputs alongside the newest means any single loss or
    /// reorder is healed by the next packet that survives, including a jump bit that would otherwise be
    /// gone for good. The server deduplicates by sequence, so redundancy is free of double-simulation;
    /// the cost is a few extra bytes on a message this small.
    /// </para>
    /// </remarks>
    public struct InputCommand : INetMessage {
        /// <summary>Most inputs one command may carry: the newest plus up to two predecessors.</summary>
        public const int MaxBundleSize = 3;

        /// <summary>Creates a command carrying a single input.</summary>
        public InputCommand(uint entityId, in PawnInput input) {
            EntityId = entityId;
            First = input;
            Second = default;
            Third = default;
            Count = 1;
        }

        /// <summary>The pawn this input is for. The server checks the sender actually owns it.</summary>
        public uint EntityId { get; set; }

        /// <summary>How many of the input slots are populated, 1 to <see cref="MaxBundleSize"/>.</summary>
        public byte Count { get; set; }

        /// <summary>Oldest populated input.</summary>
        public PawnInput First { get; set; }

        /// <summary>Middle input, populated when <see cref="Count"/> is 3.</summary>
        public PawnInput Second { get; set; }

        /// <summary>Newest input, populated when <see cref="Count"/> is 3.</summary>
        public PawnInput Third { get; set; }

        /// <summary>The newest input in the bundle — the one this send is actually for.</summary>
        public PawnInput Newest => GetInput(Count - 1);

        /// <summary>Reads a populated slot in oldest-to-newest order.</summary>
        public PawnInput GetInput(int index) {
            switch (index) {
                case 0: return First;
                case 1: return Second;
                case 2: return Third;
                default: return default;
            }
        }

        /// <summary>
        /// Builds a command carrying a newest input and whatever predecessors the caller still holds,
        /// oldest first.
        /// </summary>
        public static InputCommand Bundle(uint entityId, in PawnInput olderA, in PawnInput olderB, in PawnInput newest, int olderCount) {
            var command = new InputCommand { EntityId = entityId };

            if (olderCount >= 2) {
                command.First = olderA;
                command.Second = olderB;
                command.Third = newest;
                command.Count = 3;
                return command;
            }

            if (olderCount == 1) {
                command.First = olderB;
                command.Second = newest;
                command.Count = 2;
                return command;
            }

            command.First = newest;
            command.Count = 1;
            return command;
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            byte count = Count == 0 ? (byte)1 : Count;
            writer.WriteByte(count);

            for (int inputIndex = 0; inputIndex < count && inputIndex < MaxBundleSize; inputIndex++) {
                writer.WriteMessage(GetInput(inputIndex));
            }
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            int count = reader.ReadByte();

            if (count < 1) count = 1;
            if (count > MaxBundleSize) count = MaxBundleSize;

            Count = (byte)count;
            First = count > 0 ? reader.ReadMessage<PawnInput>() : default;
            Second = count > 1 ? reader.ReadMessage<PawnInput>() : default;
            Third = count > 2 ? reader.ReadMessage<PawnInput>() : default;
        }
    }
}
