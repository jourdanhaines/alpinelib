using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>A cold replay of a <see cref="WindField"/>'s drift: the ordered sum of its first steps and the checkpoints on the way.</summary>
    public sealed class WindReplay {
        public WindReplay(WindField field, long steps, WindVector sum, IReadOnlyList<WindVector> checkpoints) {
            Field = field;
            Steps = steps;
            Sum = sum;
            Checkpoints = checkpoints;
        }

        /// <summary>The field replayed.</summary>
        public WindField Field { get; }

        /// <summary>Whole steps summed.</summary>
        public long Steps { get; }

        /// <summary>The ordered sum of the first <see cref="Steps"/> steps.</summary>
        public WindVector Sum { get; }

        /// <summary>The sum at every multiple of <see cref="WindField.CheckpointSteps"/> up to <see cref="Steps"/>, from 0.</summary>
        public IReadOnlyList<WindVector> Checkpoints { get; }
    }
}
