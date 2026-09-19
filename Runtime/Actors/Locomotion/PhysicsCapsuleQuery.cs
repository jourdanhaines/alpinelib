using System;
using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// <see cref="ICapsuleCollisionQuery"/> answered by PhysX: capsule casts, overlaps and penetration
    /// against a layer mask, skipping the pawn's own collider.
    /// </summary>
    /// <remarks>
    /// The pawn's own capsule is in the world too — it is what triggers and other pawns collide with —
    /// and it sits at the render pose while the sweeps run from the simulated one, so it is filtered by
    /// reference rather than by layer. That is what lets the mask include the pawn layer, so pawns block
    /// each other. Triggers are always ignored; a trigger volume is never a wall.
    /// </remarks>
    public sealed class PhysicsCapsuleQuery : ICapsuleCollisionQuery {
        private const int BufferSize = 16;

        private readonly LayerMask _mask;
        private readonly Collider _self;
        private readonly Func<Rigidbody, bool> _isCarrierBody;
        private readonly RaycastHit[] _hits = new RaycastHit[BufferSize];
        private readonly Collider[] _overlaps = new Collider[BufferSize];

        /// <param name="mask">Layers the capsule collides with.</param>
        /// <param name="self">The pawn's own collider, skipped in every answer.</param>
        /// <param name="isCarrierBody">Whether a kinematic body a surface belongs to may be ridden.</param>
        public PhysicsCapsuleQuery(LayerMask mask, Collider self, Func<Rigidbody, bool> isCarrierBody) {
            _mask = mask;
            _self = self;
            _isCarrierBody = isCarrierBody;
        }

        /// <inheritdoc />
        public bool Sweep(in CapsuleShape shape, Vector3 foot, Vector3 direction, float distance, out CollisionSweepHit hit) {
            hit = default;
            int count = Physics.CapsuleCastNonAlloc(
                shape.SegmentBottom(foot), shape.SegmentTop(foot), shape.Radius, direction, _hits, distance, _mask,
                QueryTriggerInteraction.Ignore);

            int nearest = -1;
            for (int index = 0; index < count; index++) {
                if (_hits[index].collider == _self) continue;
                if (nearest >= 0 && _hits[index].distance >= _hits[nearest].distance) continue;

                nearest = index;
            }

            if (nearest < 0) return false;

            RaycastHit raycastHit = _hits[nearest];
            hit.Point = raycastHit.point;
            hit.Normal = raycastHit.normal;
            hit.Distance = raycastHit.distance;
            hit.Body = ResolveBody(raycastHit.collider);
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// The pawn's own collider is used as the probe shape, so the pawn's <c>CapsuleCollider</c> must
        /// match the motor's capsule. PhysX supports a triangle mesh only as the second geometry, which is
        /// why the pawn is always the first argument.
        /// </remarks>
        public bool TryResolveOverlap(in CapsuleShape shape, Vector3 foot, out Vector3 correction) {
            correction = Vector3.zero;
            int count = Physics.OverlapCapsuleNonAlloc(
                shape.SegmentBottom(foot), shape.SegmentTop(foot), shape.Radius, _overlaps, _mask,
                QueryTriggerInteraction.Ignore);

            bool overlapped = false;
            for (int index = 0; index < count; index++) {
                Collider other = _overlaps[index];
                if (other == _self) continue;

                Transform otherTransform = other.transform;
                if (!Physics.ComputePenetration(_self, foot, Quaternion.identity, other, otherTransform.position,
                        otherTransform.rotation, out Vector3 direction, out float depth)) continue;
                if (depth <= 0f) continue;

                correction += direction * depth;
                overlapped = true;
            }

            return overlapped;
        }

        /// <inheritdoc />
        public bool TryProbeSurface(Vector3 origin, float distance, out Vector3 normal) {
            normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, distance, _mask, QueryTriggerInteraction.Ignore);

            int nearest = -1;
            for (int index = 0; index < count; index++) {
                if (_hits[index].collider == _self) continue;
                if (nearest >= 0 && _hits[index].distance >= _hits[nearest].distance) continue;

                nearest = index;
            }

            if (nearest < 0) return false;

            normal = _hits[nearest].normal;
            return true;
        }

        private Transform ResolveBody(Collider collider) {
            Rigidbody body = collider.attachedRigidbody;
            if (body == null || !body.isKinematic) return null;
            if (_isCarrierBody != null && !_isCarrierBody(body)) return null;

            return body.transform;
        }
    }
}
