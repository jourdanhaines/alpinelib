using System;
using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Actors.Locomotion;
using AlpineLib.Collision;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using AlpineLib.Stats;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Networking-specific asset checks that no single asset can make about itself: match ids that
    /// collide inside one session config, a lobby that seats more players than the session profile
    /// admits, movement profiles that have drifted away from the stats the game actually moves with,
    /// collision capsules that describe a shape the shared motor cannot step with, scenes a session can
    /// load but no exported geometry covers, and a server bundle that would copy a server the launcher
    /// beside it will not look for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are separated from <see cref="AssetValidator"/>'s critical-field table because they are
    /// relational: every field involved is filled in, and each asset is individually correct — the fault
    /// only exists between them. <see cref="AssetValidator.ValidateAll"/> calls
    /// <see cref="Validate(List{string})"/> so the build gate covers them too, and the menu entry runs
    /// the same pass interactively while assets are being authored.
    /// </para>
    /// <para>
    /// The movement parity check is the important one. Gait speeds are authored twice on purpose — once
    /// on the prefab as a stat base and a set of gait multipliers, which is what the Unity client
    /// animates and moves with, and once on the prefab registry, which is what the dedicated server
    /// simulates with and exports to JSON. The dedicated server has no stat sheets, so the duplication
    /// cannot be removed; what it can have is a check that fails loudly the moment the two disagree,
    /// because the symptom otherwise is a client that rubber-bands only while sprinting.
    /// </para>
    /// </remarks>
    public static class SessionConfigValidator {
        private const string logPrefix = "[AlpineLib] SessionConfigValidator";

        /// <summary>
        /// Absolute slack allowed when comparing an authored speed against its derived value, before the
        /// relative term takes over. Covers the last digit of a serialized float, nothing more.
        /// </summary>
        private const float absoluteTolerance = 0.001f;

        /// <summary>Relative slack, applied to the magnitude of the expected value.</summary>
        private const float relativeTolerance = 0.001f;

        /// <summary>
        /// Runs the networking checks interactively and reports the outcome in the console and a dialog.
        /// </summary>
        [MenuItem("AlpineLib/Networking/Validate Session Configs")]
        public static void ValidateFromMenu() {
            var failures = new List<string>();
            Validate(failures);

            foreach (string failure in failures) {
                Debug.LogError($"{logPrefix}: {failure}");
            }

            string summary = failures.Count == 0
                ? "No problems found."
                : $"{failures.Count} problem(s) found. See the console.";

            EditorUtility.DisplayDialog("Validate Session Configs", summary, "OK");
        }

        /// <summary>
        /// Appends one entry per problem found to <paramref name="failures"/>. Adds nothing when the
        /// project has no networking assets, so a game that has not been wired up yet still passes.
        /// </summary>
        public static void Validate(List<string> failures) {
            ValidateSessionConfigs(failures);
            ValidatePrefabRegistries(failures);
            ValidateSpawnPlacements(failures);
            ValidateServerBundles(failures);
        }

        private static void ValidateSessionConfigs(List<string> failures) {
            List<SceneGeometryRegistry> geometryRegistries = LoadGeometryRegistries();

            foreach (string assetPath in FindAssetPaths("t:SessionConfig")) {
                var config = AssetDatabase.LoadAssetAtPath<SessionConfig>(assetPath);
                if (config == null) continue;

                ValidateMatchIds(config, assetPath, failures);
                ValidateCapacity(config, assetPath, failures);
                ValidateSpawnPrefabId(config, assetPath, failures);
                WarnOnMissingGeometry(config, assetPath, geometryRegistries);
            }
        }

        /// <summary>
        /// Checks that every match a config can launch is authored and carries a distinct wire id.
        /// </summary>
        /// <remarks>
        /// Duplicate ids are silent in play: <c>SessionConfig.FindMatch</c> returns the first row, so the
        /// second match becomes unreachable rather than erroring, and the launch that was meant to load
        /// it loads the wrong scene instead.
        /// </remarks>
        private static void ValidateMatchIds(SessionConfig config, string assetPath, List<string> failures) {
            if (config.matches == null) return;

            var seenIds = new HashSet<string>();

            for (int matchIndex = 0; matchIndex < config.matches.Length; matchIndex++) {
                MatchDefinition match = config.matches[matchIndex];

                if (match == null) {
                    failures.Add($"{assetPath}: SessionConfig.matches[{matchIndex}] is empty.");
                    continue;
                }

                ValidateMatchId(match, matchIndex, seenIds, assetPath, failures);
            }
        }

        private static void ValidateMatchId(
            MatchDefinition match, int matchIndex, HashSet<string> seenIds, string assetPath, List<string> failures) {
            if (string.IsNullOrWhiteSpace(match.matchId)) {
                failures.Add($"{assetPath}: SessionConfig.matches[{matchIndex}] ('{match.name}') has no match id.");
                return;
            }

            if (seenIds.Add(match.matchId)) return;

            failures.Add(
                $"{assetPath}: SessionConfig.matches[{matchIndex}] ('{match.name}') repeats match id " +
                $"'{match.matchId}'; only the first row with that id can ever launch.");
        }

        /// <summary>
        /// Checks that the lobby cannot seat more players than the session profile admits.
        /// </summary>
        /// <remarks>
        /// The profile's cap is the one the server enforces on join, so a larger lobby capacity is a
        /// promise the session will refuse to keep — the room advertises seats that are rejected at the
        /// door.
        /// </remarks>
        private static void ValidateCapacity(SessionConfig config, string assetPath, List<string> failures) {
            if (config.lobby == null || config.profile == null) return;
            if (config.lobby.lobbyCapacity <= config.profile.maxPlayers) return;

            failures.Add(
                $"{assetPath}: LobbyConfig '{config.lobby.name}' seats {config.lobby.lobbyCapacity} but " +
                $"SessionProfile '{config.profile.name}' admits {config.profile.maxPlayers}.");
        }

        /// <summary>
        /// Checks that the pawn a session spawns is a row its prefab registry actually has.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The prefab id is an index into the registry, and an index past the end is silent on the
        /// server — the spawn message goes out naming a prefab no client can resolve, so every player
        /// joins into a world where nobody, themselves included, has a body.
        /// </para>
        /// <para>
        /// A config with no spawn asset is checked too, against the default id every hosting path falls
        /// back to. That is the state every project is in before it adopts a spawn config, and it is the
        /// one most likely to be wrong — but it is only a warning, because a project whose registry is
        /// still empty may simply not host yet, and a build gate should not stop it.
        /// </para>
        /// </remarks>
        private static void ValidateSpawnPrefabId(SessionConfig config, string assetPath, List<string> failures) {
            if (config.prefabRegistry == null) return;

            int rowCount = config.prefabRegistry.Count;
            ushort prefabId = config.spawn != null
                ? config.spawn.pawnPrefabId
                : SpawnPlacementConfig.DefaultPawnPrefabId;

            if (prefabId < rowCount) return;

            if (config.spawn == null) {
                Debug.LogWarning(
                    $"{logPrefix}: {assetPath}: SessionConfig names no SpawnPlacementConfig, so every host spawns " +
                    $"prefab id {prefabId}, which NetPrefabRegistry '{config.prefabRegistry.name}' has no row for " +
                    "(it has none at all); nobody would get a body. Author a spawn config, or add the pawn row.");
                return;
            }

            failures.Add(
                $"{assetPath}: SpawnPlacementConfig '{config.spawn.name}' spawns prefab id {prefabId} " +
                $"but NetPrefabRegistry '{config.prefabRegistry.name}' has {rowCount} row(s); no player would get a body.");
        }

        /// <summary>
        /// Checks that every spawn placement asset describes a placement that can actually be built.
        /// </summary>
        /// <remarks>
        /// <c>SpawnPlacementConfig.ToPlacement</c> repairs both of these at runtime — an empty list falls
        /// back to a ring, a seatless ring takes the default seat count — because refusing to open a
        /// session is worse than opening a wrong one. That repair is exactly why the mistake needs
        /// catching here instead: in play it looks like the placement simply being ignored.
        /// </remarks>
        private static void ValidateSpawnPlacements(List<string> failures) {
            foreach (string assetPath in FindAssetPaths("t:SpawnPlacementConfig")) {
                var placementConfig = AssetDatabase.LoadAssetAtPath<SpawnPlacementConfig>(assetPath);
                if (placementConfig == null) continue;

                ValidateRingSettings(placementConfig, assetPath, failures);
                ValidateListSettings(placementConfig, assetPath, failures);
            }
        }

        private static void ValidateRingSettings(
            SpawnPlacementConfig placementConfig, string assetPath, List<string> failures) {
            if (placementConfig.ringSeats <= 0) {
                failures.Add(
                    $"{assetPath}: ringSeats is {placementConfig.ringSeats}; a spawn ring needs at least one seat, " +
                    "and a list placement falls back to the ring when its points are missing.");
            }

            if (placementConfig.ringRadius >= 0f) return;

            failures.Add(
                $"{assetPath}: ringRadius is {placementConfig.ringRadius:0.###}; a ring cannot have a negative " +
                "radius, and a list placement falls back to the ring when its points are missing.");
        }

        private static void ValidateListSettings(
            SpawnPlacementConfig placementConfig, string assetPath, List<string> failures) {
            if (placementConfig.placement != SpawnPlacementKind.List) return;
            if (placementConfig.BuildSpawnPoints().Count > 0) return;

            failures.Add($"{assetPath}: placement is List but no spawn points are authored; arrivals would fall back to a ring.");
        }

        /// <summary>
        /// Checks that every enabled server bundle names everything the post-build step needs, and that
        /// it names the same server the launcher will look for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The publish folder itself is deliberately not checked here. It is produced by a command-line
        /// step that usually runs after the assets are authored and often on a different machine, so a
        /// missing one is a build-time fact — <c>ServerBundleBuildStep</c> fails on it — not an authoring
        /// mistake. What is checked is everything that is wrong in the asset no matter when anybody
        /// builds.
        /// </para>
        /// <para>
        /// A disabled bundle is skipped entirely: switching the flag off is how a project states that
        /// this build ships no server, and a half-filled asset behind that flag is not a fault.
        /// </para>
        /// <para>
        /// Everything <c>ServerBundleBuildStep</c> refuses <em>in the asset itself</em> is refused here
        /// too, in the same terms, so that authoring and building cannot give two answers about one
        /// asset — including the count, which no single asset can see about itself. Its other rules read
        /// something no authoring pass has: the published binary's own header, and the macOS player's
        /// architecture, which is a build setting rather than a field on the asset.
        /// </para>
        /// </remarks>
        private static void ValidateServerBundles(List<string> failures) {
            var enabledPaths = new List<string>();

            foreach (string assetPath in FindAssetPaths("t:ServerBundleConfig")) {
                var bundle = AssetDatabase.LoadAssetAtPath<ServerBundleConfig>(assetPath);
                if (bundle == null) continue;
                if (!bundle.enabled) continue;

                enabledPaths.Add(assetPath);
                ValidateBundleSource(bundle, assetPath, failures);
                ValidateBundleLauncher(bundle, assetPath, failures);
                ValidateBundleSessionConfig(bundle, assetPath, failures);
            }

            ValidateBundleCount(enabledPaths, failures);
        }

        /// <remarks>
        /// Two enabled bundles copy into one folder, so the build step fails rather than picking a
        /// winner. That is knowable from the assets alone, which makes it an authoring mistake nobody
        /// should have to start a player build to hear about.
        /// </remarks>
        private static void ValidateBundleCount(List<string> enabledPaths, List<string> failures) {
            if (enabledPaths.Count < 2) return;

            failures.Add(
                $"{enabledPaths.Count} enabled ServerBundleConfig assets ({string.Join(", ", enabledPaths)}) would " +
                "all copy into the same folder beside the player; a standalone build fails until all but one is " +
                "deleted or disabled.");
        }

        /// <remarks>
        /// The three runtime identifiers are checked together rather than one at a time: a project that
        /// ships one platform leaves the other two blank on purpose, and only an asset with none of them
        /// filled in can never bundle anything.
        /// </remarks>
        private static void ValidateBundleSource(
            ServerBundleConfig bundle, string assetPath, List<string> failures) {
            if (string.IsNullOrWhiteSpace(bundle.publishedServerRoot)) {
                failures.Add($"{assetPath}: ServerBundleConfig is enabled but names no published server root.");
            }

            if (string.IsNullOrWhiteSpace(bundle.executableName)) {
                failures.Add($"{assetPath}: ServerBundleConfig is enabled but names no server executable.");
            }

            ValidateRuntimeIdentifier(
                bundle.windowsRuntimeIdentifier, ServerBundleConfig.WindowsPlatformName, assetPath, failures);
            ValidateRuntimeIdentifier(
                bundle.linuxRuntimeIdentifier, ServerBundleConfig.LinuxPlatformName, assetPath, failures);
            ValidateRuntimeIdentifier(
                bundle.macRuntimeIdentifier, ServerBundleConfig.MacPlatformName, assetPath, failures);

            if (HasAnyRuntimeIdentifier(bundle)) return;

            failures.Add(
                $"{assetPath}: ServerBundleConfig names no runtime identifier for any platform; no standalone build " +
                "could find a publish to copy.");
        }

        /// <remarks>
        /// One field per platform, each checked for the two things that are wrong about it no matter
        /// what any machine has published: a value that is not one folder name, and a value for another
        /// operating system. Both fail the build step too — an empty field does not, because a project
        /// that ships one platform leaves the other two blank on purpose.
        /// </remarks>
        private static void ValidateRuntimeIdentifier(
            string runtimeIdentifier, string platform, string assetPath, List<string> failures) {
            if (string.IsNullOrWhiteSpace(runtimeIdentifier)) return;

            ValidateRuntimeIdentifierShape(runtimeIdentifier, platform, assetPath, failures);
            ValidateRuntimeIdentifierPlatform(runtimeIdentifier, platform, assetPath, failures);
        }

        /// <remarks>
        /// A runtime identifier is one folder beneath the publish root, so the same rules that stop the
        /// bundle folder walking out of the player's directory stop this walking out of the publish
        /// root. The build step refuses the same values.
        /// </remarks>
        private static void ValidateRuntimeIdentifierShape(
            string runtimeIdentifier, string platform, string assetPath, List<string> failures) {
            if (ServerBundleConfig.IsSingleFolderName(runtimeIdentifier)) return;

            failures.Add(
                $"{assetPath}: ServerBundleConfig names '{runtimeIdentifier}' as its {platform} runtime identifier, " +
                "which is not a single folder name; the publish root holds one folder per runtime identifier.");
        }

        /// <remarks>
        /// Each field is labelled with the platform it is for, so another platform's value typed into it
        /// is an authoring slip and nothing else — and one nothing else can catch, because the publish
        /// folder is named after the field. <c>ServerBundleBuildStep</c> fails the build on it; this is
        /// where an author hears about it without starting one.
        /// </remarks>
        private static void ValidateRuntimeIdentifierPlatform(
            string runtimeIdentifier, string platform, string assetPath, List<string> failures) {
            string named = ServerBundleConfig.ResolveRuntimeIdentifierPlatform(runtimeIdentifier);

            if (named == null) return;
            if (string.Equals(named, platform, StringComparison.Ordinal)) return;

            failures.Add(
                $"{assetPath}: ServerBundleConfig names '{runtimeIdentifier}' as its {platform} runtime identifier, " +
                $"but that identifier is {named}; a {named} server cannot run beside a {platform} player.");
        }

        private static bool HasAnyRuntimeIdentifier(ServerBundleConfig bundle) {
            return !string.IsNullOrWhiteSpace(bundle.windowsRuntimeIdentifier)
                || !string.IsNullOrWhiteSpace(bundle.linuxRuntimeIdentifier)
                || !string.IsNullOrWhiteSpace(bundle.macRuntimeIdentifier);
        }

        /// <summary>
        /// Checks that the bundle and the launcher agree about the copied server.
        /// </summary>
        /// <remarks>
        /// They meet at two values: the folder the server is copied into, which the bundle reads off the
        /// launcher's asset, and the executable's name, which is authored on both. A build with either
        /// out of step copies a working server into a place the player will not look, and the only
        /// symptom is hosting failing in a shipped build.
        /// </remarks>
        private static void ValidateBundleLauncher(
            ServerBundleConfig bundle, string assetPath, List<string> failures) {
            if (bundle.localServer == null) {
                failures.Add(
                    $"{assetPath}: ServerBundleConfig names no LocalServerConfig, so it would copy the server into " +
                    $"'{ServerBundleConfig.DefaultBundleFolderName}' without anything confirming that is where the " +
                    "player looks.");
                return;
            }

            ValidateBundleFolderName(bundle, assetPath, failures);

            if (bundle.localServer.executableName == bundle.executableName) return;

            failures.Add(
                $"{assetPath}: ServerBundleConfig bundles '{bundle.executableName}' but LocalServerConfig " +
                $"'{bundle.localServer.name}' launches '{bundle.localServer.executableName}'; the copied server " +
                "would never be found.");
        }

        /// <remarks>
        /// The bundle folder is replaced wholesale by the build step, so a name that is a path rather
        /// than a folder name replaces whatever it reaches — <c>.</c> reaches the player that was just
        /// built. The step refuses the same values; this is where an author hears about it.
        /// </remarks>
        private static void ValidateBundleFolderName(
            ServerBundleConfig bundle, string assetPath, List<string> failures) {
            string folderName = bundle.localServer.bundledServerFolderName;

            if (string.IsNullOrWhiteSpace(folderName)) return;
            if (ServerBundleConfig.IsSingleFolderName(folderName)) return;

            failures.Add(
                $"{assetPath}: LocalServerConfig '{bundle.localServer.name}' names its bundled server folder " +
                $"'{folderName}', which is not a single folder name; the build step would replace something beside " +
                "the player rather than a folder inside it.");
        }

        /// <remarks>
        /// Without a session config the bundled server runs on library defaults — a different port, a
        /// different tick rate and a different lobby from the build standing next to it, which is a
        /// player that cannot host. <c>ServerBundleBuildStep</c> fails on the same state.
        /// </remarks>
        private static void ValidateBundleSessionConfig(
            ServerBundleConfig bundle, string assetPath, List<string> failures) {
            if (bundle.sessionConfig != null) return;

            failures.Add(
                $"{assetPath}: ServerBundleConfig names no SessionConfig; the bundled server would run on library " +
                "defaults rather than this build's settings.");
        }

        /// <summary>
        /// Warns about every scene a session can be in that no exported geometry covers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A scene with no geometry is not broken — both ends fall back to a flat plane at y=0 and agree
        /// with each other about it, which is exactly right for a lobby that is a floor and a skybox. It
        /// is nonetheless the single easiest mistake to make once geometry exists: a level gains a ramp,
        /// nobody re-runs the exporter, and the server keeps simulating the plane while the client walks
        /// up the ramp it can see.
        /// </para>
        /// <para>
        /// So it is a warning, never a failure, and it is silent in a project that has no geometry
        /// registry at all — a game that has not adopted the feature should not have its build gate
        /// complain about it.
        /// </para>
        /// </remarks>
        private static void WarnOnMissingGeometry(
            SessionConfig config, string assetPath, List<SceneGeometryRegistry> geometryRegistries) {
            if (geometryRegistries.Count == 0) return;

            if (config.lobby != null) {
                WarnWhenSceneUnresolved(
                    config.lobby.lobbySceneName,
                    $"{assetPath}: LobbyConfig '{config.lobby.name}'",
                    geometryRegistries);
            }

            if (config.matches == null) return;

            foreach (MatchDefinition match in config.matches) {
                if (match == null) continue;

                WarnWhenSceneUnresolved(
                    match.sceneName,
                    $"{assetPath}: MatchDefinition '{match.name}'",
                    geometryRegistries);
            }
        }

        private static void WarnWhenSceneUnresolved(
            string sceneName, string context, List<SceneGeometryRegistry> geometryRegistries) {
            if (string.IsNullOrWhiteSpace(sceneName)) return;

            for (int registryIndex = 0; registryIndex < geometryRegistries.Count; registryIndex++) {
                if (geometryRegistries[registryIndex].FindAsset(sceneName) != null) return;
            }

            Debug.LogWarning(
                $"{logPrefix}: {context} plays in scene '{sceneName}', which no SceneGeometryRegistry has geometry " +
                "for; both ends will simulate flat ground there. Run AlpineLib/Editor/Export Scene Geometry on it " +
                "and add the generated asset to the registry.");
        }

        private static List<SceneGeometryRegistry> LoadGeometryRegistries() {
            var registries = new List<SceneGeometryRegistry>();

            foreach (string assetPath in FindAssetPaths("t:SceneGeometryRegistry")) {
                var registry = AssetDatabase.LoadAssetAtPath<SceneGeometryRegistry>(assetPath);
                if (registry == null) continue;

                registries.Add(registry);
            }

            return registries;
        }

        private static void ValidatePrefabRegistries(List<string> failures) {
            foreach (string assetPath in FindAssetPaths("t:NetPrefabRegistry")) {
                var registry = AssetDatabase.LoadAssetAtPath<NetPrefabRegistry>(assetPath);
                if (registry == null) continue;
                if (registry.entries == null) continue;

                ValidateRegistryRows(registry, assetPath, failures);
            }
        }

        /// <remarks>
        /// A row with no prefab is not a failure: the registry is append-only, so retiring a spawnable
        /// means blanking its prefab and leaving the row in place to hold its id.
        /// </remarks>
        private static void ValidateRegistryRows(NetPrefabRegistry registry, string assetPath, List<string> failures) {
            for (int entryIndex = 0; entryIndex < registry.entries.Length; entryIndex++) {
                NetPrefabEntry entry = registry.entries[entryIndex];

                if (entry == null) {
                    failures.Add($"{assetPath}: NetPrefabRegistry.entries[{entryIndex}] is empty; the row still owns prefab id {entryIndex}.");
                    continue;
                }

                string context = $"{assetPath}: entries[{entryIndex}] ('{entry.displayName}')";
                ValidateCapsule(entry, context, failures);

                if (entry.prefab == null) continue;

                ValidateMovementParity(entry, context, failures);
            }
        }

        /// <summary>
        /// Checks that a row describes a capsule the shared motor can actually collide and step with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These are checked on every authored row, prefab or not, because the profile table the server
        /// binds is indexed by prefab id and carries a profile for every row — a retired row with a
        /// zeroed capsule would still be exported, and a zero-radius capsule degenerates to a line
        /// segment that slips between shapes rather than pushing out of them.
        /// </para>
        /// <para>
        /// The step offset is the interesting one. It is both the height of the ledge the motor lifts
        /// over and the reach of the support probe below the feet, so a step offset at or past half the
        /// capsule's height lets the pawn snap onto surfaces level with its own middle — it climbs walls
        /// by walking at them. Half the height is a generous ceiling, not a tuning suggestion.
        /// </para>
        /// </remarks>
        private static void ValidateCapsule(NetPrefabEntry entry, string context, List<string> failures) {
            if (entry.capsuleRadius <= 0f) {
                failures.Add($"{context} capsuleRadius is {entry.capsuleRadius:0.###}; the collision capsule needs a positive radius.");
            }

            if (entry.capsuleHeight <= 0f) {
                failures.Add($"{context} capsuleHeight is {entry.capsuleHeight:0.###}; the collision capsule needs a positive height.");
            }

            if (entry.stepOffset < 0f) {
                failures.Add($"{context} stepOffset is {entry.stepOffset:0.###}; a negative step offset would probe above the pawn's feet.");
            }

            ValidateCapsuleProportions(entry, context, failures);
        }

        private static void ValidateCapsuleProportions(NetPrefabEntry entry, string context, List<string> failures) {
            if (entry.capsuleHeight <= 0f || entry.capsuleRadius <= 0f) return;

            if (entry.capsuleHeight < entry.capsuleRadius * 2f) {
                failures.Add(
                    $"{context} capsuleHeight {entry.capsuleHeight:0.###} is shorter than its own diameter " +
                    $"({entry.capsuleRadius * 2f:0.###}); the capsule's segment would run backwards.");
            }

            if (entry.stepOffset < entry.capsuleHeight * 0.5f) return;

            failures.Add(
                $"{context} stepOffset {entry.stepOffset:0.###} is at least half the capsule height " +
                $"({entry.capsuleHeight:0.###}); the pawn would step onto surfaces level with its own middle.");
        }

        /// <summary>
        /// Compares a registry row's movement profile against the stats and multipliers authored on the
        /// prefab it spawns.
        /// </summary>
        private static void ValidateMovementParity(NetPrefabEntry entry, string context, List<string> failures) {
            var locomotion = entry.prefab.GetComponentInChildren<LocomotionSystem>(true);
            if (locomotion == null) {
                failures.Add($"{context} prefab '{entry.prefab.name}' has no LocomotionSystem; its gait speeds have no source to match.");
                return;
            }

            var locomotionObject = new SerializedObject(locomotion);
            var moveSpeedStat = ReadObjectReference(locomotionObject, "moveSpeedStat") as StatDefinition;
            if (moveSpeedStat == null) {
                failures.Add($"{context} prefab '{entry.prefab.name}' has a LocomotionSystem with no move speed stat.");
                return;
            }

            float baseSpeed = ResolveBaseStat(entry.prefab, moveSpeedStat);

            CompareGait(entry.walkSlowSpeed, baseSpeed, ReadFloat(locomotionObject, "walkSlowMultiplier", 1f), "walkSlowSpeed", context, failures);
            CompareGait(entry.walkSpeed, baseSpeed, 1f, "walkSpeed", context, failures);
            CompareGait(entry.jogSpeed, baseSpeed, ReadFloat(locomotionObject, "jogMultiplier", 1f), "jogSpeed", context, failures);
            CompareGait(entry.sprintSpeed, baseSpeed, ReadFloat(locomotionObject, "sprintMultiplier", 1f), "sprintSpeed", context, failures);
            CompareGait(entry.crouchSpeed, baseSpeed, ReadFloat(locomotionObject, "crouchMultiplier", 1f), "crouchSpeed", context, failures);
            CompareGait(entry.crouchFastSpeed, baseSpeed, ReadFloat(locomotionObject, "crouchFastMultiplier", 1f), "crouchFastSpeed", context, failures);

            ValidateVerticalParity(entry, context, failures);
        }

        /// <summary>
        /// Compares the row's gravity and jump impulse against the actor that owns them in the Unity
        /// build, since both ends of the wire have to integrate airborne motion with identical numbers.
        /// </summary>
        private static void ValidateVerticalParity(NetPrefabEntry entry, string context, List<string> failures) {
            var actor = entry.prefab.GetComponentInChildren<Actor>(true);
            if (actor == null) {
                failures.Add($"{context} prefab '{entry.prefab.name}' has no Actor; its gravity and jump velocity have no source to match.");
                return;
            }

            var actorObject = new SerializedObject(actor);

            CompareValue(entry.gravity, ReadFloat(actorObject, "gravity", entry.gravity), "gravity", "Actor.gravity", context, failures);
            CompareValue(entry.jumpVelocity, ReadFloat(actorObject, "jumpSpeed", entry.jumpVelocity), "jumpVelocity", "Actor.jumpSpeed", context, failures);
        }

        /// <summary>
        /// The prefab's authored base value for a stat, falling back to the definition's default when
        /// the stat sheet does not override it — which is exactly what <c>StatSheet.GetBase</c> does at
        /// runtime.
        /// </summary>
        private static float ResolveBaseStat(GameObject prefab, StatDefinition stat) {
            var statSheet = prefab.GetComponentInChildren<StatSheet>(true);
            if (statSheet == null) return stat.defaultValue;

            var statSheetObject = new SerializedObject(statSheet);
            SerializedProperty baseStats = statSheetObject.FindProperty("baseStats");
            if (baseStats == null || !baseStats.isArray) return stat.defaultValue;

            for (int entryIndex = 0; entryIndex < baseStats.arraySize; entryIndex++) {
                SerializedProperty element = baseStats.GetArrayElementAtIndex(entryIndex);
                if (element.FindPropertyRelative("stat")?.objectReferenceValue != stat) continue;

                return element.FindPropertyRelative("value")?.floatValue ?? stat.defaultValue;
            }

            return stat.defaultValue;
        }

        private static void CompareGait(
            float authored, float baseSpeed, float multiplier, string fieldName, string context, List<string> failures) {
            float expected = baseSpeed * multiplier;
            if (IsWithinTolerance(authored, expected)) return;

            failures.Add(
                $"{context} {fieldName} is {authored:0.###} but the prefab moves at {expected:0.###} " +
                $"({baseSpeed:0.###} base x {multiplier:0.###}); the server would simulate a different speed than the client.");
        }

        private static void CompareValue(
            float authored, float expected, string fieldName, string sourceName, string context, List<string> failures) {
            if (IsWithinTolerance(authored, expected)) return;

            failures.Add($"{context} {fieldName} is {authored:0.###} but {sourceName} is {expected:0.###}.");
        }

        private static bool IsWithinTolerance(float authored, float expected) {
            float tolerance = Mathf.Max(absoluteTolerance, Mathf.Abs(expected) * relativeTolerance);

            return Mathf.Abs(authored - expected) <= tolerance;
        }

        /// <summary>
        /// Reads a serialized float, warning rather than failing when the field is gone.
        /// </summary>
        /// <remarks>
        /// A renamed field means this validator is out of date, which is a problem for whoever maintains
        /// it and not a reason to fail somebody's build — so the caller's own value is handed back and
        /// the comparison passes.
        /// </remarks>
        private static float ReadFloat(SerializedObject source, string propertyName, float fallback) {
            SerializedProperty property = source.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Float) return property.floatValue;

            Debug.LogWarning($"{logPrefix}: {source.targetObject.GetType().Name} declares no float '{propertyName}' — the parity check is out of date.");
            return fallback;
        }

        private static UnityEngine.Object ReadObjectReference(SerializedObject source, string propertyName) {
            SerializedProperty property = source.FindProperty(propertyName);
            if (property == null) return null;
            if (property.propertyType != SerializedPropertyType.ObjectReference) return null;

            return property.objectReferenceValue;
        }

        internal static List<string> FindAssetPaths(string filter) {
            var assetPaths = new List<string>();

            foreach (string assetGuid in AssetDatabase.FindAssets(filter)) {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                assetPaths.Add(assetPath);
            }

            return assetPaths;
        }
    }
}
