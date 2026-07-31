namespace AlpineLib.Actors {
    /// <summary>
    /// Something that can die. Implemented by actors so that subsystems, perception and combat
    /// can react to death without depending on a concrete actor type.
    /// </summary>
    public interface IMortal {
        /// <summary>
        /// False once the owner has died. Never returns to true.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Raised once, at the moment the owner dies.
        /// </summary>
        event System.Action OnDeath;
    }
}
