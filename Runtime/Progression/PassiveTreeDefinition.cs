using UnityEngine;

namespace AlpineLib.Progression {
    /// <summary>
    /// A named collection of <see cref="PassiveNodeDefinition"/>s that a class or specialization grants
    /// as a unit.
    /// </summary>
    /// <remarks>
    /// Version one is deliberately flat: the tree is an unordered bag of nodes with no adjacency, no
    /// point costs and no allocation rules, so granting a tree simply grants every node in it.
    /// Adjacency (edges between nodes), point costs and start nodes are future fields; adding them must
    /// not rename <see cref="nodes"/>, because authored assets serialize against that name.
    /// </remarks>
    [CreateAssetMenu(fileName = "PassiveTreeDefinition", menuName = "AlpineLib/Progression/Passive Tree")]
    public class PassiveTreeDefinition : ScriptableObject {
        /// <summary>
        /// Human readable name for user interfaces and tooling.
        /// </summary>
        public string displayName;

        /// <summary>
        /// Nodes belonging to this tree. Null entries are skipped when the tree is granted.
        /// </summary>
        [Tooltip("Nodes belonging to this tree; all of them are granted together in v1")]
        public PassiveNodeDefinition[] nodes;
    }
}
