using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    // Rolls a RollingPropField's spawns one after another on a worker (or inline when synchronous), new spawns ahead
    // of rebuilds: each tries its spawn points in turn until one is clear, so a spawn's roll depends only on its seed,
    // its life and the world snapshot.
    internal sealed class RollingPropBuilds {
        private readonly object _gate = new object();
        private readonly Queue<RollingPropBuildRequest> _queue = new Queue<RollingPropBuildRequest>();
        private readonly Queue<RollingPropBuildRequest> _rebuilds = new Queue<RollingPropBuildRequest>();
        private readonly List<RollingPropBuildResult> _done = new List<RollingPropBuildResult>();
        private Task _worker;
        private RollerSpawner _spawner;
        private IRollerLooks _looks;

        public bool IsSynchronous { get; set; }

        /// <summary>Requests queued or running.</summary>
        public int Pending {
            get {
                lock (_gate) return _queue.Count + _rebuilds.Count + (_worker != null && !_worker.IsCompleted ? 1 : 0);
            }
        }

        public void Configure(RollerSpawner spawner, IRollerLooks looks) {
            Complete();
            Clear();
            _spawner = spawner;
            _looks = looks;
        }

        public void Enqueue(RollingPropBuildRequest request) {
            lock (_gate) (request.IsRebuild ? _rebuilds : _queue).Enqueue(request);
        }

        /// <summary>Runs the queue inline when synchronous, else starts the worker when it is idle.</summary>
        public void Kick() {
            if (IsSynchronous) {
                Complete();
                return;
            }

            lock (_gate) {
                if ((_queue.Count == 0 && _rebuilds.Count == 0) || (_worker != null && !_worker.IsCompleted)) return;

                RollerSpawner spawner = _spawner;
                IRollerLooks looks = _looks;
                _worker = Task.Run(() => RunQueue(spawner, looks));
            }
        }

        /// <summary>Waits for the worker, then rolls whatever is still queued here.</summary>
        public void Complete() {
            Task worker;
            lock (_gate) worker = _worker;
            worker?.Wait();
            RunQueue(_spawner, _looks);
        }

        public void Drain(List<RollingPropBuildResult> into) {
            lock (_gate) {
                into.AddRange(_done);
                _done.Clear();
            }
        }

        public void Clear() {
            lock (_gate) {
                _queue.Clear();
                _rebuilds.Clear();
                _done.Clear();
            }
        }

        /// <summary>
        /// The roll of <paramref name="spawn"/> over <paramref name="world"/> for <paramref name="life"/> metres, along the
        /// spawner's live wind (or its prevailing one): its first clear spawn point's, or null.
        /// </summary>
        public static RollingProp? Build(RollerSpawn spawn, RollerCellWorld world, double life, RollerSpawner spawner, IRollerLooks looks) {
            RollerLook look = looks.LookOf(spawn);
            double radius = look.Radius * spawn.Size;
            if (!(radius > 0.0)) return null;

            for (int attempt = 0; attempt < spawner.Spawn.PointTries; attempt++) {
                spawner.PointOf(spawn, attempt, out double x, out double z);
                RollerPath path = spawner.Clock != null
                    ? RollerPath.Build(spawn, x, z, radius, life, spawner.Clock, spawner.Paths, world, look.Shape)
                    : RollerPath.Build(spawn.Id, x, z, radius, spawn.Size, life, spawner.Prevailing, spawner.Paths, world, look.Shape);
                if (path != null) return new RollingProp(spawn, path, look.Kind, (float)spawn.Size);
            }
            return null;
        }

        private void RunQueue(RollerSpawner spawner, IRollerLooks looks) {
            if (spawner == null || looks == null) return;

            while (TryDequeue(out RollingPropBuildRequest request)) {
                RollingProp? prop = TryBuild(request, spawner, looks);
                lock (_gate) _done.Add(new RollingPropBuildResult(request.Spawn.Id, prop, request.Version, request.Generation, request.IsRebuild));
            }
        }

        private bool TryDequeue(out RollingPropBuildRequest request) {
            lock (_gate) {
                Queue<RollingPropBuildRequest> queue = _queue.Count > 0 ? _queue : _rebuilds;
                bool hasRequest = queue.Count > 0;
                request = hasRequest ? queue.Dequeue() : default;
                return hasRequest;
            }
        }

        // A build that throws costs its prop only.
        private static RollingProp? TryBuild(RollingPropBuildRequest request, RollerSpawner spawner, IRollerLooks looks) {
            try {
                return Build(request.Spawn, request.World, request.Life, spawner, looks);
            } catch (Exception exception) {
                Debug.LogException(exception);
                return null;
            }
        }
    }
}
