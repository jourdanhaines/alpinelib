using System;
using System.Numerics;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// What one corner walk did: the deepest overlap any step ended in, the longest horizontal stride a step took, how
    /// high a grounded pawn's feet rose, and how far the pawn wandered over the closing ticks.
    /// </summary>
    public sealed class CornerWalk {
        private readonly float strideLimit;
        private Vector3 restMinimum = new Vector3(float.MaxValue);
        private Vector3 restMaximum = new Vector3(float.MinValue);
        private int steps;

        public CornerWalk(float strideLimit, int restTicks) {
            this.strideLimit = strideLimit;
            RestTicks = restTicks;
        }

        public int RestTicks { get; }

        public float WorstResidual { get; private set; }

        public int WorstStep { get; private set; } = -1;

        public float LongestStride { get; private set; }

        public float GroundedRise { get; private set; }

        public float RestSpread {
            get {
                Vector3 extent = restMaximum - restMinimum;
                return MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            }
        }

        public void Observe(in PawnState before, in PawnState after, float residual, bool isResting) {
            if (residual > WorstResidual) {
                WorstResidual = residual;
                WorstStep = steps;
            }

            float strideX = after.Position.X - before.Position.X;
            float strideZ = after.Position.Z - before.Position.Z;
            LongestStride = MathF.Max(LongestStride, MathF.Sqrt(strideX * strideX + strideZ * strideZ));
            if (after.IsGrounded) GroundedRise = MathF.Max(GroundedRise, after.Position.Y);
            steps++;
            if (!isResting) return;

            restMinimum = Vector3.Min(restMinimum, after.Position);
            restMaximum = Vector3.Max(restMaximum, after.Position);
        }

        public bool IsClean(float tolerance) {
            return WorstResidual <= tolerance && LongestStride <= strideLimit;
        }

        public override string ToString() {
            return $"residual {WorstResidual:F4} at step {WorstStep}, stride {LongestStride:F4}/{strideLimit:F4}, rise {GroundedRise:F4}";
        }
    }
}
