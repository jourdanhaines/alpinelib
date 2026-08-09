using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Tags {
    /// <summary>
    /// An authored, unordered collection of <see cref="TagDefinition"/> references. Used both as a
    /// description of a thing ("this hit is Fire, Spell, Projectile") and as a requirement on a
    /// modifier ("applies only to Fire Spells"); the two meet in
    /// <see cref="Matches(TagSet, TagSet)"/>, which asks whether a requirement is satisfied by a
    /// context.
    /// </summary>
    /// <remarks>
    /// Serializable so sets can be authored inline on assets and inspectors. The set is read-only
    /// at runtime — there is no public mutator — which is what makes <see cref="Empty"/> safe to
    /// share between every caller instead of allocating a throwaway set per query.
    /// <para>
    /// Lookups switch strategy on size: sets of four or fewer entries scan the list linearly,
    /// which beats hashing at that size and allocates nothing, while larger sets build a
    /// <see cref="HashSet{T}"/> on first query and reuse it. The lookup is rebuilt whenever the
    /// entry count changes, so editing the list in the inspector during play does not leave a
    /// stale set behind; replacing entries without changing the count is the one edit that is not
    /// detected, and it only affects play-mode authoring.
    /// </para>
    /// </remarks>
    [Serializable]
    public class TagSet {
        private const int LinearScanThreshold = 4;

        private static readonly TagSet SharedEmpty = new();

        [Tooltip("Tags in this set. Order is irrelevant; duplicates are harmless")]
        [SerializeField] private List<TagDefinition> tags = new();

        [NonSerialized] private HashSet<TagDefinition> _lookup;
        [NonSerialized] private int _lookupEntryCount = -1;

        /// <summary>
        /// A shared set with no entries. Matches nothing and is a subset of everything; use it as
        /// the query context when no tags apply rather than allocating an empty set per call.
        /// </summary>
        public static TagSet Empty => SharedEmpty;

        /// <summary>
        /// The tags in this set, in authoring order. May contain nulls when an asset reference was
        /// left empty; every query on this type ignores them.
        /// </summary>
        public IReadOnlyList<TagDefinition> Tags => tags ?? (IReadOnlyList<TagDefinition>)Array.Empty<TagDefinition>();

        /// <summary>Number of entries, including any null ones.</summary>
        public int Count => tags?.Count ?? 0;

        /// <summary>True when the set holds no entries at all.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// True when the given tag is present in this set. A null tag is never present.
        /// </summary>
        public bool Contains(TagDefinition tag) {
            if (tag == null) return false;
            if (tags == null) return false;
            if (tags.Count == 0) return false;
            if (tags.Count <= LinearScanThreshold) return ContainsLinear(tag);

            EnsureLookup();
            return _lookup.Contains(tag);
        }

        /// <summary>
        /// True when every tag in this set is also present in <paramref name="context"/>, which is
        /// the test a conditional modifier has to pass before it contributes.
        /// </summary>
        /// <remarks>
        /// An empty set — or one whose only entries are null references — is a subset of anything,
        /// so an unconditional modifier applies everywhere. A null context is treated as an empty
        /// context, so it satisfies only those unconditional requirements.
        /// </remarks>
        public bool IsSubsetOf(TagSet context) {
            if (tags == null) return true;
            if (tags.Count == 0) return true;

            var resolvedContext = context ?? SharedEmpty;

            foreach (var tag in tags) {
                if (tag == null) continue;
                if (!resolvedContext.Contains(tag)) return false;
            }

            return true;
        }

        /// <summary>
        /// Null-safe form of <see cref="IsSubsetOf"/>: true when <paramref name="requirement"/> is
        /// satisfied by <paramref name="context"/>.
        /// </summary>
        /// <remarks>
        /// A null requirement matches everything, which is what makes untagged
        /// <see cref="AlpineLib.Stats.StatModifier"/> instances — the overwhelming majority —
        /// global without any authoring. A null context is treated as empty.
        /// </remarks>
        public static bool Matches(TagSet requirement, TagSet context) {
            if (requirement == null) return true;

            return requirement.IsSubsetOf(context);
        }

        private bool ContainsLinear(TagDefinition tag) {
            foreach (var candidate in tags) {
                if (candidate == tag) return true;
            }

            return false;
        }

        private void EnsureLookup() {
            if (_lookup != null && _lookupEntryCount == tags.Count) return;

            _lookup ??= new HashSet<TagDefinition>();
            _lookup.Clear();

            foreach (var tag in tags) {
                if (tag == null) continue;
                _lookup.Add(tag);
            }

            _lookupEntryCount = tags.Count;
        }
    }
}
