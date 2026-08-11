using System;
using System.Collections.Generic;
using System.IO;
using AlpineLib.Collision;
using AlpineLib.Netcode.Collision;
using AlpineLib.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SimVector3 = System.Numerics.Vector3;

namespace AlpineLib.Editor {
    /// <summary>
    /// Turns an authored scene into the collision geometry both ends of the wire simulate against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one bridge between a Unity scene and the engine-free collision world, and deliberately the
    /// only one. Colliders under a <c>NetStaticGeometry</c> marker become primitives, <c>NetMover</c>
    /// components become mover definitions, and the result is encoded once and written twice: to a
    /// <c>.geo</c> file that ships in the dedicated server's config directory, and into a
    /// <c>SceneGeometryAsset</c> the client build carries. Two writes of one encode means the two ends
    /// cannot load different worlds without someone skipping a deploy.
    /// </para>
    /// <para>
    /// <b>Validation aborts the export.</b> A mesh or terrain collider, a skewed transform a box cannot
    /// represent, a duplicate mover id, a route with fewer than two waypoints, a speed of zero, a prefab
    /// id outside the registry — each is collected into a failure list and reported together, and nothing
    /// is written. Exporting an approximation of a scene that does not validate would produce a server
    /// that quietly disagrees with its clients about where the walls are, which is exactly the failure
    /// this whole subsystem exists to remove.
    /// </para>
    /// <para>
    /// <b>Order is data.</b> Shapes are collected root by root in hierarchy order and a collider reached
    /// twice through nested markers is exported once, because a shape's index in the exported array is
    /// the order the resolver iterates it in on both ends. Re-authoring a scene reorders that array, and
    /// that is fine — the content hash changes with it and a stale server is caught at load — but an
    /// export that shuffled shapes for no reason would churn the hash on every run.
    /// </para>
    /// </remarks>
    public static class SceneGeometryExporter {
        /// <summary>Extension of the exported binary the dedicated server loads.</summary>
        public const string GeometryFileExtension = ".geo";

        /// <summary>Project-relative folder the generated assets are written to.</summary>
        public const string DefaultAssetFolder = "Assets/Generated/Geometry";

        /// <summary>
        /// Appended to the scene name to make the generated asset's file name, so that
        /// <c>GameScene.unity</c> exports to <c>GameSceneGeometry.asset</c> and the two never read as the
        /// same file in a search box.
        /// </summary>
        public const string GeometryAssetSuffix = "Geometry";

        private const string logPrefix = "SceneGeometryExporter";

        /// <summary>Where the last <c>.geo</c> was written, so a repeat export does not re-ask.</summary>
        private const string geometryDirectoryPrefKey = "AlpineLib.SceneGeometryExporter.GeometryDirectory";

        /// <summary>Root of the project asset tree, and the only place generated assets may live.</summary>
        private const string assetRootFolder = "Assets";

        /// <summary>
        /// Relative slack allowed when checking a transform against the translation-rotation-scale form it
        /// claims to have. Generous enough to absorb the float error of a deep hierarchy, tight enough
        /// that a genuine shear — a rotated parent scaled non-uniformly — never slips through.
        /// </summary>
        private const float transformSkewTolerance = 1e-4f;

        /// <summary>How many failures are spelled out in the dialog before it defers to the console.</summary>
        private const int dialogFailureLimit = 8;

        /// <summary>Exports the scene currently open in the editor.</summary>
        [MenuItem("AlpineLib/Editor/Export Scene Geometry")]
        public static void ExportActiveScene() {
            Scene scene = SceneManager.GetActiveScene();
            Export(scene, string.Empty, DefaultAssetFolder);
        }

