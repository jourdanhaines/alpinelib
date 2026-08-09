using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Progression {
    /// <summary>
    /// Tracks which <see cref="PassiveNodeDefinition"/>s an actor has been granted and keeps the
    /// actor's <see cref="StatSheet"/> in sync with them: granting a node applies its modifiers,
    /// revoking it withdraws exactly those modifiers and nothing else.
    /// </summary>
    /// <remarks>
    /// Each granted node gets its own source key object, and the authored modifiers are re-created
    /// against that key before they are added to the sheet. This matters because
    /// <see cref="StatSheet.RemoveModifiersFrom"/> removes by source identity: keying on the system or
    /// on the shared definition asset would make revoking one node strip every node's contribution, or
    /// worse, strip the same node granted to a different actor. Tags are copied across so conditional
    /// modifiers keep applying only in their intended query contexts.
    ///
    /// Grants are idempotent — a node already granted is ignored rather than stacked, so a game may
    /// re-apply a whole tree on load without double-counting it. The stat sheet is resolved lazily
    /// instead of in <c>Start</c> because character construction commonly grants a class tree from
    /// another component's <c>Awake</c> or <c>Start</c>, before this subsystem's own <c>Start</c> has
    /// necessarily run.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class ProgressionSystem : ActorSubsystem {
        private readonly Dictionary<PassiveNodeDefinition, object> _sourceKeys = new();
        private StatSheet _stats;

        /// <summary>
        /// Nodes currently granted to this actor, in no particular order.
        /// </summary>
        public IReadOnlyCollection<PassiveNodeDefinition> GrantedNodes => _sourceKeys.Keys;

        /// <summary>
        /// Grants a node and applies its stat modifiers. Does nothing when the node is null or has
        /// already been granted.
        /// </summary>
        public void GrantNode(PassiveNodeDefinition node) {
            if (node == null) return;
            if (_sourceKeys.ContainsKey(node)) return;

            var sourceKey = new object();
            _sourceKeys[node] = sourceKey;

            ApplyModifiers(node, sourceKey);
        }

        /// <summary>
        /// Revokes a node and withdraws every modifier it applied. Does nothing when the node is null
        /// or was never granted.
        /// </summary>
        public void RevokeNode(PassiveNodeDefinition node) {
            if (node == null) return;
            if (!_sourceKeys.TryGetValue(node, out object sourceKey)) return;

            _sourceKeys.Remove(node);
            Stats.RemoveModifiersFrom(sourceKey);
        }

        /// <summary>
        /// Grants every node in a tree. Null trees and null entries are skipped, and nodes already
        /// granted from another tree are left alone rather than stacked.
        /// </summary>
        public void GrantTree(PassiveTreeDefinition tree) {
            if (tree == null) return;
            if (tree.nodes == null) return;

            foreach (var node in tree.nodes) {
                GrantNode(node);
            }
        }

        /// <summary>
        /// Revokes every granted node, leaving the stat sheet as if nothing had ever been granted.
        /// </summary>
        public void Clear() {
            foreach (object sourceKey in _sourceKeys.Values) {
                Stats.RemoveModifiersFrom(sourceKey);
            }

            _sourceKeys.Clear();
        }

        private void ApplyModifiers(PassiveNodeDefinition node, object sourceKey) {
            if (node.modifiers == null) return;

            foreach (var modifier in node.modifiers) {
                if (modifier.Stat == null) continue;

                var sourced = new StatModifier(modifier.Stat, modifier.Operation, modifier.Value, sourceKey, modifier.Tags, modifier.Priority);
                Stats.AddModifier(sourced);
            }
        }

        private StatSheet Stats {
            get {
                if (_stats == null) _stats = GetComponent<StatSheet>();
                return _stats;
            }
        }
    }
}
