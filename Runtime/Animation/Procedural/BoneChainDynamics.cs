using AlpineLib.Origin;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Lets a chain of bones (a long nose, a tail, an antenna) swing behind the body that carries it: each
    /// link's tip is a damped spring pulled toward its animated place, so starts, stops, bobs and turns
    /// wave down the chain and it settles back when the body is still.
    /// </summary>
    /// <remarks>
    /// The chain is named, root link first; the bone before the first link stays animated and anchors it.
    /// Tips are simulated in the frame the actor stands in (its parent: the carrier it rides, else the world),
    /// so a train's motion is not felt as wind; only the actor's own movement and its animation drive the
    /// chain. Each step a tip accelerates toward its link's animated offset from the tip before it
    /// (<see cref="Stiffness"/>), loses its speed relative to that tip (<see cref="Damping"/>) and falls
    /// (<see cref="Gravity"/>); it is then put back at its link's length, at most <see cref="MaxBendDegrees"/>
    /// off the animated direction. Frames are split into equal steps no longer than the substep. Each link
    /// is then turned (world rotation, so the write stays additive under <see cref="PoseBones"/>) to aim at
    /// its tip, blended by <see cref="PoseModifier.Weight"/>. Weight 0 leaves the pose alone and the next
    /// live frame starts from the animated pose. A jump of the anchor past the teleport distance, or a
    /// destroyed carrier, starts over from the animated pose; a carrier change keeps the shape; a
    /// floating-origin shift moves the world-frame state with the world. Local only: every client runs its
    /// own copy for every body it shows. Stack order 10 by default, after <see cref="LookPoseModifier"/>.
    /// </remarks>
    public class BoneChainDynamics : PoseModifier, IOriginShiftListener {
        /// <summary>Stack order new components get: after the look at 0.</summary>
        public const int DefaultOrder = 10;

        private const int MaxSubsteps = 8;
        private const float MinimumLength = 1e-5f;

        [Tooltip("Bone names, root link first, each a descendant of the one before it.")]
        [SerializeField] private string[] chain = new string[0];

        [Tooltip("Spring rate pulling each tip toward its animated place, per second squared.")]
        [SerializeField] private float stiffness = 200f;

        [Tooltip("How fast a tip's speed relative to the tip before it dies away, per second.")]
        [SerializeField] private float damping = 8f;

        [Tooltip("Downward pull on every tip, metres per second squared.")]
        [SerializeField] private float gravity = 2f;

        [Tooltip("Farthest a link may swing off its animated direction, degrees.")]
        [SerializeField] private float maxBendDegrees = 45f;

        [Tooltip("Longest simulation step, seconds; a frame is split into equal steps no longer than this.")]
        [SerializeField] private float substep = 1f / 120f;

        [Tooltip("Tip of the last link in its own frame; zero takes the link before it's offset, read in the last link's frame.")]
        [SerializeField] private Vector3 endOffset;

        [Tooltip("An anchor jump longer than this in one frame (metres) is a teleport: the chain starts over.")]
        [SerializeField] private float teleportDistance = 1f;

        private Transform[] _links = new Transform[0];
        private Vector3[] _tipLocal = new Vector3[0];
        private Vector3[] _positions = new Vector3[0];
        private Vector3[] _velocities = new Vector3[0];
        private Vector3[] _animated = new Vector3[0];
        private Transform _actorRoot;
        private Transform _frame;
        private Vector3 _anchor;
        private bool _isRunning;
        private bool _isRegistered;

        public BoneChainDynamics() : base(DefaultOrder) {
        }

        /// <summary>Spring rate pulling each tip toward its animated place, per second squared.</summary>
        public float Stiffness => stiffness;

        /// <summary>How fast a tip's speed relative to the tip before it dies away, per second.</summary>
        public float Damping => damping;

        /// <summary>Downward pull on every tip, metres per second squared.</summary>
        public float Gravity => gravity;

        /// <summary>Farthest a link may swing off its animated direction, degrees.</summary>
        public float MaxBendDegrees => maxBendDegrees;

        /// <summary>The bound links, root first; empty before bind or when a name did not resolve.</summary>
        public int LinkCount => _links.Length;

        /// <summary>Bone names of the chain, root link first.</summary>
        public string[] Chain => (string[])chain.Clone();

        /// <summary>Sets the chain and the spring, e.g. from an assembler; takes effect at the next bind.</summary>
        public void Configure(string[] boneNames, float springRate, float dampingRate, float pull, float maxBend) {
            chain = boneNames ?? new string[0];
            stiffness = springRate;
            damping = dampingRate;
            gravity = pull;
            maxBendDegrees = maxBend;
        }

        /// <summary>The bound link at <paramref name="index"/>, root first.</summary>
        public Transform Link(int index) {
            return _links[index];
        }

        /// <summary>World position of a link's tip as the bones stand now (after this layer when it ran).</summary>
        public Vector3 TipPosition(int index) {
            return _links[index].TransformPoint(_tipLocal[index]);
        }

        /// <summary>Drops the swing: the next frame starts from the animated pose, at rest. Call on a teleport.</summary>
        public void ResetChain() {
            _isRunning = false;
        }

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actorRoot = actorRoot;
            _isRunning = false;
            _links = ResolveLinks(bones);
            int count = _links.Length;
            _tipLocal = new Vector3[count];
            _positions = new Vector3[count];
            _velocities = new Vector3[count];
            _animated = new Vector3[count];
            for (int index = 0; index < count; index++) {
                _tipLocal[index] = ResolveTip(index);
            }
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            if (_links.Length == 0 || _actorRoot == null || deltaTime <= 0f) return;
            if (Weight <= 0f) {
                _isRunning = false;
                return;
            }

            Transform frame = _actorRoot.parent;
            if (!ReferenceEquals(frame, _frame)) ChangeFrame(frame);

            Vector3 anchor = ToFrame(_links[0].position);
            ReadAnimated();
            if (!_isRunning || (anchor - _anchor).magnitude > teleportDistance) StartAtRest(anchor);

            Simulate(deltaTime, _anchor, anchor);
            _anchor = anchor;
            Write(bones);
        }

        /// <inheritdoc />
        protected internal override void Skip() {
            _isRunning = false;
        }

        void IOriginShiftListener.OnOriginShifted(Vector3 delta) {
            if (!_isRunning || _frame != null) return;

            _anchor += delta;
            for (int index = 0; index < _positions.Length; index++) {
                _positions[index] += delta;
            }
        }

        private void OnEnable() {
            if (!Application.isPlaying || _isRegistered) return;

            OriginShiftRegistry.RegisterListener(this);
            _isRegistered = true;
        }

        private void OnDisable() {
            if (!_isRegistered) return;

            OriginShiftRegistry.UnregisterListener(this);
            _isRegistered = false;
        }

        private Transform[] ResolveLinks(PoseBones bones) {
            if (chain == null || chain.Length == 0) return new Transform[0];

            var links = new Transform[chain.Length];
            for (int index = 0; index < chain.Length; index++) {
                links[index] = bones.FindByName(chain[index]);
                if (links[index] == null) return new Transform[0];
            }

            return links;
        }

        // A link's tip in its own frame: the next link's head, or for the last link the end offset (by default
        // the link before it's tip offset, which a rig whose bones share one axis convention lays along the last
        // link at the same length; a lone link uses its own offset from its parent).
        private Vector3 ResolveTip(int index) {
            if (index < _links.Length - 1) return _links[index].InverseTransformPoint(_links[index + 1].position);
            if (endOffset != Vector3.zero) return endOffset;
            if (index > 0) return _tipLocal[index - 1];

            return _links[index].localPosition;
        }

        // A new carrier (or none): the simulated tips keep their place relative to the actor, at rest relative
        // to the new frame; a frame destroyed since the last step leaves nothing to convert from.
        private void ChangeFrame(Transform frame) {
            Transform previous = _frame;
            _frame = frame;
            if (!_isRunning) return;

            bool previousGone = !ReferenceEquals(previous, null) && previous == null;
            if (previousGone) {
                _isRunning = false;
                return;
            }

            for (int index = 0; index < _positions.Length; index++) {
                Vector3 world = previous != null ? previous.TransformPoint(_positions[index]) : _positions[index];
                _positions[index] = ToFrame(world);
                _velocities[index] = Vector3.zero;
            }

            _anchor = ToFrame(previous != null ? previous.TransformPoint(_anchor) : _anchor);
        }

        private void ReadAnimated() {
            for (int index = 0; index < _links.Length; index++) {
                _animated[index] = ToFrame(TipPosition(index));
            }
        }

        private void StartAtRest(Vector3 anchor) {
            for (int index = 0; index < _links.Length; index++) {
                _positions[index] = _animated[index];
                _velocities[index] = Vector3.zero;
            }

            _anchor = anchor;
            _isRunning = true;
        }

        private void Simulate(float deltaTime, Vector3 anchorFrom, Vector3 anchorTo) {
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / Mathf.Max(substep, 1e-4f) - 1e-3f), 1, MaxSubsteps);
            float step = deltaTime / steps;
            Vector3 anchorVelocity = (anchorTo - anchorFrom) / deltaTime;
            Vector3 pull = ToFrameDirection(Vector3.down) * gravity;
            for (int index = 0; index < steps; index++) {
                Vector3 before = Vector3.Lerp(anchorFrom, anchorTo, (float)index / steps);
                Vector3 after = Vector3.Lerp(anchorFrom, anchorTo, (index + 1f) / steps);
                StepLinks(before, after, anchorTo, anchorVelocity, pull, step);
            }
        }

        // One step down the chain: each tip springs toward its parent tip plus the link's animated offset (both
        // read at the start of the step, so a steady carry leaves no lag), damped against the parent tip's speed,
        // then moves and is held at the link's length from the moved parent within the bend limit.
        private void StepLinks(Vector3 anchorBefore, Vector3 anchorAfter, Vector3 animatedAnchor, Vector3 anchorVelocity, Vector3 pull, float step) {
            Vector3 parentBefore = anchorBefore;
            Vector3 parent = anchorAfter;
            Vector3 parentVelocity = anchorVelocity;
            Vector3 animatedParent = animatedAnchor;
            for (int index = 0; index < _links.Length; index++) {
                Vector3 link = _animated[index] - animatedParent;
                Vector3 before = _positions[index];
                Vector3 acceleration = pull + stiffness * (parentBefore + link - before) - damping * (_velocities[index] - parentVelocity);
                Vector3 velocity = _velocities[index] + acceleration * step;
                Vector3 moved = Constrain(parent, before + velocity * step, link);
                _velocities[index] = (moved - before) / step;
                _positions[index] = moved;
                parentBefore = before;
                parent = moved;
                parentVelocity = _velocities[index];
                animatedParent = _animated[index];
            }
        }

        private Vector3 Constrain(Vector3 parent, Vector3 point, Vector3 link) {
            float length = link.magnitude;
            if (length < MinimumLength) return parent;

            Vector3 direction = point - parent;
            if (direction.sqrMagnitude < MinimumLength * MinimumLength) direction = link;
            if (Vector3.Angle(direction, link) > maxBendDegrees) {
                direction = Vector3.RotateTowards(link, direction, maxBendDegrees * Mathf.Deg2Rad, 0f);
            }

            return parent + direction.normalized * length;
        }

        // Root first, so each link turns from where its parent's turn left its head.
        private void Write(PoseBones bones) {
            for (int index = 0; index < _links.Length; index++) {
                Transform bone = _links[index];
                Vector3 head = bone.position;
                Vector3 current = TipPosition(index) - head;
                Vector3 wanted = FromFrame(_positions[index]) - head;
                if (current.sqrMagnitude < MinimumLength * MinimumLength || wanted.sqrMagnitude < MinimumLength * MinimumLength) continue;

                Quaternion turn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, wanted), Weight);
                bones.RotateWorldTo(bone, turn * bone.rotation);
            }
        }

        private Vector3 ToFrame(Vector3 world) {
            return _frame != null ? _frame.InverseTransformPoint(world) : world;
        }

        private Vector3 FromFrame(Vector3 local) {
            return _frame != null ? _frame.TransformPoint(local) : local;
        }

        private Vector3 ToFrameDirection(Vector3 world) {
            return _frame != null ? _frame.InverseTransformDirection(world) : world;
        }
    }
}
