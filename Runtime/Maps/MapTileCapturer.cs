using System;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AlpineLib.Maps {
    /// <summary>
    /// Renders a cell's <see cref="TopDownCaptureArea"/> into a square MSAA target under literal lighting,
    /// resolves it into a mipped sRGB texture and hands that to an <see cref="IMapTileSink"/>. The targets
    /// (≈ 38 MiB at 1024² MSAA 4) are created on demand and released after <see cref="IdleReleaseSeconds"/>
    /// without a capture. Does nothing on a null graphics device.
    /// </summary>
    public sealed class MapTileCapturer : IDisposable {
        /// <summary>Seconds without a capture before the targets are released.</summary>
        public const float IdleReleaseSeconds = 5f;

        private readonly int _size;
        private readonly int _msaaSamples;
        private Camera _camera;
        private RenderTexture _multisampled;
        private RenderTexture _resolved;
        private float _lastCaptureTime;

        /// <summary>A capturer of <paramref name="size"/>² tiles seeing <paramref name="cullingMask"/> over a transparent clear.</summary>
        public MapTileCapturer(int size = 1024, int msaaSamples = 4, int cullingMask = 1) {
            _size = size;
            _msaaSamples = Mathf.Max(1, msaaSamples);
            CullingMask = cullingMask;
        }

        /// <summary>Layers captured.</summary>
        public int CullingMask { get; set; }

        /// <summary>Background where nothing is drawn (alpha 0 lets the map's paper show).</summary>
        public Color Clear { get; set; } = new Color(0f, 0f, 0f, 0f);

        /// <summary>Light the tiles are drawn under.</summary>
        public CaptureLighting Lighting { get; set; } = CaptureLighting.Literal;

        /// <summary>Tile side, texels.</summary>
        public int Size => _size;

        /// <summary>False on a null graphics device.</summary>
        public bool CanCapture => CaptureRender.CanRender;

        /// <summary>True while the render targets are allocated.</summary>
        public bool HasTargets => _resolved != null;

        /// <summary>Captures <paramref name="area"/> as <paramref name="cell"/>'s tile into <paramref name="sink"/>; false when nothing rendered.</summary>
        public bool Capture(CellCoord cell, TopDownCaptureArea area, IMapTileSink sink, float time) {
            if (!CanCapture || sink == null) return false;

            EnsureTargets();
            CaptureCameraSetup.Configure(_camera, CullingMask, Clear);
            if (!TopDownCapture.Render(_camera, area, _multisampled, Lighting)) return false;

            RenderTexture active = RenderTexture.active;
            Graphics.Blit(_multisampled, _resolved);
            RenderTexture.active = active;
            _resolved.GenerateMips();
            sink.Accept(cell, _resolved);
            _lastCaptureTime = time;
            return true;
        }

        /// <summary>Releases the targets once idle for <see cref="IdleReleaseSeconds"/>.</summary>
        public void Tick(float time) {
            if (_resolved == null || time - _lastCaptureTime < IdleReleaseSeconds) return;

            ReleaseTargets();
        }

        /// <summary>Releases the targets and the camera.</summary>
        public void Dispose() {
            ReleaseTargets();
            if (_camera != null) CaptureRender.Destroy(_camera.gameObject);
            _camera = null;
        }

        private void EnsureTargets() {
            if (_camera == null) _camera = CaptureCameraSetup.Create("MapTileCaptureCamera");
            if (_resolved != null) return;

            var multisampled = new RenderTextureDescriptor(_size, _size, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt) {
                msaaSamples = _msaaSamples,
            };
            _multisampled = new RenderTexture(multisampled) { name = "MapTileCapture", hideFlags = HideFlags.DontSave };
            var resolved = new RenderTextureDescriptor(_size, _size, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.None) {
                useMipMap = true,
                autoGenerateMips = false,
            };
            _resolved = new RenderTexture(resolved) { name = "MapTileResolved", filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
            _multisampled.Create();
            _resolved.Create();
        }

        private void ReleaseTargets() {
            if (_multisampled != null) _multisampled.Release();
            if (_resolved != null) _resolved.Release();
            CaptureRender.Destroy(_multisampled);
            CaptureRender.Destroy(_resolved);
            _multisampled = null;
            _resolved = null;
        }
    }
}
