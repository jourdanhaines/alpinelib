namespace AlpineLib.Maps {
    /// <summary>Something that must stand still while the map is dragged (a held-menu look follow, a camera).</summary>
    public interface IMapInputLock {
        /// <summary>A map drag started.</summary>
        void BeginMapGesture();

        /// <summary>The map drag ended or was cut short.</summary>
        void EndMapGesture();
    }
}
