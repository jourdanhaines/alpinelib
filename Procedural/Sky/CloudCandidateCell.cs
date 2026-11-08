namespace AlpineLib.Procedural.Sky {
    /// <summary>The drawn candidates of a run of slots of one field cell (place in the cell, grown reach, priority, keep roll, coverage), memoised by the scatter.</summary>
    internal sealed class CloudCandidateCell {
        public CloudCandidateCell(int count) {
            X = new double[count];
            Z = new double[count];
            Reach = new double[count];
            Priority = new ulong[count];
            Covered = new bool[count];
            Roll = new double[count];
        }

        public double[] X { get; }
        public double[] Z { get; }
        public double[] Reach { get; }
        public ulong[] Priority { get; }
        public bool[] Covered { get; }
        public double[] Roll { get; }
        public int Count => X.Length;
    }
}
