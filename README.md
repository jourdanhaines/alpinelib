# AlpineLib

A UPM package of reusable gameplay systems for Unity 6000.0+: dependency injection, stats,
needs, anatomical injuries, melee combat, AI perception, pointer input, spawning and an
application bootstrap. Runtime code lives in the `FluxInteractive.AlpineLib` assembly under the
`AlpineLib.*` namespaces; editor tooling lives in `FluxInteractive.AlpineLib.Editor`. The package
depends on the Universal Render Pipeline (17.0.0) because the visibility overlay shader,
`AlpineLib/VisibilityDarken`, is URP-only — as is the material fading in `VisibilityOccludable`.
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
| `AlpineLib.Stats` | Data-driven stat sheets. Stats are `ScriptableObject` assets, so the vocabulary lives in game data. A sheet holds authored base values, accumulates modifiers and resolves final values through a lazily rebuilt cache; modifiers apply flat, then percent, then multiply, and are withdrawn by source object. | `StatSheet`, `StatDefinition`, `StatModifier`, `ModifierOperation`, `StatEntry` |
| `AlpineLib.Needs` | Depleting resources (health, hunger, fatigue). A need decays over time, raises `OnDepleted` once at zero, and tracks authored `Threshold` bands, applying each band's stat modifiers to the sibling `StatSheet` while it holds. Games subclass `Need` and supply the thresholds. | `Need`, `Threshold`, `ThresholdDirection`, `MoodleType` |
| `AlpineLib.Vitals` | Consumable pools — health, mana, stamina, a shield. A pool's ceiling and refill rate come from its `ResourceDefinition` asset, each optionally bound to a `StatDefinition` and read live from the sibling `StatSheet`, so buffs and injuries move the pool without extra wiring. Every change goes through `Drain`, `Spend` or `Restore`, keeping mutation on a small set of methods; draining restarts the regeneration delay, so a pool under sustained fire never recharges. `ResourceSet` indexes an object's pools by definition, and `DamageAbsorptionChain` runs damage through an ordered stack of them, so a shield listed ahead of health soaks the hit until it empties and the overflow cascades on. | `ResourceDefinition`, `ResourcePool`, `ResourceSet`, `DamageAbsorptionChain` |
| `AlpineLib.Body` | Anatomical damage model. A body plan asset lists body part assets; the system builds runtime parts from it, holds `Injury` instances per part, applies their stat debuffs, ticks bleeding and rolls timed injury conditions. It never touches health itself — bleeding and hit damage are reported through `OnDamageTick` for the game to interpret. | `BodySystem`, `BodyPlanDefinition`, `BodyPartDefinition`, `BodyPart`, `Injury`, `InjuryDefinition`, `InjuryCondition` |
| `AlpineLib.Combat` | Animator-driven melee. The combat system fires an attack's trigger, opens its hit box inside a normalized-time damage window, enforces cooldown and a per-attack rotation budget, then rolls a weighted outcome and applies the resulting injury to the struck body. Hurt boxes tag colliders with the body part they stand in for. Contact stagger is a separate subsystem carrying both the mover and target sides. | `CombatSystem`, `AttackDefinition`, `AttackOutcome`, `HitBox`, `HurtBox`, `IHitReceiver`, `StaggerSystem` |
| `AlpineLib.Perception` | AI senses. `ViewCone` sweeps and raycasts for line of sight each fixed step; `NoiseEmitter` broadcasts world noises to every registered `NoiseListener` in radius; `NoiseEmitterFootstep` emits on foot plants detected from humanoid toe bone height; `TargetMemory` stores last known positions. Distances, angles and radii accept `Func<float>` providers so a game can drive them from stats. | `ViewCone`, `NoiseEmitter`, `NoiseListener`, `NoiseEmitterFootstep`, `TargetMemory` |
| `AlpineLib.Perception.Visibility` | Player-perspective visibility. `VisibilityField` publishes its view cone and hearing circle as `_AlpineVisibilitySource*` shader globals, maintains a ground quad drawn with `AlpineLib/VisibilityDarken` to darken everything outside them, and answers the matching CPU query. `VisibilityOccludable` crossfades an object's renderers as it enters and leaves that region. One field exists at a time. | `VisibilityField`, `VisibilityOccludable` |
| `AlpineLib.Pointer` | World-space pointer input. `PointerService` owns the single scene-wide pointer raycast, dispatching enter, exit and interact to the `PointerInteractable` under the pointer, and provides itself to the injector. The device sits behind `IPointerSource`; a legacy-`Input` mouse source is the default. | `IPointerService`, `PointerService`, `IPointerSource`, `MousePointerSource`, `PointerInteractable`, `IInteractable`, `PointerIndicator` |
| `AlpineLib.Cameras` | Camera rigs. `Isometric3DCameraController` holds a fixed offset and aims at its target, with no smoothing or collision handling. `ThirdPersonCameraRig` is an orbiting spring arm whose rig object is the pivot: it chases the target with damped follow and carries the yaw and pitch orbit, while a child anchor holds the camera at a shoulder offset behind it and a sphere cast pulls that anchor in when geometry gets between the two. It reads no input device of its own — the game feeds it look deltas through `AddLookInput` — and publishes yaw-only `PlanarForward` and `PlanarRight` so movement stays camera-relative. | `Isometric3DCameraController`, `ThirdPersonCameraRig` |
| `AlpineLib.Spawning` | Scene-placed spawn points. A spawner picks a random position inside its config's radius, optionally snaps it to the ground, and instantiates the configured prefab. The service does typed spawner lookups (games filter by declaring empty `Spawner` subclasses), spawns everything on start by default, and raises `OnSpawned`. | `Spawner`, `SpawnConfig`, `ISpawnerService`, `SpawnerService` |
| `AlpineLib.Actors` | The actor itself: a `CharacterController` character that can be possessed by a `Controller` brain, moves either in code or by root motion scaled to its current move speed, and publishes speed and turn to the animator. It owns movement and liveness only — `Kill()` raises `OnDeath` and everything else reacts. `ActorSubsystem` is the base for behaviours that self-disable on their owner's death. | `Actor`, `IActor`, `IMortal`, `Controller`, `ActorSubsystem`, `RootMotionForwarder`, `IRootMotionSuppressor` |
| `AlpineLib.Actors.Locomotion` | Gait handling: translates the current gait into move-speed and noise-radius stat modifiers, swapped out whenever the gait changes. Walk is neutral. | `LocomotionSystem`, `LocomotionState` |
| `AlpineLib.Animation` | Animator parameter hashes shared by the actor systems (see the contract below), plus a helper that re-rolls a blend tree index whenever a watched parameter crosses a threshold, so idles vary. | `AnimatorParameters`, `AnimateRandomIndex` |
| `AlpineLib.Utilities` | Weighted random selection over any read-only list. | `WeightedRandom` |
| `AlpineLib.Editor` | Editor tooling. `BootSceneLoader` redirects play mode to a designated boot scene (`AlpineLib/Editor/Play From Boot Scene`). `RegenerateProjectFiles` syncs the external script editor's project files on demand or after every script reload, for scripts added outside Unity. `AssetValidator` is a batch-mode integrity gate over prefabs, ScriptableObjects and build scenes: `-batchmode -quit -executeMethod AlpineLib.Editor.AssetValidator.ValidateAll`. `BodySystemEditor` adds a play-mode inspector showing injuries, bleed rate breakdown and condition progress per body part. | `BootSceneLoader`, `RegenerateProjectFiles`, `AssetValidator`, `BodySystemEditor` |

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

