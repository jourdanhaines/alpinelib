using System;
using AlpineLib.Netcode.Appearance;
using AlpineLib.Netcode.Protocol;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The outfit wire format round-tripped through the real writer and reader, plus the outfit value
    /// semantics the state channel relies on to spot a change.
    /// </summary>
    public sealed class AppearanceOutfitMessageCodecTests {
        [Fact]
        public void APopulatedOutfitRoundTrips() {
            AppearanceOutfit original = AppearanceOutfit.Create(3, new[] {
                new AppearanceSlotPick(10, 1),
                AppearanceSlotPick.Empty,
                new AppearanceSlotPick(65535, 255),
            });

            AppearanceOutfit decoded = RoundTrip(original, out int written);

            Assert.Equal(original, decoded);
            Assert.Equal(3, decoded.ModelId);
            Assert.Equal(3, decoded.SlotCount);
            Assert.True(decoded[1].IsEmpty);
            Assert.Equal(new AppearanceSlotPick(65535, 255), decoded[2]);
            Assert.Equal(AppearanceOutfitMessage.ByteCountFor(3), written);
        }

        [Fact]
        public void AZeroSlotOutfitIsJustTheHeader() {
            AppearanceOutfit decoded = RoundTrip(AppearanceOutfit.EmptyFor(5, 0), out int written);

            Assert.Equal(5, decoded.ModelId);
            Assert.Equal(0, decoded.SlotCount);
            Assert.Equal(AppearanceOutfitMessage.HeaderByteCount, written);
        }

        [Fact]
        public void TheDefaultOutfitRoundTripsAsModelZeroWithNoSlots() {
            AppearanceOutfit decoded = RoundTrip(default, out int written);

            Assert.Equal(default(AppearanceOutfit), decoded);
            Assert.Equal(3, written);
        }

        [Fact]
        public void AMaxSlotOutfitFillsMaxByteCount() {
            AppearanceOutfit original = AppearanceOutfit.EmptyFor(1, AppearanceOutfit.MaxSlots);
            for (int index = 0; index < AppearanceOutfit.MaxSlots; index++) {
                original = original.With(index, new AppearanceSlotPick((ushort)(index + 1), (byte)index));
            }

            AppearanceOutfit decoded = RoundTrip(original, out int written);

            Assert.Equal(original, decoded);
            Assert.Equal(AppearanceOutfitMessage.MaxByteCount, written);
            Assert.Equal(51, AppearanceOutfitMessage.MaxByteCount);
        }

        [Fact]
        public void EmptySlotsKeepTheirPositions() {
            AppearanceOutfit original = AppearanceOutfit.EmptyFor(1, 4).With(3, new AppearanceSlotPick(9, 0));

            AppearanceOutfit decoded = RoundTrip(original, out _);

            Assert.True(decoded[0].IsEmpty);
            Assert.True(decoded[2].IsEmpty);
            Assert.Equal(9, decoded[3].ItemId);
        }

        [Fact]
        public void ByteCountForMatchesTheLayout() {
            Assert.Equal(3, AppearanceOutfitMessage.ByteCountFor(0));
            Assert.Equal(27, AppearanceOutfitMessage.ByteCountFor(8));
        }

        [Fact]
        public void ASlotCountAboveTheCapIsRejectedBeforeAnySlotIsRead() {
            byte[] buffer = new byte[AppearanceOutfitMessage.MaxByteCount + 16];
            var writer = new NetWriter(buffer);
            writer.WriteUShort(1);
            writer.WriteByte(AppearanceOutfit.MaxSlots + 1);

            Assert.Throws<NetProtocolException>(() => Decode(buffer, buffer.Length));
        }

        [Fact]
        public void ATruncatedMessageIsRejected() {
            AppearanceOutfit original = AppearanceOutfit.EmptyFor(1, 2).With(1, new AppearanceSlotPick(4, 0));
            byte[] buffer = Encode(original, out int written);

            Assert.Throws<NetProtocolException>(() => Decode(buffer, written - 1));
        }

        [Fact]
        public void OutfitsCompareByModelAndEverySlot() {
            AppearanceOutfit left = AppearanceOutfit.EmptyFor(1, 2).With(0, new AppearanceSlotPick(4, 1));
            AppearanceOutfit same = AppearanceOutfit.Create(1, new[] { new AppearanceSlotPick(4, 1), AppearanceSlotPick.Empty });

            Assert.Equal(left, same);
            Assert.True(left == same);
            Assert.Equal(left.GetHashCode(), same.GetHashCode());
            Assert.NotEqual(left, left.With(0, new AppearanceSlotPick(4, 0)));
            Assert.NotEqual(left, AppearanceOutfit.Create(2, new[] { new AppearanceSlotPick(4, 1), AppearanceSlotPick.Empty }));
            Assert.NotEqual(left, AppearanceOutfit.EmptyFor(1, 3).With(0, new AppearanceSlotPick(4, 1)));
        }

        [Fact]
        public void WithLeavesTheOriginalUntouched() {
            AppearanceOutfit original = AppearanceOutfit.EmptyFor(1, 2);

            AppearanceOutfit changed = original.With(1, new AppearanceSlotPick(7, 0));

            Assert.True(original[1].IsEmpty);
            Assert.Equal(7, changed[1].ItemId);
        }

        [Fact]
        public void CreateCopiesTheCallersPicks() {
            var picks = new[] { new AppearanceSlotPick(4, 0) };
            AppearanceOutfit outfit = AppearanceOutfit.Create(1, picks);

            picks[0] = new AppearanceSlotPick(9, 9);

            Assert.Equal(4, outfit[0].ItemId);
        }

        [Fact]
        public void OutfitsBeyondTheCapCannotBeBuilt() {
            Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceOutfit.EmptyFor(1, AppearanceOutfit.MaxSlots + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceOutfit.Create(1, new AppearanceSlotPick[AppearanceOutfit.MaxSlots + 1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceOutfit.EmptyFor(1, 2).With(2, AppearanceSlotPick.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceOutfit.EmptyFor(1, 2)[-1]);
        }

        private static AppearanceOutfit RoundTrip(AppearanceOutfit outfit, out int written) {
            byte[] buffer = Encode(outfit, out written);
            return Decode(buffer, written);
        }

        private static byte[] Encode(AppearanceOutfit outfit, out int written) {
            byte[] buffer = new byte[AppearanceOutfitMessage.MaxByteCount];
            var writer = new NetWriter(buffer);
            writer.WriteMessage(new AppearanceOutfitMessage(outfit));
            written = writer.Written;
            return buffer;
        }

        private static AppearanceOutfit Decode(byte[] buffer, int count) {
            var reader = new NetReader(buffer, 0, count);
            return reader.ReadMessage<AppearanceOutfitMessage>().Outfit;
        }
    }
}
