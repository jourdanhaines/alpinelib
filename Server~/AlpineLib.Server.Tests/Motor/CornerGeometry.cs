using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Synthetic acute corners for the motor tests, the shipped-like profile they are walked with, and the residual
    /// penetration a finished step leaves.
    /// </summary>
    public static class CornerGeometry {
        public const float TickSeconds = 1f / 30f;

        private const float WallLength = 40f;
        private const float WallThickness = 2f;
        private const float ApexOverlap = 1f;
        private const float WallHalfHeight = 1.5f;

        /// <summary>A game's player profile: 0.48 m radius, 1.4 m tall, 0.3 m step, 45° slope limit.</summary>
        public static MovementProfile ShippedProfile() {
            return new MovementProfile {
                DisplayName = "Player", WalkSlowSpeed = 2f, WalkSpeed = 4f, JogSpeed = 5.4f, SprintSpeed = 7f, CrouchSpeed = 1.6f, CrouchFastSpeed = 2.8f,
                Gravity = -20f, JumpVelocity = 6f, AirAcceleration = 16f, AirDrag = 0f, CapsuleRadius = 0.48f, CapsuleHeight = 1.4f, StepOffset = 0.3f,
                SlopeLimitDegrees = 45f,
            };
        }

        /// <summary>The deepest overlap of the capsule the motor collides with in <paramref name="state"/>.</summary>
        public static float Residual(CollisionWorld world, in PawnState state, MovementProfile profile, uint tick) {
            float lift = state.IsGrounded ? profile.StepOffset : 0f;
            var pose = new CapsulePose(state.Position + new Vector3(0f, lift, 0f), profile.CapsuleRadius, MathF.Max(profile.CapsuleHeight - lift, profile.CapsuleRadius * 2f));
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int count = world.CollectContacts(in pose, tick, contacts);
            float deepest = 0f;
            for (int index = 0; index < count; index++) deepest = MathF.Max(deepest, contacts[index].Depth);
            return deepest;
        }

        /// <summary>
        /// A floor and two tall prisms meeting at <paramref name="apex"/>, their wall normals
        /// <paramref name="normalDegrees"/> apart; the free wedge between them opens toward <paramref name="opening"/>.
        /// </summary>
        public static CollisionShape[] Wedge(double normalDegrees, Vector2 apex, Vector2 opening) {
            double halfInterior = (180.0 - normalDegrees) * 0.5 * Math.PI / 180.0;
            Vector2 first = Rotate(opening, halfInterior);
            Vector2 second = Rotate(opening, -halfInterior);
            return new[] {
                CollisionShape.MakePlane(0f),
                Wall(apex, first, second),
                Wall(apex, second, first),
            };
        }

        /// <summary>The position <paramref name="distance"/> out along the wedge's opening.</summary>
        public static Vector3 Mouth(Vector2 apex, Vector2 opening, float distance) {
            return new Vector3(apex.X + opening.X * distance, 0f, apex.Y + opening.Y * distance);
        }

        /// <summary>How far from the apex a capsule of <paramref name="radius"/> rests when wedged.</summary>
        public static float RestDistance(double normalDegrees, float radius) {
            double halfInterior = (180.0 - normalDegrees) * 0.5 * Math.PI / 180.0;
            return (float)(radius / Math.Sin(halfInterior));
        }

        /// <summary><paramref name="direction"/> turned counter-clockwise by <paramref name="radians"/> in the XZ plan.</summary>
        public static Vector2 Rotate(Vector2 direction, double radians) {
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vector2((float)(direction.X * cosine - direction.Y * sine), (float)(direction.X * sine + direction.Y * cosine));
        }

        /// <summary>
        /// A floor, a tall prism wall whose face is the plane z = 0 (solid on +z), and a box leaning against it: yawed
        /// <paramref name="yawDegrees"/> toward the wall and rolled <paramref name="tiltDegrees"/> about its long axis.
        /// </summary>
        public static CollisionShape[] LeaningBox(double yawDegrees, double tiltDegrees) {
            double yaw = yawDegrees * Math.PI / 180.0;
            double tilt = tiltDegrees * Math.PI / 180.0;
            var right = new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw));
            var across = new Vector3((float)-Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
            Vector3 up = Vector3.Normalize(Vector3.UnitY * (float)Math.Cos(tilt) + across * (float)Math.Sin(tilt));
            Vector3 forward = Vector3.Normalize(Vector3.Cross(right, up));
            var wallPlan = new[] { new Vector2(-8f, 0f), new Vector2(8f, 0f), new Vector2(8f, 3f), new Vector2(-8f, 3f) };
            return new[] {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakePrism(new Vector3(0f, 2f, 0f), 2f, PositiveWinding(wallPlan)),
                CollisionShape.MakeBox(new Vector3(2f, 0.4f, -1.4f), right, up, forward, new Vector3(3f, 0.7f, 0.6f)),
            };
        }

        // Inner face from just past the apex out along `along`, solid on the side away from `other`.
        private static CollisionShape Wall(Vector2 apex, Vector2 along, Vector2 other) {
            var outward = new Vector2(along.Y, -along.X);
            if (Vector2.Dot(outward, other) > 0f) outward = -outward;
            Vector2 start = apex - along * ApexOverlap;
            Vector2 end = apex + along * WallLength;
            var corners = new[] { start, end, end + outward * WallThickness, start + outward * WallThickness };
            Vector2 middle = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            for (int index = 0; index < corners.Length; index++) corners[index] -= middle;
            return CollisionShape.MakePrism(new Vector3(middle.X, WallHalfHeight, middle.Y), WallHalfHeight, PositiveWinding(corners));
        }

        // The prism builder wants a positive shoelace area; flip the order when the plan winds the other way.
        private static Vector2[] PositiveWinding(Vector2[] corners) {
            double area = 0.0;
            for (int index = 0; index < corners.Length; index++) {
                Vector2 current = corners[index];
                Vector2 next = corners[(index + 1) % corners.Length];
                area += (double)current.X * next.Y - (double)next.X * current.Y;
            }

            if (area < 0.0) Array.Reverse(corners);
            return corners;
        }
    }
}
