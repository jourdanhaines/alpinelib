using System;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Perception.Visibility {
    /// <summary>
    /// The single point the world is perceived from. Every frame it publishes its view cone and
    /// hearing circle as shader globals, keeps a ground-level quad centred on itself so the
    /// <c>AlpineLib/VisibilityDarken</c> shader can darken everything outside that region, and
    /// answers the matching CPU query through <see cref="IsVisible"/>.
    /// </summary>
    /// <remarks>
    /// One field exists at a time: the newest instance to start claims <see cref="Instance"/> and the
    /// globals it writes are process-wide, so a second field would fight the first for them.
    /// <para>
    /// Cone and circle are evaluated once per frame and cached, which is what keeps the CPU query and
    /// the darkening on screen agreeing with each other. Before the first <c>Update</c> the cached
    /// values are zero, so <see cref="IsVisible"/> reports nothing as visible; once the owner dies the
    /// field stops ticking and the query freezes at the last values it published.
    /// </para>
    /// <para>
    /// The globals are named <c>_AlpineVisibilitySource*</c> and are a contract shared with the
    /// shader: renaming one without the other silently leaves the shader reading zeroes.
    /// </para>
    /// </remarks>
    public class VisibilityField : ActorSubsystem {
        [Tooltip("Material the ground overlay is drawn with, normally AlpineLib/VisibilityDarken")]
        [SerializeField] private Material darkenMaterial;

        [Tooltip("Height the ground overlay sits at, above the ground plane")]
        [SerializeField] private float overlayHeight = 0.05f;

        [Tooltip("Edge length of the ground overlay quad; must cover everything the camera can see")]
        [SerializeField] private float overlaySize = 200f;

        [SerializeField] private float viewDistance = 15f;
        [SerializeField] private float viewAngle = 90f;
        [SerializeField] private float hearingRadius = 5f;

        private static readonly int SourcePositionId = Shader.PropertyToID("_AlpineVisibilitySourcePosition");
        private static readonly int SourceForwardId = Shader.PropertyToID("_AlpineVisibilitySourceForward");
        private static readonly int ViewDistanceId = Shader.PropertyToID("_AlpineVisibilitySourceViewDistance");
        private static readonly int ViewAngleCosineId = Shader.PropertyToID("_AlpineVisibilitySourceViewAngleCosine");
        private static readonly int HearingRadiusId = Shader.PropertyToID("_AlpineVisibilitySourceHearingRadius");
        private static readonly int VisibilityEnabledId = Shader.PropertyToID("_AlpineVisibilitySourceEnabled");

        /// <summary>
        /// The field currently driving the shader globals, or null while none is running.
        /// </summary>
        public static VisibilityField Instance { get; private set; }

        /// <summary>
        /// Overrides the serialized view distance when set. Evaluated every frame, so a game can feed
        /// it from a stat sheet or any other live source.
        /// </summary>
        public Func<float> ViewDistanceProvider;

        /// <summary>
        /// Overrides the serialized view angle when set. Evaluated every frame.
        /// </summary>
        public Func<float> ViewAngleProvider;

        /// <summary>
        /// Overrides the serialized hearing radius when set. Evaluated every frame.
        /// </summary>
        public Func<float> HearingRadiusProvider;

        /// <summary>
        /// How far this field can see. Reads <see cref="ViewDistanceProvider"/> when one is set.
        /// </summary>
        public float ViewDistance {
            get => ViewDistanceProvider != null ? ViewDistanceProvider() : viewDistance;
            set => viewDistance = value;
        }

        /// <summary>
        /// Full width of the view cone in degrees. Reads <see cref="ViewAngleProvider"/> when one is set.
        /// </summary>
        public float ViewAngle {
            get => ViewAngleProvider != null ? ViewAngleProvider() : viewAngle;
            set => viewAngle = value;
        }

        /// <summary>
        /// Radius of the circle that is visible in every direction regardless of facing. Reads
        /// <see cref="HearingRadiusProvider"/> when one is set.
        /// </summary>
        public float HearingRadius {
            get => HearingRadiusProvider != null ? HearingRadiusProvider() : hearingRadius;
            set => hearingRadius = value;
        }

        private float _currentViewDistance;
        private float _currentViewAngleCosine;
        private float _currentHearingRadius;
        private GameObject _overlayObject;

        protected override void Start() {
            base.Start();

            Instance = this;

            Shader.SetGlobalFloat(VisibilityEnabledId, 1f);
            CreateOverlay();
        }

        protected override void OnDestroy() {
            base.OnDestroy();

            if (Instance == this)
                Instance = null;

            Shader.SetGlobalFloat(VisibilityEnabledId, 0f);

            if (_overlayObject != null)
                Destroy(_overlayObject);
        }

        private void Update() {
            _currentViewDistance = ViewDistance;
            _currentHearingRadius = HearingRadius;
            _currentViewAngleCosine = Mathf.Cos(ViewAngle * 0.5f * Mathf.Deg2Rad);

            Shader.SetGlobalVector(SourcePositionId, transform.position);
            Shader.SetGlobalVector(SourceForwardId, transform.forward);
            Shader.SetGlobalFloat(ViewDistanceId, _currentViewDistance);
            Shader.SetGlobalFloat(ViewAngleCosineId, _currentViewAngleCosine);
            Shader.SetGlobalFloat(HearingRadiusId, _currentHearingRadius);

            if (_overlayObject != null) {
                Vector3 overlayPosition = transform.position;
                overlayPosition.y = overlayHeight;
                _overlayObject.transform.position = overlayPosition;
            }
        }

        /// <summary>
        /// Whether a world position falls inside the hearing circle or the view cone, using the values
        /// published to the shader this frame. Height is ignored; the test is flat in XZ.
        /// </summary>
        public bool IsVisible(Vector3 position) {
            Vector3 toTarget = position - transform.position;
            toTarget.y = 0;
            float distance = toTarget.magnitude;

            if (distance <= _currentHearingRadius) return true;

            if (distance <= _currentViewDistance) {
                Vector3 forward = transform.forward;
                forward.y = 0;
                forward.Normalize();
                float dot = Vector3.Dot(toTarget.normalized, forward);
                if (dot >= _currentViewAngleCosine) return true;
            }

            return false;
        }

        private void CreateOverlay() {
            _overlayObject = new GameObject("VisibilityOverlay");

            var meshFilter = _overlayObject.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh();

            var meshRenderer = _overlayObject.AddComponent<MeshRenderer>();
            meshRenderer.material = darkenMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private Mesh CreateQuadMesh() {
            float half = overlaySize * 0.5f;
            var mesh = new Mesh {
                name = "VisibilityOverlayMesh",
                vertices = new[] {
                    new Vector3(-half, 0, -half),
                    new Vector3(-half, 0, half),
                    new Vector3(half, 0, half),
                    new Vector3(half, 0, -half)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        protected override void OnOwnerDeath() {
            Shader.SetGlobalFloat(VisibilityEnabledId, 0f);

            if (_overlayObject != null)
                _overlayObject.SetActive(false);

            base.OnOwnerDeath();
        }
    }
}
