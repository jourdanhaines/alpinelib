using System.Collections.Generic;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>Supplies the positions a <see cref="CellStreamer"/> keeps cells loaded around (players, vehicles).</summary>
    public interface ICellObserverSource {
        /// <summary>Appends every current observer; ids must be stable while an observer lives.</summary>
        void CollectObservers(List<CellObserver> observers);
    }
}