        /// <summary>
        /// Walks a scene and builds its geometry, appending a human-readable line to
        /// <paramref name="failures"/> for every authoring problem found.
        /// </summary>
        /// <returns>The geometry, or null when <paramref name="failures"/> is non-empty.</returns>
        /// <remarks>
        /// Split out from <see cref="Export"/> so the walk can be run without touching the file system —
        /// a build gate wanting to know whether a scene would export cleanly should call this and read
        /// the list, not export to a temporary folder and delete it.
        ///
        /// The content hash is filled in here rather than left to the codec, so that a caller comparing
        /// two builds of the same scene has the number in hand without encoding twice.
        /// </remarks>
        public static SceneGeometry Build(Scene scene, List<string> failures) {
            List<string> problems = failures ?? new List<string>();
            int problemsBefore = problems.Count;

            if (!scene.IsValid() || !scene.isLoaded) {
                problems.Add("The scene is not open and loaded; open it before exporting its geometry.");
                return null;
            }

            if (string.IsNullOrEmpty(scene.name)) {
                problems.Add("The scene has no name; save it before exporting, since its name is the key both ends resolve geometry by.");
                return null;
            }

            var shapes = new List<CollisionShape>();
            var movers = new List<MoverDefinition>();

            CollectStaticShapes(scene, shapes, problems);
            CollectMovers(scene, movers, problems);

            if (problems.Count > problemsBefore) return null;

            WarnWhenEmpty(scene, shapes.Count, movers.Count);

            var draft = new SceneGeometry(scene.name, 0u, shapes.ToArray(), movers.ToArray());
            uint contentHash = SceneGeometryCodec.ComputeContentHash(draft);

            return new SceneGeometry(scene.name, contentHash, draft.StaticShapes, draft.Movers);
        }

        /// <summary>
        /// Builds, validates and writes a scene's geometry: the <c>.geo</c> file for the server and the
        /// <c>SceneGeometryAsset</c> for the client. Reports validation failures in a dialog and writes
        /// nothing when there are any.
        /// </summary>
        /// <param name="scene">Scene to export. Must be open and loaded.</param>
        /// <param name="geometryDirectory">Directory the <c>.geo</c> file is written to; empty asks the user.</param>
        /// <param name="assetFolder">Project-relative folder the generated asset lives in.</param>
        /// <returns>True when both artefacts were written.</returns>
        public static bool Export(Scene scene, string geometryDirectory, string assetFolder) {
            var failures = new List<string>();
            SceneGeometry geometry = Build(scene, failures);

            if (geometry == null) {
                ReportFailures(scene, failures);
                return false;
            }

            string directory = ResolveGeometryDirectory(geometryDirectory);
            if (string.IsNullOrEmpty(directory)) return false;

            string folder = string.IsNullOrEmpty(assetFolder) ? DefaultAssetFolder : assetFolder;
            byte[] payload = SceneGeometryCodec.Encode(geometry);

            if (!WriteGeometryFile(directory, scene.name, payload)) return false;
            if (!WriteAsset(folder, scene.name, payload, geometry.ContentHash)) return false;

            RefreshRegistries(scene.name);

            Debug.Log(
                $"{logPrefix}::Export->Wrote {geometry.StaticShapes.Length} shape(s) and " +
                $"{geometry.Movers.Length} mover(s) for '{scene.name}' (hash {geometry.ContentHash:X8}) " +
                $"to {directory} and {folder}.");

            return true;
        }

        private static void CollectStaticShapes(Scene scene, List<CollisionShape> shapes, List<string> failures) {
            var colliderScratch = new List<Collider>();
            var exported = new HashSet<Collider>();

            foreach (GameObject root in scene.GetRootGameObjects()) {
                AppendRootShapes(root, colliderScratch, exported, shapes, failures);
            }
        }

        /// <remarks>
        /// Markers on inactive objects are skipped: a disabled subtree is not part of the world the
        /// player walks through, and exporting it would give the server a wall the client cannot see.
        /// Whether a marker reaches its own inactive <i>children</i> is the marker's decision, not this
        /// one's.
        /// </remarks>
        private static void AppendRootShapes(
            GameObject root,
            List<Collider> colliderScratch,
            HashSet<Collider> exported,
            List<CollisionShape> shapes,
            List<string> failures) {
            foreach (NetStaticGeometry marker in root.GetComponentsInChildren<NetStaticGeometry>(true)) {
                if (marker == null || !marker.gameObject.activeInHierarchy) continue;

                colliderScratch.Clear();
                marker.CollectColliders(colliderScratch);
                AppendColliders(colliderScratch, exported, shapes, failures);
            }
        }

