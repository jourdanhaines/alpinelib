using System;
using AlpineLib.DI;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Origin;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using UnityEngine;
using Numerics = System.Numerics;

namespace AlpineLib.Origin {
    /// <summary>
    /// Carries a session's floating-origin rebase from <see cref="ClientReplication.OnOriginShifted"/> into
    /// the Unity side: the client collision world when this client owns it, game code through
    /// <see cref="OriginShifted"/>, then every root and listener in <see cref="OriginShiftRegistry"/> and a
    /// physics sync — all inside the network pump, before this frame's motor steps.
    /// </summary>
    /// <remarks>
    /// Follows the session service's live replication, binding ahead of the network pump so no shift can
    /// arrive unheard; <see cref="Bind"/> drives it by hand instead (gates, custom stacks). A collision
    /// world shared with a listen server or a scene-geometry cache is left alone: its owner rebases it.
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.NetworkService - 1)]
    [DisallowMultipleComponent]
    public sealed class FloatingOriginService : MonoBehaviour {
        private ISessionService _sessionService;
        private ClientReplication _boundReplication;
        private bool _isManuallyBound;
        private bool _ownsManualWorld;
        private bool _hasWarnedSharedWorld;

        /// <summary>
        /// Raised after the collision world moved and before roots and listeners do: previous origin, new
        /// origin, and what was added to every world-frame position. Game-owned placement (streamed cells,
        /// vehicles on a track) rebases here.
        /// </summary>
        public event Action<SessionOrigin, SessionOrigin, Vector3> OriginShifted;

        /// <summary>The origin the scene is in now.</summary>
        public SessionOrigin Origin { get; private set; } = SessionOrigin.Initial;

        /// <summary>The replication being followed, or null offline.</summary>
        public ClientReplication Replication => _boundReplication;

        /// <summary>
        /// Follows <paramref name="replication"/> instead of the session service's. With
        /// <paramref name="ownsCollisionWorld"/> its collision world is translated by every shift.
        /// </summary>
        public void Bind(ClientReplication replication, bool ownsCollisionWorld) {
            if (replication == null) throw new ArgumentNullException(nameof(replication));

            BindReplication(replication);
            _isManuallyBound = true;
            _ownsManualWorld = ownsCollisionWorld;
        }

        /// <summary>Stops following any replication; a manual binding returns to the session service's.</summary>
        public void Unbind() {
            UnbindReplication();
            _isManuallyBound = false;
            _ownsManualWorld = false;
        }

        /// <summary>
        /// Applies a shift that did not come through replication (offline tools): roots, listeners and a
        /// physics sync. The collision world and <see cref="Origin"/> are left alone.
        /// </summary>
        public void Shift(Vector3 delta) {
            OriginShiftRegistry.Apply(delta);
        }

        private void Start() {
            if (!Injector.HasInstance) return;

            Injector.Instance.TryResolve(out _sessionService);
        }

        private void Update() {
            if (_isManuallyBound) return;

            ClientReplication replication = _sessionService?.Replication;
            if (ReferenceEquals(replication, _boundReplication)) return;

            UnbindReplication();
            if (replication != null) BindReplication(replication);
        }

        private void OnDestroy() {
            UnbindReplication();
        }

        private void BindReplication(ClientReplication replication) {
            if (ReferenceEquals(replication, _boundReplication)) return;

            UnbindReplication();
            _boundReplication = replication;
            _boundReplication.OnOriginShifted += HandleOriginShifted;
            Origin = replication.Origin;
            _hasWarnedSharedWorld = false;

            if (!Origin.Equals(SessionOrigin.Initial)) {
                Debug.LogWarning($"FloatingOriginService::BindReplication->Bound at origin epoch {Origin.Epoch}; scene objects placed before binding were not rebased.");
            }
        }

        private void UnbindReplication() {
            if (_boundReplication == null) return;

            _boundReplication.OnOriginShifted -= HandleOriginShifted;
            _boundReplication = null;
            Origin = SessionOrigin.Initial;
        }

        private void HandleOriginShifted(SessionOrigin previous, SessionOrigin next, Numerics.Vector3 delta) {
            Vector3 unityDelta = delta.ToUnity();
            TranslateCollisionWorld(delta);
            Origin = next;
            OriginShifted?.Invoke(previous, next, unityDelta);
            OriginShiftRegistry.Apply(unityDelta);
        }

        private void TranslateCollisionWorld(Numerics.Vector3 delta) {
            CollisionWorld world = _boundReplication?.CollisionWorld;
            if (world == null) return;

            if (OwnsCollisionWorld()) {
                world.TranslateAll(delta);
                return;
            }

            if (world.IsFallback || _hasWarnedSharedWorld) return;

            _hasWarnedSharedWorld = true;
            Debug.LogWarning("FloatingOriginService::TranslateCollisionWorld->The client collision world is shared, so it stays in the previous origin; give the session a private world to predict across rebases.");
        }

        private bool OwnsCollisionWorld() {
            if (_isManuallyBound) return _ownsManualWorld;

            return _sessionService != null && _sessionService.OwnsCollisionWorld;
        }
    }
}
