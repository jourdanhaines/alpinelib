using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Perception.Visibility {
    /// <summary>
    /// Hides an object while it sits outside the <see cref="VisibilityField"/>, crossfading its
    /// renderers in and out instead of popping them.
    /// </summary>
    /// <remarks>
    /// URP Lit only. Fading works by pushing every material of every child renderer into transparent
    /// mode by hand — it writes <c>_BaseColor</c>, <c>_Surface</c>, <c>_SrcBlend</c>, <c>_DstBlend</c>
    /// and <c>_ZWrite</c>, toggles the <c>_SURFACE_TYPE_TRANSPARENT</c> and <c>LOD_FADE_CROSSFADE</c>
    /// keywords, and drives <c>unity_LODFade</c> through a property block so shadows dither with the
    /// body. Materials that do not expose those properties and keywords (non-Lit URP shaders, HDRP,
    /// built-in) simply will not fade — nothing errors, the object just pops.
    /// <para>
    /// Reading <c>renderer.materials</c> instantiates every material this object uses, permanently
    /// and per object: expect the batching and memory cost of unique materials on anything that
    /// carries this component.
    /// </para>
    /// <para>
    /// With no field in the scene everything counts as visible, so the component is inert rather than
    /// hiding the world.
    /// </para>
    /// </remarks>
    public class VisibilityOccludable : MonoBehaviour {
        [Tooltip("Seconds to fade up to fully opaque when this object becomes visible")]
        [SerializeField] private float fadeInDuration = 1f;

        [Tooltip("Seconds to fade out to hidden when this object leaves the visible region")]
        [SerializeField] private float fadeOutDuration = 2f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceType = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int LodFade = Shader.PropertyToID("unity_LODFade");

        private Renderer[] _renderers;
        private Material[][] _materials;
        private MaterialPropertyBlock _propertyBlock;
        private Color[] _originalColors;
        private float _fadeProgress = 1f;
        private bool _isVisible = true;
        private bool _isFading;

        private void Start() {
            _renderers = GetComponentsInChildren<Renderer>();
            _materials = new Material[_renderers.Length][];
            _originalColors = new Color[_renderers.Length];
            _propertyBlock = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++) {
                _materials[i] = _renderers[i].materials;
                _originalColors[i] = _materials[i][0].GetColor(BaseColor);
            }

            _isVisible = VisibilityField.Instance == null || VisibilityField.Instance.IsVisible(transform.position);
            if (!_isVisible) {
                _fadeProgress = 0f;
                SetRenderersEnabled(false);
            }
        }

        private void LateUpdate() {
            bool wasVisible = _isVisible;
            _isVisible = VisibilityField.Instance == null || VisibilityField.Instance.IsVisible(transform.position);

            if (_isVisible && !wasVisible) {
                _fadeProgress = 0f;
                SetRenderersEnabled(true);
                SetFading(true);
                _isFading = true;
            } else if (!_isVisible && wasVisible) {
                SetFading(true);
                _isFading = true;
            }

            if (!_isFading) return;

            if (_isVisible) {
                _fadeProgress = Mathf.MoveTowards(_fadeProgress, 1f, Time.deltaTime / fadeInDuration);
                ApplyFade(_fadeProgress);

                if (_fadeProgress >= 1f) {
                    SetFading(false);
                    _isFading = false;
                }
            } else {
                _fadeProgress = Mathf.MoveTowards(_fadeProgress, 0f, Time.deltaTime / fadeOutDuration);
                ApplyFade(_fadeProgress);

                if (_fadeProgress <= 0f) {
                    SetRenderersEnabled(false);
                    SetFading(false);
                    _isFading = false;
                }
            }
        }

        private void ApplyFade(float alpha) {
            for (int i = 0; i < _renderers.Length; i++) {
                foreach (var material in _materials[i]) {
                    Color color = _originalColors[i];
                    color.a = alpha;
                    material.SetColor(BaseColor, color);
                }

                _propertyBlock.SetVector(LodFade, new Vector4(alpha, 0, 0, 0));
                _renderers[i].SetPropertyBlock(_propertyBlock);
            }
        }

        private void SetFading(bool isFading) {
            for (int i = 0; i < _renderers.Length; i++) {
                foreach (var material in _materials[i]) {
                    if (isFading) {
                        material.SetFloat(SurfaceType, 1);
                        material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                        material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                        material.SetFloat(ZWriteId, 0);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.EnableKeyword("LOD_FADE_CROSSFADE");
                    } else {
                        material.SetFloat(SurfaceType, 0);
                        material.SetFloat(SrcBlendId, (float)BlendMode.One);
                        material.SetFloat(DstBlendId, (float)BlendMode.Zero);
                        material.SetFloat(ZWriteId, 1);
                        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.DisableKeyword("LOD_FADE_CROSSFADE");

                        Color color = _originalColors[i];
                        color.a = 1f;
                        material.SetColor(BaseColor, color);
                    }
                }
            }
        }

        private void SetRenderersEnabled(bool isEnabled) {
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].enabled = isEnabled;
        }
    }
}
