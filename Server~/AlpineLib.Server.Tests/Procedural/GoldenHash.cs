using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural {
    /// <summary>Bit-exact fingerprints of plan geometry, the same mix the golden generator used.</summary>
    public static class GoldenHash {
        public const ulong Start = StableHash.Fnv1a64OffsetBasis;

        public static ulong Add(ulong hash, double value) => StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(value));

        public static ulong Add(ulong hash, long value) => StableHash.Mix(hash, (ulong)value);

        public static ulong Add(ulong hash, bool value) => StableHash.Mix(hash, value ? 1UL : 0UL);

        public static ulong Add(ulong hash, PlanPoint point) => Add(Add(hash, point.X), point.Z);

        public static ulong Add(ulong hash, IReadOnlyList<PlanPoint> points) {
            hash = Add(hash, (long)points.Count);
            foreach (PlanPoint point in points) hash = Add(hash, point);
            return hash;
        }

        public static ulong Piece(ulong hash, SlopedPrismPiece piece) {
            hash = Add(hash, piece.Plan);
            hash = Add(Add(Add(hash, piece.CentreX), piece.CentreZ), piece.TopY0);
            hash = Add(Add(hash, piece.SlopeX), piece.SlopeZ);
            return Add(hash, piece.IsFan);
        }

        public static ulong PieceKey(SlopedPrismPiece piece) => Piece(Start, piece);

        public static ulong Result(TalusApronResult result) {
            ulong hash = Add(Start, result.IsLaid);
            hash = Add(hash, (long)result.Parts.Count);
            foreach (TalusApronPart part in result.Parts) hash = Part(hash, part);
            hash = Add(hash, (long)result.Bare.Count);
            foreach (TalusBareStretch stretch in result.Bare) hash = Add(Add(hash, stretch.Start), stretch.End);
            return hash;
        }

        private static ulong Part(ulong hash, TalusApronPart part) {
            hash = Add(Add(Add(Add(hash, part.InnerStart), part.InnerEnd), part.OuterStart), part.OuterEnd);
            hash = Add(Add(Add(hash, part.Rise), part.Width), part.IsFan);
            hash = Add(hash, (long)part.Pieces.Count);
            foreach (SlopedPrismPiece piece in part.Pieces) hash = Piece(hash, piece);
            return Add(hash, part.Surface);
        }
    }
}
