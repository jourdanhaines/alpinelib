using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A replay of a <see cref="WindField"/>'s drift: the ordered sum of its first <see cref="Steps"/> steps and the
    /// checkpoints passed on the way, from <see cref="FirstCheckpoint"/> on.
    /// </summary>
    public sealed class WindReplay {
        public WindReplay(WindField field, long steps, WindVector sum, int firstCheckpoint, IReadOnlyList<WindVector> checkpoints) {
            Field = field;
            Steps = steps;
            Sum = sum;
            FirstCheckpoint = firstCheckpoint;
            Checkpoints = checkpoints;
        }

        /// <summary>The field replayed.</summary>
        public WindField Field { get; }

        /// <summary>Whole steps summed.</summary>
        public long Steps { get; }

        /// <summary>The ordered sum of the first <see cref="Steps"/> steps.</summary>
        public WindVector Sum { get; }

        /// <summary>Checkpoint index (in <see cref="WindField.CheckpointSteps"/>) of <see cref="Checkpoints"/>[0].</summary>
        public int FirstCheckpoint { get; }

        /// <summary>The sum at consecutive multiples of <see cref="WindField.CheckpointSteps"/>, from <see cref="FirstCheckpoint"/>.</summary>
        public IReadOnlyList<WindVector> Checkpoints { get; }
    }
}
