using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// One recorded walk into a corner: the shapes, the starting state and tick, how many ticks to run and the
    /// walking input, which may turn once.
    /// </summary>
    public sealed class CornerCase {
        private const float TickSeconds = 1f / 30f;

        public CornerCase(string name, CollisionShape[] shapes, in PawnState start, uint firstTick, int ticks, Vector2 direction)
            : this(name, shapes, in start, firstTick, ticks, direction, uint.MaxValue, direction) {
        }

        public CornerCase(string name, CollisionShape[] shapes, in PawnState start, uint firstTick, int ticks, Vector2 direction, uint turnTick, Vector2 turnedDirection) {
            Name = name;
            Shapes = shapes;
            Start = start;
            FirstTick = firstTick;
            Ticks = ticks;
            Direction = direction;
            TurnTick = turnTick;
            TurnedDirection = turnedDirection;
        }

        public string Name { get; }

        public CollisionShape[] Shapes { get; }

        public PawnState Start { get; }

        public uint FirstTick { get; }

        public int Ticks { get; }

        public Vector2 Direction { get; }

        public uint TurnTick { get; }

        public Vector2 TurnedDirection { get; }

        public CollisionWorld World() {
            return new CollisionWorld(new SceneGeometry(Name, 1u, Shapes, Array.Empty<MoverDefinition>()), TickSeconds);
        }

        public PawnInput Input(uint tick) {
            Vector2 direction = tick < TurnTick ? Direction : TurnedDirection;
            return new PawnInput(tick, direction, WireLocomotion.Walk, false, false);
        }

        public override string ToString() {
            return Name;
        }
    }
}
