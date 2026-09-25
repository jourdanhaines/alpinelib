using AlpineLib.Netcode.Appearance;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A small two-model catalog the appearance tests share: model 1 has four slots, model 2 has two.
    /// </summary>
    public static class AppearanceTestCatalog {
        public const ushort ModelA = 1;
        public const ushort ModelB = 2;

        /// <summary>Slot 0, two variants, model A only.</summary>
        public const ushort HatForA = 10;

        /// <summary>Slot 1, one variant, any model.</summary>
        public const ushort ScarfForAny = 11;

        /// <summary>Slot 0, three variants, model B only.</summary>
        public const ushort HatForB = 12;

        /// <summary>Slot 3, one variant, model A only.</summary>
        public const ushort BootsForA = 13;

        public static AppearanceCatalogTable Build() {
            return new AppearanceCatalogTable(
                ModelA,
                new[] {
                    new AppearanceModelInfo(ModelA, 4, 7),
                    new AppearanceModelInfo(ModelB, 2, 0),
                },
                new[] {
                    new AppearanceItemInfo(HatForA, 0, 2, new[] { ModelA }),
                    new AppearanceItemInfo(ScarfForAny, 1, 1, null),
                    new AppearanceItemInfo(HatForB, 0, 3, new[] { ModelB }),
                    new AppearanceItemInfo(BootsForA, 3, 1, new[] { ModelA }),
                });
        }
    }
}
