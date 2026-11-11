using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The half-spaces a corner resolution has met so far, each as a row on the total push from the moved position:
    /// one per shape, the latest contact with a shape replacing the earlier one, in first-met order.
    /// </summary>
    /// <remarks>
    /// A contact met after a push <c>p</c> with depth <c>d</c> and row <c>r</c> asks for <c>r · (x − p) ≥ d</c>, which
    /// is stored as <c>r · x ≥ d + r · p</c>. Statics key by shape index and movers by the complement of theirs, so
    /// the two never collide.
    /// </remarks>
    internal ref struct PawnCornerConstraints {
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

        /// <summary>Adds or refreshes a row for every contact met at <paramref name="push"/>.</summary>
        public void Merge(ReadOnlySpan<CollisionContact> contacts, in Vector3 push, bool startedGrounded) {
            for (int index = 0; index < contacts.Length; index++) {
                MergeOne(in contacts[index], in push, startedGrounded);
            }
        }

        /// <summary>The shortest total push satisfying every held row.</summary>
        public bool TrySolve(out Vector3 push) {
            return PawnContactSolver.TrySolve(rows.Slice(0, Count), bounds.Slice(0, Count), out push);
        }

        private void MergeOne(in CollisionContact contact, in Vector3 push, bool startedGrounded) {
            int key = contact.IsMover ? ~contact.MoverIndex : contact.ShapeIndex;
            int slot = IndexOf(key);
            if (slot < 0 && Count >= rows.Length) {
                return;
            }

            if (slot < 0) {
                slot = Count;
                Count++;
            }

            Vector3 row = PawnMotor.BuildCornerRow(in contact, startedGrounded);
            rows[slot] = row;
            bounds[slot] = (float)(contact.Depth + PawnContactSolver.Dot(in row, in push));
            keys[slot] = key;
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