**Trigger parameters**

| Trigger | Purpose |
| --- | --- |
| `Hit` | Hit reaction, fired when the actor takes damage. |
| `Die` | Death animation, fired once. |
| One per attack | Fired by `CombatSystem`, named by each `AttackDefinition.animationTrigger` (for example `Scratch`). |
| Stagger trigger | Fired by `StaggerSystem`, named by its `animationTrigger` field (`Stagger` by default). |

`Hit` and `Die` are not fired by the library — `AnimatorParameters` caches their hashes for the
game's own damage and death handling to use.

**State tags** — the systems poll the current state's tag, not state names, so state naming is
free:

| Tag | Required on |
| --- | --- |
| `Attack` | Every attack state, so `CombatSystem` can see an attack playing and time its damage window against the state's normalized time. |
| `Stagger` | Every stagger state, so `StaggerSystem` knows when the reaction ends. The tag is configurable per actor via `animationTag`. |

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
`BodyPartDefinition` assets, `InjuryDefinition`s, `AttackDefinition`s, `SpawnConfig`s — and any
glue components that connect library systems to each other (for example, routing
`BodySystem.OnDamageTick` into a health `Need`). The library ships a humanoid body plan and its
body part assets as a starting point, plus the injector and pointer indicator prefabs; it ships
no game art and no game-specific data.
