using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// One tick's worth of a player's intent: which way they are pushing, which gait they are in, and
    /// whether they are asking to jump or crouch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In the default <see cref="AuthorityMode.Server"/> this is the only thing the owning client sends
    /// about its pawn — the server turns it into a <see cref="PawnState"/> by running
    /// <see cref="PawnMotor.Step"/>, and the client runs the same step locally to predict.
    /// </para>
    /// <para>
    /// <see cref="Tick"/> is the client's own tick counter, not the server's. It is an opaque stamp that
    /// comes back on <c>AuthorityCorrection</c>, which is what lets
    /// <see cref="PredictionBuffer.Reconcile"/> know exactly how far to rewind.
    /// </para>
    /// <para>
    /// <b>Why the move direction is not quantized.</b> Every other client-to-server field on this
    /// protocol is packed down, but the motor is deterministic only if the server steps the exact floats
    /// the client predicted with. Quantizing here would force the client to predict from the rounded
    /// value or accept a correction every tick; at thirty packets a second, eight bytes is a far cheaper
    /// price than either.
    /// </para>
    /// </remarks>
    public struct PawnInput : INetMessage {
        /// <summary>Bit 0 of <see cref="Buttons"/>: jump was pressed this tick.</summary>
        public const byte JumpBit = 0b0000_0001;

        /// <summary>Bit 1 of <see cref="Buttons"/>: crouch is held this tick.</summary>
        public const byte CrouchBit = 0b0000_0010;

        /// <summary>Creates an input from unpacked intent.</summary>
        public PawnInput(uint tick, Vector2 moveDirection, WireLocomotion gait, bool jump, bool crouch) {
            Tick = tick;
            MoveDirection = moveDirection;
            Gait = gait;
            Buttons = PackButtons(jump, crouch);
        }

        /// <summary>The sending client's tick counter. Echoed back on a correction.</summary>
        public uint Tick { get; set; }

        /// <summary>
        /// Desired movement on the ground plane, X east and Y north, in the range [-1, 1] per axis. The
        /// motor clamps the magnitude, so a client cannot buy speed by sending a longer vector.
        /// </summary>
        public Vector2 MoveDirection { get; set; }

        /// <summary>The gait the player's locomotion state machine is in.</summary>
        public WireLocomotion Gait { get; set; }

        /// <summary>Packed button bits; see <see cref="JumpBit"/> and <see cref="CrouchBit"/>.</summary>
        public byte Buttons { get; set; }

        /// <summary>True when bit 0 is set.</summary>
        public bool Jump => (Buttons & JumpBit) != 0;

        /// <summary>True when bit 1 is set.</summary>
        public bool Crouch => (Buttons & CrouchBit) != 0;

        /// <summary>Packs the two button intents into the bits the wire carries.</summary>
        public static byte PackButtons(bool jump, bool crouch) {
            byte buttons = 0;

            if (jump) {
                buttons |= JumpBit;
            }

            if (crouch) {
                buttons |= CrouchBit;
            }

            return buttons;
        }

        /// <summary>
        /// The same input with the jump bit cleared.
        /// </summary>
        /// <remarks>
        /// The server repeats a pawn's last input when the owner's stream stutters, so the pawn keeps
        /// walking instead of stopping dead on every dropped packet. Repeating the jump bit with it would
        /// turn one press into a launch on every tick the gap lasted, so it is dropped and only the
        /// continuous part of the intent is carried forward. The tick is deliberately left alone: it is
        /// what gets acknowledged, and inventing input ticks the client never sent would make the owner's
        /// prediction buffer discard work the server has not actually seen.
        /// </remarks>
        public PawnInput WithoutJump() {
            return new PawnInput {
                Tick = Tick,
                MoveDirection = MoveDirection,
                Gait = Gait,
                Buttons = (byte)(Buttons & ~JumpBit)
            };
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(Tick);
            writer.WriteFloat(MoveDirection.X);
            writer.WriteFloat(MoveDirection.Y);
            writer.WriteByte((byte)Gait);
            writer.WriteByte(Buttons);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Tick = reader.ReadUInt();
            float moveX = reader.ReadFloat();
            float moveY = reader.ReadFloat();
            MoveDirection = new Vector2(moveX, moveY);
            Gait = (WireLocomotion)reader.ReadByte();
            Buttons = reader.ReadByte();
        }
    }
}
