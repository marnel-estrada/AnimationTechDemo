using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class FootPelvisIk : MonoBehaviour {
        [SerializeField]
        private Transform leftFoot;
        
        [SerializeField]
        private Transform rightFoot;

        [SerializeField]
        private Transform pelvis;

        [SerializeField]
        private AnimationClip animationClip;

        [SerializeField]
        private LayerMask groundMask = 0;

        [SerializeField]
        private float rayStartHeight = 0.5f;
        
        // How far below the root a foot may reach. The pelvis drops by up to this much
        [SerializeField]
        private float maxStepDown = 0.4f;

        private PlayableGraph playableGraph;
        private AnimationScriptPlayable ikPlayable;

        // Last raycast, for gizmos
        private FootRay leftRay;
        private FootRay rightRay;

        private void OnEnable() {
            Assert.IsNotNull(this.leftFoot);
            Assert.IsNotNull(this.rightFoot);
            Assert.IsNotNull(this.pelvis);
            Assert.IsNotNull(this.animationClip);

            this.playableGraph = PlayableGraph.Create("FootIk");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            Animator animator = GetComponent<Animator>();

            FootPelvisIkJob ikJob = new() {
                pelvis = animator.BindStreamTransform(this.pelvis),
                left = CreateLeg(animator, this.leftFoot),
                right = CreateLeg(animator, this.rightFoot)
            };
            
            // We call SetApplyFootIK(false) so that our custom IK works on the foot
            AnimationClipPlayable animationClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.animationClip);
            animationClipPlayable.SetApplyFootIK(false);
            
            this.ikPlayable = AnimationScriptPlayable.Create(this.playableGraph, ikJob);
            this.ikPlayable.AddInput(animationClipPlayable, 0, 1f);
            
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", animator);
            output.SetSourcePlayable(this.ikPlayable);
            this.playableGraph.Play();
        }

        private static Leg CreateLeg(Animator animator, Transform foot) {
            Transform knee = foot.parent;
            Assert.IsNotNull(knee);
            
            Transform hip = knee.parent;
            Assert.IsNotNull(hip);

            return new Leg() {
                top = animator.BindStreamTransform(hip),
                mid = animator.BindStreamTransform(knee),
                low = animator.BindStreamTransform(foot),
                groundNormal = Vector3.up
            };
        }

        private void OnDisable() {
            this.playableGraph.Destroy();
        }

        private void Update() {
            // Do the raycast here since Physics can't run in the animation job.
            // This uses the last frame's foot position, though
            float rootHeight = this.transform.position.y;

            this.leftRay = CastFootRay(this.leftFoot, rootHeight);
            this.rightRay = CastFootRay(this.rightFoot, rootHeight);

            FootPelvisIkJob ikJob = this.ikPlayable.GetJobData<FootPelvisIkJob>();
            ikJob.rootHeight = rootHeight;
            ApplyRay(ref ikJob.left, ref this.leftRay);
            ApplyRay(ref ikJob.right, ref this.rightRay);
            this.ikPlayable.SetJobData(ikJob);
        }

        private FootRay CastFootRay(Transform foot, float rootHeight) {
            Vector3 origin = foot.position;
            origin.y = rootHeight + this.rayStartHeight;

            FootRay ray = new() {
                origin = origin,
                rayLength = this.rayStartHeight + this.maxStepDown
            };

            ray.hasHit = Physics.Raycast(origin, Vector3.down, out ray.hit, ray.rayLength, this.groundMask);

            return ray;
        }

        private static void ApplyRay(ref Leg leg, ref FootRay ray) {
            if (ray.hasHit) {
                leg.groundHeight = ray.hit.point.y;
                leg.groundNormal = ray.hit.normal;
                leg.weight = 1.0f;
            } else {
                leg.weight = 0;
            }
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) {
                return;
            }

            DrawFootRay(ref this.leftRay);
            DrawFootRay(ref this.rightRay);
        }

        private static void DrawFootRay(ref FootRay ray) {
            if (ray.hasHit) {
                // Green up to the hit, then the hit point and its normal
                Gizmos.color = Color.green;
                Gizmos.DrawLine(ray.origin, ray.hit.point);
                Gizmos.DrawWireSphere(ray.hit.point, 0.03f);

                Gizmos.color = Color.blue;
                Gizmos.DrawLine(ray.hit.point, ray.hit.point + ray.hit.normal * 0.3f);
            } else {
                // Red full length on a miss
                Gizmos.color = Color.red;
                Gizmos.DrawLine(ray.origin, ray.origin + Vector3.down * ray.rayLength);
            }
        }

        private struct FootRay {
            internal RaycastHit hit;
            internal Vector3 origin;
            internal float rayLength;
            internal bool hasHit;
        }

        private struct Leg {
            public TransformStreamHandle top;
            public TransformStreamHandle mid;
            public TransformStreamHandle low;

            public Vector3 groundNormal;
            public float groundHeight;
            public float weight;
        }
        
        [BurstCompile]
        private struct FootPelvisIkJob : IAnimationJob {
            public TransformStreamHandle pelvis;
            public Leg left;
            public Leg right;

            public float rootHeight;
            
            public void ProcessRootMotion(AnimationStream stream) {
            }
            
            public void ProcessAnimation(AnimationStream stream) {
                // Read the animated feet before moving the pelvis, since that moves them too
                Vector3 leftFootPosition = this.left.low.GetPosition(stream);
                Quaternion leftFootRotation = this.left.low.GetRotation(stream);

                Vector3 rightFootPosition = this.right.low.GetPosition(stream);
                Quaternion rightFootRotation = this.right.low.GetRotation(stream);

                float leftOffset = GroundOffset(ref this.left);
                float rightOffset = GroundOffset(ref this.right);
                
                // Lower the pelvis by the deepest drop so that foot can reach. Raised feet just bend the knee
                float pelvisOffset = Mathf.Min(0f, Mathf.Min(leftOffset, rightOffset));
                Vector3 pelvisPosition = this.pelvis.GetPosition(stream);
                this.pelvis.SetPosition(stream, pelvisPosition + Vector3.up * pelvisOffset);
                
                Solve(stream, ref this.left, leftFootPosition, leftFootRotation, leftOffset);
                Solve(stream, ref this.right, rightFootPosition, rightFootRotation, rightOffset);
            }
            
            // How far the ground under this foot is from the root. Zero on a miss so the foot keeps its animated height
            private float GroundOffset(ref Leg leg) {
                return leg.weight > 0f ? leg.groundHeight - this.rootHeight : 0f;
            }

            private void Solve(AnimationStream stream, ref Leg leg, Vector3 footPosition, Quaternion footRotation, 
                float groundOffset) {
                // Keep the clip's ankle height, but measured from the ground under the foot instead of the root.
                // Always solve, even on a miss, since the pelvis may have moved the foot
                Vector3 target = footPosition + Vector3.up * groundOffset;
                
                // Keep the clip's ankle height, but measure it from the hit ground instead of the root
                // float ankleHeight = footPosition.y - this.rootHeight;
                // Vector3 target = new(footPosition.x, leg.groundHeight + ankleHeight, footPosition.z);
                TwoBoneIkSolver.Solve(stream, leg.top, leg.mid, leg.low, target);
                
                // The solver rotated the parents, so put back the animation world rotation, then tilt it to the ground
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, leg.groundNormal);
                leg.low.SetRotation(stream, Quaternion.Slerp(footRotation, tilt * footRotation, leg.weight));
            }
        }
    }
}