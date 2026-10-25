using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace TechDemo {
    // Moves the character with the walk clip's root motion. WASD picks a direction relative to the camera; the
    // character turns toward it and walks forward, so it only moves as much as the clip's feet do
    [RequireComponent(typeof(Animator))]
    public class RootMotionWalker : MonoBehaviour {
        [SerializeField]
        private AnimationClip idleClip;

        [SerializeField]
        private AnimationClip walkClip;

        [SerializeField]
        private Transform cameraTransform;

        [SerializeField]
        private float turnSpeed = 180f; // Degrees per second

        [SerializeField]
        private float blendDuration = 0.3f;

        [SerializeField]
        private LayerMask groundMask;

        [SerializeField]
        private float groundRayHeight = 1.0f;

        [SerializeField]
        private float groundRayLength = 2.0f;

        [SerializeField]
        private float groundSmoothTime = 0.1f;

        private const int IDLE_INPUT_ID = 0;
        private const int WALK_INPUT_ID = 1;

        private Animator animator;
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixer;

        private float walkWeight;
        private float groundVelocity;

        private void OnEnable() {
            Assert.IsNotNull(this.idleClip);
            Assert.IsNotNull(this.walkClip);
            Assert.IsNotNull(this.cameraTransform);

            this.animator = GetComponent<Animator>();
            
            // Unity only computes root motion when this is set. OnAnimatorMove() decides how it's applied.
            this.animator.applyRootMotion = true;

            this.playableGraph = PlayableGraph.Create("RootMotionWalker");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            
            this.mixer = AnimationMixerPlayable.Create(this.playableGraph, 2);
            
            AnimationClipPlayable idleClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.idleClip);
            this.playableGraph.Connect(idleClipPlayable, 0, this.mixer, IDLE_INPUT_ID);
            
            AnimationClipPlayable walkClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.walkClip);
            this.playableGraph.Connect(walkClipPlayable, 0, this.mixer, WALK_INPUT_ID);
            
            this.mixer.SetInputWeight(IDLE_INPUT_ID, 1.0f);
            
            // Optional step between the mixer and the output, like FootPlacementPostProcess
            IAnimationPostProcess postProcess = GetComponent<IAnimationPostProcess>();
            Assert.IsNotNull(postProcess);
            
            // A post-process like FootPlacementPostProcess places the feet itself. Humanoid foot IK would undo that
            idleClipPlayable.SetApplyFootIK(false);
            walkClipPlayable.SetApplyFootIK(false);

            Playable postProcessPlayable = postProcess.CreatePlayable(this.playableGraph, this.animator, this.mixer);
            
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", this.animator);
            output.SetSourcePlayable(postProcessPlayable);
            this.playableGraph.Play();
        }

        private void OnDisable() {
            this.playableGraph.Destroy();
        }

        private void Update() {
            Vector3 direction = ReadMoveDirection();
            bool hasInput = direction.sqrMagnitude > 0;
            
            // Crossfade instead of snapping between idle and walk
            float targetWeight = hasInput ? 1f : 0f;
            this.walkWeight = Mathf.MoveTowards(this.walkWeight, targetWeight, Time.deltaTime / this.blendDuration);
            this.mixer.SetInputWeight(IDLE_INPUT_ID, 1f - this.walkWeight);
            this.mixer.SetInputWeight(WALK_INPUT_ID, this.walkWeight);

            if (!hasInput) {
                return;
            }

            // Set rotation when there's input
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            this.transform.rotation = Quaternion.RotateTowards(this.transform.rotation, targetRotation,
                this.turnSpeed * Time.deltaTime);
        }

        // Flat direction from WASD, relative to the camera so W always walks away from it. Zero without input
        private Vector3 ReadMoveDirection() {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) {
                return Vector3.zero;
            }

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0)) {
                // Nothing pressed
                return Vector3.zero;
            }

            Vector3 forward = this.cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = this.cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            return (forward * z + right * x).normalized;
        }

        // Called after the Animator computes root motion. The walk clip's root motion is flat, so take its XZ
        // movement and follow the ground for the height. Its rotation is baked into the pose, so steering stays ours
        private void OnAnimatorMove() {
            Vector3 position = this.transform.position + this.animator.deltaPosition;
            position.y = FollowGround(position);
            this.transform.position = position;
        }

        private float FollowGround(Vector3 position) {
            Vector3 origin = position + new Vector3(0, this.groundRayHeight, 0);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, this.groundRayLength, this.groundMask)) {
                // No ground found
                return position.y;
            }
            
            // Smooth so that the root doesn't snap up and down on stairs
            return Mathf.SmoothDamp(position.y, hit.point.y, ref this.groundVelocity, this.groundSmoothTime);
        }
    }
}