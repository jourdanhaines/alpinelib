using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Everything the network needs to know about where a pawn is and what it is doing: a position, a
    /// facing, a velocity and one byte of packed locomotion bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the output of <see cref="PawnMotor"/>, the payload of every snapshot record, and the unit
    /// the interpolator blends. It deliberately carries no identity — the entity id lives in whatever
    /// message wraps it — so the same struct serves the authoritative record, the predicted record and
    /// the interpolated render pose without three near-identical types drifting apart.
    /// </para>
    /// <para>
    /// <b>Flags layout</b> (a wire contract, never repack): bits 0-2 hold a <see cref="WireLocomotion"/>
    /// gait, bit 3 is crouching, bit 4 is grounded. Bits 5-7 are reserved and must stay zero so a future
    /// reader can tell "old sender" from "garbage".
    /// </para>
    /// <para>
    /// Position travels as full floats because it is what everything else is measured against; yaw and
    /// velocity travel quantized (see <see cref="NetQuantization"/>). <see cref="Quantized"/> exists so
    /// prediction can compare like with like: a client that predicted in full precision and compares
    /// against a wire-rounded authoritative state would see a correction on every single tick.
    /// </para>
    /// </remarks>
    public struct PawnState : INetMessage {
        /// <summary>Mask covering the three gait bits.</summary>
        public const byte LocomotionMask = 0b0000_0111;

        /// <summary>Bit 3: the pawn is crouching.</summary>
        public const byte CrouchBit = 0b0000_1000;

        /// <summary>Bit 4: the pawn is standing on ground.</summary>
        public const byte GroundedBit = 0b0001_0000;

        /// <summary>Creates a fully specified state.</summary>
        public PawnState(Vector3 position, float yawDegrees, Vector3 velocity, byte flags) {
            Position = position;
            YawDegrees = yawDegrees;
            Velocity = velocity;
            Flags = flags;
        }

        /// <summary>World position in metres.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Facing around the up axis, in degrees. Not normalised until it is written.</summary>
        public float YawDegrees { get; set; }

        /// <summary>Velocity in metres per second, including the vertical component.</summary>
        public Vector3 Velocity { get; set; }

        /// <summary>Packed locomotion bits; see the layout note on the type.</summary>
        public byte Flags { get; set; }

        /// <summary>The gait in bits 0-2.</summary>
        public WireLocomotion Locomotion => (WireLocomotion)(Flags & LocomotionMask);

        /// <summary>True when bit 3 is set.</summary>
        public bool IsCrouching => (Flags & CrouchBit) != 0;

        /// <summary>True when bit 4 is set.</summary>
        public bool IsGrounded => (Flags & GroundedBit) != 0;

        /// <summary>Horizontal-only velocity, which is what the validator and the motor reason about.</summary>
        public Vector3 HorizontalVelocity => new Vector3(Velocity.X, 0f, Velocity.Z);

        /// <summary>Position difference below which two states count as the same pose, in metres.</summary>
        public const float PositionEpsilon = 0.0005f;

        /// <summary>
        /// True when two states are the same to within the resolution the wire can express.
        /// </summary>
        /// <remarks>
        /// Dirty tracking rests on this. Exact float equality would call a resting pawn changed forever,
        /// because the motor recomputes its gravity-and-clamp cycle every tick and the last bit wanders;
        /// comparing at wire resolution means "changed" only ever describes a difference a peer could
        /// actually have received.
        /// </remarks>
        public static bool ApproximatelyEquals(in PawnState left, in PawnState right) {
            if (left.Flags != right.Flags) {
                return false;
            }

            return IsClose(left.Position, right.Position, PositionEpsilon)
                && IsClose(left.Velocity, right.Velocity, NetQuantization.VelocityTolerance)
                && IsCloseAngle(left.YawDegrees, right.YawDegrees, NetQuantization.YawToleranceDegrees);
        }

        /// <summary>Packs a gait plus the two state bits into the flags byte.</summary>
        public static byte PackFlags(WireLocomotion locomotion, bool isCrouching, bool isGrounded) {
            byte flags = (byte)((byte)locomotion & LocomotionMask);

            if (isCrouching) {
                flags |= CrouchBit;
            }

            if (isGrounded) {
                flags |= GroundedBit;
            }

            return flags;
        }

        /// <summary>Returns the same state with a rebuilt flags byte.</summary>
        public PawnState WithFlags(WireLocomotion locomotion, bool isCrouching, bool isGrounded) {
            return new PawnState(Position, YawDegrees, Velocity, PackFlags(locomotion, isCrouching, isGrounded));
        }

        /// <summary>
        /// The state as it would come back off the wire: yaw and velocity pushed through their
        /// quantizers, position untouched. Prediction compares against this so wire rounding alone never
        /// looks like a divergence.
        /// </summary>
        public PawnState Quantized() {
            return new PawnState(
                Position,
                NetQuantization.QuantizeYaw(YawDegrees),
                NetQuantization.QuantizeVelocity(Velocity),
                Flags);
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteVector3(Position);
            writer.WriteQuantizedYaw(YawDegrees);
            writer.WriteQuantizedVelocity(Velocity);
            writer.WriteByte(Flags);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Position = reader.ReadVector3();
            YawDegrees = reader.ReadQuantizedYaw();
            Velocity = reader.ReadQuantizedVelocity();
            Flags = reader.ReadByte();
        }

        private static bool IsClose(Vector3 left, Vector3 right, float epsilon) {
            return MathF.Abs(left.X - right.X) <= epsilon
                && MathF.Abs(left.Y - right.Y) <= epsilon
                && MathF.Abs(left.Z - right.Z) <= epsilon;
        }

        private static bool IsCloseAngle(float leftDegrees, float rightDegrees, float epsilonDegrees) {
            float difference = MathF.Abs(leftDegrees - rightDegrees) % 360f;

            if (difference > 180f) {
                difference = 360f - difference;
            }

            return difference <= epsilonDegrees;
        }
    }
}
