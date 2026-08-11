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

- **Owner movement architecture (current)**: the visible local pawn is moved by `Actor`/`CharacterController`; `PawnMotor` predicts in parallel and reconciles via epsilon-gated corrections. The two models are kept aligned by hand (air acceleration, gravity, jump timing live in both `MovementProfile` and the Actor).
- **Post-heightfield migration path**: once a real `IGroundProvider` heightfield exists (server-side collision), promote `PawnMotor` to the single simulation for networked owned pawns — fixed-step sim + render interpolation, Actor demoted to presentation, `CharacterController` used only for collision resolution inside the step. Removes the dual-model alignment burden entirely. Do not attempt before server-side ground data exists; the flat-plane sim cannot own Y.
