namespace AlpineLib.Appearance {
    /// <summary>The default resolver: an item is worn only through the variant authored for that exact model.</summary>
    public sealed class ExactModelVariantResolver : IAppearanceVariantResolver {
        /// <summary>Shared stateless instance.</summary>
        public static readonly ExactModelVariantResolver Instance = new ExactModelVariantResolver();

        /// <inheritdoc />
        public bool TryResolve(AppearanceItem item, CharacterModel model, out AppearanceItemVariant variant) {
            variant = null;
            if (item == null) return false;

            return item.TryGetVariantFor(model, out variant);
        }
    }
}
