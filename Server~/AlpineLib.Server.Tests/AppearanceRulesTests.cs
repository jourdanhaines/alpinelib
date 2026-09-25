using AlpineLib.Netcode.Appearance;
using Xunit;
using static AlpineLib.Server.Tests.AppearanceTestCatalog;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// <see cref="AppearanceRules"/> against <see cref="AppearanceTestCatalog"/>: every rule accepted at
    /// its edge and refused one step past it.
    /// </summary>
    public sealed class AppearanceRulesTests {
        private readonly AppearanceCatalogTable catalog = Build();

        [Fact]
        public void AValidOutfitPasses() {
            AppearanceOutfit outfit = AppearanceOutfit.EmptyFor(ModelA, 4)
                .With(0, new AppearanceSlotPick(HatForA, 1))
                .With(1, new AppearanceSlotPick(ScarfForAny, 0))
                .With(3, new AppearanceSlotPick(BootsForA, 0));

            Assert.True(AppearanceRules.Validate(outfit, catalog, out string error), error);
            Assert.Null(error);
        }

        [Fact]
        public void AnAllEmptyOutfitPasses() {
            Assert.True(AppearanceRules.Validate(AppearanceOutfit.EmptyFor(ModelB, 2), catalog, out _));
        }

        [Fact]
        public void AnUnknownModelIsRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(99, 4), "Unknown model");
        }

        [Fact]
        public void ModelZeroIsRefused() {
            AssertRefused(default, "Unknown model");
        }

        [Fact]
        public void TooFewSlotsAreRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 3), "slot(s)");
        }

        [Fact]
        public void TooManySlotsAreRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 5), "slot(s)");
        }

        [Fact]
        public void AnUnknownItemIsRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 4).With(0, new AppearanceSlotPick(500, 0)), "unknown item");
        }

        [Fact]
        public void AnItemInTheWrongSlotIsRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 4).With(2, new AppearanceSlotPick(HatForA, 0)), "belongs in slot 0");
        }

        [Fact]
        public void AnItemNotMadeForTheModelIsRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 4).With(0, new AppearanceSlotPick(HatForB, 0)), "not made for model");
        }

        [Fact]
        public void AnItemWithNoAllowedModelsFitsAnyModel() {
            AppearanceOutfit onA = AppearanceOutfit.EmptyFor(ModelA, 4).With(1, new AppearanceSlotPick(ScarfForAny, 0));
            AppearanceOutfit onB = AppearanceOutfit.EmptyFor(ModelB, 2).With(1, new AppearanceSlotPick(ScarfForAny, 0));

            Assert.True(AppearanceRules.Validate(onA, catalog, out _));
            Assert.True(AppearanceRules.Validate(onB, catalog, out _));
        }

        [Fact]
        public void TheLastVariantPassesAndTheCountItselfIsRefused() {
            AppearanceOutfit last = AppearanceOutfit.EmptyFor(ModelA, 4).With(0, new AppearanceSlotPick(HatForA, 1));
            AppearanceOutfit beyond = AppearanceOutfit.EmptyFor(ModelA, 4).With(0, new AppearanceSlotPick(HatForA, 2));

            Assert.True(AppearanceRules.Validate(last, catalog, out _));
            AssertRefused(beyond, "variant 2 does not exist");
        }

        [Fact]
        public void AnEmptySlotNamingAVariantIsRefused() {
            AssertRefused(AppearanceOutfit.EmptyFor(ModelA, 4).With(2, new AppearanceSlotPick(0, 1)), "is empty but names variant");
        }

        [Fact]
        public void ValidateForModelRefusesAMismatchBeforeAnythingElse() {
            AppearanceOutfit outfit = AppearanceOutfit.EmptyFor(ModelB, 2);

            Assert.False(AppearanceRules.ValidateForModel(outfit, ModelA, catalog, out string error));
            Assert.Contains("but the character is model 1", error);
            Assert.True(AppearanceRules.ValidateForModel(outfit, ModelB, catalog, out error), error);
        }

        [Fact]
        public void ValidateForModelStillAppliesEveryOtherRule() {
            AppearanceOutfit outfit = AppearanceOutfit.EmptyFor(ModelB, 2).With(0, new AppearanceSlotPick(HatForA, 0));

            Assert.False(AppearanceRules.ValidateForModel(outfit, ModelB, catalog, out string error));
            Assert.Contains("not made for model", error);
        }

        private void AssertRefused(AppearanceOutfit outfit, string expectedFragment) {
            Assert.False(AppearanceRules.Validate(outfit, catalog, out string error));
            Assert.NotNull(error);
            Assert.Contains(expectedFragment, error);
        }
    }
}
