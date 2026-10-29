using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    // Like RootMotionWalker, but steered by code: picks a random point between minPos and maxPos, idles for a random
    // time, then walks there and repeats. Moves with the walk clip's root motion, so it only moves as much as the
    // clip's feet do
    [RequireComponent(typeof(Animator))]
    public class AgentWalker : MonoBehaviour {
        [SerializeField]
        private AnimationClip idleClip;

        [SerializeField]
        private AnimationClip walkClip;

        [SerializeField]
        private Vector3 minPos = new(-25, 0, -25);

        [SerializeField]
        private Vector3 maxPos = new(25, 0, 25);

        // How close on XZ counts as reached. Keep it above the turning radius (walk speed / turn speed in radians),
        // or the agent can circle a target right beside it without ever reaching it
        [SerializeField]
        private float arriveDistance = 1.0f;

        // How long to idle after picking a target before walking to it, picked at random between these
        [SerializeField]
        private float minDuration = 2f;

        [SerializeField]
        private float maxDuration = 5f;

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

        private Vector3 currentTargetPos;
        private Timer waitTimer;

        private void OnEnable() {
            Assert.IsNotNull(this.idleClip);
            Assert.IsNotNull(this.walkClip);

            this.animator = GetComponent<Animator>();

            // Unity only computes root motion when this is set. OnAnimatorMove() decides how it's applied.
            this.animator.applyRootMotion = true;

            this.playableGraph = PlayableGraph.Create("AgentWalker");
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

            PickTarget();
        }

        private void OnDisable() {
            this.playableGraph.Destroy();
        }

        private void Update() {
            this.waitTimer.Update(Time.deltaTime);
            bool isWalking = this.waitTimer.HasElapsed;

            Vector3 toTarget = FlatToTarget();
            if (isWalking && toTarget.sqrMagnitude <= this.arriveDistance * this.arriveDistance) {
                // Reached it. Pick the next one, but idle here first
                PickTarget();
                isWalking = false;
            }

            // Crossfade instead of snapping between idle and walk
            float targetWeight = isWalking ? 1f : 0f;
            this.walkWeight = Mathf.MoveTowards(this.walkWeight, targetWeight, Time.deltaTime / this.blendDuration);
            this.mixer.SetInputWeight(IDLE_INPUT_ID, 1f - this.walkWeight);
            this.mixer.SetInputWeight(WALK_INPUT_ID, this.walkWeight);

            if (!isWalking || toTarget.sqrMagnitude <= 0f) {
                // Stay facing the same way while idling. LookRotation also needs a direction
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(toTarget);
            this.transform.rotation = Quaternion.RotateTowards(this.transform.rotation, targetRotation,
                this.turnSpeed * Time.deltaTime);
        }

        // Height is ignored, since the ground decides it
        private Vector3 FlatToTarget() {
            Vector3 toTarget = this.currentTargetPos - this.transform.position;
            toTarget.y = 0f;
            return toTarget;
        }

        private void PickTarget() {
            this.currentTargetPos = new Vector3(
                Random.Range(this.minPos.x, this.maxPos.x),
                Random.Range(this.minPos.y, this.maxPos.y),
                Random.Range(this.minPos.z, this.maxPos.z));

            this.waitTimer = new Timer(Random.Range(this.minDuration, this.maxDuration));
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

        private void OnDrawGizmosSelected() {
            if (!Application.isPlaying) {
                return;
            }

            // Drawn at the agent's height, since the target's own height is ignored
            Vector3 target = this.currentTargetPos;
            target.y = this.transform.position.y;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(this.transform.position, target);
            Gizmos.DrawWireSphere(target, this.arriveDistance);
        }
    }
}
