namespace AlpineLib.Appearance {
    /// <summary>How an item variant's prefab joins the character.</summary>
    public enum AppearanceAttachMode {
        /// <summary>Skinned meshes rebound by bone name onto the body's skeleton.</summary>
        Skinned,

        /// <summary>A rigid prefab parented to a named socket transform under the model.</summary>
        Socket
    }
}