        private static void AppendColliders(
            List<Collider> colliders,
            HashSet<Collider> exported,
            List<CollisionShape> shapes,
            List<string> failures) {
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++) {
                Collider collider = colliders[colliderIndex];

                if (collider == null) continue;
                if (!exported.Add(collider)) continue;

                AppendCollider(collider, shapes, failures);
            }
        }

        private static void AppendCollider(Collider collider, List<CollisionShape> shapes, List<string> failures) {
            if (collider is BoxCollider box) {
                AppendBox(box, shapes, failures);
                return;
            }

            if (collider is SphereCollider sphere) {
                AppendSphere(sphere, shapes, failures);
                return;
            }

            if (collider is CapsuleCollider capsule) {
                AppendCapsule(capsule, shapes, failures);
                return;
            }

            failures.Add(
                $"'{GetHierarchyPath(collider.transform)}' is marked as net static geometry but carries a " +
                $"{collider.GetType().Name}, which the shared simulation cannot represent. Rebuild it from box, " +
                "sphere or capsule colliders, or move it out from under the NetStaticGeometry marker.");
        }

        /// <summary>
        /// Exports a box as its world centre, the three columns of its rotation, and its size scaled into
        /// world units.
        /// </summary>
        /// <remarks>
        /// The basis comes off the rotation rather than off the matrix columns directly, because the
        /// columns carry the scale as well and the shared shape wants unit axes with the scale already
        /// folded into the half extents. That substitution is only legitimate for a transform that really
        /// is a translation, a rotation and a scale — hence the skew check first.
        /// </remarks>
        private static void AppendBox(BoxCollider box, List<CollisionShape> shapes, List<string> failures) {
            Transform owner = box.transform;
            if (!IsRepresentable(owner, failures)) return;

            Vector3 scale = owner.lossyScale;
            var halfExtents = new SimVector3(
                Mathf.Abs(box.size.x * scale.x) * 0.5f,
                Mathf.Abs(box.size.y * scale.y) * 0.5f,
                Mathf.Abs(box.size.z * scale.z) * 0.5f);

            WarnWhenFlat(owner, halfExtents);

            Quaternion rotation = owner.rotation;
            shapes.Add(CollisionShape.MakeBox(
                ToSim(owner.TransformPoint(box.center)),
                ToSim(rotation * Vector3.right),
                ToSim(rotation * Vector3.up),
                ToSim(rotation * Vector3.forward),
                halfExtents));
        }

        /// <remarks>
        /// A sphere has one radius and a transform may have three scales, so the largest wins: a
        /// non-uniformly scaled sphere collides as the smallest sphere that contains it. That is a
        /// deliberate over-approximation — the alternative is an ellipsoid primitive nothing else in the
        /// stack supports — and it is warned about rather than silently accepted.
        /// </remarks>
        private static void AppendSphere(SphereCollider sphere, List<CollisionShape> shapes, List<string> failures) {
            Transform owner = sphere.transform;
            if (!IsRepresentable(owner, failures)) return;

            WarnWhenNonUniform(owner);

            float radius = sphere.radius * MaxAbsComponent(owner.lossyScale);
            shapes.Add(CollisionShape.MakeSphere(ToSim(owner.TransformPoint(sphere.center)), radius));
        }

        /// <remarks>
        /// Unity measures a capsule by its total height along one of the object's local axes, with the
        /// caps included; the shared shape wants the half length of the inner <i>segment</i>, which is
        /// that height less the two caps. A capsule authored shorter than it is wide collapses to a
        /// sphere, which is what clamping the half length at zero produces.
        /// </remarks>
        private static void AppendCapsule(CapsuleCollider capsule, List<CollisionShape> shapes, List<string> failures) {
            Transform owner = capsule.transform;
            if (!IsRepresentable(owner, failures)) return;

            WarnWhenNonUniform(owner);

            GetCapsuleScales(owner.lossyScale, capsule.direction, out float axisScale, out float radialScale);

            float radius = capsule.radius * radialScale;
            float halfLength = Mathf.Max(0f, capsule.height * 0.5f * axisScale - radius);
            Vector3 worldAxis = owner.rotation * GetDirectionAxis(capsule.direction);

            shapes.Add(CollisionShape.MakeCapsule(
                ToSim(owner.TransformPoint(capsule.center)),
                ToSim(worldAxis),
                halfLength,
                radius));
        }

