using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlpineLib.Sky {
    /// <summary>
    /// Casts the drifting clouds' shadows on the ground through the sun's light cookie: it clears a square R8 texture
    /// to white and min-splats one soft disc per cloud footprint (<see cref="CloudField.CopyFootprints(Camera, List{CloudFootprint}, Matrix4x4, Vector2, float, float)"/>)
    /// in the sun's light space, centred on the drawing camera's ground point, then hands it to the main light.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cookie spans <see cref="CloudShadowSettings.CookieSize"/> metres; its centre (the cookie offset) is the
    /// camera's ground point in light space snapped to whole texels, so the shadows do not swim as the camera moves.
    /// A puff splats at its own light-space position, which is where its shadow falls on the ground along the sun,
    /// and fades out over the cookie's outer <see cref="CloudShadowSettings.EdgeShare"/> (beyond the cookie the clamped
    /// edge texels are white). A pipeline render re-splats only when the snapped offset, the sun or the camera changed or
    /// the last splat has served <see cref="ReuseRenders"/> more renders (clouds move a few centimetres a frame), and
    /// otherwise keeps the texture (<see cref="IsReused"/>).
    /// </para>
    /// <para>
    /// While the sun is off (a <c>CaptureEnvironmentScope</c> disables scene lights) the cookie is cleared to white
    /// and nothing splats, so map tiles and portraits stay clean. In play mode the sun keeps the cookie until this is
    /// disabled; in edit mode the sun's own cookie, size and offset are put back after every render, so a scene is
    /// never saved holding the runtime texture. A render whose only cameras are filtered out (captures, portraits)
    /// touches nothing, so in play mode the sun keeps the last game camera's cookie; harmless, since a
    /// <c>CaptureEnvironmentScope</c> turns the sun off. Sky geometry opts out with <c>_NO_LIGHT_COOKIE</c>.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CloudShadowCookie : MonoBehaviour {
        /// <summary>Most discs one instanced draw carries.</summary>
        public const int BatchSize = 1023;

        /// <summary>Renders a pipeline splat is kept for after its own before the cookie re-splats regardless.</summary>
        public const int ReuseRenders = 1;

        private static readonly int ShadowLightId = Shader.PropertyToID("_ShadowLight");
        private static readonly int EdgeShareId = Shader.PropertyToID("_EdgeShare");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");

        private readonly List<CloudFootprint> _footprints = new List<CloudFootprint>(1024);
        private readonly Matrix4x4[] _matrices = new Matrix4x4[BatchSize];
        private CloudField _field;
        private Light _sun;
        private UniversalAdditionalLightData _lightData;
        private Material _material;
        private CloudShadowSettings _settings;
        private RenderTexture _texture;
        private Mesh _quad;
        private CommandBuffer _command;
        private MaterialPropertyBlock _block;
        private Matrix4x4 _worldToLight;
        private Texture _savedCookie;
        private Vector2 _savedSize;
        private Vector2 _savedOffset;
        private bool _isApplied;
        private bool _isSubscribed;
        private bool _canReuse;
        private int _splatCamera;
        private int _rendersSinceSplat;

        /// <summary>The cookie texture, or null before <see cref="Configure"/>, after destroy and (edit mode) while disabled.</summary>
        public RenderTexture Texture => _texture;

        /// <summary>The light the cookie is put on.</summary>
        public Light Sun => _sun;

        public CloudShadowSettings Settings => _settings;

        /// <summary>Discs splatted by the last render.</summary>
        public int LastSplatCount { get; private set; }

        /// <summary>True when the last render only cleared the cookie (the sun was off).</summary>
        public bool IsCleared { get; private set; }

        /// <summary>The cookie's centre in the sun's light space (metres, texel-snapped) at the last render.</summary>
        public Vector2 CookieOffset { get; private set; }

        /// <summary>True while the cookie sits on the sun.</summary>
        public bool IsApplied => _isApplied;

        /// <summary>True when the last <see cref="PrepareFrame"/> kept the previous splat instead of recording a new one.</summary>
        public bool IsReused { get; private set; }

        /// <summary>
        /// Shadows <paramref name="field"/>'s clouds through <paramref name="sun"/>'s cookie, splatted with
        /// <paramref name="splatMaterial"/> (<c>AlpineLib/Sky/CloudShadowSplat</c>, not owned) as <paramref name="settings"/> say.
        /// </summary>
        public void Configure(CloudField field, Light sun, Material splatMaterial, CloudShadowSettings settings) {
            if (splatMaterial == null) throw new ArgumentNullException(nameof(splatMaterial));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Validate();
            Restore();
            _canReuse = false;
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _sun = sun != null ? sun : throw new ArgumentNullException(nameof(sun));
            _lightData = sun.GetComponent<UniversalAdditionalLightData>();
            if (_lightData == null) Debug.LogWarning($"CloudShadowCookie::Configure->'{sun.name}' has no UniversalAdditionalLightData; its cookie cannot be sized, no cloud shadows.", this);
            _material = splatMaterial;
            _settings = settings;
            EnsureResources();
        }

        /// <summary>
        /// Splats the clouds <paramref name="camera"/> draws into the cookie now (or clears it while the sun is off) and
        /// returns the discs splatted. Puts nothing on the sun; renders do that themselves.
        /// </summary>
        public int Render(Camera camera) {
            return Execute(camera, null) ? LastSplatCount : 0;
        }

        /// <summary>
        /// Splats <paramref name="footprints"/> (instead of the field's clouds) into the cookie centred on
        /// <paramref name="camera"/>'s ground point now and returns the discs splatted; for gates and tools.
        /// </summary>
        public int Render(Camera camera, IReadOnlyList<CloudFootprint> footprints) {
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));

            return Execute(camera, footprints) ? LastSplatCount : 0;
        }

        /// <summary>
        /// Does what a pipeline render does for <paramref name="camera"/>: splats (or clears while the sun is off), then
        /// puts the cookie on the sun unless it only cleared. Returns the discs splatted. Disabling or destroying this
        /// gives the sun its own cookie back.
        /// </summary>
        public int RenderToSun(Camera camera) {
            if (!Execute(camera, null)) return 0;

            if (!IsCleared) Apply();
            return LastSplatCount;
        }

        /// <summary>
        /// Records the cookie's commands for <paramref name="camera"/> without running them (what a render does on the
        /// main thread before handing them to the pipeline) and returns the discs recorded, or −1 when not ready.
        /// </summary>
        public int Prepare(Camera camera) {
            _canReuse = false;
            return BuildCommands(camera, null) ? LastSplatCount : -1;
        }

        /// <summary>
        /// What a pipeline render records for <paramref name="camera"/> on the main thread: keeps the last frame splat
        /// (<see cref="IsReused"/>) while it may, else records a new one. Returns the discs the cookie holds, or −1 when not ready.
        /// </summary>
        public int PrepareFrame(Camera camera) {
            IsReused = CanReuse(camera);
            if (IsReused) {
                _rendersSinceSplat++;
                return LastSplatCount;
            }

            _canReuse = false;
            if (!BuildCommands(camera, null)) return -1;

            _canReuse = !IsCleared;
            _splatCamera = camera != null ? camera.GetInstanceID() : 0;
            _rendersSinceSplat = 0;
            return LastSplatCount;
        }

        // The last frame splat is for this camera, sun and snapped offset and has not served its renders yet.
        private bool CanReuse(Camera camera) {
            if (!_canReuse || !IsReady || camera == null || !_sun.isActiveAndEnabled || _texture == null || !_texture.IsCreated()) return false;
            if (camera.GetInstanceID() != _splatCamera || _rendersSinceSplat >= ReuseRenders) return false;

            Matrix4x4 worldToLight = WorldToLight(_sun.transform);
            return worldToLight == _worldToLight && SnappedOffset(camera.transform.position, worldToLight) == CookieOffset;
        }

        private bool Execute(Camera camera, IReadOnlyList<CloudFootprint> footprints) {
            _canReuse = false;
            if (!BuildCommands(camera, footprints)) return false;

            Graphics.ExecuteCommandBuffer(_command);
            _texture.IncrementUpdateCount();
            return true;
        }

        private bool IsReady => _field != null && _sun != null && _lightData != null && _material != null && _settings != null;

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginContextRendering += HandleBeginContext;
            RenderPipelineManager.endContextRendering += HandleEndContext;
            _isSubscribed = true;
        }

        // Edit mode frees the GPU resources here too (domain reloads skip OnDestroy); the next render rebuilds them.
        private void OnDisable() {
            Restore();
            if (!Application.isPlaying) ReleaseResources();
            if (!_isSubscribed) return;

            RenderPipelineManager.beginContextRendering -= HandleBeginContext;
            RenderPipelineManager.endContextRendering -= HandleEndContext;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            Restore();
            ReleaseResources();
        }

        // The first game camera that draws the sky (else a scene view) owns the cookie for this render.
        private void HandleBeginContext(ScriptableRenderContext context, List<Camera> cameras) {
            if (!IsReady) return;

            Camera camera = PickCamera(cameras);
            if (camera == null && _sun.isActiveAndEnabled) return;
            if (PrepareFrame(camera) < 0) return;

            if (!IsReused) {
                context.ExecuteCommandBuffer(_command);
                _texture.IncrementUpdateCount();
            }
            if (!IsCleared) Apply();
        }

        private void HandleEndContext(ScriptableRenderContext context, List<Camera> cameras) {
            if (!Application.isPlaying) Restore();
        }

        private Camera PickCamera(List<Camera> cameras) {
            Camera sceneView = null;
            foreach (Camera camera in cameras) {
                if (!SkyCameraFilter.ShouldDraw(camera, _field.Layer)) continue;
                if (camera.cameraType == CameraType.Game) return camera;

                sceneView = sceneView != null ? sceneView : camera;
            }

            return sceneView;
        }

        // Clears to white, then (sun on) splats every footprint inside the cookie (the field's, culled per cloud, unless
        // given in world space); false when nothing can render.
        private bool BuildCommands(Camera camera, IReadOnlyList<CloudFootprint> footprints) {
            if (!IsReady) return false;

            EnsureResources();
            _command.Clear();
            _command.SetRenderTarget(_texture);
            _command.ClearRenderTarget(false, true, Color.white);
            LastSplatCount = 0;
            IsCleared = !_sun.isActiveAndEnabled || camera == null;
            if (IsCleared) return true;

            // Plain matrices: the native side applies the platform's render-into-texture flip itself, as URP relies on.
            _command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            _worldToLight = WorldToLight(_sun.transform);
            CookieOffset = SnappedOffset(camera.transform.position, _worldToLight);
            bool isWorld = footprints != null;
            if (!isWorld) {
                _field.CopyFootprints(camera, _footprints, _worldToLight, CookieOffset, _settings.CookieSize * 0.5f, _settings.FootprintScale);
                footprints = _footprints;
            }

            int count = 0;
            for (int index = 0; index < footprints.Count; index++) {
                CloudFootprint footprint = footprints[index];
                Vector3 local = isWorld ? _worldToLight.MultiplyPoint3x4(footprint.Centre) : footprint.Centre;
                if (!TryPlace(local, footprint.Radius, out Matrix4x4 matrix)) continue;

                _matrices[count++] = matrix;
                if (count == BatchSize) count = Flush(count);
            }
            Flush(count);
            return true;
        }

        /// <summary>World to the sun's light space, unscaled like URP's main-light cookie matrix (x, y across the cookie).</summary>
        public static Matrix4x4 WorldToLight(Transform sun) {
            return Matrix4x4.TRS(sun.position, sun.rotation, Vector3.one).inverse;
        }

        // The camera's ground point in light space, snapped to whole texels.
        private Vector2 SnappedOffset(Vector3 cameraWorld, Matrix4x4 worldToLight) {
            Vector3 ground = new Vector3(cameraWorld.x, _field.GroundY, cameraWorld.z);
            Vector3 local = worldToLight.MultiplyPoint3x4(ground);
            float texel = _settings.TexelSize;
            return new Vector2(Mathf.Round(local.x / texel) * texel, Mathf.Round(local.y / texel) * texel);
        }

        // A disc at light-space centre local in cookie clip space ([−1, 1] across the cookie), or false when it lies wholly outside.
        private bool TryPlace(Vector3 local, float footprintRadius, out Matrix4x4 matrix) {
            float size = _settings.CookieSize;
            float clipX = 2f * (local.x - CookieOffset.x) / size;
            float clipY = 2f * (local.y - CookieOffset.y) / size;
            float radius = 2f * footprintRadius * _settings.FootprintScale / size;
            matrix = Matrix4x4.identity;
            if (Mathf.Abs(clipX) > 1f + radius || Mathf.Abs(clipY) > 1f + radius) return false;

            matrix.m00 = radius;
            matrix.m11 = radius;
            matrix.m03 = clipX;
            matrix.m13 = clipY;
            return true;
        }

        private int Flush(int count) {
            if (count == 0) return 0;

            _command.DrawMeshInstanced(_quad, 0, _material, 0, _matrices, count, _block);
            LastSplatCount += count;
            return 0;
        }

        private void Apply() {
            if (!_isApplied) {
                _savedCookie = _sun.cookie;
                _savedSize = _lightData.lightCookieSize;
                _savedOffset = _lightData.lightCookieOffset;
                _isApplied = true;
            }

            _sun.cookie = _texture;
            _lightData.lightCookieSize = new Vector2(_settings.CookieSize, _settings.CookieSize);
            _lightData.lightCookieOffset = CookieOffset;
        }

        // Puts the sun's own cookie, size and offset back.
        private void Restore() {
            if (!_isApplied) return;

            _isApplied = false;
            if (_sun != null) _sun.cookie = _savedCookie;
            if (_lightData == null) return;

            _lightData.lightCookieSize = _savedSize;
            _lightData.lightCookieOffset = _savedOffset;
        }

        private void EnsureResources() {
            if (_settings == null) return;

            _command ??= new CommandBuffer { name = "Cloud Shadow Cookie" };
            _block ??= new MaterialPropertyBlock();
            _block.SetFloat(ShadowLightId, _settings.ShadowLight);
            _block.SetFloat(EdgeShareId, _settings.EdgeShare);
            _block.SetFloat(SoftnessId, _settings.Softness);
            if (_quad == null) _quad = CreateQuad();
            if (_texture != null && _texture.width == _settings.Resolution) return;

            ReleaseTexture();
            _texture = new RenderTexture(_settings.Resolution, _settings.Resolution, GraphicsFormat.R8_UNorm, GraphicsFormat.None) {
                name = "Cloud Shadow Cookie",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
            };
            _texture.Create();
        }

        private void ReleaseResources() {
            ReleaseTexture();
            DestroyResource(_quad);
            _quad = null;
            _command?.Release();
            _command = null;
        }

        private void ReleaseTexture() {
            _canReuse = false;
            if (_texture == null) return;

            _texture.Release();
            DestroyResource(_texture);
            _texture = null;
        }

        private static Mesh CreateQuad() {
            var mesh = new Mesh { name = "Cloud Shadow Disc", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 1f));
            return mesh;
        }

        private static void DestroyResource(UnityEngine.Object target) {
            if (target == null) return;

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
