using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// Instantiates a <see cref="KitLayout"/>'s parts under a root: each placement in a named group child,
    /// posed through <see cref="KitAxes"/>, and optionally socket-mounted onto one another.
    /// </summary>
    /// <remarks>
    /// Parts are created with plain <c>Object.Instantiate</c> in edit and play mode alike, as
    /// <c>RailingRun</c> and <c>TrainCarBuilder</c> do: generated objects are DontSave and rebuilt on demand,
    /// so a prefab link would buy nothing, and <c>PrefabUtility.InstantiatePrefab</c> would refuse a part
    /// source that hands out scene templates and make editor previews differ from play-mode builds.
    /// </remarks>
    public sealed class KitAssembler {
        /// <summary>How far apart two seam sockets' forward yaws may be from exactly opposed, degrees.</summary>
        public const float SeamYawToleranceDegrees = 0.5f;

        private readonly Transform _root;
        private readonly IKitPartSource _parts;
        private readonly KitAxisConvention _convention;
        private readonly bool _markPreview;
        private readonly List<Transform> _placed = new List<Transform>();
        private readonly Dictionary<string, Transform> _groups = new Dictionary<string, Transform>();

        /// <summary>
        /// An assembler building under <paramref name="root"/> from <paramref name="parts"/>. With
        /// <paramref name="markPreview"/> every created object gets <see cref="GeneratedPreview.PreviewFlags"/>.
        /// </summary>
        public KitAssembler(Transform root, IKitPartSource parts, KitAxisConvention convention, bool markPreview) {
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _parts = parts ?? throw new ArgumentNullException(nameof(parts));
            _convention = convention;
            _markPreview = markPreview;
        }

        /// <summary>Every part placed or mounted since the last <see cref="Clear"/>, in creation order.</summary>
        public IReadOnlyList<Transform> Placed => _placed;

        /// <summary>
        /// Instantiates a placement's part named <see cref="KitPlacement.InstanceName"/> under
        /// <paramref name="parent"/>, or under the group child for <see cref="KitPlacement.Group"/> when
        /// null, at the placement's pose. Returns null (and logs) when the kit lacks the part.
        /// </summary>
        public Transform Place(KitPlacement placement, Transform parent = null) {
            if (placement == null) throw new ArgumentNullException(nameof(placement));

            Transform container = parent != null ? parent : GroupFor(placement.Group);
            if (!TryInstantiate(placement.PartName, container, placement.InstanceName, out Transform instance)) return null;

            (Vector3 position, Quaternion rotation) = KitAxes.ToUnityPose(placement.Pose, _convention);
            instance.SetLocalPositionAndRotation(position, rotation);
            return instance;
        }

        /// <summary>Instantiates one part at a kit pose; see <see cref="Place(KitPlacement, Transform)"/>.</summary>
        public Transform Place(string partName, KitPose pose, string instanceName, string group, Transform parent = null) {
            return Place(new KitPlacement(partName, instanceName, group, pose), parent);
        }

        /// <summary>
        /// Instantiates a part under <paramref name="parentModel"/> so its <paramref name="childSocket"/>
        /// lands on the parent's <paramref name="parentSocket"/>, turned by that socket's rotation:
        /// local = socketLocal − R·mountLocal. The child socket's own rotation is not used, so mount sockets
        /// should share their part's axes. Returns null (and logs) when a socket or the part is missing.
        /// </summary>
        public Transform Mount(string partName, Transform parentModel, string parentSocket, string childSocket, string instanceName) {
            if (parentModel == null) {
                Debug.LogError($"KitAssembler::Mount->No parent model to mount '{partName}' on.");
                return null;
            }

            Transform socket = KitSockets.Find(parentModel, parentSocket);
            if (socket == null) return null;
            if (!TryInstantiate(partName, parentModel, instanceName, out Transform piece)) return null;

            piece.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform mount = KitSockets.Find(piece, childSocket);
            if (mount == null) {
                _placed.Remove(piece);
                GeneratedPreview.DestroyChild(piece);
                return null;
            }

            Vector3 socketLocal = parentModel.InverseTransformPoint(socket.position);
            Quaternion rotation = Quaternion.Inverse(parentModel.rotation) * socket.rotation;
            Vector3 mountLocal = Vector3.Scale(piece.localScale, piece.InverseTransformPoint(mount.position));
            piece.SetLocalPositionAndRotation(socketLocal - rotation * mountLocal, rotation);
            return piece;
        }

        /// <summary>
        /// The child of the root that holds <paramref name="group"/>'s parts, created on first use; an
        /// existing direct child of that name is adopted. An empty group is the root itself.
        /// </summary>
        public Transform GroupFor(string group) {
            if (string.IsNullOrEmpty(group)) return _root;
            if (_groups.TryGetValue(group, out Transform known) && known != null) return known;

            Transform container = FindDirectChild(_root, group);
            if (container == null) container = CreateGroup(group);
            _groups[group] = container;
            return container;
        }

        /// <summary>Destroys every placed part and every group this assembler created or adopted.</summary>
        public void Clear() {
            for (int index = _placed.Count - 1; index >= 0; index--) {
                GeneratedPreview.DestroyChild(_placed[index]);
            }
            _placed.Clear();

            foreach (Transform group in _groups.Values) {
                GeneratedPreview.DestroyChild(group);
            }
            _groups.Clear();
        }

        /// <summary>
        /// Checks that every <paramref name="prevSocket"/> and <paramref name="nextSocket"/> on the placed
        /// parts meets an opposite-role socket on another part; see the role-pair overload.
        /// </summary>
        public static List<string> FindOpenSeams(IReadOnlyList<Transform> placed, string prevSocket, string nextSocket, float tolerance) {
            return FindOpenSeams(placed, tolerance, (prevSocket, nextSocket));
        }

        /// <summary>
        /// Checks that every socket named in <paramref name="rolePairs"/> on the placed parts has a partner
        /// socket (the other name of any pair it appears in) on a different part within
        /// <paramref name="tolerance"/> metres, with forward yaws 180° apart. Returns one description per
        /// unmatched or mis-yawed socket; empty when every seam closes.
        /// </summary>
        /// <remarks>
        /// A socket belongs to its nearest placed ancestor, so parts mounted onto other parts keep their own
        /// sockets. Pass e.g. <c>("Socket_Prev", "Socket_Next"), ("Socket_Branch", "Socket_Prev")</c>.
        /// </remarks>
        public static List<string> FindOpenSeams(IReadOnlyList<Transform> placed, float tolerance, params (string First, string Second)[] rolePairs) {
            var problems = new List<string>();
            if (placed == null || rolePairs == null || rolePairs.Length == 0) return problems;

            Dictionary<string, HashSet<string>> partners = BuildPartnerRoles(rolePairs);
            List<(Transform Socket, Transform Owner)> sockets = CollectSeamSockets(placed, partners);
            foreach ((Transform Socket, Transform Owner) entry in sockets) {
                string problem = DescribeSeam(entry, sockets, partners[entry.Socket.name], tolerance);
                if (problem != null) problems.Add(problem);
            }

            return problems;
        }

        private bool TryInstantiate(string partName, Transform parent, string instanceName, out Transform instance) {
            instance = null;
            if (!_parts.TryGetPrefab(partName, out GameObject prefab)) {
                Debug.LogError($"KitAssembler::TryInstantiate->The kit has no part '{partName}'; '{instanceName}' was skipped.");
                return false;
            }

            GameObject created = UnityEngine.Object.Instantiate(prefab, parent, false);
            created.name = string.IsNullOrEmpty(instanceName) ? partName : instanceName;
            if (_markPreview) GeneratedPreview.MarkPreview(created);

            instance = created.transform;
            _placed.Add(instance);
            return true;
        }

        private Transform CreateGroup(string group) {
            var created = new GameObject(group);
            created.transform.SetParent(_root, false);
            if (_markPreview) GeneratedPreview.MarkPreview(created);

            return created.transform;
        }

        private static Transform FindDirectChild(Transform parent, string childName) {
            for (int index = 0; index < parent.childCount; index++) {
                Transform child = parent.GetChild(index);
                if (child.name == childName) return child;
            }

            return null;
        }

        private static Dictionary<string, HashSet<string>> BuildPartnerRoles((string First, string Second)[] rolePairs) {
            var partners = new Dictionary<string, HashSet<string>>();
            foreach ((string first, string second) in rolePairs) {
                if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) continue;

                AddPartner(partners, first, second);
                AddPartner(partners, second, first);
            }

            return partners;
        }

        private static void AddPartner(Dictionary<string, HashSet<string>> partners, string role, string partner) {
            if (!partners.TryGetValue(role, out HashSet<string> roles)) {
                roles = new HashSet<string>();
                partners[role] = roles;
            }

            roles.Add(partner);
        }

        private static List<(Transform Socket, Transform Owner)> CollectSeamSockets(
            IReadOnlyList<Transform> placed, Dictionary<string, HashSet<string>> partners) {
            var placedSet = new HashSet<Transform>();
            foreach (Transform part in placed) {
                if (part != null) placedSet.Add(part);
            }

            var sockets = new List<(Transform Socket, Transform Owner)>();
            var visited = new HashSet<Transform>();
            foreach (Transform part in placed) {
                if (part == null || !visited.Add(part)) continue;

                AppendPartSockets(part, placedSet, partners, sockets);
            }

            return sockets;
        }

        private static void AppendPartSockets(
            Transform part, HashSet<Transform> placedSet, Dictionary<string, HashSet<string>> partners,
            List<(Transform Socket, Transform Owner)> sockets) {
            foreach (Transform child in part.GetComponentsInChildren<Transform>(true)) {
                if (!partners.ContainsKey(child.name)) continue;
                if (OwnerOf(child, placedSet) != part) continue;

                sockets.Add((child, part));
            }
        }

        private static Transform OwnerOf(Transform socket, HashSet<Transform> placedSet) {
            for (Transform current = socket; current != null; current = current.parent) {
                if (placedSet.Contains(current)) return current;
            }

            return null;
        }

        private static string DescribeSeam(
            (Transform Socket, Transform Owner) entry, List<(Transform Socket, Transform Owner)> sockets,
            HashSet<string> partnerRoles, float tolerance) {
            Vector3 position = entry.Socket.position;
            (Transform Socket, Transform Owner) nearest = default;
            (Transform Socket, Transform Owner) misYawed = default;
            float nearestDistance = float.MaxValue;

            foreach ((Transform Socket, Transform Owner) candidate in sockets) {
                if (candidate.Owner == entry.Owner || !partnerRoles.Contains(candidate.Socket.name)) continue;

                float distance = Vector3.Distance(position, candidate.Socket.position);
                if (distance < nearestDistance) {
                    nearestDistance = distance;
                    nearest = candidate;
                }
                if (distance > tolerance) continue;
                if (AreOpposed(entry.Socket, candidate.Socket)) return null;

                misYawed = candidate;
            }

            if (misYawed.Socket != null) return DescribeMisYawed(entry, misYawed);

            return DescribeUnmatched(entry, partnerRoles, tolerance, nearest, nearestDistance);
        }

        private static bool AreOpposed(Transform first, Transform second) {
            float delta = Mathf.DeltaAngle(KitAxes.UnityYawDegrees(first.forward), KitAxes.UnityYawDegrees(second.forward));
            return Mathf.Abs(Mathf.Abs(delta) - 180f) <= SeamYawToleranceDegrees;
        }

        private static string DescribeMisYawed((Transform Socket, Transform Owner) entry, (Transform Socket, Transform Owner) partner) {
            float delta = Mathf.DeltaAngle(KitAxes.UnityYawDegrees(entry.Socket.forward), KitAxes.UnityYawDegrees(partner.Socket.forward));
            return $"{entry.Owner.name}/{entry.Socket.name} meets {partner.Owner.name}/{partner.Socket.name} but their forwards are {Mathf.Abs(delta):0.##}° apart, not 180°.";
        }

        private static string DescribeUnmatched(
            (Transform Socket, Transform Owner) entry, HashSet<string> partnerRoles, float tolerance,
            (Transform Socket, Transform Owner) nearest, float nearestDistance) {
            string roles = string.Join(" or ", partnerRoles);
            string message = $"{entry.Owner.name}/{entry.Socket.name} at {entry.Socket.position.ToString("F3")} has no {roles} within {tolerance:0.###} m";
            if (nearest.Socket == null) return message + ".";

            return message + $" (nearest {nearest.Owner.name}/{nearest.Socket.name} at {nearestDistance:0.###} m).";
        }
    }
}
