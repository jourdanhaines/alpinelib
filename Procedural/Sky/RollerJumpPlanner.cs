using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Plans one <see cref="RollerPath"/>'s jumps: a leap over every obstacle and crest run of its sampled support, seeded
    /// bumps on the way (each spot fires with the path's chance), runs that overlap merged into one leap (a bump yields to
    /// an obstacle), one small bounce after each landing when it fits, and every arc raised until it clears the prop's
    /// contact height under it. A jump of height h spans the flight time of h at the jump wind's rolling speed.
    /// </summary>
    internal sealed class RollerJumpPlanner {
        private const double Gravity = 9.81;
        private const double ContactGap = 0.15;
        private const double ClearStep = 0.02;
        private const double LeastBounce = 0.01;

        private readonly RollerPath _path;
        private readonly RollerPathSettings _settings;
        private readonly SplitMix64 _random;

        public RollerJumpPlanner(RollerPath path, RollerPathSettings settings, SplitMix64 random) {
            _path = path;
            _settings = settings;
            _random = random;
            BumpChance = Lerp(settings.MinBumpChance, settings.MaxBumpChance, random.NextDouble());
        }

        public double BumpChance { get; }

        /// <summary>The path's jumps in order; returns the rolled length kept (cut short before an obstacle it cannot clear before its end).</summary>
        public double Plan(RollerObstacle[] obstacles, out RollerJump[] jumps) {
            var candidates = new List<RollerJump>();
            AddRuns(obstacles, candidates);
            AddBumps(candidates);
            candidates.Sort((first, second) => first.Takeoff.CompareTo(second.Takeoff));
            List<RollerJump> merged = Merge(candidates);
            double kept = Trim(merged);
            List<RollerJump> withBounces = AddBounces(merged, kept);
            jumps = new RollerJump[withBounces.Count];
            for (int index = 0; index < jumps.Length; index++) jumps[index] = Cleared(withBounces[index]);

            return kept;
        }

        /// <summary>Rolled metres a jump of <paramref name="height"/> spans: its flight time at the jump wind's rolling speed, no steeper than the slope limit.</summary>
        public double LengthOf(double height) {
            double speed = _settings.JumpWindSpeed * _path.GroundSpeedShare;
            double flight = 2.0 * speed * Math.Sqrt(2.0 * Math.Max(0.0, height) / Gravity);
            return Math.Max(Math.Max(flight, 4.0 * height / _settings.MaxJumpSlope), 2.0 * _settings.JumpLead + ContactGap);
        }

        private void AddRuns(RollerObstacle[] obstacles, List<RollerJump> candidates) {
            double step = _path.ProfileStep;
            for (int index = 0; index < obstacles.Length; index++) {
                if (obstacles[index] == RollerObstacle.None) continue;

                int first = index;
                RollerObstacle strongest = obstacles[index];
                while (index + 1 < obstacles.Length && obstacles[index + 1] != RollerObstacle.None) {
                    index++;
                    if (obstacles[index] > strongest) strongest = obstacles[index];
                }
                bool isObstacle = strongest == RollerObstacle.Obstacle;
                double share = _random.NextDouble();
                double height = isObstacle
                    ? Lerp(_settings.MinObstacleJump, _settings.MaxObstacleJump, share) * _path.Size
                    : Lerp(_settings.MinCrestJump, _settings.MaxCrestJump, share);
                double takeoff = first * step - _settings.JumpLead;
                double landing = Math.Max(index * step + _settings.JumpLead, takeoff + LengthOf(height));
                candidates.Add(new RollerJump(isObstacle ? RollerJumpKind.Obstacle : RollerJumpKind.Crest, takeoff, landing, height, 0.0, 0.0));
            }
        }

        private void AddBumps(List<RollerJump> candidates) {
            double spot = Lerp(_settings.MinBumpSpacing, _settings.MaxBumpSpacing, _random.NextDouble());
            while (spot < _path.Length) {
                bool fires = _random.NextDouble() < BumpChance;
                double height = Lerp(_settings.MinBumpJump, _settings.MaxBumpJump, _random.NextDouble());
                if (fires && height > 0.0) candidates.Add(new RollerJump(RollerJumpKind.Bump, spot, spot + LengthOf(height), height, 0.0, 0.0));

                spot += Lerp(_settings.MinBumpSpacing, _settings.MaxBumpSpacing, _random.NextDouble());
            }
        }

        // Overlapping (or nearly touching) jumps become one; a bump gives way to an obstacle or crest leap.
        private List<RollerJump> Merge(List<RollerJump> candidates) {
            var merged = new List<RollerJump>();
            foreach (RollerJump candidate in candidates) {
                while (merged.Count > 0 && merged[merged.Count - 1].Kind == RollerJumpKind.Bump && candidate.Kind != RollerJumpKind.Bump
                        && candidate.Takeoff < merged[merged.Count - 1].Landing + ContactGap) {
                    merged.RemoveAt(merged.Count - 1);
                }
                if (merged.Count == 0 || candidate.Takeoff >= merged[merged.Count - 1].Landing + ContactGap) {
                    merged.Add(candidate);
                    continue;
                }
                if (candidate.Kind == RollerJumpKind.Bump) continue;

                merged[merged.Count - 1] = Joined(merged[merged.Count - 1], candidate);
            }
            return merged;
        }

        private RollerJump Joined(RollerJump first, RollerJump second) {
            RollerJumpKind kind = first.Kind == RollerJumpKind.Obstacle || second.Kind == RollerJumpKind.Obstacle ? RollerJumpKind.Obstacle : RollerJumpKind.Crest;
            double height = Math.Max(first.Height, second.Height);
            double landing = Math.Max(Math.Max(first.Landing, second.Landing), first.Takeoff + LengthOf(height));
            return new RollerJump(kind, first.Takeoff, landing, height, 0.0, 0.0);
        }

        // A takeoff before the start leaves from the start; a leap that cannot land before the end cuts the roll short
        // at its takeoff (a bump there is dropped instead). Returns the rolled length kept.
        private double Trim(List<RollerJump> jumps) {
            double kept = _path.Length;
            for (int index = jumps.Count - 1; index >= 0; index--) {
                RollerJump jump = jumps[index];
                if (jump.Landing <= kept - _settings.JumpLead) break;

                jumps.RemoveAt(index);
                if (jump.Kind != RollerJumpKind.Bump) kept = Math.Max(0.0, jump.Takeoff);
            }
            if (jumps.Count > 0 && jumps[0].Takeoff < 0.0) {
                RollerJump first = jumps[0];
                jumps[0] = new RollerJump(first.Kind, 0.0, Math.Max(first.Landing, LengthOf(first.Height)), first.Height, 0.0, 0.0);
            }
            return kept;
        }

        private List<RollerJump> AddBounces(List<RollerJump> jumps, double kept) {
            var all = new List<RollerJump>();
            for (int index = 0; index < jumps.Count; index++) {
                RollerJump jump = jumps[index];
                all.Add(jump);
                double height = jump.Height * _settings.BounceShare;
                if (height < LeastBounce) continue;

                double landing = jump.Landing + LengthOf(height);
                double next = index + 1 < jumps.Count ? jumps[index + 1].Takeoff : kept - _settings.JumpLead;
                if (landing + ContactGap <= next) all.Add(new RollerJump(RollerJumpKind.Bounce, jump.Landing, landing, height, 0.0, 0.0));
            }
            return all;
        }

        // The jump with its contact heights at both ends and its arc raised wherever the prop's contact height under it,
        // plus the clearance toward the middle, would poke through.
        private RollerJump Cleared(RollerJump jump) {
            double takeoffY = Contact(jump.Takeoff);
            double landingY = Contact(jump.Landing);
            double height = jump.Height;
            double length = jump.Length;
            for (double distance = jump.Takeoff + ClearStep; distance < jump.Landing; distance += ClearStep) {
                double share = (distance - jump.Takeoff) / length;
                double arc = 4.0 * share * (1.0 - share);
                if (arc <= 1e-6) continue;

                double need = Contact(distance) - (takeoffY + (landingY - takeoffY) * share) + _settings.JumpClearance * arc;
                height = Math.Max(height, need / arc);
            }
            return new RollerJump(jump.Kind, jump.Takeoff, jump.Landing, height, takeoffY, landingY);
        }

        private double Contact(double distance) {
            return _path.ContactHeight(distance, _path.AngleAt(distance));
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