        private static void CollectMovers(Scene scene, List<MoverDefinition> movers, List<string> failures) {
            NetPrefabRegistry registry = ResolvePrefabRegistry();
            var seenMoverIds = new HashSet<ushort>();

            foreach (GameObject root in scene.GetRootGameObjects()) {
                AppendRootMovers(root, registry, seenMoverIds, movers, failures);
            }
        }

        private static void AppendRootMovers(
            GameObject root,
            NetPrefabRegistry registry,
            HashSet<ushort> seenMoverIds,
            List<MoverDefinition> movers,
            List<string> failures) {
            foreach (NetMover mover in root.GetComponentsInChildren<NetMover>(true)) {
                if (mover == null || !mover.gameObject.activeInHierarchy) continue;

                AppendMover(mover, registry, seenMoverIds, movers, failures);
            }
        }

        /// <summary>
        /// Validates one authored platform and, when it is sound, appends its definition.
        /// </summary>
        /// <remarks>
        /// Every check runs even after one has already failed, so a designer fixing a scene sees the
        /// whole list once instead of peeling it back one export at a time.
        /// </remarks>
        private static void AppendMover(
            NetMover mover,
            NetPrefabRegistry registry,
            HashSet<ushort> seenMoverIds,
            List<MoverDefinition> movers,
            List<string> failures) {
            string moverPath = GetHierarchyPath(mover.transform);
            bool isSound = true;

            if (!seenMoverIds.Add(mover.MoverId)) {
                failures.Add(
                    $"'{moverPath}' repeats mover id {mover.MoverId}; ids are how a client matches a replicated " +
                    "platform to its path, so two platforms cannot share one.");
                isSound = false;
            }

            if (mover.WaypointCount < 2) {
                failures.Add(
                    $"'{moverPath}' has {mover.WaypointCount} waypoint(s); a route needs at least two child " +
                    "transforms to travel between.");
                isSound = false;
            }

            if (mover.Speed <= 0f) {
                failures.Add($"'{moverPath}' has a speed of {mover.Speed:0.###}; a platform's speed must be positive.");
                isSound = false;
            }

            if (!IsKnownPrefabId(registry, mover.PrefabId, moverPath, failures)) {
                isSound = false;
            }

            if (!isSound) return;

            AppendSoundMover(mover, moverPath, movers, failures);
        }

        private static void AppendSoundMover(
            NetMover mover, string moverPath, List<MoverDefinition> movers, List<string> failures) {
            MoverDefinition definition = BuildMover(mover);

            if (definition.Path.TotalLength <= 0f) {
                failures.Add(
                    $"'{moverPath}' has a route of zero length; its waypoints all sit in the same place, so the " +
                    "platform would never leave it. Move them apart or delete the mover.");
                return;
            }

            movers.Add(definition);
        }

        /// <summary>
        /// Builds the shared definition for one authored platform.
        /// </summary>
        /// <remarks>
        /// The collision box is authored around the origin and the path supplies its position, which is
        /// what makes a mover's pose a pure translation of a fixed shape. The mover object's own rotation
        /// is deliberately not read: v1 movers translate only, and honouring a rotation here would export
        /// a box the simulation has no way to spin.
        /// </remarks>
        private static MoverDefinition BuildMover(NetMover mover) {
            var waypointScratch = new List<Vector3>();
            mover.CollectWaypoints(waypointScratch);

            var waypoints = new SimVector3[waypointScratch.Count];

            for (int waypointIndex = 0; waypointIndex < waypointScratch.Count; waypointIndex++) {
                waypoints[waypointIndex] = ToSim(waypointScratch[waypointIndex]);
            }

            Vector3 halfExtents = mover.BoxHalfExtents;
            CollisionShape localShape = CollisionShape.MakeBox(
                SimVector3.Zero,
                SimVector3.UnitX,
                SimVector3.UnitY,
                SimVector3.UnitZ,
                new SimVector3(Mathf.Abs(halfExtents.x), Mathf.Abs(halfExtents.y), Mathf.Abs(halfExtents.z)));

            var path = new MoverPath(waypoints, mover.Speed, mover.LoopMode, mover.PhaseTicks);
            return new MoverDefinition(mover.MoverId, mover.PrefabId, in localShape, path);
        }

