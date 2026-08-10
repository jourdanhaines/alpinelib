namespace AlpineLib.Chat {
    /// <summary>
    /// Where a chat provider stands with its backend. The UI drives the input box off this, so a player
    /// is never typing into a channel that cannot deliver.
    /// </summary>
    public enum ChatProviderState : byte {
        /// <summary>Not connected and not trying to be.</summary>
        Disconnected = 0,

        /// <summary>A connect attempt is in flight.</summary>
        Connecting = 1,

        /// <summary>Connected and able to send.</summary>
        Connected = 2,

        /// <summary>A connect or send failed in a way the provider cannot recover from on its own.</summary>
        Faulted = 3
    }
}
