using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Landmarks;

namespace AlpineLib.Server.Tests.Procedural.Landmarks {
    /// <summary>
    /// Pinned landmark outputs for the preset tiers [Mesa, Giant] and the cluster tiers [Clusters, Lonely]. A consumer can port
    /// <see cref="Fingerprint"/> into a Mono smoke gate and compare against the same constants.
    /// </summary>
    public static class LandmarkFieldGolden {
        /// <summary>Lowest cell (both axes) of the fingerprint window, inclusive.</summary>
        public const int WindowMin = -20;

        /// <summary>Highest cell (both axes) of the fingerprint window, exclusive.</summary>
        public const int WindowMax = 20;

        /// <summary>The reach every window cell is queried with.</summary>
        public const int Reach = 1;

        /// <summary>Seeds with a pinned fingerprint.</summary>
        public static readonly ulong[] Seeds = { 1UL, 7UL, 42UL };

        /// <summary>Pinned <see cref="Fingerprint"/> per entry of <see cref="Seeds"/>.</summary>
        public static readonly ulong[] Fingerprints = { 0xAE4AB05B2B214515UL, 0x3467C11B7586784AUL, 0x421B1E17048F267FUL };

        /// <summary>Pinned <see cref="ClusterFingerprint"/> per entry of <see cref="Seeds"/> for the tiers [Clusters, Lonely].</summary>
        public static readonly ulong[] ClusterFingerprints = { 0xEC7384AC5F5CA520UL, 0x5923D23CAC069DB9UL, 0xA49560FEF1F30D28UL };

        /// <summary>Lowest cell (both axes) of the cluster fingerprint window, inclusive; the highest is its negation, exclusive.</summary>
        public const int ClusterWindowMin = -50;

        /// <summary>The seed <see cref="Sites"/> and <see cref="AbsentBlocks"/> are pinned for.</summary>
        public const ulong SiteSeed = 42UL;

        /// <summary>Exact sites at <see cref="SiteSeed"/>, negative blocks included.</summary>
        public static readonly LandmarkSite[] Sites = {
            new LandmarkSite(0, new CellCoord(-3, -3), new CellCoord(-10, -11), -7.7058, -6.6487, 0x583A70DAA9A1121DUL,
                             new LandmarkDraws(0.621686, 0.411954, 0.315004, 0.077673), 0x82728B579E7E0EB9UL),
            new LandmarkSite(0, new CellCoord(2, -2), new CellCoord(10, -6), -7.8661, 7.6233, 0x6FF56480BDF0566EUL,
                             new LandmarkDraws(0.154965, 0.175954, 0.999992, 0.603032), 0xB002ECCA9FC6E5A4UL),
            new LandmarkSite(1, new CellCoord(-2, -1), new CellCoord(-14, -3), 0.0, 0.0, 0xA485E8E6B66274E3UL,
                             new LandmarkDraws(0.365899, 0.208971, 0.802127, 0.159993), 0x38F6507E395C8CBEUL),
        };

        /// <summary>(tier, block) pairs that hold no site at <see cref="SiteSeed"/>.</summary>
        public static readonly (int Tier, CellCoord Block)[] AbsentBlocks = {
            (0, new CellCoord(-3, -2)),
            (1, new CellCoord(-1, -3)),
        };

