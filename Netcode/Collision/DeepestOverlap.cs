namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The running deepest overlap of a contact query that stops counting after a fixed number of contacts, with the
    /// same tie rule as picking the deepest from a written list: the first contact always counts, a later one only
    /// when strictly deeper.
    /// </summary>
    internal struct DeepestOverlap {
        private readonly int capacity;
        private int count;

        public DeepestOverlap(int capacity) {
            this.capacity = capacity;
            count = 0;
            Depth = 0f;
        }

        /// <summary>The deepest overlap counted so far, or zero before any.</summary>
        public float Depth { get; private set; }

        /// <summary>Whether as many contacts have been counted as the written list would hold.</summary>
        public bool IsFull => count >= capacity;

        /// <summary>Counts one more contact.</summary>
        public void Add(float depth) {
            if (count == 0 || depth > Depth) {
                Depth = depth;
            }

            count++;
        }
    }
}
