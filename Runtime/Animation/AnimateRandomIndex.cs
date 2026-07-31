using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Adds variety to looping animation: picks a random index into a blend tree at start and
    /// re-rolls it whenever a watched parameter crosses a threshold, so a character shows a
    /// different idle every time it stops moving.
    /// </summary>
    /// <remarks>
    /// The animator controller must expose <c>fieldName</c> as a float driving a blend tree of
    /// <c>count</c> variants, and <c>watchField</c> as the float to watch (usually <c>Speed</c>).
    /// </remarks>
    public class AnimateRandomIndex : MonoBehaviour {
        [SerializeField] private int count = 2;
        [SerializeField] private string fieldName = "IdleIndex";
        [SerializeField] private string watchField = "Speed";
        [SerializeField] private float watchThreshold = 0.1f;

        private Animator _animator;
        private int _fieldHash;
        private int _watchFieldHash;
        private bool _wasAboveThreshold;

        private void Start() {
            _animator = GetComponentInChildren<Animator>();
            _fieldHash = Animator.StringToHash(fieldName);
            _watchFieldHash = Animator.StringToHash(watchField);

            _animator.SetFloat(_fieldHash, Random.Range(0, count));
        }

        private void LateUpdate() {
            bool isAboveThreshold = _animator.GetFloat(_watchFieldHash) > watchThreshold;

            if (_wasAboveThreshold != isAboveThreshold)
                _animator.SetFloat(_fieldHash, Random.Range(0, count));

            _wasAboveThreshold = isAboveThreshold;
        }
    }
}
