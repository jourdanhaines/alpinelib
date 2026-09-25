namespace AlpineLib.Appearance {
    /// <summary>Whether an item is hidden from its own wearer's first-person view.</summary>
    public enum AppearanceFirstPersonVisibility {
        /// <summary>Follow the slot's <see cref="AppearanceSlotDefinition.HiddenInFirstPerson"/>.</summary>
        UseSlot,

        /// <summary>Always hidden in first person (shadows only).</summary>
        Hidden,

        /// <summary>Always drawn in first person.</summary>
        Visible
    }
}
