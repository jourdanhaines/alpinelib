using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The half-spaces a corner resolution is holding, each as a row on the total push from the moved position: one
    /// per shape, the latest contact with a shape replacing the earlier one, in first-met order.
    /// </summary>
    /// <remarks>
    /// A contact met after a push <c>p</c> with depth <c>d</c> and row <c>r</c> asks for <c>r · (x − p) ≥ d</c>, which
    /// is stored as <c>r · x ≥ d + r · p</c>. Statics key by shape index and movers by the complement of theirs, so
    /// the two never collide. Rows the current push clears by more than <see cref="InactiveClearance"/> are dropped
    /// before new contacts merge, so a face left behind cannot keep holding the pawn off where it is no longer near.
    /// </remarks>
    internal ref struct PawnCornerConstraints {
        /// <summary>
        /// Most rows held. Corners in practice put two to four shapes on a capsule; beyond eight the shallowest row
        /// gives way, which bounds one solve to 8 singles, 28 pairs and 56 triples.
        /// </summary>
        public const int Capacity = 8;

        /// <summary>A held row the push clears by more than this is no longer a contact and is dropped.</summary>
        private const float InactiveClearance = 0.0001f;

        private readonly Span<Vector3> rows;
        private readonly Span<float> bounds;
        private readonly Span<int> keys;

        public PawnCornerConstraints(Span<Vector3> rows, Span<float> bounds, Span<int> keys) {
            this.rows = rows;
            this.bounds = bounds;
            this.keys = keys;
            Count = 0;
        }

        /// <summary>How many rows are held.</summary>
        public int Count { get; private set; }

        /// <summary>The held rows.</summary>
        public ReadOnlySpan<Vector3> Rows => rows.Slice(0, Count);

        /// <summary>Drops the rows <paramref name="push"/> clears, then adds or refreshes a row for every contact met there.</summary>
        public void Merge(ReadOnlySpan<CollisionContact> contacts, in Vector3 push, bool startedGrounded) {
            DropInactive(in push);
            for (int index = 0; index < contacts.Length; index++) {
                MergeOne(in contacts[index], in push, startedGrounded);
            }
        }

        /// <summary>The shortest total push satisfying every held row.</summary>
        public bool TrySolve(out Vector3 push) {
            return PawnContactSolver.TrySolve(rows.Slice(0, Count), bounds.Slice(0, Count), out push);
        }

        /// <summary>Drops the rows <paramref name="push"/> clears by more than <see cref="InactiveClearance"/>.</summary>
        public void DropInactive(in Vector3 push) {
            int kept = 0;
            for (int index = 0; index < Count; index++) {
                float clearance = (float)(PawnContactSolver.Dot(in rows[index], in push) - bounds[index]);
                if (clearance > InactiveClearance) {
                    continue;
                }

                rows[kept] = rows[index];
                bounds[kept] = bounds[index];
                keys[kept] = keys[index];
                kept++;
            }

            Count = kept;
        }

        private void MergeOne(in CollisionContact contact, in Vector3 push, bool startedGrounded) {
            Vector3 row = PawnMotor.BuildCornerRow(in contact, startedGrounded);
            float bound = (float)(contact.Depth + PawnContactSolver.Dot(in row, in push));
            int key = contact.IsMover ? ~contact.MoverIndex : contact.ShapeIndex;
            int slot = IndexOf(key);
            if (slot < 0) {
                slot = FreeSlot(contact.Depth, in push);
            }

            // Full, and every held row is at least as deep as this contact: it is the one left out.
            if (slot < 0) {
                return;
            }

            rows[slot] = row;
            bounds[slot] = bound;
            keys[slot] = key;
        }

        // The next empty slot, or when full the shallowest row's when this contact is deeper than it, else none.
        private int FreeSlot(float depth, in Vector3 push) {
            int capacity = Math.Min(Capacity, rows.Length);
            if (Count < capacity) {
                Count++;
                return Count - 1;
            }

            int shallowest = -1;
            float shallowestDepth = depth;
            for (int index = 0; index < Count; index++) {
                float held = (float)(bounds[index] - PawnContactSolver.Dot(in rows[index], in push));
                if (held < shallowestDepth) {
                    shallowestDepth = held;
                    shallowest = index;
                }
            }

            return shallowest;
        }

        private int IndexOf(int key) {
            for (int index = 0; index < Count; index++) {
                if (keys[index] == key) {
                    return index;
                }
            }

            return -1;
        }
    }
}
