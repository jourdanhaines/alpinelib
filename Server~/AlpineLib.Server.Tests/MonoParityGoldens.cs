using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Integer-built inputs and FNV-1a 64 hashes over the collision narrow phase (box, sphere, capsule, plane) and
    /// over pawn motor step sequences. Every input float is exact on every runtime, so the hashes compare the
    /// arithmetic alone. The game's Unity gate re-runs these under the editor's Mono; this file has no test-framework
    /// dependency so a Mono console can compile it as is.
    /// </summary>
    internal static class MonoParityGoldens {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private const float TickSeconds = 1f / 30f;

        /// <summary>The shapes of the narrow-phase golden: turned and plain boxes, a sphere, a tilted capsule, a plane.</summary>
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

        /// <summary>Golden pose <paramref name="index"/>, built from integers.</summary>
        public static CapsulePose Pose(int index) {
            float x = ((index * 37) % 97 - 48) / 16f;
            float z = ((index * 53) % 89 - 44) / 16f;
            float y = ((index * 29) % 61 - 20) / 16f;
            float radius = 0.25f + (index % 5) / 16f;
            return new CapsulePose(new Vector3(x, y, z), radius, radius * 2f + (index % 7) / 8f);
        }

        /// <summary>Contact and support outputs of every golden pose against every golden shape.</summary>
        public static ulong ShapeHash() {
            CollisionShape[] shapes = Shapes();
            ulong hash = OffsetBasis;
            for (int index = 0; index < 4096; index++) {
                CapsulePose pose = Pose(index);
                foreach (CollisionShape shape in shapes) hash = HashShape(hash, in pose, in shape);
            }
            return hash;
        }

        /// <summary>
        /// Pawn states over a fixed input script, several pawns, through a world of every primitive and a mover. Yaw
        /// is left out: it comes from <c>MathF.Atan2</c>, whose last bit belongs to each runtime's libm, and never feeds
        /// a position.
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

        private static CollisionWorld World() {
            CollisionShape[] shapes = Shapes();
            var moverPath = new MoverPath(new[] { new Vector3(2f, 0.25f, -2f), new Vector3(2f, 0.25f, 2f), new Vector3(-1.5f, 0.75f, 2f) }, 1.25f, MoverLoopMode.PingPong, 7u);
            CollisionShape platform = CollisionShape.MakeBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.75f, 0.125f, 0.75f));
            var mover = new MoverDefinition(1, 1, in platform, moverPath);
            return new CollisionWorld(new SceneGeometry("Parity", 1u, shapes, new[] { mover }), TickSeconds);
        }

        // A deterministic script: turning directions from integer steps, gaits, jumps and crouches.
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

        private static ulong HashShape(ulong hash, in CapsulePose pose, in CollisionShape shape) {
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
            return (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        private static ulong Mix(ulong hash, uint value) {
            for (int shift = 0; shift < 32; shift += 8) hash = (hash ^ ((value >> shift) & 0xFFu)) * Prime;
            return hash;
        }
    }
}
