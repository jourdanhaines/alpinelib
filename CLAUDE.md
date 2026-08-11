# alpinelib

Core reusable library for genre-agnostic Unity game development. Provides reusable systems (animation, networking, editor tooling, build pipelining, etc.) to make bootstrapping and developing Unity games easier and more consistent. No limits on scope of what it provides.

## Git Conventions

- **Never** use `Co-authored-by` trailers.
- No commit message bodies unless explicitly requested — subject line only.
- Commit subjects are short and concise.
- Always prefix the subject with a single word (no scope brackets). Allowed prefixes: `feat`, `fix`, `chore`, `docs`, `refactor`.
- Never undo changes to split work across commits. Unrelated changes may share a commit as long as each commit is still a logical group.

## Code Style (C#)

- **Access modifiers** — every class member declares one explicitly; `private` is written out, never implied.
- **One class per file** — file name matches the class name.
- **Class-file helpers** — helpers in class-bearing files live as methods on the class: `private` for internal use, `public static` if consumed externally. No loose module-level utility sprawl.
- **Guard clauses** — invert conditions and early-return; nesting depth ≤ 2. Extract inner loops/branches into helpers rather than deep-nesting.
- **Naming** — descriptive variables; expand lambda/LINQ params (`(t)` → `(task)`) and loop counters (`index` not `i`).
- **Inline functions** — single-statement lambdas may be inlined; multi-statement handlers must be extracted to a named method.

## Netcode Roadmap

- **Server-side collision (exists)**: `Netcode/Collision/` carries a real, engine-free collision world — planes, boxes, spheres and capsules in a 2m XZ grid, plus translation-only movers whose pose is a pure function of the sim tick. Scenes are authored in Unity (`NetStaticGeometry`, `NetMover`), baked by `AlpineLib/Editor/Export Scene Geometry` into a `.geo` blob the dedicated server loads from `config/geometry/` and a `SceneGeometryAsset` the client resolves through a `SceneGeometryRegistry`. Both ends step the same `PawnMotor` against the same bytes, so the simulation owns Y: ramps, walls, step-ups and platform rides all resolve identically on server and predicting client.
- **Owner movement architecture (current)**: the visible local pawn is still moved by `Actor`/`CharacterController`; `PawnMotor` predicts in parallel and `NetActorSync` walks the actor toward the predicted pose every frame (exponential convergence, hard teleport only past the snap distance). Prediction owns all three axes now — no axis is copied back off the transform. The two models are still kept aligned by hand (air acceleration, gravity, jump timing live in both `MovementProfile` and the Actor), and that duplication remains the standing cost.
- **Single-sim promotion (unblocked, still out of scope)**: the prerequisite this was waiting on — real server-side ground data — is met, so promoting `PawnMotor` to the single simulation for networked owned pawns is now a scheduling question rather than a blocked one. The shape is unchanged: fixed-step sim + render interpolation, Actor demoted to presentation, `CharacterController` used only for collision resolution inside the step, removing the dual-model alignment burden entirely. Deliberately not attempted in this pass; treat it as the next architectural move, not an incidental refactor.
