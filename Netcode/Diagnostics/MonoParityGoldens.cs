using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Netcode.Diagnostics {
    /// <summary>
    /// FNV-1a 64 hashes of the collision narrow phase and the pawn motor over integer-built inputs, with the values
    /// CoreCLR produces. A runtime that returns anything else (the Unity editor's Mono, say) will drift from the server.
    /// </summary>
    public static class MonoParityGoldens {
        /// <summary>Box, sphere, capsule and plane contacts and supports over the golden poses.</summary>
        public const ulong ShapeGolden = 0x2A398B624CD6B11DUL;

        /// <summary>Pawn motor states over the golden script and world.</summary>
        public const ulong MotorGolden = 0xEE6E28DCD15A17E5UL;

        /// <summary>Prism contacts and supports over the golden poses.</summary>
        public const ulong PrismGolden = 0xE7F2B71C41C1F340UL;

        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private const float TickSeconds = 1f / 30f;
        private const int PoseCount = 4096;

        /// <summary>The narrow-phase golden's shapes: turned and plain boxes, a sphere, two capsules, a plane.</summary>
        public static CollisionShape[] Shapes() {
            return new[] {
                CollisionShape.MakeBox(new Vector3(0.25f, 0.5f, -0.5f), new Vector3(0.6f, 0f, -0.8f), Vector3.UnitY, new Vector3(0.8f, 0f, 0.6f), new Vector3(1.125f, 0.5f, 0.75f)),
                CollisionShape.MakeBox(new Vector3(-0.5f, 0.75f, -1f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1.5f, 0.75f, 1.125f)),
                CollisionShape.MakeBox(new Vector3(0.5f, 1.5f, 0.25f), new Vector3(0.6f, 0.8f, 0f), new Vector3(-0.8f, 0.6f, 0f), Vector3.UnitZ, new Vector3(0.5f, 0.25f, 0.75f)),
                CollisionShape.MakeSphere(new Vector3(-1f, 1.25f, 0.75f), 1.125f),
                CollisionShape.MakeCapsule(new Vector3(0.5f, 1f, 0.5f), new Vector3(0.6f, 0.8f, 0f), 0.75f, 0.375f),
                CollisionShape.MakeCapsule(new Vector3(-0.25f, 0.5f, -0.75f), Vector3.UnitY, 0.5f, 0.625f),
                CollisionShape.MakePlane(0.0625f),
            };
        }

        /// <summary>The prism golden's four literal prisms, one with sixteen vertices.</summary>
        public static CollisionShape[] Prisms() {
            return new[] {
                CollisionShape.MakePrism(new Vector3(0.25f, 0.5f, -0.5f), 0.5f, new[] { new Vector2(-1f, -1f), new Vector2(1.5f, -0.75f), new Vector2(1f, 1f), new Vector2(-0.5f, 1.25f) }),
                CollisionShape.MakePrism(new Vector3(-1f, 1.25f, 0.75f), 1.25f, new[] { new Vector2(0f, -1f), new Vector2(0.875f, -0.5f), new Vector2(0.875f, 0.5f), new Vector2(0f, 1f), new Vector2(-0.875f, 0.5f), new Vector2(-0.875f, -0.5f) }),
                CollisionShape.MakePrism(new Vector3(0.5f, 2f, 0.5f), 0.25f, new[] { new Vector2(-0.75f, -0.5f), new Vector2(0.75f, -0.5f), new Vector2(0f, 0.75f) }),
                CollisionShape.MakePrism(new Vector3(-0.5f, 0.75f, -1f), 0.75f, new[] {
                    new Vector2(1.5f, 0.109375f), new Vector2(1.328125f, 0.515625f), new Vector2(0.953125f, 0.859375f), new Vector2(0.4375f, 1.046875f),
                    new Vector2(-0.15625f, 1.09375f), new Vector2(-0.703125f, 0.96875f), new Vector2(-1.15625f, 0.703125f), new Vector2(-1.4375f, 0.3125f),
                    new Vector2(-1.5f, -0.109375f), new Vector2(-1.328125f, -0.515625f), new Vector2(-0.953125f, -0.859375f), new Vector2(-0.4375f, -1.046875f),
                    new Vector2(0.15625f, -1.09375f), new Vector2(0.703125f, -0.96875f), new Vector2(1.15625f, -0.703125f), new Vector2(1.4375f, -0.3125f),
                }),
            };
        }

        /// <summary>Golden pose <paramref name="index"/>, built from integers.</summary>
        public static CapsulePose Pose(int index) {
            float x = ((index * 37) % 97 - 48) / 16f;
            float z = ((index * 53) % 89 - 44) / 16f;
            float y = ((index * 29) % 61 - 20) / 16f;
            float radius = 0.25f + (index % 5) / 16f;
            return new CapsulePose(new Vector3(x, y, z), radius, radius * 2f + (index % 7) / 8f);
        }

        /// <summary>Contact and support outputs of every golden pose against every shape of <see cref="Shapes"/>.</summary>
        public static ulong ShapeHash() {
            return HashPoses(Shapes(), true);
        }

        /// <summary>Contact and support heights of every golden pose against every prism of <see cref="Prisms"/>.</summary>
        public static ulong PrismHash() {
            return HashPoses(Prisms(), false);
        }

        /// <summary>
        /// Pawn states over a fixed input script through a world of every primitive and a mover. Yaw is left out: it
        /// comes from <c>MathF.Atan2</c> and never feeds a position.
        /// </summary>
        public static ulong MotorHash() {
            CollisionWorld world = World();
            var profile = new MovementProfile { SlopeLimitDegrees = 45f };
            ulong hash = OffsetBasis;
            for (int pawn = 0; pawn < 12; pawn++) {
                var start = new Vector3((pawn % 4 - 1.5f) * 1.25f, 0.0625f + (pawn % 3) * 0.5f, (pawn / 4 - 1f) * 1.5f);
                var state = new PawnState(start, pawn * 30f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, pawn % 3 == 0));
                for (int tick = 1; tick <= 180; tick++) {
                    PawnInput input = Input(pawn, tick);
                    state = PawnMotor.Step(in input, in state, profile, world, (uint)tick, TickSeconds);
                    hash = HashState(hash, in state);
                }
            }
            return hash;
        }

        private static ulong HashPoses(CollisionShape[] shapes, bool withSupportNormal) {
            ulong hash = OffsetBasis;
            for (int index = 0; index < PoseCount; index++) {
                CapsulePose pose = Pose(index);
                foreach (CollisionShape shape in shapes) hash = HashShape(hash, in pose, in shape, withSupportNormal);
            }
            return hash;
        }

        private static CollisionWorld World() {
            CollisionShape[] shapes = Shapes();
            var moverPath = new MoverPath(new[] { new Vector3(2f, 0.25f, -2f), new Vector3(2f, 0.25f, 2f), new Vector3(-1.5f, 0.75f, 2f) }, 1.25f, MoverLoopMode.PingPong, 7u);
            CollisionShape platform = CollisionShape.MakeBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.75f, 0.125f, 0.75f));
            var mover = new MoverDefinition(1, 1, in platform, moverPath);
            return new CollisionWorld(new SceneGeometry("Parity", 1u, shapes, new[] { mover }), TickSeconds);
        }

        // Turning directions from integer steps, gaits, jumps and crouches.
        private static PawnInput Input(int pawn, int tick) {
            int phase = (tick / 15 + pawn) % 8;
            float x = phase == 0 || phase == 1 || phase == 7 ? 1f : phase == 3 || phase == 4 || phase == 5 ? -1f : 0f;
            float z = phase == 1 || phase == 2 || phase == 3 ? 1f : phase == 5 || phase == 6 || phase == 7 ? -1f : 0f;
            float scale = (phase % 2 == 1) ? 0.6875f : 1f;
            var gait = (WireLocomotion)((tick / 20 + pawn) % 4);
            bool jump = (tick + pawn * 7) % 45 == 0;
            bool crouch = (tick / 30 + pawn) % 5 == 0;
            return new PawnInput((uint)tick, new Vector2(x * scale, z * scale), gait, jump, crouch);
        }

        private static ulong HashShape(ulong hash, in CapsulePose pose, in CollisionShape shape, bool withSupportNormal) {
            bool touched = CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact);
            hash = Mix(hash, touched ? 1u : 0u);
            if (touched) {
                hash = Mix(hash, Bits(contact.Normal.X));
                hash = Mix(hash, Bits(contact.Normal.Y));
                hash = Mix(hash, Bits(contact.Normal.Z));
                hash = Mix(hash, Bits(contact.Depth));
            }

            bool supported = CollisionResolver.TryGetSupport(in shape, pose.FootPosition.X, pose.FootPosition.Z, 5f, -5f, out float height, out Vector3 normal);
            hash = Mix(hash, supported ? 1u : 0u);
            if (!supported) return hash;

            hash = Mix(hash, Bits(height));
            if (!withSupportNormal) return hash;

            hash = Mix(hash, Bits(normal.X));
            hash = Mix(hash, Bits(normal.Y));
            return Mix(hash, Bits(normal.Z));
        }

        private static ulong HashState(ulong hash, in PawnState state) {
            hash = Mix(hash, Bits(state.Position.X));
            hash = Mix(hash, Bits(state.Position.Y));
            hash = Mix(hash, Bits(state.Position.Z));
            hash = Mix(hash, Bits(state.Velocity.X));
            hash = Mix(hash, Bits(state.Velocity.Y));
            hash = Mix(hash, Bits(state.Velocity.Z));
            return Mix(hash, state.Flags);
        }

        private static uint Bits(float value) {
            return (uint)BitConverter.SingleToInt32Bits(value);
        }

        private static ulong Mix(ulong hash, uint value) {
            for (int shift = 0; shift < 32; shift += 8) hash = (hash ^ ((value >> shift) & 0xFFu)) * Prime;
            return hash;
        }
    }
}
