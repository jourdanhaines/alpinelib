using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// What an animator is told about an actor being off the ground: whether it is, and for how long
    /// it has been.
    /// </summary>
    /// <remarks>
    /// A jump is announced before the ground contact that goes with it changes — by a frame for a
    /// simulated actor, by the whole interpolation delay for a replicated one. The grace that follows
    /// <see cref="NoteJump"/> reports the actor as airborne across that gap, so a controller that lands
    /// on ground contact does not land the instant the jump starts.
    /// </remarks>
    public class AnimatorAirState {
        /// <summary>Longest a jump is taken on trust before the ground contact has to agree.</summary>
        public const float JumpGraceSeconds = 0.35f;

        /// <summary>Ground contact as the animator should see it.</summary>
        public bool Grounded { get; private set; } = true;

        /// <summary>Seconds spent really off the ground, zero while on it. Grace does not count.</summary>
        public float AirTime { get; private set; }

        private float _graceRemaining;

        /// <summary>Marks a jump as started; the actor reads as airborne from here on.</summary>
        public void NoteJump() {
            _graceRemaining = JumpGraceSeconds;
            Grounded = false;
        }

        /// <summary>Advances by one frame of real ground contact.</summary>
        public void Tick(bool isGrounded, float deltaTime) {
            if (!isGrounded) {
                _graceRemaining = 0f;
                Grounded = false;
                AirTime += deltaTime;
                return;
            }

            AirTime = 0f;
            _graceRemaining = Mathf.Max(_graceRemaining - deltaTime, 0f);
            Grounded = _graceRemaining <= 0f;
        }
    }
}
