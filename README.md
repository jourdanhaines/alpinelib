# AlpineLib

A UPM package of reusable gameplay systems for Unity 6000.0+: dependency injection, tagged
stats, needs, anatomical injuries, melee combat, projectiles, skills, equipment, passive
progression, AI perception, pointer input, spawning, camera rigs, an input facade, menu screens,
an application bootstrap, and client/server netcode with sessions, chat and a dedicated server.
Runtime code
lives in the `FluxInteractive.AlpineLib` assembly under the
`AlpineLib.*` namespaces; editor tooling lives in `FluxInteractive.AlpineLib.Editor`. Netcode and
chat live in engine-free assemblies of their own — see
[Netcode, sessions and dedicated server](#netcode-sessions-and-dedicated-server). The package
depends on the Universal Render Pipeline (17.0.0) because the visibility overlay shader,
`AlpineLib/VisibilityDarken`, is URP-only — as is the material fading in `VisibilityOccludable` —
and on the Input System (1.19.0), which `AlpineLib.Input` is a facade over.
The library ships systems and a small amount of neutral data (a humanoid body plan, an injector
prefab, a pointer indicator prefab, the visibility shader); it ships no game art, no stat
definitions and no attack definitions — those are authored per game.

## Install

Add one of the following to `Packages/manifest.json`:

```json
"com.fluxinteractive.alpinelib": "https://github.com/jourdanhaines/alpinelib.git"
```

```json
"com.fluxinteractive.alpinelib": "file:/absolute/path/to/alpinelib"
```

The `file:` form is for developing the library alongside a game — edits are picked up by the
consuming project's next compile.

## Modules

| Namespace | What it provides | Key types |
| --- | --- | --- |
| `AlpineLib.DI` | Reflection-based dependency injection. A `RuntimeInstaller` spawns the injector from a `BeforeSceneLoad` hook; the injector registers every `IDependencyProvider` in the scene, then injects `[Inject]` fields and methods on every scene object, re-running on each scene load. Unresolved dependencies throw. | `Injector`, `InjectAttribute`, `ProvideAttribute`, `IDependencyProvider` |
| `AlpineLib.Arch` | Generic MonoBehaviour singleton with lazy find-or-create access and a quit guard so teardown never resurrects an instance. | `Singleton<T>` |
| `AlpineLib.App` | Code-driven startup: `AppBootstrapper.Boot<TRoot>()` forces the injector into existence and creates a persistent `AppRoot` whose `InstallServices` override adds application-wide service components. A boot scene holding `BootSceneController` hands over to the first real scene through the awaitable scene transition service. | `AppBootstrapper`, `AppRoot`, `BootSceneController`, `ISceneTransitionService`, `SceneTransitionService` |
| `AlpineLib.Tags` | Tag vocabulary as data. A `TagDefinition` asset is pure identity — matching is reference equality, there is no hierarchy — and a `TagSet` is a serializable bag of them, used both to describe a thing ("this hit is Eldritch, Spell, Projectile") and to condition a modifier on one ("applies only to Spells"). `TagSet.Matches` asks whether a requirement is a subset of a context; an empty or null requirement matches everything, so unconditional data needs no authoring. | `TagDefinition`, `TagSet` |
| `AlpineLib.Stats` | Data-driven stat sheets. Stats are `ScriptableObject` assets, so the vocabulary lives in game data. A sheet holds authored base values, accumulates modifiers and resolves values as `(base + sum of Flat) * max(0, 1 + sum of Percent) * product of Multiply` — Percent is the additive "increased" bucket, Multiply the multiplicative "more" bucket — so evaluation is order-independent and modifiers are withdrawn by source object. Each modifier may carry a `TagSet` and then contributes only when those tags are a subset of the query context: the cached `Get(stat)` uses an empty context and reports the unconditional value, while `Get(stat, context)` and `Evaluate(stat, context, baseOverride)` fold conditional modifiers in on demand. `StatConverter` keeps derived stats live, re-applying each `StatConversionDefinition` ("two Health per Strength") off `StatSheet.OnChanged` instead of computing it once at startup. | `StatSheet`, `StatDefinition`, `StatModifier`, `ModifierOperation`, `StatEntry`, `StatConverter`, `StatConversionDefinition` |
| `AlpineLib.Needs` | Depleting resources (health, hunger, fatigue). A need decays over time, raises `OnDepleted` once at zero, and tracks authored `Threshold` bands, applying each band's stat modifiers to the sibling `StatSheet` while it holds. Games subclass `Need` and supply the thresholds. | `Need`, `Threshold`, `ThresholdDirection`, `MoodleType` |
| `AlpineLib.Vitals` | Consumable pools — health, mana, stamina, a shield. A pool's ceiling and refill rate come from its `ResourceDefinition` asset, each optionally bound to a `StatDefinition` and read live from the sibling `StatSheet`, so buffs and injuries move the pool without extra wiring. Every change goes through `Drain`, `Spend` or `Restore`, keeping mutation on a small set of methods; draining restarts the regeneration delay, so a pool under sustained fire never recharges. `ResourceSet` indexes an object's pools by definition, and `DamageAbsorptionChain` runs damage through an ordered stack of them, so a shield listed ahead of health soaks the hit until it empties and the overflow cascades on. | `ResourceDefinition`, `ResourcePool`, `ResourceSet`, `DamageAbsorptionChain` |
| `AlpineLib.Body` | Anatomical damage model. A body plan asset lists body part assets; the system builds runtime parts from it, holds `Injury` instances per part, applies their stat debuffs, ticks bleeding and rolls timed injury conditions. It never touches health itself — bleeding and hit damage are reported through `OnDamageTick` for the game to interpret. | `BodySystem`, `BodyPlanDefinition`, `BodyPartDefinition`, `BodyPart`, `Injury`, `InjuryDefinition`, `InjuryCondition` |
| `AlpineLib.Combat` | Animator-driven melee. The combat system fires an attack's trigger, opens its hit box inside a normalized-time damage window, enforces cooldown and a per-attack rotation budget, then rolls a weighted outcome and applies the resulting injury to the struck body. Hurt boxes tag colliders with the body part they stand in for. Contact stagger is a separate subsystem carrying both the mover and target sides. Applying a hit is split out from deciding one: whoever owns a hit box fills a `DamagePacket` and hands it to `DamageResolver`, so a swing, a projectile and a hazard all land damage through the same path. | `CombatSystem`, `AttackDefinition`, `AttackOutcome`, `HitBox`, `HurtBox`, `IHitBoxOwner`, `IHitReceiver`, `DamagePacket`, `DamageResolver`, `StaggerSystem` |
| `AlpineLib.Skills` | The actor's skill bar. `SkillSystem` holds every granted skill, bills its costs all-or-nothing, tracks cooldowns, and drives the active one off the animator rather than a timer. A skill's body domain decides what it takes from the actor: `FullBody` plays on the base layer, suppresses locomotion and skips root motion unless the skill opts in; `UpperBody` blends the upper-body layer in over unchanged locomotion and merely slows the actor. `MeleeSkillDefinition` opens the hit box inside a normalized-time damage window, `ProjectileSkillDefinition` releases a volley at its spawn time. Damage is a stat query filtered by the skill's `TagSet`, so "increased Melee damage" reaches only melee skills. | `SkillSystem`, `SkillDefinition`, `SkillBodyDomain`, `MeleeSkillDefinition`, `ProjectileSkillDefinition`, `SkillInstance`, `ResourceCost` |
| `AlpineLib.Projectiles` | Fire-and-forget damage carriers. A launched `Projectile` translates along a fixed direction until its lifetime expires or it overlaps a `HurtBox` that is not its owner's, then applies its `DamagePacket` and destroys itself; it is inert and triggerless until `Launch`. Movement is transform translation, not physics — the rigidbody exists only to generate trigger callbacks and is forced kinematic. `ProjectilePatterns` turns a pattern and a shot index into a direction, so volley geometry lives outside the projectile. Version one passes through level geometry: keep lifetimes short. | `Projectile`, `ProjectilePattern`, `ProjectilePatterns` |
| `AlpineLib.Equipment` | The held weapon and everything it projects onto its actor. `EquipmentSystem.Equip` unequips first and then applies the weapon's locomotion override, spawns its visual on a named bone, adds its implicit stat modifiers, grants its skills and publishes its damage as `SkillSystem.WeaponDamageProvider` — every side effect keyed on the system so `Unequip` reverses it exactly. A weapon's `locomotionOverride` must be built on the actor's own base controller; an override on a different base swaps the state machine wholesale and desynchronises anything reading state tags. | `EquipmentSystem`, `WeaponDefinition` |
| `AlpineLib.Progression` | Classes, specializations and passive trees. `ProgressionSystem` grants and revokes `PassiveNodeDefinition`s, re-creating each node's authored modifiers under a private source key so revoking one node withdraws exactly its own contribution and nothing else; grants are idempotent, so a whole tree can be re-applied on load. Trees are flat in v1 — an unordered bag of nodes with no adjacency or point costs — and a `ClassDefinition` or `SpecializationDefinition` simply grants all of them. Node modifiers keep their tags, so conditional passives stay conditional. | `ProgressionSystem`, `ClassDefinition`, `SpecializationDefinition`, `PassiveTreeDefinition`, `PassiveNodeDefinition` |
| `AlpineLib.Perception` | AI senses. `ViewCone` sweeps and raycasts for line of sight each fixed step; `NoiseEmitter` broadcasts world noises to every registered `NoiseListener` in radius; `NoiseEmitterFootstep` emits on foot plants detected from humanoid toe bone height; `TargetMemory` stores last known positions. Distances, angles and radii accept `Func<float>` providers so a game can drive them from stats. | `ViewCone`, `NoiseEmitter`, `NoiseListener`, `NoiseEmitterFootstep`, `TargetMemory` |
| `AlpineLib.Perception.Visibility` | Player-perspective visibility. `VisibilityField` publishes its view cone and hearing circle as `_AlpineVisibilitySource*` shader globals, maintains a ground quad drawn with `AlpineLib/VisibilityDarken` to darken everything outside them, and answers the matching CPU query. `VisibilityOccludable` crossfades an object's renderers as it enters and leaves that region. One field exists at a time. | `VisibilityField`, `VisibilityOccludable` |
| `AlpineLib.Input` | Input facade over the Input System. `InputReader` is an abstract MonoBehaviour that owns one action map of a serialized `InputActionAsset`, resolves it in `Awake`, enables and disables it with the component, and hands subclasses cached actions through `ResolveAction("Player/Move")`. Games subclass it once per map with `const` action paths and typed properties, so the reader is the only file in a game that spells an action name and a rename in the `.inputactions` asset breaks exactly one file, loudly, on resolve. Polling rather than callbacks — a controller already runs per frame, and `WasPressedThisFrame` covers edge-triggered input. The asset is required and unguarded: a reader with none assigned throws in `Awake` rather than silently reporting no input. Note that this namespace shadows the legacy `UnityEngine.Input` class inside `AlpineLib.*`, which must therefore be spelled out in full. | `InputReader` |
| `AlpineLib.Pointer` | World-space pointer input. `PointerService` owns the single scene-wide pointer raycast, dispatching enter, exit and interact to the `PointerInteractable` under the pointer, and provides itself to the injector. The device sits behind `IPointerSource`; a legacy-`Input` mouse source is the default. | `IPointerService`, `PointerService`, `IPointerSource`, `MousePointerSource`, `PointerInteractable`, `IInteractable`, `PointerIndicator` |
| `AlpineLib.Cameras` | Camera rigs behind one contract. `ICameraRig` is what a game steers: look deltas in through `AddLookInput`, a target, readable yaw and pitch, yaw-only `PlanarForward`/`PlanarRight` so movement stays camera-relative, and a `CameraAnchor` transform carrying the pose a camera should adopt. Rigs never own a camera, which is what makes one camera shared between two of them possible. `ThirdPersonCameraRig` is an orbiting spring arm whose rig object is the pivot: it chases the target with damped follow and carries the orbit, while its anchor sits at a shoulder offset behind and a sphere cast pulls that anchor in when geometry gets between the two. `FirstPersonCameraRig` is the plainest rig in the module — an eye offset applied in the target's local space, a pitch clamp, no damping and no collision probe, because the camera is the player's head; the eye height rides the target's `CharacterController` capsule, so an actor that crouches takes the camera down with it instead of leaving it inside the ceiling it just ducked under. `CameraPerspectiveController` owns the shared camera and slides it between the two anchors over `blendDuration` along an authored curve, copying yaw and pitch across on the switch so aim is continuous, and raising `OnPerspectiveChanged` at the *start* of the blend so a game can hide the player's body before the camera travels through it. It runs at `[DefaultExecutionOrder(200)]`, after the rigs' own `LateUpdate`. `Isometric3DCameraController` predates the contract: a fixed offset aimed at its target, no smoothing, no collision. | `ICameraRig`, `ThirdPersonCameraRig`, `FirstPersonCameraRig`, `CameraPerspectiveController`, `CameraPerspective`, `Isometric3DCameraController` |
| `AlpineLib.UI` | Menu and overlay plumbing, dependency-free — a `CanvasGroup` and nothing else, so the package still needs no uGUI or TextMeshPro reference. `UIScreen` fades a screen in and out over `fadeDuration` on unscaled time, so menus animate at the same rate whatever the game does to its timescale, and flips `interactable` and `blocksRaycasts` at the *start* of each fade: a half-arrived screen already swallows clicks, and a dissolving one stops taking them. `IsVisible` tracks intent rather than alpha, and the GameObject deliberately stays active while hidden so a fade is never stranded — drive screens through `Show`/`Hide`, never `SetActive`. `UIScreenStack` is a per-scene LIFO of screens with an optional `hideBeneath` for full-screen menus; overlays leave it off. | `UIScreen`, `UIScreenStack` |
| `AlpineLib.Spawning` | Scene-placed spawn points. A spawner picks a random position inside its config's radius, optionally snaps it to the ground, and instantiates the configured prefab. The service does typed spawner lookups (games filter by declaring empty `Spawner` subclasses), spawns everything on start by default, and raises `OnSpawned`. | `Spawner`, `SpawnConfig`, `ISpawnerService`, `SpawnerService` |
| `AlpineLib.Actors` | The actor itself: a `CharacterController` character that can be possessed by a `Controller` brain, moves either in code or by root motion scaled to its current move speed, and publishes speed and turn to the animator. It owns movement and liveness only — `Kill()` raises `OnDeath` and everything else reacts. Code-driven actors carry horizontal momentum through the air: airborne velocity is seeded from the last grounded stride and steered towards input at a tunable `airAcceleration` (with optional `airDrag`), while grounded movement stays direct and instant; root-motion actors are exempt. `ActorSubsystem` is the base for behaviours that self-disable on their owner's death. | `Actor`, `IActor`, `IMortal`, `Controller`, `ActorSubsystem`, `RootMotionForwarder`, `IRootMotionSuppressor` |
| `AlpineLib.Actors.Locomotion` | Gait handling: `LocomotionSystem` translates the current gait into move-speed and noise-radius stat modifiers, swapped out whenever the gait changes. Walk is neutral. `CrouchSystem` owns capsule geometry only — it lerps the `CharacterController` between a standing and a crouched height, recentring so the feet stay planted, and refuses to stand while an upward sphere cast finds a ceiling. Standing is a request, not a command: releasing crouch under a low ceiling latches `WantsToStand` and the actor pops up on its own the first frame the ceiling clears, which is what makes a crouch tunnel feel right. The two are independent — a controller that crouches an actor calls both, because neither speed nor noise is this system's business. | `LocomotionSystem`, `LocomotionState`, `CrouchSystem` |
| `AlpineLib.Animation` | Animator parameter hashes shared by the actor systems (see the contract below), plus a helper that re-rolls a blend tree index whenever a watched parameter crosses a threshold, so idles vary, `ExpressionSystem`, which fires face-expression triggers (auto-blink plus a scripted `Play`) on a masked expressions layer, and `IdleVariationSystem`, which fires a random variation trigger (optionally paired with an expression) after a random stretch of genuine idleness. | `AnimatorParameters`, `AnimateRandomIndex`, `ExpressionSystem`, `IdleVariationSystem`, `IdleVariation` |
| `AlpineLib.Utilities` | Weighted random selection over any read-only list. | `WeightedRandom` |
| `AlpineLib.Editor` | Editor tooling. `BootSceneLoader` redirects play mode to a designated boot scene (`AlpineLib/Editor/Play From Boot Scene`). `RegenerateProjectFiles` syncs the external script editor's project files on demand or after every script reload, for scripts added outside Unity. `AssetValidator` is a batch-mode integrity gate over prefabs, ScriptableObjects and build scenes: `-batchmode -quit -executeMethod AlpineLib.Editor.AssetValidator.ValidateAll`. `BodySystemEditor` adds a play-mode inspector showing injuries, bleed rate breakdown and condition progress per body part. | `BootSceneLoader`, `RegenerateProjectFiles`, `AssetValidator`, `BodySystemEditor` |

## Netcode, sessions and dedicated server

A client/server stack, not a peer mesh and not a listen server: even a player who hosts from the menu
starts a real server process and then connects to it like anybody else. The simulation is engine-free
so the same sources run inside Unity and inside a headless .NET process — that is the whole reason the
transport, protocol and session layers live outside the runtime assembly.

### Assemblies

| Assembly / folder | Namespace | What it provides | Key types |
| --- | --- | --- | --- |
| `Netcode/` → `FluxInteractive.AlpineLib.Netcode` (netstandard2.1, no `UnityEngine`) | `AlpineLib.Netcode.*` | The wire. LiteNetLib transport behind `INetTransport`, a `MessageRouter` over one byte of message id, `NetReader`/`NetWriter` and quantization, clock sync, and the two facades a game holds — `NetClient` and `NetServer`. `MessageIdBudget` is the whole-protocol view of which ids the library has spoken for and which band (136–191) a game may author in; the id map is a permanent wire contract, so a retired id stays reserved. | `NetClient`, `NetServer`, `MessageRouter`, `MessageIdBudget`, `NetConfig`, `NetProtocol`, `NetReader`, `NetWriter`, `NetClock` |
| `Netcode/Replication/` | `AlpineLib.Netcode.Replication` | Entities, snapshots and prediction. `PawnMotor` is the shared character step; the owning client predicts with it and the server re-runs it, `MovementValidator` judging the result. Remote entities are smoothed by `StateInterpolator` over an `InterpolationTimeline`. `PawnState` carries a **carrier id**: a pawn standing on a moving deck replicates its position *relative to that carrier* rather than in world space, so a rider on a train moving 30 m/s is judged, interpolated and corrected on the one metre a second it is actually responsible for. | `ServerReplication`, `ClientReplication`, `NetEntity`, `PawnMotor`, `PawnState`, `PawnInput`, `MovementValidator`, `StateInterpolator`, `PredictionBuffer` |
| `Netcode/Replication/StateChannel/` | `AlpineLib.Netcode.Replication` | A generic replicated dictionary for game state that is not a pawn — a train's velocity, a door's angle, a score. The server publishes records under a game-owned message id; the channel sends keyframes to a joiner and deltas to everyone else, chunked to fit a datagram, so a late arrival is caught up without the steady stream paying for it. | `ServerStateChannel<T>`, `ClientStateChannel<T>`, `StateChannelRecord`, `StateChannelEnvelope` |
| `Netcode/Sessions/` | `AlpineLib.Netcode.Sessions` | The front desk. Create or join a session by six-character join code, a roster with an owner, phases, rejoin and late-load policies, ownership transfer when the owner leaves, and anonymous auth behind `IAuthProvider`/`IAuthValidator`. `SessionClient` is what a client holds; `SessionHost` is what a server runs. | `SessionClient`, `SessionHost`, `ISessionFrontDesk`, `SessionConfigData`, `SessionProfileData`, `LobbySnapshot`, `JoinCodeGenerator`, `SessionEndReason` |
| `Netcode/Sessions/Claims/` | `AlpineLib.Netcode.Sessions.Claims` | Server-arbitrated exclusive slots: one player at a time may hold a lever, a turret, a workbench. The server owns the map and broadcasts every change; clients read `ClientClaims` and never assume a grant. A slot is a `ushort` the game derives however it likes, and everything a holder is entitled to do is checked against the map on the server. | `ServerClaimRegistry`, `ClientClaims` |
| `Netcode/Sessions/Spawning/` | `AlpineLib.Netcode.Sessions.Spawning` | Where a joining player's pawn appears: an authored list (`ListSpawnPlacement`) or a ring around the origin (`RingSpawnPlacement`), both behind `ISpawnPlacement`, with `SpawnGroundProbe` dropping the seat onto the collision world's floor. `SessionPawnSpawner` gives each member a body as they arrive and takes it back when they go. | `SessionPawnSpawner`, `ISpawnPlacement`, `ListSpawnPlacement`, `RingSpawnPlacement`, `SpawnGroundProbe` |
| `Netcode/Collision/` | `AlpineLib.Netcode.Collision` | The engine-free collision world both ends step against: planes, boxes, spheres and capsules in a 2 m XZ grid, plus translation-only movers whose pose is a pure function of the sim tick. Scenes are authored in Unity, baked by `AlpineLib/Editor/Export Scene Geometry` into a `.geo` blob the server reads from `config/geometry/`, and resolved on the client through a `SceneGeometryRegistry`. Same bytes, same arithmetic, so ramps, walls, step-ups and platform rides resolve identically. | `CollisionWorld`, `CollisionGrid`, `CollisionResolver`, `SceneGeometry`, `SceneGeometryCodec`, `MoverDefinition` |
| `Runtime/Collision/` | `AlpineLib.Collision` | Authoring for the above. `NetStaticGeometry` and `NetMover` mark what the bake picks up, `SceneGeometryAsset` is the baked blob as a Unity asset, `SceneGeometryRegistry` maps a scene name to one, and `NetMoverView` draws a mover at the pose the sim computes rather than one animated beside it. | `NetStaticGeometry`, `NetMover`, `NetMoverView`, `SceneGeometryAsset`, `SceneGeometryRegistry` |
| `Chat/` → `FluxInteractive.AlpineLib.Chat` (netstandard2.1, no `UnityEngine`) | `AlpineLib.Chat` | Channels, identities, a delivery ledger, rate limiting and a filter chain, behind `IChatProvider` so a game can swap the transport. Rides one envelope message id, kept in its own assembly so a game that wants no chat links none of it. | `ChatServerService`, `IChatProvider`, `LiteNetChatProvider`, `ChatChannelId`, `IChatFilter`, `TokenBucket` |
| `Runtime/Networking/` | `AlpineLib.Networking` | The Unity side of replication. `NetworkService` owns the client/server facades and the mode; `NetEntityView` marks a replicated object and `NetEntitySpawner` instantiates them from an append-only `NetPrefabRegistry` (a row's index is its prefab id on the wire). `NetActorSync` walks the visible `Actor` toward the predicted pose, `NetController` possesses a remote pawn, and `PossessionGate` disables the local-only behaviours — input reader, controller, interactor — on a pawn that is not ours, which is why those components ship *authored disabled* on a prefab. `NetCarrier` gives a moving deck the session-wide id riders name, `NetCarrierRegistry` resolves it, and `NetCarrierFrame` converts between the two frames. | `NetworkService`, `NetEntityView`, `NetEntitySpawner`, `NetActorSync`, `NetController`, `PossessionGate`, `NetCarrier`, `NetCarrierRegistry`, `NetPrefabRegistry`, `NetworkConfig` |
| `Runtime/Sessions/` | `AlpineLib.Sessions` | The Unity side of sessions and hosting. `SessionConfig` is the single asset that says how a build networks — transport tuning, session rules, lobby, matches, matchmaking, prefab registry, spawn placement — and the same asset reaches three places: the client that loads it, the server that reads it as exported JSON, and every joining client that receives the session half in `JoinAccepted`. `SessionService` drives create/join/leave and the scene flow; `LocalServerLauncher` starts the bundled server for a player who hosts, reading its `[ready] port=N` line off stdout; `ServerBundleConfig` says which published build to carry into a player build. | `SessionConfig`, `SessionService`, `SessionSceneFlow`, `LocalServerLauncher`, `LocalServerConfig`, `LocalServerPaths`, `ServerBundleConfig`, `MatchmakingConfig`, `SpawnPlacementConfig` |
| `Server~/` (net10.0, excluded from Unity by the `~`) | `AlpineLib.Server` | The dedicated server host a game builds its own executable on: `AlpineServerHost` (Generic Host, argument parsing, `[ready]` line, idle shutdown), `GameLoopService` (fixed tick, snapshot rate, `GameThreadInbox` for off-thread work), `SessionRegistry`/`SessionEntry` (one live session each, front desk, claims, spawner, chat adapter), and `SessionConfigLoader` binding the exported `session-config.json`. A game plugs in through `ISessionModuleFactory`/`ISessionModule`. | `AlpineServerHost`, `ServerHostBuilder`, `GameLoopService`, `SessionRegistry`, `SessionEntry`, `ISessionModule`, `ISessionModuleFactory`, `SessionConfigLoader` |
| `Editor/` | `AlpineLib.Editor` | `SessionConfigExporter` flattens a `SessionConfig` into the server's JSON (`AlpineLib/Editor/Export Session Config`, or `Export(config, path)` from a build script). `SceneGeometryExporter` bakes the `.geo` blob. `SessionConfigValidator` checks the assets, `ServerBundleBuildStep` copies a published server into a player build, and `ServerAddressOverrideWindow` points a build at another server without touching the asset. | `SessionConfigExporter`, `SceneGeometryExporter`, `SessionConfigValidator`, `ServerBundleBuildStep`, `ServerAddressOverrideWindow` |

### How a game builds a server on `Server~`

`Server~` is a folder Unity ignores and `dotnet` does not, which is what lets one repository hold both
sides. A game adds its own console project referencing `Server~/AlpineLib.Server`, plus a
netstandard2.1 project compiling whatever simulation sources the Unity client also compiles — that
shared assembly is the only guarantee both ends step the same arithmetic.

1. **`Program.cs`** hands `AlpineServerHost` the command line and attaches a module factory:
   `--port`, `--config`, `--idle-exit-seconds` and `--max-sessions` are parsed for you, and the host
   prints exactly one machine-readable line, `[ready] port=N`, once the socket is bound. With
   `--port 0` that is the port the OS actually gave, which is how a launcher finds a server it started.
2. **`ISessionModuleFactory`** registers the game's own message ids on the router once, and builds one
   `ISessionModule` per live session. The module is ticked by the game loop and is where the game's
   authoritative state lives; it publishes through a `ServerStateChannel<T>` and reads claims off the
   entry's `ServerClaimRegistry`.
3. **Configuration** is exported from the editor rather than hand-written: `SessionConfigExporter`
   writes `config/session-config.json` from the same `SessionConfig` asset the client runs on, and a
   game's own exporter writes whatever else its scenes hold beside it. Both are read at startup and a
   missing or malformed one is fatal, deliberately — a server that came up without them would accept
   connections and then publish nothing, which reads to a player as a frozen world and no log line.
4. **Publishing** is a self-contained single file into `Build/Server/<rid>` with `config/` beside it.
   `ServerBundleConfig` names that root and one runtime identifier per platform, and
   `ServerBundleBuildStep` copies the matching folder into a player build so a hosting player has a
   server to start; in the editor `LocalServerConfig.editorServerDirectory` points at the same place.

### The session, end to end

A player who hosts: `SessionService` asks `LocalServerLauncher` for a server, which starts the bundled
executable on port 0 and waits for its `[ready]` line; the client then connects to `127.0.0.1:<that
port>`, creates a session and is handed a join code. A friend types the address and the code and joins
the same session on the same process. Both are ordinary clients — the host has no special path — so a
session that outlives its host simply transfers ownership, and the launcher's idle window stops an
orphaned server once everybody has gone.

## Animator contract

An animator controller driven by the actor systems must satisfy the following. Nothing validates
it at import time; a missing parameter or state tag shows up as an attack that never starts or a
stagger that never resolves.

**Float parameters**

| Parameter | Meaning |
| --- | --- |
| `Speed` | Signed forward locomotion speed. Written every frame by `Actor`. |
| `Turn` | Signed turn rate. Written every frame by `Actor`. |
| `SlowWalk` | 0 to 1 blend towards the slow walk / aiming gait. |
| `StrafeX`, `StrafeY` | Local-space strafe direction, used while the actor holds a facing independent of its movement direction. |

The `Speed` and `Turn` parameter names are serialized on `Actor` and may be renamed per actor;
the rest are fixed.

**Bool parameters**

| Parameter | Meaning |
| --- | --- |
| `Grounded` | Character controller ground contact. Written every frame by `Actor`, but only when the controller declares it — controllers with no airborne states are never written to. |
| `Crouching` | Crouch state, written by `CrouchSystem` on change, but only when the controller declares it — controllers without crouched locomotion are never written to. |

**Trigger parameters**

| Trigger | Purpose |
| --- | --- |
| `Hit` | Hit reaction, fired when the actor takes damage. |
| `Die` | Death animation, fired once. |
| `Jump` | Fired by `Actor.Jump` when a grounded, living actor leaves the ground. Only ever set from there, so an animator that never declares it never sees it — zombies and jumpless games need no parameter. The name is serialized on `Actor` and may be renamed per actor. |
| One per attack | Fired by `CombatSystem`, named by each `AttackDefinition.animationTrigger` (for example `Scratch`). |
| One per skill | Fired by `SkillSystem`, named by each `SkillDefinition.animationTrigger` (for example `LungingStrike`). |
| Stagger trigger | Fired by `StaggerSystem`, named by its `animationTrigger` field (`Stagger` by default). |

`Hit` and `Die` are not fired by the library — `AnimatorParameters` caches their hashes for the
game's own damage and death handling to use.

**State tags** — the systems poll the current state's tag, not state names, so state naming is
free:

| Tag | Required on |
| --- | --- |
| `Attack` | Every attack state, so `CombatSystem` can see an attack playing and time its damage window against the state's normalized time. `SkillSystem` tracks full-body skills through the same tag. |
| `UpperSkill` | Every upper-body skill state, on the upper-body layer, so `SkillSystem` can time a skill that plays over locomotion. |
| `Stagger` | Every stagger state, so `StaggerSystem` knows when the reaction ends. The tag is configurable per actor via `animationTag`. |

**Layers** — `ExpressionSystem` fires expression triggers (auto-blink on a randomized timer, plus
`Play(trigger)` for scripted moments) that expect a masked expressions layer — face bones only,
empty default state, weight 1 — so expressions ride over any base motion. Author it above the
`UpperBody` layer described next, which keeps layer 1 free.
`IdleVariationSystem` fires at the base layer: each variation's trigger must reach a base-layer
state from the locomotion state (`locomotionStateName`, `Locomotion` by default), and nothing
fires unless the animator is resting in that state with `Speed` near zero and `Grounded` true.
A variation's paired expression trigger rides the expressions layer above.
`SkillSystem` expects layer 0 to be the base locomotion layer and layer 1 to be an
override layer named `UpperBody` carrying an avatar mask over the upper body. `SkillSystem` owns
that layer's weight, blending it in and out at `upperBodyBlendSpeed`, so it must be authored at a
default weight of 0. Both indices are serialized on `SkillSystem`, so a
controller laid out differently can be pointed at the right layers, but a skill whose trigger
cannot reach a state tagged `UpperSkill` on that layer never completes and holds the skill bar
until something cancels it.

**Motion** — locomotion, attack and stagger clips must carry root motion, and the actor's
animator needs `applyRootMotion` enabled — `Actor` sets it from its own Use Root Motion toggle —
so movement arrives through `OnAnimatorMove` rather than being integrated in code. When the
animator sits on a child object, `RootMotionForwarder` relays the delta up to the actor; it is
added automatically.

## Actor prefab convention

Actors are authored as a chain of prefab variants:

1. **Model** — the imported FBX, art only.
2. **Base actor prefab** — a variant of the model carrying the system roster every actor of that
   shape shares: `StatSheet`, `Actor`, `LocomotionSystem`, and whichever subsystems apply
   (`BodySystem`, `CombatSystem`, `StaggerSystem`, `VisibilityOccludable`, hurt boxes).
3. **Species variants** — variants of the base that override the animator controller, the base
   stat values, and add the components specific to that species (its controller/brain, its
   perception components, its needs).

Games supply the data these systems reference — `StatDefinition`, `BodyPlanDefinition` and
`BodyPartDefinition` assets, `InjuryDefinition`s, `AttackDefinition`s, `SpawnConfig`s,
`TagDefinition`s and the `StatConversionDefinition`s, `SkillDefinition`s, `WeaponDefinition`s and
passive trees built on them — and any
glue components that connect library systems to each other (for example, routing
`BodySystem.OnDamageTick` into a health `Need`). The library ships a humanoid body plan and its
body part assets as a starting point, plus the injector and pointer indicator prefabs; it ships
no game art and no game-specific data.
