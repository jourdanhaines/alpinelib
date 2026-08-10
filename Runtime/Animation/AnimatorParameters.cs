using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Cached hashes for the animator parameters the library's actor systems drive.
    /// </summary>
    /// <remarks>
    /// An animator controller consumed by these systems must satisfy the following contract.
    ///
    /// Float parameters:
    /// <list type="bullet">
    /// <item><description><c>Speed</c> — signed forward locomotion speed, driven every frame by the actor.</description></item>
    /// <item><description><c>Turn</c> — signed turn rate, driven every frame by the actor.</description></item>
    /// <item><description><c>StrafeX</c>, <c>StrafeY</c> — local-space movement direction, <c>+X</c> right and
    /// <c>+Y</c> forward, scaled the same way as <c>Speed</c>. Driven every frame by the actor whenever its
    /// animator controller declares both, which is what lets a controller blend movement against a facing
    /// the camera or a skill stage is steering independently. Controllers that declare neither are never
    /// written to.</description></item>
    /// </list>
    ///
    /// Bool parameters:
    /// <list type="bullet">
    /// <item><description><c>Grounded</c> — whether the actor's character controller reports ground
    /// contact. Driven every frame by the actor whenever its animator controller declares it, so a
    /// controller can leave locomotion while airborne and land when contact returns. Controllers
    /// that do not declare it are never written to.</description></item>
    /// </list>
    ///
    /// Legacy parameters — hashed here for games that still reference them, but not driven by any library
    /// system:
    /// <list type="bullet">
    /// <item><description><c>SlowWalk</c> — Bool, not a float blend, despite living alongside the float
    /// hashes. Nothing in the library reads or writes it; a game that wants a slow walk gait owns both
    /// ends of it.</description></item>
    /// </list>
    ///
    /// Trigger parameters:
    /// <list type="bullet">
    /// <item><description><c>Hit</c> — hit reaction, fired when the actor takes damage.</description></item>
    /// <item><description><c>Die</c> — death animation, fired once.</description></item>
    /// <item><description>One trigger per attack, named by each attack definition (for example <c>Scratch</c>).</description></item>
    /// <item><description>A stagger trigger, named by the stagger system (<c>Stagger</c> by default).</description></item>
    /// </list>
    ///
    /// State tags — the systems poll the current state's tag rather than state names:
    /// <list type="bullet">
    /// <item><description><c>Attack</c> — every attack state, so the combat system knows an attack is playing.</description></item>
    /// <item><description><c>Stagger</c> — every stagger state, so the stagger system knows when the reaction ends.</description></item>
    /// </list>
    ///
    /// Motion: locomotion clips carry root motion, and a root motion actor enables
    /// <c>applyRootMotion</c> so locomotion displacement arrives through <c>OnAnimatorMove</c> instead of
    /// being integrated in code. Skill clips are not held to that rule — each skill stage opts into root
    /// motion individually, and a stage that opts out has its root motion suppressed and its displacement
    /// driven from code instead. Code driven displacement during such a stage is the intended path, not a
    /// workaround, because a stage may need to travel at a speed or along a direction the authored clip
    /// does not encode.
    /// </remarks>
    public static class AnimatorParameters {
        /// <summary>Float. Forward locomotion speed.</summary>
        public static readonly int Speed = Animator.StringToHash("Speed");

        /// <summary>Float. Turn rate.</summary>
        public static readonly int Turn = Animator.StringToHash("Turn");

        /// <summary>Bool. Character controller ground contact.</summary>
        public static readonly int Grounded = Animator.StringToHash("Grounded");

        /// <summary>Trigger. Hit reaction.</summary>
        public static readonly int Hit = Animator.StringToHash("Hit");

        /// <summary>Trigger. Death animation.</summary>
        public static readonly int Die = Animator.StringToHash("Die");

        /// <summary>Bool. Legacy slow walk / aiming gait flag, unused by library systems.</summary>
        public static readonly int SlowWalk = Animator.StringToHash("SlowWalk");

        /// <summary>Float. Local-space movement direction on the X axis, positive to the actor's right.</summary>
        public static readonly int StrafeX = Animator.StringToHash("StrafeX");

        /// <summary>Float. Local-space movement direction on the Y axis, positive along the actor's forward.</summary>
        public static readonly int StrafeY = Animator.StringToHash("StrafeY");
    }
}
