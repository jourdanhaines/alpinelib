using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>The regular clouds a field cell keeps under a landmark-aware scatter: slot, place in the cell and boosted reach.</summary>
    internal sealed class CloudKeptCell {
        private readonly List<int> _slots = new List<int>();
        private readonly List<double> _x = new List<double>();
        private readonly List<double> _z = new List<double>();
        private readonly List<double> _reach = new List<double>();

        public int[] Slots { get; private set; }
        public double[] X { get; private set; }
        public double[] Z { get; private set; }
        public double[] Reach { get; private set; }
        public int Count => Slots.Length;

        public void Add(int slot, double x, double z, double reach) {
            _slots.Add(slot);
            _x.Add(x);
            _z.Add(z);
            _reach.Add(reach);
        }

        /// <summary>Freezes the added clouds into arrays.</summary>
        public CloudKeptCell Close() {
            Slots = _slots.ToArray();
            X = _x.ToArray();
            Z = _z.ToArray();
            Reach = _reach.ToArray();
            return this;
        }
    }
}
