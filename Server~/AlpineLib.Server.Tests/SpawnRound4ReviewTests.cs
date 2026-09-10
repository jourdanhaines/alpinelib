using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Round-four sweep: every authored list length from one marker to twice the ring, taken far enough
    /// past the list that each marker sees sixty-four overflow rounds. The three promises the class
    /// remarks make are checked on every one of them at once.
    /// </summary>
    public sealed class SpawnRound4ReviewTests {
        private const uint AnyTick = 11u;

        /// <summary>Overflow rounds each marker is taken through in the sweep.</summary>
        private const int RoundsPerMarker = 64;

        /// <summary>The longest authored list the sweep covers.</summary>
        private const int LongestList = 32;

        /// <summary>How far the outermost seat may stand from its marker.</summary>
        private const float ReachMetres =
            ListSpawnPlacement.OverflowRadiusMetres * ListSpawnPlacement.OverflowRevolutions;

        /// <summary>Places a marker holds before its seats start over.</summary>
        private const int PromisedPlaces =
            ListSpawnPlacement.OverflowSeats * ListSpawnPlacement.OverflowRevolutions;

        /// <summary>
        /// Nothing a marker seats — first pass or overflow, any list length — stands further from that
        /// marker than the surface the remarks ask a scene to author around it.
        /// </summary>
        [Fact]
        public void NoArrivalOfAnyListLengthStandsFurtherThanTheRingsReach() {
            for (int markerCount = 1; markerCount <= LongestList; markerCount++) {
                SpawnPoint[] points = RowOf(markerCount);
                List<Vector3>[] byMarker = SweepMarkers(points);

                for (int marker = 0; marker < markerCount; marker++) {
                    IReadOnlyList<Vector3> seated = byMarker[marker];

                    for (int index = 0; index < seated.Count; index++) {
                        float reach = HorizontalDistance(seated[index], points[marker].Position);

                        Assert.True(
                            reach <= ReachMetres + 0.001f,
                            $"list of {markerCount}: marker {marker} seated arrival {index} {reach} m out, at {seated[index]}");
                    }
                }
            }
        }

        /// <summary>
        /// Every marker of every authored list length holds the full set of overflow places, and no more:
        /// sixteen distinct spots over sixty-four rounds, which is the claim REVIEW-3 disproved for the
        /// list lengths sharing a factor with the seat count.
        /// </summary>
        [Fact]
        public void EveryMarkerOfEveryListLengthHoldsExactlyThePromisedPlaces() {
            for (int markerCount = 1; markerCount <= LongestList; markerCount++) {
                SpawnPoint[] points = RowOf(markerCount);
                List<Vector3>[] byMarker = SweepMarkers(points);

                for (int marker = 0; marker < markerCount; marker++) {
                    IReadOnlyList<Vector3> overflow = OverflowOnly(byMarker[marker]);
                    int distinct = CountDistinct(overflow);

                    Assert.True(
                        distinct == PromisedPlaces,
                        $"list of {markerCount}: marker {marker} held {distinct} overflow places, not {PromisedPlaces}");
                }
            }
        }

        /// <summary>
        /// The places are not merely sixteen in number over the long run: no two arrivals at one marker
        /// share a spot inside a window of sixteen, so a lobby churning through a marker sixteen deep
        /// never stands two pawns on one place.
        /// </summary>
        [Fact]
        public void NoTwoArrivalsAtAMarkerShareASpotWithinSixteen() {
            for (int markerCount = 1; markerCount <= LongestList; markerCount++) {
                SpawnPoint[] points = RowOf(markerCount);
                List<Vector3>[] byMarker = SweepMarkers(points);

                for (int marker = 0; marker < markerCount; marker++) {
                    AssertNoRepeatWithinWindow(byMarker[marker], markerCount, marker);
                }
            }
        }

        /// <summary>
        /// The other half of "starts over": the seventeenth overflow arrival at a marker really does stand
        /// where its first did, for every authored list length rather than only the odd ones.
        /// </summary>
        [Fact]
        public void EveryMarkersOverflowPlacesRepeatOnTheSeventeenth() {
            for (int markerCount = 1; markerCount <= LongestList; markerCount++) {
                SpawnPoint[] points = RowOf(markerCount);
                List<Vector3>[] byMarker = SweepMarkers(points);

                for (int marker = 0; marker < markerCount; marker++) {
                    IReadOnlyList<Vector3> overflow = OverflowOnly(byMarker[marker]);

                    for (int round = 0; round + PromisedPlaces < overflow.Count; round++) {
                        Assert.True(
                            overflow[round] == overflow[round + PromisedPlaces],
                            $"list of {markerCount}: marker {marker} overflow round {round} did not repeat at {round + PromisedPlaces}");
                    }
                }
            }
        }

        /// <summary>
        /// Every position a marker seats, in arrival order, bucketed by the marker that seated it. The list
        /// is taken round once for the authored pass and then <see cref="RoundsPerMarker"/> times more.
        /// </summary>
        private static List<Vector3>[] SweepMarkers(SpawnPoint[] points) {
            var placement = new ListSpawnPlacement(points);
            var byMarker = new List<Vector3>[points.Length];

            for (int marker = 0; marker < points.Length; marker++) {
                byMarker[marker] = new List<Vector3>();
            }

            int arrivals = points.Length * (RoundsPerMarker + 1);

            for (int arrival = 0; arrival < arrivals; arrival++) {
                Vector3 position = placement.NextSpawnState(Member(), false, null, AnyTick).Position;

                byMarker[arrival % points.Length].Add(position);
            }

            return byMarker;
        }

        /// <summary>Drops the marker's first-pass arrival, which stands on the authored point itself.</summary>
        private static IReadOnlyList<Vector3> OverflowOnly(IReadOnlyList<Vector3> seated) {
            var overflow = new List<Vector3>();

            for (int index = 1; index < seated.Count; index++) {
                overflow.Add(seated[index]);
            }

            return overflow;
        }

        private static void AssertNoRepeatWithinWindow(
            IReadOnlyList<Vector3> seated, int markerCount, int marker) {
            for (int start = 0; start + PromisedPlaces <= seated.Count; start++) {
                var window = new HashSet<Vector3>();

                for (int offset = 0; offset < PromisedPlaces; offset++) {
                    Assert.True(
                        window.Add(seated[start + offset]),
                        $"list of {markerCount}: marker {marker} repeated {seated[start + offset]} inside the window starting at arrival {start}");
                }
            }
        }

        private static SpawnPoint[] RowOf(int count) {
            var points = new SpawnPoint[count];

            for (int index = 0; index < count; index++) {
                points[index] = new SpawnPoint(new Vector3(index * 10f, 0f, 0f));
            }

            return points;
        }

        private static int CountDistinct(IReadOnlyList<Vector3> positions) {
            var distinct = new HashSet<Vector3>();

            for (int index = 0; index < positions.Count; index++) {
                distinct.Add(positions[index]);
            }

            return distinct.Count;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right) {
            float deltaX = left.X - right.X;
            float deltaZ = left.Z - right.Z;

            return MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static SessionMember Member() {
            return new SessionMember(1, PlayerId.NewId(), "Reviewer", false, 0);
        }
    }
}