        /// <remarks>
        /// With no registry in the project there is nothing to check against, which is a warning rather
        /// than a failure: a library consumer may be exporting geometry before wiring its spawn table up.
        /// </remarks>
        private static bool IsKnownPrefabId(
            NetPrefabRegistry registry, ushort prefabId, string moverPath, List<string> failures) {
            if (registry == null) {
                Debug.LogWarning(
                    $"{logPrefix}::Build->No single NetPrefabRegistry to check '{moverPath}' prefab id {prefabId} " +
                    "against; clients will fail to spawn the platform if the id is wrong.");
                return true;
            }

            NetPrefabEntry entry = registry.FindEntry(prefabId);

            if (entry == null) {
                failures.Add(
                    $"'{moverPath}' names prefab id {prefabId}, which is past the end of the net prefab registry " +
                    $"({registry.Count} row(s)). Append a row for the platform prefab first.");
                return false;
            }

            if (entry.prefab != null) return true;

            failures.Add(
                $"'{moverPath}' names prefab id {prefabId}, whose registry row has no prefab authored; clients " +
                "would receive a spawn they cannot instantiate.");
            return false;
        }

        /// <summary>
        /// The project's net prefab registry, when there is exactly one to be sure about.
        /// </summary>
        /// <remarks>
        /// Several registries in one project means the exporter cannot know which one a scene's movers
        /// are authored against, so it checks against none of them rather than guessing and reporting a
        /// failure that is not one.
        /// </remarks>
        private static NetPrefabRegistry ResolvePrefabRegistry() {
            List<string> assetPaths = FindAssetPaths("t:NetPrefabRegistry");
            if (assetPaths.Count != 1) return null;

            return AssetDatabase.LoadAssetAtPath<NetPrefabRegistry>(assetPaths[0]);
        }

        /// <summary>
        /// True when a transform is a translation, a rotation and a scale, and nothing else.
        /// </summary>
        /// <remarks>
        /// A rotated parent with a non-uniform scale shears its children, and a sheared box is not a box —
        /// there is no centre, orthonormal basis and half extent triple that describes it. Unity resolves
        /// such a hierarchy into a <c>lossyScale</c> that is, as the name says, lossy; rebuilding the
        /// matrix from the parts and comparing is the cheapest honest test of whether anything was lost.
        /// </remarks>
        private static bool IsRepresentable(Transform owner, List<string> failures) {
            Matrix4x4 authored = owner.localToWorldMatrix;
            Matrix4x4 rebuilt = Matrix4x4.TRS(owner.position, owner.rotation, owner.lossyScale);
            float tolerance = transformSkewTolerance * Mathf.Max(1f, MaxAbsComponent(owner.lossyScale));

            if (IsWithinTolerance(authored, rebuilt, tolerance)) return true;

            failures.Add(
                $"'{GetHierarchyPath(owner)}' has a sheared transform — a rotated parent with a non-uniform " +
                "scale — which no box, sphere or capsule can describe. Re-author it so that rotation and " +
                "non-uniform scale do not meet in the same chain.");
            return false;
        }

        private static bool IsWithinTolerance(Matrix4x4 left, Matrix4x4 right, float tolerance) {
            for (int elementIndex = 0; elementIndex < 16; elementIndex++) {
                if (Mathf.Abs(left[elementIndex] - right[elementIndex]) > tolerance) return false;
            }

            return true;
        }

        private static void GetCapsuleScales(Vector3 scale, int direction, out float axisScale, out float radialScale) {
            float absoluteX = Mathf.Abs(scale.x);
            float absoluteY = Mathf.Abs(scale.y);
            float absoluteZ = Mathf.Abs(scale.z);

            if (direction == 0) {
                axisScale = absoluteX;
                radialScale = Mathf.Max(absoluteY, absoluteZ);
                return;
            }

            if (direction == 1) {
                axisScale = absoluteY;
                radialScale = Mathf.Max(absoluteX, absoluteZ);
                return;
            }

            axisScale = absoluteZ;
            radialScale = Mathf.Max(absoluteX, absoluteY);
        }

