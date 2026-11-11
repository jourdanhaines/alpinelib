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
    /// <remarks>
    /// Each golden runs twice: exact inputs (sixteenths, so many products are exact) and rough ones (thirteenths,
    /// elevenths, sevenths and seventeenths: full mantissas, still one correctly rounded division on every runtime).
    /// The generator's own arithmetic is narrowed for the same reason the code under test is.
    /// </remarks>
    public static class MonoParityGoldens {
        /// <summary>Box, sphere, capsule and plane contacts and supports over the exact poses.</summary>
        public const ulong ShapeGolden = 0x226E3ADFDE4DB867UL;

        /// <summary>Pawn motor states over the exact script and world.</summary>
        public const ulong MotorGolden = 0xD2D03DA2234E2E54UL;

        /// <summary>Prism contacts and support heights over the exact poses.</summary>
        public const ulong PrismGolden = 0xE7F2B71C41C1F340UL;

        /// <summary>Sloped prism contacts, support heights and normals over the exact poses.</summary>
        public const ulong SlopedPrismGolden = 0x5F1E9E4644E21060UL;

        /// <summary>Pawn motor states over the exact script on the sloped prism world.</summary>
        public const ulong SlopedMotorGolden = 0xA683802B1DAD9316UL;

        /// <summary><see cref="SlopedPrismGolden"/> over the rough poses.</summary>
        public const ulong RoughSlopedPrismGolden = 0x61F7C0271E3ED34DUL;

        /// <summary><see cref="SlopedMotorGolden"/> over the rough script.</summary>
        public const ulong RoughSlopedMotorGolden = 0x79D582EE7873843DUL;

        /// <summary><see cref="ShapeGolden"/> over the rough poses.</summary>
        public const ulong RoughShapeGolden = 0x03A44F2656D55DCEUL;

        /// <summary><see cref="MotorGolden"/> over the rough script and world.</summary>
        public const ulong RoughMotorGolden = 0x86C3844F8949D43CUL;

        /// <summary><see cref="PrismGolden"/> over the rough poses.</summary>
        public const ulong RoughPrismGolden = 0xA52A7C80C478586EUL;

        /// <summary>Pawn motor states walking and jumping into acute concave corners; see <see cref="CornerMotorHash()"/>.</summary>
        public const ulong CornerMotorGolden = 0x23CB97FED57D73ABUL;

        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private const float TickSeconds = 1f / 30f;
        private const int PoseCount = 4096;
        private const int PawnCount = 18;
        private const int TickCount = 180;
        private const uint ShiftTick = 90u;
        private const int RampShapeIndex = 13;
        private const float Diagonal = 0.70710677f;
        private const int SlopedPawnCount = 12;
        private const int SlopedTickCount = 150;
        private const int CornerPawnCount = 9;
        private const int CornerTickCount = 150;

        /// <summary>
        /// The narrow-phase golden's shapes: turned, plain and tilted boxes, a sphere, two capsules, a plane, a
        /// zero-length capsule and a box with a collapsed up axis (a support probe passes through it without entering).
        /// </summary>
        public static CollisionShape[] Shapes() {
            return new[] {
                CollisionShape.MakeBox(new Vector3(0.25f, 0.5f, -0.5f), new Vector3(0.6f, 0f, -0.8f), Vector3.UnitY, new Vector3(0.8f, 0f, 0.6f), new Vector3(1.125f, 0.5f, 0.75f)),
                CollisionShape.MakeBox(new Vector3(-0.5f, 0.75f, -1f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(1.5f, 0.75f, 1.125f)),
                CollisionShape.MakeBox(new Vector3(0.5f, 1.5f, 0.25f), new Vector3(0.6f, 0.8f, 0f), new Vector3(-0.8f, 0.6f, 0f), Vector3.UnitZ, new Vector3(0.5f, 0.25f, 0.75f)),
                CollisionShape.MakeSphere(new Vector3(-1f, 1.25f, 0.75f), 1.125f),
                CollisionShape.MakeCapsule(new Vector3(0.5f, 1f, 0.5f), new Vector3(0.6f, 0.8f, 0f), 0.75f, 0.375f),
                CollisionShape.MakeCapsule(new Vector3(-0.25f, 0.5f, -0.75f), Vector3.UnitY, 0.5f, 0.625f),
                CollisionShape.MakePlane(0.0625f),
                CollisionShape.MakeCapsule(new Vector3(1.5f, 0.5f, -1.5f), Vector3.UnitY, 0f, 0.5f),
                CollisionShape.MakeBox(new Vector3(-1.5f, 0.25f, 1.75f), Vector3.UnitX, Vector3.Zero, Vector3.UnitZ, new Vector3(0.75f, 0.5f, 0.5f)),
            };
        }

        /// <summary>The prism golden's four literal prisms, one with sixteen vertices.</summary>
        public static CollisionShape[] Prisms() {
            return new[] {
                CollisionShape.MakePrism(new Vector3(0.25f, 0.5f, -0.5f), 0.5f, new[] { new Vector2(-1f, -1f), new Vector2(1.5f, -0.75f), new Vector2(1f, 1f), new Vector2(-0.5f, 1.25f) }),
                CollisionShape.MakePrism(new Vector3(-1f, 1.25f, 0.75f), 1.25f, new[] { new Vector2(0f, -1f), new Vector2(0.875f, -0.5f), new Vector2(0.875f, 0.5f), new Vector2(0f, 1f), new Vector2(-0.875f, 0.5f), new Vector2(-0.875f, -0.5f) }),
                CollisionShape.MakePrism(new Vector3(0.5f, 2f, 0.5f), 0.25f, new[] { new Vector2(-0.75f, -0.5f), new Vector2(0.75f, -0.5f), new Vector2(0f, 0.75f) }),
                CollisionShape.MakePrism(new Vector3(-0.5f, 0.75f, -1f), 0.75f, SixteenGon()),
            };
        }

        // A sixteen-vertex plan in sixteenths and sixty-fourths.
        private static Vector2[] SixteenGon() {
            return new[] {
                new Vector2(1.5f, 0.109375f), new Vector2(1.328125f, 0.515625f), new Vector2(0.953125f, 0.859375f), new Vector2(0.4375f, 1.046875f),
                new Vector2(-0.15625f, 1.09375f), new Vector2(-0.703125f, 0.96875f), new Vector2(-1.15625f, 0.703125f), new Vector2(-1.4375f, 0.3125f),
                new Vector2(-1.5f, -0.109375f), new Vector2(-1.328125f, -0.515625f), new Vector2(-0.953125f, -0.859375f), new Vector2(-0.4375f, -1.046875f),
                new Vector2(0.15625f, -1.09375f), new Vector2(0.703125f, -0.96875f), new Vector2(1.15625f, -0.703125f), new Vector2(1.4375f, -0.3125f),
            };
        }

        /// <summary>
        /// The sloped prism golden's literal shapes: a 34.5° ramp, a diagonal slope, a 45° wedge, two pieces sharing an
        /// edge under one plane, and a sixteen-vertex slope.
        /// </summary>
        public static CollisionShape[] SlopedPrisms() {
            var seamPlan = new[] { new Vector2(-1.5f, -1.5f), new Vector2(1.5f, -1.5f), new Vector2(1.5f, 1.5f), new Vector2(-1.5f, 1.5f) };
            return new[] {
                CollisionShape.MakeSlopedPrism(new Vector3(0.25f, 0f, -0.5f), 0f, 1f, 0.6875f, 0f, new[] { new Vector2(-1f, -1f), new Vector2(1.5f, -0.75f), new Vector2(1f, 1f), new Vector2(-0.5f, 1.25f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(-1f, 0f, 0.75f), -0.25f, 1.25f, 0.5f, -0.375f, new[] { new Vector2(0f, -1f), new Vector2(0.875f, -0.5f), new Vector2(0.875f, 0.5f), new Vector2(0f, 1f), new Vector2(-0.875f, 0.5f), new Vector2(-0.875f, -0.5f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(0.5f, 0f, 0.5f), 1.5f, 2.5f, -1f, 0f, new[] { new Vector2(-0.75f, -0.5f), new Vector2(0.75f, -0.5f), new Vector2(0f, 0.75f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(0.5f, 0f, -1f), -1f, 0.75f, 0.25f, 0.5f, new[] { seamPlan[0], seamPlan[1], seamPlan[2] }),
                CollisionShape.MakeSlopedPrism(new Vector3(0.5f, 0f, -1f), -1f, 0.75f, 0.25f, 0.5f, new[] { seamPlan[0], seamPlan[2], seamPlan[3] }),
                CollisionShape.MakeSlopedPrism(new Vector3(-0.5f, 0f, -1f), -0.5f, 1f, -0.3125f, 0.625f, SixteenGon()),
            };
        }

        /// <summary>Exact golden pose <paramref name="index"/>, built from integers.</summary>
        public static CapsulePose Pose(int index) {
            return Pose(index, false);
        }

        /// <summary>Rough golden pose <paramref name="index"/>, built from integers.</summary>
        public static CapsulePose RoughPose(int index) {
            return Pose(index, true);
        }

        /// <summary>Contacts and supports of every exact pose against every shape of <see cref="Shapes"/>.</summary>
        public static ulong ShapeHash() {
            return HashPoses(Shapes(), false, true);
        }

        /// <summary><see cref="ShapeHash"/> over the rough poses.</summary>
        public static ulong RoughShapeHash() {
            return HashPoses(Shapes(), true, true);
        }

        /// <summary>Contacts and support heights of every exact pose against every prism of <see cref="Prisms"/>.</summary>
        public static ulong PrismHash() {
            return HashPoses(Prisms(), false, false);
        }

        /// <summary><see cref="PrismHash"/> over the rough poses.</summary>
        public static ulong RoughPrismHash() {
            return HashPoses(Prisms(), true, false);
        }

        /// <summary>Contacts, support heights and normals of every exact pose against every shape of <see cref="SlopedPrisms"/>.</summary>
        public static ulong SlopedPrismHash() {
            return HashPoses(SlopedPrisms(), false, true);
        }

        /// <summary><see cref="SlopedPrismHash"/> over the rough poses.</summary>
        public static ulong RoughSlopedPrismHash() {
            return HashPoses(SlopedPrisms(), true, true);
        }

        /// <summary>Pawn states over the exact script on the sloped world; see <see cref="SlopedMotorHash(bool, MotorParityCoverage)"/>.</summary>
        public static ulong SlopedMotorHash() {
            return SlopedMotorHash(false, null);
        }

        /// <summary>Pawn states over the rough script on the sloped world.</summary>
        public static ulong RoughSlopedMotorHash() {
            return SlopedMotorHash(true, null);
        }

        /// <summary>
        /// Pawn states, tick by tick, over a floor, a 35° ramp ending at a wall and a ramp split into two pieces along a
        /// seam, half the pawns on a 40° profile that cannot climb the 45° wedge. <paramref name="coverage"/>, when given,
        /// counts steps grounded on and touching sloped prisms.
        /// </summary>
        public static ulong SlopedMotorHash(bool rough, MotorParityCoverage coverage) {
            CollisionWorld world = SlopedWorld(rough);
            MovementProfile shipped = SlopedProfile(45f);
            MovementProfile steep = SlopedProfile(40f);
            var states = new PawnState[SlopedPawnCount];
            for (int pawn = 0; pawn < SlopedPawnCount; pawn++) states[pawn] = SlopedStartState(pawn, rough);

            ulong hash = OffsetBasis;
            for (uint tick = 1u; tick <= SlopedTickCount; tick++) {
                for (int pawn = 0; pawn < SlopedPawnCount; pawn++) {
                    PawnInput input = SlopedInput(pawn, (int)tick, rough);
                    MovementProfile profile = pawn % 2 == 0 ? shipped : steep;
                    states[pawn] = PawnMotor.Step(in input, in states[pawn], profile, world, tick, TickSeconds);
                    if (coverage != null) ObserveSloped(coverage, world, profile, in states[pawn], tick);
                    hash = HashState(hash, in states[pawn]);
                }
            }
            return hash;
        }

        /// <summary>Pawn states over the exact script; see <see cref="MotorHash(bool, MotorParityCoverage)"/>.</summary>
        public static ulong MotorHash() {
            return MotorHash(false, null);
        }

        /// <summary>Pawn states over the rough script; see <see cref="MotorHash(bool, MotorParityCoverage)"/>.</summary>
        public static ulong RoughMotorHash() {
            return MotorHash(true, null);
        }

        /// <summary>
        /// Pawn states, tick by tick, through every primitive, the prisms, a 45° ramp, a ping-pong and a looping mover
        /// and a floating-origin rebase halfway. Yaw is left out: it comes from <c>MathF.Atan2</c> and never feeds a
        /// position. <paramref name="coverage"/>, when given, counts the paths the run reached.
        /// </summary>
        public static ulong MotorHash(bool rough, MotorParityCoverage coverage) {
            CollisionWorld world = World(rough);
            MovementProfile steep = Profile(45f);
            MovementProfile gentle = Profile(50f);
            Vector3 shift = rough ? new Vector3(41f / 13f, 0f, -23f / 11f) : new Vector3(3.25f, 0f, -1.75f);
            var states = new PawnState[PawnCount];
            for (int pawn = 0; pawn < PawnCount; pawn++) states[pawn] = StartState(pawn, rough);

            ulong hash = OffsetBasis;
            for (uint tick = 1u; tick <= TickCount; tick++) {
                if (tick == ShiftTick) Rebase(world, states, shift, coverage);
                if (coverage != null && IsLoopWrap(world, tick)) coverage.LoopWraps++;
                for (int pawn = 0; pawn < PawnCount; pawn++) {
                    PawnInput input = Input(pawn, (int)tick, rough);
                    PawnState before = states[pawn];
                    MovementProfile profile = pawn % 2 == 0 ? steep : gentle;
                    states[pawn] = PawnMotor.Step(in input, in before, profile, world, tick, TickSeconds);
                    if (coverage != null) Observe(coverage, world, profile, in input, in before, in states[pawn], tick);
                    hash = HashState(hash, in states[pawn]);
                }
            }
            return hash;
        }

        /// <summary>
        /// Pawn states, tick by tick, pushed into two V notches between prisms (wall normals about 162° and 127° apart)
        /// and along a wall into the slot under a box leaning on it, walking, sprinting, jumping and falling in. These
        /// are the corners where one contact at a time ping-pongs, so the joint corner resolution runs every few ticks.
        /// </summary>
        public static ulong CornerMotorHash() {
            return CornerMotorHash(true, out _);
        }

        /// <summary>
        /// <see cref="CornerMotorHash()"/>, optionally without the joint corner resolution, with the deepest overlap any
        /// step ended in.
        /// </summary>
        internal static ulong CornerMotorHash(bool resolveCorners, out float worstResidual) {
            var world = new CollisionWorld(new SceneGeometry("CornerParity", 1u, CornerShapes(), Array.Empty<MoverDefinition>()), TickSeconds);
            MovementProfile narrow = new MovementProfile { CapsuleRadius = 0.35f, CapsuleHeight = 1.1f, StepOffset = 0.3f, SlopeLimitDegrees = 45f };
            MovementProfile wide = new MovementProfile { CapsuleRadius = 0.5f, CapsuleHeight = 1.5f, StepOffset = 0.25f, SlopeLimitDegrees = 40f };
            var states = new PawnState[CornerPawnCount];
            for (int pawn = 0; pawn < CornerPawnCount; pawn++) states[pawn] = CornerStartState(pawn);

            ulong hash = OffsetBasis;
            worstResidual = 0f;
            for (uint tick = 1u; tick <= CornerTickCount; tick++) {
                for (int pawn = 0; pawn < CornerPawnCount; pawn++) {
                    PawnInput input = CornerInput(pawn, (int)tick);
                    MovementProfile profile = pawn % 2 == 0 ? narrow : wide;
                    states[pawn] = PawnMotor.Step(in input, in states[pawn], profile, world, tick, TickSeconds, resolveCorners);
                    worstResidual = MathF.Max(worstResidual, Residual(world, in states[pawn], profile, tick));
                    hash = HashState(hash, in states[pawn]);
                }
            }
            return hash;
        }

        // A floor; a 162° notch at the origin opening to −X; a 127° notch at z = 10; a wall at z = −10 with a box leaning on it.
        private static CollisionShape[] CornerShapes() {
            return new[] {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakePrism(new Vector3(0f, 1.5f, 0f), 1.5f, new[] { new Vector2(1f, 3f), new Vector2(-8f, 3f), new Vector2(-8f, 1f), new Vector2(1f, -0.125f) }),
                CollisionShape.MakePrism(new Vector3(0f, 1.5f, 0f), 1.5f, new[] { new Vector2(1f, 0.1875f), new Vector2(-8f, -1.5f), new Vector2(-8f, -3.5f), new Vector2(1f, -3.5f) }),
                CollisionShape.MakePrism(new Vector3(0f, 1.5f, 10f), 1.5f, new[] { new Vector2(1f, 5f), new Vector2(-6f, 5f), new Vector2(-6f, 3f), new Vector2(1f, -0.5f) }),
                CollisionShape.MakePrism(new Vector3(0f, 1.5f, 10f), 1.5f, new[] { new Vector2(1f, 0.5f), new Vector2(-6f, -3f), new Vector2(-6f, -5f), new Vector2(1f, -5f) }),
                CollisionShape.MakePrism(new Vector3(0f, 1.5f, -10f), 1.5f, new[] { new Vector2(-4f, 0f), new Vector2(4f, 0f), new Vector2(4f, 2f), new Vector2(-4f, 2f) }),
                CollisionShape.MakeBox(new Vector3(0.5f, 0.5f, -11.25f), new Vector3(0.96f, 0f, 0.28f), new Vector3(-0.168f, 0.8f, 0.576f), new Vector3(-0.224f, -0.6f, 0.768f), new Vector3(2.5f, 0.5f, 0.5f)),
            };
        }

        // Four pawns in the sharp notch (one dropped in from above), two in the wider one, two along the wall.
        private static PawnState CornerStartState(int pawn) {
            Vector3 start = pawn switch {
                0 => new Vector3(-6f, 0f, -0.25f),
                1 => new Vector3(-6f, 0f, 0f),
                2 => new Vector3(-5.5f, 0f, 0.125f),
                3 => new Vector3(-6.5f, 0f, -0.375f),
                4 => new Vector3(-4f, 0f, 10f),
                5 => new Vector3(-3.5f, 0f, 10.5f),
                6 => new Vector3(-3.5f, 0f, -10.5f),
                7 => new Vector3(-3f, 0f, -10.75f),
                _ => new Vector3(-3f, 1.5f, -0.0625f),
            };
            return new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, pawn != 8));
        }

        // Each pawn holds a heading into its corner; gaits cycle, and three pawns jump every 47 ticks.
        private static PawnInput CornerInput(int pawn, int tick) {
            Vector2 direction = pawn switch {
                1 => new Vector2(1f, 0.25f),
                2 => new Vector2(1f, -0.3125f),
                3 => new Vector2(1f, 0.125f),
                5 => new Vector2(1f, -0.1875f),
                6 => new Vector2(1f, 0.125f),
                _ => new Vector2(1f, 0f),
            };
            var gait = (WireLocomotion)((tick / 40 + pawn) % 4);
            bool jump = (pawn == 2 || pawn == 3 || pawn == 5) && tick % 47 == 0;
            return new PawnInput((uint)tick, direction, gait, jump, false);
        }

        // The deepest overlap of the capsule the motor collides with: feet raised by the step offset while grounded.
        private static float Residual(CollisionWorld world, in PawnState state, MovementProfile profile, uint tick) {
            float lift = state.IsGrounded ? profile.StepOffset : 0f;
            float height = MathF.Max((float)(profile.CapsuleHeight - lift), (float)(profile.CapsuleRadius * 2f));
            var pose = new CapsulePose(new Vector3(state.Position.X, (float)(state.Position.Y + lift), state.Position.Z), profile.CapsuleRadius, height);
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in pose, tick, contacts);
            float deepest = 0f;
            for (int index = 0; index < count; index++) deepest = MathF.Max(deepest, contacts[index].Depth);
            return deepest;
        }

        private static CapsulePose Pose(int index, bool rough) {
            float x = (float)(((index * 37) % 97 - 48) / (rough ? 13f : 16f));
            float z = (float)(((index * 53) % 89 - 44) / (rough ? 11f : 16f));
            float y = (float)(((index * 29) % 61 - 20) / (rough ? 7f : 16f));
            float radius = (float)(0.25f + (float)((index % 5) / (rough ? 17f : 16f)));
            return new CapsulePose(new Vector3(x, y, z), radius, (float)((float)(radius * 2f) + (float)((index % 7) / 8f)));
        }

        private static ulong HashPoses(CollisionShape[] shapes, bool rough, bool withSupportNormal) {
            ulong hash = OffsetBasis;
            for (int index = 0; index < PoseCount; index++) {
                CapsulePose pose = Pose(index, rough);
                foreach (CollisionShape shape in shapes) hash = HashShape(hash, in pose, in shape, withSupportNormal);
            }
            return hash;
        }

        private static MovementProfile Profile(float slopeLimitDegrees) {
            return new MovementProfile { SlopeLimitDegrees = slopeLimitDegrees, AirDrag = 0.5f };
        }

        private static PawnState StartState(int pawn, bool rough) {
            float spacing = rough ? 1.3f : 1.25f;
            var start = new Vector3((float)((pawn % 6 - 2.5f) * spacing), (float)(0.0625f + (float)((pawn % 3) * 0.5f)), (float)((pawn / 6 - 1f) * 1.5f));
            return new PawnState(start, pawn * 30f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, pawn % 3 == 0));
        }

        // Shapes() then Prisms() then the ramp, so the ramp's static index is fixed.
        private static CollisionWorld World(bool rough) {
            CollisionShape[] shapes = Shapes();
            CollisionShape[] prisms = Prisms();
            var statics = new CollisionShape[shapes.Length + prisms.Length + 1];
            shapes.CopyTo(statics, 0);
            prisms.CopyTo(statics, shapes.Length);
            statics[RampShapeIndex] = CollisionShape.MakeBox(new Vector3(-2.5f, 0f, 2.5f), new Vector3(Diagonal, Diagonal, 0f), new Vector3(-Diagonal, Diagonal, 0f), Vector3.UnitZ, new Vector3(1.25f, 0.25f, 1f));

            float speed = rough ? 1.3f : 1.25f;
            var pingPong = new MoverPath(new[] { new Vector3(2f, 0.25f, -2f), new Vector3(2f, 0.25f, 2f), new Vector3(-1.5f, 0.75f, 2f) }, speed, MoverLoopMode.PingPong, 7u);
            var loop = new MoverPath(new[] { new Vector3(-2.5f, 0.25f, -2.5f), new Vector3(-2.5f, 0.25f, -0.5f) }, speed, MoverLoopMode.Loop, 0u);
            CollisionShape platform = CollisionShape.MakeBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.75f, 0.125f, 0.75f));
            var movers = new[] { new MoverDefinition(1, 1, in platform, pingPong), new MoverDefinition(2, 1, in platform, loop) };
            return new CollisionWorld(new SceneGeometry("Parity", 1u, statics, movers), TickSeconds);
        }

        // A floor, a 34.5° ramp (rise 0.6875 per metre, 8 m) up to a wall, a seam ramp in two pieces and a 45° wedge.
        private static CollisionWorld SlopedWorld(bool rough) {
            float shift = rough ? 1f / 13f : 0.0625f;
            var seam = new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f) };
            var statics = new[] {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakeSlopedPrism(new Vector3(shift, 0f, 3f), -1f, 0f, 0.6875f, 0f, new[] { new Vector2(0f, -1.5f), new Vector2(8f, -1.5f), new Vector2(8f, 1.5f), new Vector2(0f, 1.5f) }),
                CollisionShape.MakePrism(new Vector3((float)(shift + 9f), 3.5f, 3f), 3.5f, new[] { new Vector2(-1f, -2f), new Vector2(1f, -2f), new Vector2(1f, 2f), new Vector2(-1f, 2f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(-3f, 0f, (float)(-3f + shift)), -1f, 1f, 0.25f, 0.375f, new[] { seam[0], seam[1], seam[2] }),
                CollisionShape.MakeSlopedPrism(new Vector3(-3f, 0f, (float)(-3f + shift)), -1f, 1f, 0.25f, 0.375f, new[] { seam[0], seam[2], seam[3] }),
                CollisionShape.MakeSlopedPrism(new Vector3(1f, 0f, -3f), -1f, 0f, 1f, 0f, new[] { new Vector2(0f, -1.5f), new Vector2(3f, -1.5f), new Vector2(3f, 1.5f), new Vector2(0f, 1.5f) }),
            };
            return new CollisionWorld(new SceneGeometry("SlopedParity", 1u, statics, Array.Empty<MoverDefinition>()), TickSeconds);
        }

        private static MovementProfile SlopedProfile(float slopeLimitDegrees) {
            return new MovementProfile { CapsuleRadius = 0.35f, CapsuleHeight = 1.1f, StepOffset = 0.3f, SlopeLimitDegrees = slopeLimitDegrees };
        }

        // Pawns start on the floor in front of the 34.5° ramp, the seam ramp and the wedge.
        private static PawnState SlopedStartState(int pawn, bool rough) {
            float jitter = rough ? (pawn % 5) / 13f : (pawn % 5) / 16f;
            Vector3 start = (pawn % 3) switch {
                0 => new Vector3((float)(-1f - jitter), 0f, (float)(2f + jitter)),
                1 => new Vector3(-5.5f, 0f, (float)(-4.5f + jitter)),
                _ => new Vector3((float)(-0.5f + jitter), 0f, (float)(-3.5f + jitter * 2f)),
            };
            return new PawnState(start, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true));
        }

        // Mostly uphill (+X), turning across the seam every 25 ticks, with a jump every 60 and gaits cycling.
        private static PawnInput SlopedInput(int pawn, int tick, bool rough) {
            int phase = (tick / 25 + pawn) % 4;
            float across = phase == 1 ? 1f : phase == 3 ? -1f : 0f;
            float forward = rough ? 0.9f : 1f;
            var gait = (WireLocomotion)((tick / 30 + pawn) % 3);
            bool jump = (tick + pawn * 11) % 60 == 0;
            return new PawnInput((uint)tick, new Vector2(forward, (float)(across * 0.5f)), gait, jump, false);
        }

        // Counts only; nothing here feeds the hash.
        private static void ObserveSloped(MotorParityCoverage coverage, CollisionWorld world, MovementProfile profile, in PawnState state, uint tick) {
            if (state.IsGrounded && world.TryGetSupport(state.Position.X, state.Position.Z, (float)(state.Position.Y + 0.01f), (float)(state.Position.Y - 0.01f), tick, out SupportHit hit)
                && world.GetStaticShape(hit.ShapeIndex).Type == CollisionShapeType.SlopedPrism) {
                coverage.SlopedPrismGrounded++;
            }

            var reach = new CapsulePose(state.Position - new Vector3(0f, 0.05f, 0f), profile.CapsuleRadius + 0.05f, profile.CapsuleHeight + 0.1f);
            Span<CollisionContact> contacts = stackalloc CollisionContact[16];
            int count = world.CollectContacts(in reach, tick, contacts);
            for (int index = 0; index < count; index++) {
                if (world.GetStaticShape(contacts[index].ShapeIndex).Type == CollisionShapeType.SlopedPrism) coverage.SlopedPrismTouches++;
            }
        }

        // Turning directions, gaits, jumps and crouches; a third of the pawns let go after each jump and a quarter
        // push their diagonals past unit length.
        private static PawnInput Input(int pawn, int tick, bool rough) {
            int phase = (tick / 15 + pawn) % 8;
            int sinceJump = (tick + pawn * 7) % 45;
            var gait = (WireLocomotion)((tick / 20 + pawn) % 4);
            bool jump = sinceJump == 0;
            bool crouch = (tick / 30 + pawn) % 5 == 0;
            if (pawn % 3 == 2 && sinceJump >= 1 && sinceJump <= 20) return new PawnInput((uint)tick, Vector2.Zero, gait, jump, crouch);

            float x = phase == 0 || phase == 1 || phase == 7 ? 1f : phase == 3 || phase == 4 || phase == 5 ? -1f : 0f;
            float z = phase == 1 || phase == 2 || phase == 3 ? 1f : phase == 5 || phase == 6 || phase == 7 ? -1f : 0f;
            float scale = InputScale(pawn, phase, rough);
            return new PawnInput((uint)tick, new Vector2((float)(x * scale), (float)(z * scale)), gait, jump, crouch);
        }

        private static float InputScale(int pawn, int phase, bool rough) {
            if (phase % 2 == 0) return rough ? 0.9f : 1f;
            if (pawn % 4 == 3) return rough ? 1.3f : 1.25f;
            return rough ? 0.7f : 0.6875f;
        }

        private static void Rebase(CollisionWorld world, PawnState[] states, Vector3 shift, MotorParityCoverage coverage) {
            world.TranslateAll(shift);
            for (int pawn = 0; pawn < states.Length; pawn++) {
                if (coverage != null && !states[pawn].IsCarrierRelative) coverage.OriginShifts++;
                states[pawn] = states[pawn].WithOriginShift(shift);
            }
        }

        private static bool IsLoopWrap(CollisionWorld world, uint tick) {
            Vector3 previous = world.EvaluateMoverPosition(1, tick - 1u);
            Vector3 current = world.EvaluateMoverPosition(1, tick);
            return Vector3.DistanceSquared(previous, current) > 1f;
        }

        // Counts only; nothing here feeds the hash.
        private static void Observe(MotorParityCoverage coverage, CollisionWorld world, MovementProfile profile, in PawnInput input, in PawnState before, in PawnState after, uint tick) {
            bool idle = input.MoveDirection == Vector2.Zero;
            if (idle && !before.IsGrounded && profile.AirDrag > 0f) coverage.AirborneWithoutInput++;
            if (idle && after.YawDegrees == before.YawDegrees) coverage.HeldYaws++;
            if (input.MoveDirection.LengthSquared() > 1f) coverage.OverLengthInputs++;

            var reach = new CapsulePose(after.Position - new Vector3(0f, 0.05f, 0f), profile.CapsuleRadius + 0.05f, profile.CapsuleHeight + 0.1f);
            Span<CollisionContact> contacts = stackalloc CollisionContact[16];
            int count = world.CollectContacts(in reach, tick, contacts);
            for (int index = 0; index < count; index++) CountTouch(coverage, world, in contacts[index]);
        }

        private static void CountTouch(MotorParityCoverage coverage, CollisionWorld world, in CollisionContact contact) {
            if (contact.IsMover) return;
            if (contact.ShapeIndex == RampShapeIndex) coverage.RampTouches++;
            if (world.GetStaticShape(contact.ShapeIndex).Type == CollisionShapeType.Prism) coverage.PrismTouches++;
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
