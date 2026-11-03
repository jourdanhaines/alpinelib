using System.Collections.Generic;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Drives a corrective blendshape from how far one or more bones have turned about an axis, e.g. a
    /// shoulder fold shape that fades in as the arm swings.
    /// </summary>
    /// <remarks>
    /// Each source's angle is the twist of its rotation away from rest about its axis; the largest one,
    /// as a fraction of <c>maxDegrees</c>, sets the weight on every target renderer carrying the shape.
    /// Runs after the pose modifiers so it reads the finished pose. Renderers that come and go at runtime,
    /// such as worn clothing carrying the same shape, join through <see cref="SetExtraTargets"/>.
    /// </remarks>
    [DefaultExecutionOrder(ActorExecutionOrder.BlendShapeDrivers)]
    public class BoneAngleBlendShapeDriver : MonoBehaviour {
        private const float FullWeight = 100f;
        private const int MissingIndex = -1;
        private const float MinimumAxisLengthSquared = 1e-10f;

        [Tooltip("Every skinned renderer carrying the shape; the weight is written to each that has it.")]
        [SerializeField] private SkinnedMeshRenderer[] targets = new SkinnedMeshRenderer[0];

        [SerializeField] private string blendShapeName;

        [SerializeField] private BoneAngleSource[] sources = new BoneAngleSource[0];

        [Tooltip("Angle, in degrees, at which the shape reaches full weight.")]
        [Min(0.1f)]
        [SerializeField] private float maxDegrees = 26f;

        private int[] _shapeIndices;
        private readonly List<SkinnedMeshRenderer> _extraTargets = new List<SkinnedMeshRenderer>();
        private readonly List<int> _extraIndices = new List<int>();

        /// <summary>Weight written on the last evaluate, 0 to 100.</summary>
        public float Weight { get; private set; }

        /// <summary>Number of measured bones.</summary>
        public int SourceCount => sources != null ? sources.Length : 0;

        /// <summary>Name of the driven blendshape.</summary>
        public string BlendShapeName => blendShapeName;

        /// <summary>Renderers driven at runtime besides the configured targets; never serialized.</summary>
        public IReadOnlyList<SkinnedMeshRenderer> ExtraTargets => _extraTargets;

        /// <summary>Sets everything at once, e.g. from an assembler building the body prefab.</summary>
        public void Configure(SkinnedMeshRenderer[] shapeTargets, string shapeName, BoneAngleSource[] angleSources, float fullWeightDegrees) {
            targets = shapeTargets ?? new SkinnedMeshRenderer[0];
            blendShapeName = shapeName;
            sources = angleSources ?? new BoneAngleSource[0];
            maxDegrees = Mathf.Max(0.1f, fullWeightDegrees);
            _shapeIndices = null;
        }

        /// <summary>
        /// Absolute turn, in degrees, of source <paramref name="sourceIndex"/> about its axis away from
        /// rest; 0 for a missing bone or a zero axis.
        /// </summary>
        public float MeasureDegrees(int sourceIndex) {
            if (sources == null || sourceIndex < 0 || sourceIndex >= sources.Length) return 0f;

            BoneAngleSource source = sources[sourceIndex];
            if (source.bone == null || source.localAxis.sqrMagnitude < MinimumAxisLengthSquared) return 0f;

            Quaternion delta = Quaternion.Inverse(source.restLocalRotation) * source.bone.localRotation;
            return TwistDegrees(delta, source.localAxis.normalized);
        }

        /// <summary>
        /// Replaces the runtime renderers driven alongside the configured targets and writes the current weight
        /// to them; renderers without the shape are skipped silently, since a worn piece may simply not carry it.
        /// </summary>
        public void SetExtraTargets(IEnumerable<SkinnedMeshRenderer> renderers) {
            _extraTargets.Clear();
            _extraIndices.Clear();
            if (renderers != null) {
                foreach (SkinnedMeshRenderer renderer in renderers) AddExtraTarget(renderer);
            }

            WriteExtraWeight();
        }

        /// <summary>Takes each source bone's current local rotation as its rest.</summary>
        public void CaptureRest() {
            if (sources == null) return;

            for (int index = 0; index < sources.Length; index++) {
                if (sources[index].bone != null) sources[index].restLocalRotation = sources[index].bone.localRotation;
            }
        }

        /// <summary>Measures the sources and writes the weight. Public so gates and previews can step it.</summary>
        public void Evaluate() {
            float largest = 0f;
            for (int index = 0; index < SourceCount; index++) {
                largest = Mathf.Max(largest, MeasureDegrees(index));
            }

            Weight = Mathf.Clamp01(largest / maxDegrees) * FullWeight;
            WriteWeight();
        }

        /// <summary>
        /// Absolute twist, in degrees within [0, 180], of <paramref name="rotation"/> about the unit
        /// <paramref name="axis"/>.
        /// </summary>
        public static float TwistDegrees(Quaternion rotation, Vector3 axis) {
            float along = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
            float degrees = 2f * Mathf.Atan2(along, rotation.w) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(0f, degrees));
        }

        private void LateUpdate() {
            Evaluate();
        }

        private void WriteWeight() {
            WriteExtraWeight();
            if (targets == null) return;

            EnsureIndices();
            for (int index = 0; index < targets.Length; index++) {
                if (targets[index] != null && _shapeIndices[index] >= 0) targets[index].SetBlendShapeWeight(_shapeIndices[index], Weight);
            }
        }

        private void AddExtraTarget(SkinnedMeshRenderer renderer) {
            if (renderer == null || renderer.sharedMesh == null || string.IsNullOrEmpty(blendShapeName)) return;

            int shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (shapeIndex < 0 || _extraTargets.Contains(renderer)) return;

            _extraTargets.Add(renderer);
            _extraIndices.Add(shapeIndex);
        }

        // A destroyed piece leaves a null until the next SetExtraTargets.
        private void WriteExtraWeight() {
            for (int index = 0; index < _extraTargets.Count; index++) {
                if (_extraTargets[index] != null) _extraTargets[index].SetBlendShapeWeight(_extraIndices[index], Weight);
            }
        }

        // Resolves the shape by name once per target; a target without it warns once and is skipped.
        private void EnsureIndices() {
            if (_shapeIndices != null && _shapeIndices.Length == targets.Length) return;

            _shapeIndices = new int[targets.Length];
            for (int index = 0; index < targets.Length; index++) {
                _shapeIndices[index] = ResolveIndex(targets[index]);
            }
        }

        private int ResolveIndex(SkinnedMeshRenderer target) {
            if (target == null || target.sharedMesh == null || string.IsNullOrEmpty(blendShapeName)) return MissingIndex;

            int shapeIndex = target.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (shapeIndex < 0) Debug.LogWarning($"{nameof(BoneAngleBlendShapeDriver)} on '{name}': '{target.name}' has no blendshape '{blendShapeName}'.", this);

            return shapeIndex;
        }

        private void OnValidate() {
            _shapeIndices = null;
        }
    }
}
