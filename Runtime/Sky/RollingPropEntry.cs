using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // One spawn a RollingPropField keeps: its build state, its roll once built (none when every spawn point was blocked)
    // and its runtime fades (a moving blocker's fade-out, the ramp of a prop first drawn mid-roll).
    internal sealed class RollingPropEntry {
        public RollingPropEntry(RollerSpawn spawn) {
            Spawn = spawn;
        }

        public RollerSpawn Spawn { get; }

        /// <summary>The built roll; null before the first build lands or when the spawn found no clear point.</summary>
        public RollingProp? Prop { get; set; }

        public bool IsBuilt { get; set; }

        public bool IsQueued { get; set; }

        /// <summary>World version the roll was built over (an edge-cut roll is rebuilt over a wider world).</summary>
        public int BuiltVersion { get; set; } = -1;

        /// <summary>Session time a moving blocker first met the prop, negative while none has.</summary>
        public double BlockedAt { get; set; } = -1.0;

        /// <summary>Session time the prop was first drawn, NaN before; negative infinity when it needs no ramp.</summary>
        public double ShownAt { get; set; } = double.NaN;

        /// <summary>True once a moving blocker's fade is over: never drawn again.</summary>
        public bool IsSpent { get; set; }
    }
}
