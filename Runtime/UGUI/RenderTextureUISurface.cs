using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AlpineLib.UI {
    /// <summary>
    /// Renders a uGUI canvas into a render texture and shows that texture on a world mesh, so a menu can
    /// be drawn onto a held prop rather than over the screen.
    /// </summary>
    /// <remarks>
    /// The canvas camera must be a sibling of the canvas, never its ancestor or descendant: a
    /// screen-space-camera canvas repositions itself relative to its camera, so nesting either way
    /// feeds the transform back into itself. Pointer input reaches the canvas through
    /// <see cref="MeshUVPointerRaycaster"/>; any <see cref="GraphicRaycaster"/> left on the canvas is
    /// disabled because it would hit-test raw screen positions against a canvas nobody sees.
    ///
    /// The texture is never created explicitly — the camera allocates it on first render — and on a
    /// null graphics device (batchmode -nographics) nothing renders at all, so headless gates that
    /// treat every logged error as a failure stay quiet.
    /// </remarks>
    public class RenderTextureUISurface : MonoBehaviour {
        [Tooltip("Canvas drawn into the texture. Switched to ScreenSpaceCamera on Awake.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Camera that draws the canvas. Must be a sibling of the canvas, not its parent or child.")]
        [SerializeField] private Camera canvasCamera;

        [Tooltip("Optional full-canvas background. Left empty, it adopts the bound renderer's own texture.")]
        [SerializeField] private RawImage background;

        [Tooltip("Texture size in pixels. Match the aspect of the surface's UV layout.")]
        [SerializeField] private Vector2Int resolution = new Vector2Int(1536, 1024);

        [Tooltip("Material texture property the render texture replaces on the bound renderer.")]
        [SerializeField] private string texturePropertyName = "_BaseMap";

        [Tooltip("Material slot on the bound renderer that shows the canvas.")]
        [SerializeField] private int materialIndex;

        [Tooltip("Copy the bound material's texture into an empty background, so the canvas draws over the original artwork.")]
        [SerializeField] private bool adoptRendererTextureAsBackground = true;

        [Tooltip("Anisotropic filtering level of the render texture; the surface is usually seen at an angle.")]
        [SerializeField] private int anisoLevel = 8;

        [Tooltip("Colour the canvas camera clears to before drawing.")]
        [SerializeField] private Color clearColor = new Color(0.10f, 0.36f, 0.67f, 1f);

        private RenderTexture _texture;
        private Renderer _boundRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private bool _isConfigured;
        private bool _isMisconfigured;

        /// <summary>The render texture the canvas draws into; null until <c>Awake</c> or when misconfigured.</summary>
        public RenderTexture Texture => _texture;

        public Canvas Canvas => canvas;

        public Camera CanvasCamera => canvasCamera;

        /// <summary>True while the canvas is enabled and asked to render (see <see cref="SetRendering"/>).</summary>
        public bool IsRendering { get; private set; }

        private static bool IsNullDevice => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private void Awake() {
            Configure();
            SetRendering(false);
        }

        /// <summary>
        /// Shows the canvas texture on <paramref name="target"/>'s material slot through a property block,
        /// leaving the shared material untouched.
        /// </summary>
        public void Bind(Renderer target) {
            Configure();
            if (target == null || _texture == null) return;

            if (_boundRenderer != null && _boundRenderer != target) Unbind();

            AdoptBackgroundFrom(target);

            _propertyBlock ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_propertyBlock, materialIndex);
            _propertyBlock.SetTexture(texturePropertyName, _texture);
            target.SetPropertyBlock(_propertyBlock, materialIndex);
            _boundRenderer = target;
        }

        /// <summary>Clears the property block from the bound renderer, restoring its own material.</summary>
        public void Unbind() {
            if (_boundRenderer != null) _boundRenderer.SetPropertyBlock(null, materialIndex);

            _boundRenderer = null;
        }

        /// <summary>
        /// Turns canvas and camera on or off together; a closed surface costs nothing per frame. The
        /// camera stays off on a null graphics device.
        /// </summary>
        public void SetRendering(bool rendering) {
            Configure();
            IsRendering = rendering && !_isMisconfigured;

            if (canvas != null) canvas.enabled = IsRendering;
            if (canvasCamera != null) canvasCamera.enabled = IsRendering && !IsNullDevice;
        }

        /// <summary>Lays out the canvas and renders it into the texture immediately. No-op on a null device.</summary>
        public void RenderNow() {
            Configure();
            if (_isMisconfigured || IsNullDevice) return;

            Canvas.ForceUpdateCanvases();
            canvasCamera.Render();
        }

        /// <summary>Maps a 0–1 texture coordinate to a pixel position on a canvas of <paramref name="pixelSize"/>.</summary>
        public static Vector2 UVToPixel(Vector2 uv, Vector2 pixelSize) {
            return Vector2.Scale(uv, pixelSize);
        }

        /// <summary>
        /// One-time setup: validates the camera/canvas pairing, points both at the texture, and disables
        /// stray raycasters. Idempotent so callers reaching the surface before its <c>Awake</c> still work.
        /// </summary>
        private void Configure() {
            if (_isConfigured) return;

            _isConfigured = true;
            _isMisconfigured = !ValidateHierarchy();
            if (_isMisconfigured) return;

            _texture = CreateTexture();
            ConfigureCamera();
            ConfigureCanvas();
            DisableGraphicRaycasters();
        }

        private bool ValidateHierarchy() {
            if (canvas == null || canvasCamera == null) {
                Debug.LogError($"{nameof(RenderTextureUISurface)} on '{name}' needs both a canvas and a canvas camera.", this);
                return false;
            }

            Transform cameraTransform = canvasCamera.transform;
            Transform canvasTransform = canvas.transform;
            if (!cameraTransform.IsChildOf(canvasTransform) && !canvasTransform.IsChildOf(cameraTransform)) return true;

            Debug.LogError($"{nameof(RenderTextureUISurface)} on '{name}': camera '{canvasCamera.name}' and canvas '{canvas.name}' must be siblings; nesting either under the other feeds the canvas transform back into itself.", this);
            return false;
        }

        /// <summary>Allocates the texture object only; the camera creates the GPU resource on first render.</summary>
        private RenderTexture CreateTexture() {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
                Mathf.Max(resolution.x, 1), Mathf.Max(resolution.y, 1), RenderTextureFormat.ARGB32, 24) {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = true,
                autoGenerateMips = true,
                msaaSamples = 1,
            };

            return new RenderTexture(descriptor) {
                name = $"{name}_UISurface",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = anisoLevel,
            };
        }

        private void ConfigureCamera() {
            canvasCamera.orthographic = true;
            canvasCamera.clearFlags = CameraClearFlags.SolidColor;
            canvasCamera.backgroundColor = clearColor;
            canvasCamera.cullingMask = 1 << canvas.gameObject.layer;
            canvasCamera.depth = -100f;
            canvasCamera.targetTexture = _texture;
        }

        private void ConfigureCanvas() {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = canvasCamera;
            canvas.planeDistance = 1f;
        }

        private void DisableGraphicRaycasters() {
            foreach (GraphicRaycaster raycaster in canvas.GetComponentsInChildren<GraphicRaycaster>(true)) {
                if (!raycaster.enabled) continue;

                Debug.LogWarning($"{nameof(RenderTextureUISurface)} on '{name}': disabling GraphicRaycaster on '{raycaster.name}'; pointer input reaches this canvas through {nameof(MeshUVPointerRaycaster)}.", raycaster);
                raycaster.enabled = false;
            }
        }

        private void AdoptBackgroundFrom(Renderer target) {
            if (!adoptRendererTextureAsBackground || background == null || background.texture != null) return;

            Material[] materials = target.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length) return;

            Material material = materials[materialIndex];
            if (material == null || !material.HasProperty(texturePropertyName)) return;

            background.texture = material.GetTexture(texturePropertyName);
        }

        private void OnDestroy() {
            Unbind();
            if (_texture == null) return;

            if (canvasCamera != null && canvasCamera.targetTexture == _texture) canvasCamera.targetTexture = null;

            _texture.Release();
            Destroy(_texture);
            _texture = null;
        }
    }
}
