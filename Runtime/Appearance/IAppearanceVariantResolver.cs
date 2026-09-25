namespace AlpineLib.Appearance {
    /// <summary>
    /// Picks the prefab an item is worn with on a character model. The seam where a runtime fitter could
    /// adapt an item made for another body.
    /// </summary>
    public interface IAppearanceVariantResolver {
        /// <summary>Finds the variant of <paramref name="item"/> to build on <paramref name="model"/>.</summary>
        bool TryResolve(AppearanceItem item, CharacterModel model, out AppearanceItemVariant variant);
    }
}