        private static Vector3 GetDirectionAxis(int direction) {
            if (direction == 0) return Vector3.right;
            if (direction == 1) return Vector3.up;

            return Vector3.forward;
        }

        private static float MaxAbsComponent(Vector3 value) {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
        }

        private static SimVector3 ToSim(Vector3 value) {
            return new SimVector3(value.x, value.y, value.z);
        }

        private static void WarnWhenNonUniform(Transform owner) {
            Vector3 scale = owner.lossyScale;
            float largest = MaxAbsComponent(scale);
            float smallest = Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

            if (largest - smallest <= transformSkewTolerance * Mathf.Max(1f, largest)) return;

            Debug.LogWarning(
                $"{logPrefix}::Build->'{GetHierarchyPath(owner)}' is scaled non-uniformly ({scale}); its radius " +
                "is exported from the largest axis, so the simulated shape is wider than the drawn one.");
        }

        private static void WarnWhenFlat(Transform owner, SimVector3 halfExtents) {
            if (halfExtents.X > 0f && halfExtents.Y > 0f && halfExtents.Z > 0f) return;

            Debug.LogWarning(
                $"{logPrefix}::Build->'{GetHierarchyPath(owner)}' exports a box with a zero half extent; a pawn " +
                "can pass through a shape with no thickness. Give it a size on every axis.");
        }

        private static void WarnWhenEmpty(Scene scene, int shapeCount, int moverCount) {
            if (shapeCount > 0 || moverCount > 0) return;

            Debug.LogWarning(
                $"{logPrefix}::Build->'{scene.name}' exports no shapes and no movers. Both ends will simulate an " +
                "empty world; add a NetStaticGeometry marker to the scene's collision if that is not intended.");
        }

        /// <summary>
        /// Settles on the directory the <c>.geo</c> goes in, asking the user when the caller did not say
        /// and remembering the answer for next time.
        /// </summary>
        private static string ResolveGeometryDirectory(string geometryDirectory) {
            if (!string.IsNullOrEmpty(geometryDirectory)) return geometryDirectory;

            string remembered = EditorPrefs.GetString(geometryDirectoryPrefKey, string.Empty);
            string chosen = EditorUtility.SaveFolderPanel("Export Scene Geometry", remembered, string.Empty);

            if (string.IsNullOrEmpty(chosen)) {
                Debug.Log($"{logPrefix}::Export->Cancelled; nothing was written.");
                return string.Empty;
            }

            EditorPrefs.SetString(geometryDirectoryPrefKey, chosen);
            return chosen;
        }

        private static bool WriteGeometryFile(string directory, string sceneName, byte[] payload) {
            string filePath = Path.Combine(directory, sceneName + GeometryFileExtension);

            try {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(filePath, payload);
                return true;
            }
            catch (IOException exception) {
                ReportWriteFailure(filePath, exception);
                return false;
            }
            catch (UnauthorizedAccessException exception) {
                ReportWriteFailure(filePath, exception);
                return false;
            }
        }

        private static void ReportWriteFailure(string filePath, Exception exception) {
            Debug.LogError($"{logPrefix}::Export->Could not write {filePath}: {exception.Message}");
            EditorUtility.DisplayDialog(
                "Export Scene Geometry",
                $"Could not write {filePath}.\n\n{exception.Message}",
                "OK");
        }