        /// <summary>The field the golden values come from.</summary>
        public static LandmarkField CreateField(ulong seed) {
            return new LandmarkField(seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
        }

        /// <summary>The cluster field the cluster golden values come from (no bias).</summary>
        public static LandmarkField CreateClusterField(ulong seed) {
            return new LandmarkField(seed, new[] { LandmarkTierSettings.Clusters, LandmarkTierSettings.Lonely });
        }

        /// <summary>
        /// FNV-1a 64 over the cluster window (every 5th cell per axis, reach 1): per cell the site count, then each site
        /// through <see cref="AppendSite"/> followed by <see cref="AppendMember"/>.
        /// </summary>
        public static ulong ClusterFingerprint(ulong seed) {
            LandmarkField field = CreateClusterField(seed);
            var sites = new List<LandmarkSite>();
            ulong hash = StableHash.Fnv1a64OffsetBasis;
            for (int cellY = ClusterWindowMin; cellY < -ClusterWindowMin; cellY += 5) {
                for (int cellX = ClusterWindowMin; cellX < -ClusterWindowMin; cellX += 5) {
                    sites.Clear();
                    field.SitesTouching(new CellCoord(cellX, cellY), Reach, sites);
                    hash = StableHash.Fnv1a64Append(hash, (uint)sites.Count);
                    foreach (LandmarkSite site in sites) {
                        hash = AppendMember(AppendSite(hash, site), site.Member);
                    }
                }
            }
            return hash;
        }

        /// <summary>Appends a member: kind, slot, count, large flag, bearing as uint words, then Across, Ring bits and the cluster id low word first.</summary>
        public static ulong AppendMember(ulong hash, LandmarkMember member) {
            hash = StableHash.Fnv1a64Append(hash, (uint)member.Kind);
            hash = StableHash.Fnv1a64Append(hash, (uint)member.Slot);
            hash = StableHash.Fnv1a64Append(hash, (uint)member.Count);
            hash = StableHash.Fnv1a64Append(hash, member.IsLarge ? 1u : 0u);
            hash = StableHash.Fnv1a64Append(hash, (uint)member.BearingDegrees);
            hash = AppendULong(hash, (ulong)BitConverter.DoubleToInt64Bits(member.Across));
            hash = AppendULong(hash, (ulong)BitConverter.DoubleToInt64Bits(member.Ring));
            return AppendULong(hash, member.ClusterId);
        }

        /// <summary>
        /// FNV-1a 64 over the window, cells row by row (Y then X ascending): per cell the site count of
        /// <c>SitesTouching(cell, Reach)</c>, then each site in that order through <see cref="AppendSite"/>.
        /// </summary>
        public static ulong Fingerprint(ulong seed) {
            LandmarkField field = CreateField(seed);
            var sites = new List<LandmarkSite>();
            ulong hash = StableHash.Fnv1a64OffsetBasis;
            for (int cellY = WindowMin; cellY < WindowMax; cellY++) {
                for (int cellX = WindowMin; cellX < WindowMax; cellX++) {
                    sites.Clear();
                    field.SitesTouching(new CellCoord(cellX, cellY), Reach, sites);
                    hash = AppendCell(hash, sites);
                }
            }
            return hash;
        }

        /// <summary>
        /// Appends one site as little-endian uint words: tier, block X, Y, anchor X, Y, then the
        /// 64-bit fields low word first: OffsetX bits, OffsetZ bits, ShapeSeed, UDraws 0–3 bits, Id.
        /// </summary>
        public static ulong AppendSite(ulong hash, LandmarkSite site) {
            hash = StableHash.Fnv1a64Append(hash, (uint)site.Tier);
            hash = StableHash.Fnv1a64Append(hash, (uint)site.Block.X);
            hash = StableHash.Fnv1a64Append(hash, (uint)site.Block.Y);
            hash = StableHash.Fnv1a64Append(hash, (uint)site.Anchor.X);
            hash = StableHash.Fnv1a64Append(hash, (uint)site.Anchor.Y);
            hash = AppendULong(hash, (ulong)BitConverter.DoubleToInt64Bits(site.OffsetX));
            hash = AppendULong(hash, (ulong)BitConverter.DoubleToInt64Bits(site.OffsetZ));
            hash = AppendULong(hash, site.ShapeSeed);
            for (int index = 0; index < LandmarkDraws.Count; index++) {
                hash = AppendULong(hash, (ulong)BitConverter.DoubleToInt64Bits(site.UDraws[index]));
            }
            return AppendULong(hash, site.Id);
        }

        private static ulong AppendCell(ulong hash, List<LandmarkSite> sites) {
            hash = StableHash.Fnv1a64Append(hash, (uint)sites.Count);
            foreach (LandmarkSite site in sites) {
                hash = AppendSite(hash, site);
            }
            return hash;
        }

        private static ulong AppendULong(ulong hash, ulong value) {
            hash = StableHash.Fnv1a64Append(hash, (uint)value);
            return StableHash.Fnv1a64Append(hash, (uint)(value >> 32));
        }
    }
}
