using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // One spawn a RollingPropField keeps: its build state, its roll once built (none when every spawn point was blocked)
    // and its runtime state (a moving blocker's fade-out, the ramp of a prop first drawn mid-roll, a viewer's overrun).
    internal sealed class RollingPropEntry {
        public RollingPropEntry(RollerSpawn spawn) {
            Spawn = spawn;
        }

        public RollerSpawn Spawn { get; }

        /// <summary>The built roll; null before the first build lands or when the spawn found no clear point.</summary>
        public RollingProp? Prop { get; set; }

        public bool IsBuilt { get; set; }

        public bool IsQueued { get; set; }

        /// <summary>World version the roll was built over (an edge-cut or provisional roll is rebuilt over a wider world).</summary>
        public int BuiltVersion { get; set; } = -1;

        /// <summary>Metres rolled past the spawn's own life this viewer asked for (its life would have ended in view).</summary>
        public double Overrun { get; set; }

        /// <summary>True when this viewer saw the cell it was born in up close: it is never drawn here.</summary>
        public bool IsHidden { get; set; }

        /// <summary>Session time a moving blocker first met the prop, negative while none has.</summary>
        public double BlockedAt { get; set; } = -1.0;

        /// <summary>Session time the prop's ramp began, NaN while not drawn; negative infinity when it needs no ramp.</summary>
        public double ShownAt { get; set; } = double.NaN;

        /// <summary>Share of its size the ramp starts from (0 for a prop first drawn, more for one that resumes a fade).</summary>
        public double ShownFrom { get; set; }

        /// <summary>Drift the roll ends at (its whole life when not built yet).</summary>
        public double EndDrift => Prop.HasValue ? Spawn.Drift + Prop.Value.Path.Length / Spawn.GroundSpeedShare : Spawn.EndDrift;

        /// <summary>Metres rolled at <paramref name="drift"/>.</summary>
        public double RolledAt(double drift) {
            return Spawn.RolledAt(drift);
        }
    }
}
