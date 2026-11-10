using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>What <see cref="TalusApron.Lay"/> laid round one foot: its parts and bare stretches, or nothing when two pieces met at a step.</summary>
    public sealed class TalusApronResult {
        /// <summary>Builds a result.</summary>
        internal TalusApronResult(bool isLaid, IReadOnlyList<TalusApronPart> parts, IReadOnlyList<TalusBareStretch> bare) {
            IsLaid = isLaid;
            Parts = parts;
            Bare = bare;
        }

        /// <summary>False when pieces sharing an edge disagreed on its height (the caller may lay again lower); parts and bare are then empty.</summary>
        public bool IsLaid { get; }

        /// <summary>Strips, corner fans and end fans in chain order.</summary>
        public IReadOnlyList<TalusApronPart> Parts { get; }

        /// <summary>Exposed foot stretches with no apron.</summary>
        public IReadOnlyList<TalusBareStretch> Bare { get; }
    }
}
