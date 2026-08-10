using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpineLib.Input {
    /// <summary>
    /// Polling facade over an <see cref="InputActionAsset"/>. Owns one action map, enables it while the
    /// component is enabled, and hands subclasses resolved <see cref="InputAction"/> instances to poll.
    /// </summary>
    /// <remarks>
    /// This is deliberately a polling facade rather than a callback dispatcher. Character controllers
    /// already run per frame and want to ask "is sprint held right now"; routing that through
    /// <c>performed</c>/<c>canceled</c> callbacks would mean mirroring every action into a field and
    /// keeping those fields in sync with enable/disable, which is more state for no gain. Edge-triggered
    /// input is still available — <see cref="InputAction.WasPressedThisFrame"/> is frame accurate.
    ///
    /// The discipline this class exists to enforce: <b>the subclass is the only file in a game allowed
    /// to spell action names</b>. A subclass declares one <c>private const string</c> per action path
    /// ("Player/Move", "Player/Jump", …), calls <see cref="ResolveAction"/> once per path, and exposes
    /// typed properties over the result — <c>Vector2 Move => _move.ReadValue&lt;Vector2&gt;()</c>,
    /// <c>bool JumpPressed => _jump.WasPressedThisFrame</c>, <c>bool SprintHeld => _sprint.IsPressed</c>.
    /// Gameplay code then depends on those properties and never on a string, so renaming an action in the
    /// <c>.inputactions</c> asset breaks exactly one file and does so loudly, at resolve time.
    ///
    /// <b>Namespace pitfall:</b> this namespace is <c>AlpineLib.Input</c>, which collides with the legacy
    /// <see cref="UnityEngine.Input"/> class. A file that writes both <c>using AlpineLib.Input;</c> and
    /// <c>using UnityEngine;</c> and then references a bare <c>Input</c> gets CS0104 (ambiguous reference)
    /// — the compiler cannot tell the namespace from the class. Fix it by qualifying the legacy call as
    /// <c>UnityEngine.Input.…</c>, which is the correct move anyway: a project on the Input System should
    /// not be reading the legacy static.
    ///
    /// The action asset is required and is not guarded. A reader with no asset assigned is a
    /// configuration mistake, and the same reasoning as <see cref="AlpineLib.Actors.ActorSubsystem"/>
    /// applies — failing immediately in <c>Awake</c> beats silently never reporting input.
    /// </remarks>
    public abstract class InputReader : MonoBehaviour {
        [Tooltip("Input actions asset every action on this reader is resolved from. Required — the reader throws on Awake without it.")]
        [SerializeField] private InputActionAsset actionAsset;

        /// <summary>
        /// Name of the action map inside <c>actionAsset</c> this reader owns, for example "Player".
        /// The map is enabled in <c>OnEnable</c> and disabled again in <c>OnDisable</c>.
        /// </summary>
        protected abstract string MapName { get; }

        /// <summary>
        /// Action map this reader owns, resolved in <c>Awake</c>.
        /// </summary>
        /// <remarks>
        /// Exposed so a subclass that needs map-wide behaviour — rebinding, or suppressing all input
        /// while a menu is open — can reach it without resolving the map a second time.
        /// </remarks>
        protected InputActionMap Map { get; private set; }

        private readonly Dictionary<string, InputAction> _resolvedActions = new Dictionary<string, InputAction>();

        /// <remarks>
        /// The map is resolved in <c>Awake</c> rather than <c>OnEnable</c> so a subclass can resolve its
        /// actions from its own <c>Awake</c> or <c>Start</c> and cache them in fields, which is the shape
        /// the per-frame properties want. <c>throwIfNotFound</c> is on for the same reason it is on in
        /// <see cref="ResolveAction"/>: a misspelled map name should stop the game at startup, not
        /// degrade into an actor that silently never moves.
        /// </remarks>
        protected virtual void Awake() {
            Map = actionAsset.FindActionMap(MapName, throwIfNotFound: true);
        }

        /// <remarks>
        /// Enabling per component rather than once globally means input follows the lifetime of the thing
        /// that reads it: a possessed actor's reader is enabled, a pooled or despawned one is not, and
        /// nothing has to remember to switch the asset off.
        /// </remarks>
        protected virtual void OnEnable() {
            Map.Enable();
        }

        protected virtual void OnDisable() {
            Map.Disable();
        }

        /// <summary>
        /// Resolves an action by its asset path — "Player/Move", "UI/Cancel" — and caches it, so
        /// repeated calls for the same path return the same instance.
        /// </summary>
        /// <param name="actionPath">Path of the action inside the assigned asset, "&lt;Map&gt;/&lt;Action&gt;".</param>
        /// <returns>The resolved action. Never null.</returns>
        /// <remarks>
        /// Throws rather than returning null when the path does not exist. A typo in an action path is a
        /// build-time authoring error wearing a runtime disguise: surfacing it as an exception on the
        /// first resolve names the offending path, whereas a null would surface much later as an actor
        /// that ignores one input for reasons nobody can see.
        ///
        /// The cache is here rather than left to subclasses because <see cref="InputActionAsset.FindAction(string, bool)"/>
        /// does a string lookup across the whole asset; a subclass polling from <c>Update</c> would
        /// otherwise pay for that every frame. Subclasses are still expected to hold the returned action
        /// in a field — the cache is a safety net, not the intended per-frame path.
        /// </remarks>
        protected InputAction ResolveAction(string actionPath) {
            if (_resolvedActions.TryGetValue(actionPath, out InputAction cachedAction)) return cachedAction;

            InputAction resolvedAction = actionAsset.FindAction(actionPath, throwIfNotFound: true);
            _resolvedActions[actionPath] = resolvedAction;

            return resolvedAction;
        }
    }
}