        /// <summary>
        /// Writes the same bytes into the generated asset, creating it the first time a scene is
        /// exported and overwriting it — in place, keeping its GUID and every reference to it —
        /// afterwards.
        /// </summary>
        private static bool WriteAsset(string assetFolder, string sceneName, byte[] payload, uint contentHash) {
            string folder = string.IsNullOrEmpty(assetFolder) ? DefaultAssetFolder : assetFolder;
            if (!EnsureAssetFolder(folder)) return false;

            string assetPath = $"{folder}/{sceneName}{GeometryAssetSuffix}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SceneGeometryAsset>(assetPath);

            if (existing != null) {
                existing.SetExport(sceneName, payload, contentHash);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return true;
            }

            var created = ScriptableObject.CreateInstance<SceneGeometryAsset>();
            created.SetExport(sceneName, payload, contentHash);
            AssetDatabase.CreateAsset(created, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>Creates every missing folder along a project-relative path.</summary>
        private static bool EnsureAssetFolder(string folder) {
            string trimmed = folder.Trim('/');
            string[] parts = trimmed.Split('/');

            if (parts.Length == 0 || parts[0] != assetRootFolder) {
                Debug.LogError($"{logPrefix}::Export->'{folder}' is not under {assetRootFolder}/; nothing was written.");
                return false;
            }

            string current = parts[0];

            for (int partIndex = 1; partIndex < parts.Length; partIndex++) {
                current = CreateChildFolder(current, parts[partIndex]);
            }

            if (AssetDatabase.IsValidFolder(current)) return true;

            Debug.LogError($"{logPrefix}::Export->Could not create the asset folder '{folder}'.");
            return false;
        }

        private static string CreateChildFolder(string parentFolder, string childName) {
            if (string.IsNullOrEmpty(childName)) return parentFolder;

            string childFolder = $"{parentFolder}/{childName}";
            if (AssetDatabase.IsValidFolder(childFolder)) return childFolder;

            AssetDatabase.CreateFolder(parentFolder, childName);
            return childFolder;
        }

        /// <summary>
        /// Drops the cached worlds a re-export invalidates, and warns when nothing in the project points
        /// at the geometry that was just written.
        /// </summary>
        /// <remarks>
        /// An asset no registry lists is an asset no client loads: the session falls back to flat ground
        /// and the scene's walls exist only on the server, which is the one failure mode this exporter
        /// cannot detect at load time. It is a warning rather than a failure because the registry is
        /// usually filled in immediately after the first export of a new scene.
        /// </remarks>
        private static void RefreshRegistries(string sceneName) {
            bool isReferenced = false;

            foreach (string assetPath in FindAssetPaths("t:SceneGeometryRegistry")) {
                var registry = AssetDatabase.LoadAssetAtPath<SceneGeometryRegistry>(assetPath);
                if (registry == null) continue;

                registry.ClearCache();
                isReferenced = isReferenced || registry.FindAsset(sceneName) != null;
            }

            if (isReferenced) return;

            Debug.LogWarning(
                $"{logPrefix}::Export->No SceneGeometryRegistry lists geometry for '{sceneName}'; clients will fall " +
                "back to flat ground there. Add the generated asset to the registry the SessionService references.");
        }

        private static void ReportFailures(Scene scene, List<string> failures) {
            foreach (string failure in failures) {
                Debug.LogError($"{logPrefix}::Export->{failure}");
            }

            EditorUtility.DisplayDialog(
                "Export Scene Geometry",
                $"'{scene.name}' has {failures.Count} authoring problem(s); nothing was written.\n\n" +
                BuildFailureSummary(failures),
                "OK");
        }

        private static string BuildFailureSummary(List<string> failures) {
            var summary = new System.Text.StringBuilder();

            for (int failureIndex = 0; failureIndex < failures.Count && failureIndex < dialogFailureLimit; failureIndex++) {
                summary.Append("• ").Append(failures[failureIndex]).Append('\n');
            }

            if (failures.Count > dialogFailureLimit) {
                summary.Append($"…and {failures.Count - dialogFailureLimit} more. See the console.");
            }

            return summary.ToString();
        }

        private static List<string> FindAssetPaths(string filter) {
            var assetPaths = new List<string>();

            foreach (string assetGuid in AssetDatabase.FindAssets(filter)) {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                assetPaths.Add(assetPath);
            }

            return assetPaths;
        }

        private static string GetHierarchyPath(Transform target) {
            string path = target.name;

            for (Transform parent = target.parent; parent != null; parent = parent.parent) {
                path = $"{parent.name}/{path}";
            }

            return path;
        }
    }
}
