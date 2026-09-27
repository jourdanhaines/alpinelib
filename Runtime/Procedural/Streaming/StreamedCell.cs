using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using UnityEngine;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>One cell tracked by a <see cref="CellStreamer"/>, from queued to released.</summary>
    public sealed class StreamedCell {
        private readonly Stack<IEnumerator> _steps = new Stack<IEnumerator>();

        internal StreamedCell(CellCoord coord) {
            Coord = coord;
            State = StreamedCellState.Queued;
        }

        /// <summary>The cell's address.</summary>
        public CellCoord Coord { get; }

        /// <summary>Where the build is.</summary>
        public StreamedCellState State { get; internal set; }

        /// <summary>Parent of the cell's objects, at the cell centre under the streamer; null until instantiation starts.</summary>
        public Transform Root { get; internal set; }

        /// <summary>True once the streamer has dropped the cell; a builder holding it should stop.</summary>
        public bool IsReleased => State == StreamedCellState.Released;

        internal object Prepared { get; set; }

        internal Task<object> PrepareTask { get; set; }

        internal CancellationTokenSource Cancellation { get; set; }

        internal void BeginSteps(IEnumerator steps) {
            _steps.Clear();
            if (steps != null) _steps.Push(steps);
        }

        // One checkpoint of the build; false once every nested step has finished.
        internal bool StepBuild() {
            while (_steps.Count > 0) {
                IEnumerator top = _steps.Peek();
                if (!top.MoveNext()) {
                    DisposeStep(_steps.Pop());
                    continue;
                }

                if (top.Current is IEnumerator nested) _steps.Push(nested);
                return true;
            }

            return false;
        }

        // Disposing an abandoned iterator runs its finally blocks.
        internal void AbandonSteps() {
            while (_steps.Count > 0) {
                DisposeStep(_steps.Pop());
            }
        }

        internal void DisposePrepared() {
            object prepared = Prepared;
            Prepared = null;
            if (prepared is IDisposable disposable) disposable.Dispose();
        }

        private static void DisposeStep(IEnumerator step) {
            if (step is IDisposable disposable) disposable.Dispose();
        }
    }
}
