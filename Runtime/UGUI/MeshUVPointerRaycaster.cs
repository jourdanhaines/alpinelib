using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlpineLib.UI {
    /// <summary>
    /// Event-system raycaster for a canvas drawn onto a mesh by <see cref="RenderTextureUISurface"/>:
    /// casts the pointer ray at the mesh collider, turns the hit's texture coordinate into a canvas
    /// pixel, and hit-tests the canvas graphics there.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphicRaycaster"/> cannot be reused with a remapped point — it reads only the event's
    /// screen position and rejects anything outside its own camera's viewport — so its graphic loop is
    /// reimplemented here on public API: raycast targets, culling, unbatched (depth −1) graphics,
    /// raycast padding, <see cref="Graphic.Raycast"/> (CanvasGroups and masks), sorted front to back.
    ///
    /// The ray is tested with <see cref="Collider.Raycast"/> on the one collider, never a physics
    /// query, so the surface can sit on a layer that collides with nothing. The collider must be a
    /// non-convex <see cref="MeshCollider"/> on a readable mesh, or the hit carries no texture coordinate.
    ///
    /// Event positions stay in the pointer camera's screen space, so press/hover, Submit and navigation
    /// work, but drag controls (Slider, Scrollbar, ScrollRect, text drag-select) do not. Overlay canvases
    /// outrank this raycaster, so any HUD graphic under the cursor must have raycastTarget off.
    /// </remarks>
    public class MeshUVPointerRaycaster : BaseRaycaster {
        [Tooltip("Surface whose canvas this raycaster hit-tests.")]
        [SerializeField] private RenderTextureUISurface surface;

        [Tooltip("Longest pointer ray, in metres, that can reach the surface.")]
        [SerializeField] private float maxDistance = 3f;

        private readonly List<Graphic> _hitGraphics = new List<Graphic>();
        private Camera _pointerCamera;
        private MeshCollider _surfaceCollider;
        private bool _canReadUv;
        private int _syncedFrame = -1;
        private Matrix4x4 _syncedPose;

        /// <summary>The camera pointer rays are cast from, null while no source is set.</summary>
        public override Camera eventCamera => _pointerCamera;

        public RenderTextureUISurface Surface => surface;

        /// <summary>Longest pointer ray, in metres, that can reach the surface.</summary>
        public float MaxDistance => maxDistance;

        /// <summary>Starts routing pointer rays from <paramref name="pointerCamera"/> onto <paramref name="surfaceCollider"/>.</summary>
        public void SetSource(Camera pointerCamera, MeshCollider surfaceCollider) {
            _pointerCamera = pointerCamera;
            _surfaceCollider = surfaceCollider;
            _canReadUv = false;
            _syncedFrame = -1;
            _syncedPose = default;
            if (surfaceCollider == null) return;

            Mesh mesh = surfaceCollider.sharedMesh;
            if (mesh == null || !mesh.isReadable) {
                Debug.LogWarning($"{nameof(MeshUVPointerRaycaster)}: collider '{surfaceCollider.name}' has no readable mesh; hits would carry no texture coordinate.", surfaceCollider);
                return;
            }

            _canReadUv = true;
            if (!surfaceCollider.convex) return;

            Debug.LogWarning($"{nameof(MeshUVPointerRaycaster)}: collider '{surfaceCollider.name}' is convex; convex hits carry no texture coordinate.", surfaceCollider);
        }

        /// <summary>Stops hit-testing until the next <see cref="SetSource"/>.</summary>
        public void ClearSource() {
            _pointerCamera = null;
            _surfaceCollider = null;
            _canReadUv = false;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList) {
            if (!_canReadUv || !HasLiveSource()) return;

            SyncMovedCollider();
            Ray ray = _pointerCamera.ScreenPointToRay(eventData.position);
            if (!_surfaceCollider.Raycast(ray, out RaycastHit hit, maxDistance)) return;

            Canvas canvas = surface.Canvas;
            Camera canvasCamera = surface.CanvasCamera;
            Vector2 canvasPixel = RenderTextureUISurface.UVToPixel(hit.textureCoord, canvasCamera.pixelRect.size);
            CollectGraphicHits(canvas, canvasCamera, canvasPixel, _hitGraphics);

            foreach (Graphic graphic in _hitGraphics) {
                resultAppendList.Add(new RaycastResult {
                    gameObject = graphic.gameObject,
                    module = this,
                    distance = hit.distance,
                    worldPosition = hit.point,
                    worldNormal = hit.normal,
                    screenPosition = eventData.position,
                    displayIndex = eventData.displayIndex,
                    index = resultAppendList.Count,
                    depth = graphic.depth,
                    sortingLayer = canvas.sortingLayerID,
                    sortingOrder = canvas.sortingOrder,
                });
            }

            _hitGraphics.Clear();
        }

        /// <summary>
        /// Screen position on the pointer camera that lands on <paramref name="canvasPixel"/>, for tests
        /// and tooling that need to aim the pointer at a control. False with no source, an unreadable
        /// mesh, or a point behind the camera.
        /// </summary>
        public bool TryGetScreenPoint(Vector2 canvasPixel, out Vector2 screenPoint) {
            screenPoint = default;
            if (!HasLiveSource()) return false;

            Vector2 pixelSize = surface.CanvasCamera.pixelRect.size;
            if (pixelSize.x <= 0f || pixelSize.y <= 0f) return false;

            Vector2 uv = new Vector2(canvasPixel.x / pixelSize.x, canvasPixel.y / pixelSize.y);
            if (!TryFindLocalPointAtUV(_surfaceCollider.sharedMesh, uv, out Vector3 localPoint)) return false;

            Vector3 worldPoint = _surfaceCollider.transform.TransformPoint(localPoint);
            Vector3 projected = _pointerCamera.WorldToScreenPoint(worldPoint);
            if (projected.z <= 0f) return false;

            screenPoint = projected;
            return true;
        }

        /// <summary>
        /// Mesh-local point whose UV0 is <paramref name="uv"/>, found by barycentric search over the
        /// triangles in submesh order, so the first (front) submesh wins over a back shell sharing its UVs.
        /// Requires a readable mesh.
        /// </summary>
        public static bool TryFindLocalPointAtUV(Mesh mesh, Vector2 uv, out Vector3 localPoint) {
            localPoint = default;
            if (mesh == null || !mesh.isReadable) return false;

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            if (uvs.Length != vertices.Length) return false;

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) {
                int[] triangles = mesh.GetTriangles(subMesh);
                if (TryFindInTriangles(triangles, vertices, uvs, uv, out localPoint)) return true;
            }

            return false;
        }

        /// <summary>
        /// A prop posed in <c>LateUpdate</c> (hand-held, following bones) has moved since physics last
        /// read transforms, and the event system raycasts in the next <c>Update</c>, before any fixed
        /// step syncs it; without this the ray would hit where the prop was a frame ago. At most once
        /// per frame, and only when the collider actually moved.
        /// </summary>
        private void SyncMovedCollider() {
            int frame = Time.frameCount;
            if (_syncedFrame == frame) return;

            _syncedFrame = frame;
            Matrix4x4 pose = _surfaceCollider.transform.localToWorldMatrix;
            if (pose == _syncedPose) return;

            _syncedPose = pose;
            Physics.SyncTransforms();
        }

        private bool HasLiveSource() {
            if (_pointerCamera == null || _surfaceCollider == null || surface == null) return false;
            if (!_surfaceCollider.enabled || !_surfaceCollider.gameObject.activeInHierarchy) return false;

            Canvas canvas = surface.Canvas;
            return canvas != null && canvas.isActiveAndEnabled && surface.CanvasCamera != null;
        }

        private static bool TryFindInTriangles(int[] triangles, Vector3[] vertices, Vector2[] uvs, Vector2 uv, out Vector3 localPoint) {
            const float edgeTolerance = -1e-5f;

            for (int corner = 0; corner + 2 < triangles.Length; corner += 3) {
                int first = triangles[corner];
                int second = triangles[corner + 1];
                int third = triangles[corner + 2];

                if (!TryBarycentric(uvs[first], uvs[second], uvs[third], uv, out Vector3 weights)) continue;
                if (weights.x < edgeTolerance || weights.y < edgeTolerance || weights.z < edgeTolerance) continue;

                localPoint = vertices[first] * weights.x + vertices[second] * weights.y + vertices[third] * weights.z;
                return true;
            }

            localPoint = default;
            return false;
        }

        /// <summary>Barycentric weights of <paramref name="point"/> in triangle A-B-C; false for a degenerate triangle.</summary>
        private static bool TryBarycentric(Vector2 cornerA, Vector2 cornerB, Vector2 cornerC, Vector2 point, out Vector3 weights) {
            weights = default;
            Vector2 edgeAB = cornerB - cornerA;
            Vector2 edgeAC = cornerC - cornerA;
            Vector2 toPoint = point - cornerA;

            float denominator = edgeAB.x * edgeAC.y - edgeAC.x * edgeAB.y;
            if (Mathf.Abs(denominator) < 1e-12f) return false;

            float weightB = (toPoint.x * edgeAC.y - edgeAC.x * toPoint.y) / denominator;
            float weightC = (edgeAB.x * toPoint.y - toPoint.x * edgeAB.y) / denominator;
            weights = new Vector3(1f - weightB - weightC, weightB, weightC);
            return true;
        }

        /// <summary>
        /// Faithful copy of <see cref="GraphicRaycaster"/>'s private graphic loop, fed a canvas pixel
        /// instead of the event's screen position. Results come back front to back.
        /// </summary>
        private static void CollectGraphicHits(Canvas canvas, Camera canvasCamera, Vector2 canvasPixel, List<Graphic> results) {
            IList<Graphic> graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);

            for (int index = 0; index < graphics.Count; index++) {
                Graphic graphic = graphics[index];
                if (IsHitBy(graphic, canvasCamera, canvasPixel)) results.Add(graphic);
            }

            results.Sort(CompareDepthDescending);
        }

        private static bool IsHitBy(Graphic graphic, Camera canvasCamera, Vector2 canvasPixel) {
            // Depth −1 means the canvas has not batched the graphic yet, so it is not actually drawn.
            if (!graphic.raycastTarget || graphic.canvasRenderer.cull || graphic.depth == -1) return false;

            if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, canvasPixel, canvasCamera, graphic.raycastPadding)) return false;

            if (canvasCamera.WorldToScreenPoint(graphic.rectTransform.position).z > canvasCamera.farClipPlane) return false;

            return graphic.Raycast(canvasPixel, canvasCamera);
        }

        private static int CompareDepthDescending(Graphic first, Graphic second) {
            return second.depth.CompareTo(first.depth);
        }
    }
}
