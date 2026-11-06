using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Appearance;
using AlpineLib.Procedural.Expressions;
using UnityEngine;

namespace AlpineLib.Face {
    /// <summary>
    /// Brings a character's worn face pieces to life: every <see cref="FacePoseSet"/> under this object gets a
    /// mixer stepped each frame from the body's <see cref="IFaceSignals"/>, its weights written to the piece's
    /// blendshapes. Wearing nothing with a pose set leaves the driver idle.
    /// </summary>
    /// <remarks>
    /// Pieces are found again whenever the <see cref="CharacterAppearance"/> rebuilds. Runs with the blendshape
    /// drivers, after the pose stack. A piece drawn as shadows only (first person) keeps stepping but is not
    /// written, so it is right again the frame it shows. Disabling the driver puts every piece back at rest.
    /// Each driver draws its own blink seed, so every client's faces blink on their own; pieces on one face
    /// share the seed and blink together. The signals are sampled every step, worn eyes or not, so their edges never
    /// go stale; a refresh mid-flight lets that flight pass unremarked (no worry, no landing). Play mode only; gates
    /// call <see cref="Refresh"/> and <see cref="Step"/>.
    /// </remarks>
    [DefaultExecutionOrder(ActorExecutionOrder.BlendShapeDrivers)]
    [DisallowMultipleComponent]
    public class FaceExpressionDriver : MonoBehaviour {
        [Tooltip("Appearance whose rebuilds refresh the pieces; this object's when empty.")]
        [SerializeField] private CharacterAppearance appearance;

        [Tooltip("Component implementing IFaceSignals; the first one on this object when empty.")]
        [SerializeField] private MonoBehaviour signalSource;

        private readonly List<FaceExpressionTarget> _targets = new List<FaceExpressionTarget>();
        private CharacterAppearance _subscribed;
        private IFaceSignals _signals;
        private bool _hasSignalsOverride;
        private bool _ignoringFlight;
        private uint _seed;

        /// <summary>Worn pieces being driven.</summary>
        public int TargetCount => _targets.Count;

        /// <summary>Signals read on the last step.</summary>
        public FaceSignalFrame LastSignals { get; private set; }

        /// <summary>Seed the blink rhythms draw from.</summary>
        public uint Seed => _seed;

        /// <summary>One driven piece.</summary>
        public FaceExpressionTarget TargetAt(int index) {
            return _targets[index];
        }

        /// <summary>Sets the appearance and the signal source, e.g. from an assembler; null picks this object's.</summary>
        public void Configure(CharacterAppearance source, MonoBehaviour signals) {
            appearance = source;
            signalSource = signals is IFaceSignals ? signals : null;
        }

        /// <summary>Replaces the signal source at runtime (a gate's script); null returns to the configured one.</summary>
        public void SetSignals(IFaceSignals signals) {
            _signals = signals;
            _hasSignalsOverride = signals != null;
        }

        /// <summary>Re-seeds the blink rhythms and rebuilds the mixers, so a gate can replay a run.</summary>
        public void SetSeed(uint seed) {
            _seed = seed != 0u ? seed : 1u;
            Refresh();
        }

        /// <summary>Finds the pose sets under this object and starts each from rest. Public so gates can force it.</summary>
        public void Refresh() {
            WriteRest();
            _targets.Clear();
            _ignoringFlight = true;
            if (_seed == 0u) _seed = (uint)Random.Range(1, int.MaxValue);

            foreach (FacePoseSet poseSet in GetComponentsInChildren<FacePoseSet>(true)) {
                if (poseSet.Target == null) continue;

                var target = new FaceExpressionTarget(poseSet, _seed);
                if (target.MissingShapes != null) Debug.LogWarning($"{nameof(FaceExpressionDriver)} on '{name}': '{poseSet.name}' lacks blendshape(s) {target.MissingShapes}.", this);

                _targets.Add(target);
            }
        }

        /// <summary>Samples the signals once and advances every piece by <paramref name="deltaTime"/> seconds.</summary>
        public void Step(float deltaTime) {
            LastSignals = SkipUnseenFlight(ResolveSignals()?.Sample(deltaTime) ?? default);
            if (_targets.Count == 0) return;

            FaceSignalFrame signals = LastSignals;
            foreach (FaceExpressionTarget target in _targets) {
                target.Mixer.Step(in signals, deltaTime);
                if (target.IsDrawn) target.Write(false);
            }
        }

        private void OnEnable() {
            Subscribe();
            Refresh();
        }

        private void OnDisable() {
            Unsubscribe();
            WriteRest();
            _targets.Clear();
        }

        private void LateUpdate() {
            Step(Time.deltaTime);
        }

        private void OnValidate() {
            if (signalSource != null && !(signalSource is IFaceSignals)) {
                Debug.LogWarning($"{nameof(FaceExpressionDriver)} on '{name}': '{signalSource.GetType().Name}' does not implement {nameof(IFaceSignals)}.", this);
                signalSource = null;
            }
        }

        // Until the body takes off or stands after a refresh, the flight under way began before these faces did.
        private FaceSignalFrame SkipUnseenFlight(FaceSignalFrame signals) {
            if (!_ignoringFlight) return signals;

            _ignoringFlight = signals.Airborne && !signals.Jumped;
            signals.Landed = false;
            if (_ignoringFlight) signals.AirTime = 0f;
            return signals;
        }

        private IFaceSignals ResolveSignals() {
            if (_hasSignalsOverride) return _signals;
            if (_signals != null) return _signals;

            _signals = signalSource as IFaceSignals ?? GetComponent<IFaceSignals>();
            return _signals;
        }

        // A destroyed piece leaves a null renderer, which Write skips.
        private void WriteRest() {
            foreach (FaceExpressionTarget target in _targets) target.Write(true);
        }

        private void Subscribe() {
            _subscribed = appearance != null ? appearance : GetComponent<CharacterAppearance>();
            if (_subscribed != null) _subscribed.PiecesChanged += Refresh;
        }

        private void Unsubscribe() {
            if (_subscribed != null) _subscribed.PiecesChanged -= Refresh;
            _subscribed = null;
        }
    }
}
