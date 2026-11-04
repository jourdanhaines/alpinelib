using System;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    /// <summary>One wind-rolled prop of a <see cref="RollingPropField"/> cell: its roll, the kind it draws as and its mesh scale.</summary>
    public readonly struct RollingProp {
        public RollingProp(RollerPath path, int kind, float scale) {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Kind = kind;
            Scale = scale;
        }

        /// <summary>The roll, in the cell root's local plane.</summary>
        public RollerPath Path { get; }

        /// <summary>Index returned by <see cref="RollingPropField.AddKind"/>.</summary>
        public int Kind { get; }

        /// <summary>Uniform scale of the kind's mesh, normally the path's <see cref="RollerPath.Size"/> (its radius already includes it).</summary>
        public float Scale { get; }
    }
}
