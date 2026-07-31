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
    /// <item><description><c>SlowWalk</c> — 0 to 1 blend towards the slow walk / aiming gait.</description></item>
    /// <item><description><c>StrafeX</c>, <c>StrafeY</c> — local-space strafe direction used while the actor
    /// holds a facing independent of its movement direction.</description></item>
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
    /// Motion: locomotion, attack and stagger clips must carry root motion, and the actor's animator
    /// needs <c>applyRootMotion</c> enabled so movement is delivered through <c>OnAnimatorMove</c>
    /// instead of being integrated in code.
    /// </remarks>
    public static class AnimatorParameters {
        /// <summary>Float. Forward locomotion speed.</summary>
        public static readonly int Speed = Animator.StringToHash("Speed");

        /// <summary>Float. Turn rate.</summary>
        public static readonly int Turn = Animator.StringToHash("Turn");

        /// <summary>Trigger. Hit reaction.</summary>
        public static readonly int Hit = Animator.StringToHash("Hit");

        /// <summary>Trigger. Death animation.</summary>
        public static readonly int Die = Animator.StringToHash("Die");

        /// <summary>Float. Blend towards the slow walk gait.</summary>
        public static readonly int SlowWalk = Animator.StringToHash("SlowWalk");

        /// <summary>Float. Local-space strafe direction on the X axis.</summary>
        public static readonly int StrafeX = Animator.StringToHash("StrafeX");

        /// <summary>Float. Local-space strafe direction on the Y axis.</summary>
        public static readonly int StrafeY = Animator.StringToHash("StrafeY");
    }
}
