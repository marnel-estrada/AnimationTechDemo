using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class FootPlacementPostProcess : MonoBehaviour, IAnimationPostProcess {
        [SerializeField]
        private Transform leftFoot;
        
        [SerializeField]
        private Transform leftToe;
        
        [SerializeField]
        private Transform rightFoot;
        
        [SerializeField]
        private Transform rightToe;

        [SerializeField]
        private Transform pelvis;

        [SerializeField]
        private LayerMask groundMask = 0;

        [SerializeField]
        private float rayStartHeight = 0.5f;
        
        // How far below the root a foot may reach. The pelvis drops by up to this much
        [SerializeField]
        private float maxStepDown = 0.4f;

        [SerializeField]
        private float maxFootTilt = 30.0f;

        private AnimationScriptPlayable ikPlayable;

        // Last raycast, for gizmos
        private FootRay leftRay;
        private FootRay rightRay;
        
        public Playable CreatePlayable(PlayableGraph graph, Animator animator, Playable input) {
            Assert.IsNotNull(this.leftFoot);
            Assert.IsNotNull(this.leftToe);
            Assert.IsNotNull(this.rightFoot);
            Assert.IsNotNull(this.rightToe);
            Assert.IsNotNull(this.pelvis);
            
            FootPelvisIkJob ikJob = new() {
                pelvis = animator.BindStreamTransform(this.pelvis),
                left = CreateLeg(animator, this.leftFoot, this.leftToe),
                right = CreateLeg(animator, this.rightFoot, this.rightToe),
                maxFootTilt = this.maxFootTilt
            };
            
            this.ikPlayable = AnimationScriptPlayable.Create(graph, ikJob);
            this.ikPlayable.AddInput(input, 0, 1f);
            return this.ikPlayable;
        }
        
        private static Leg CreateLeg(Animator animator, Transform foot, Transform toe) {
            Transform knee = foot.parent;
            Assert.IsNotNull(knee);
            
            Transform hip = knee.parent;
            Assert.IsNotNull(hip);

            return new Leg() {
                top = animator.BindStreamTransform(hip),
                mid = animator.BindStreamTransform(knee),
                low = animator.BindStreamTransform(foot),
                toe = animator.BindStreamTransform(toe),
                groundHitNormal = Vector3.up
            };
        }

        private void Update() {
            // Nothing to drive until the graph owner has called CreatePlayable(). It also becomes invalid when the
            // owner destroys its graph
            if (!this.ikPlayable.IsValid()) {
                return;
            }
            
            // Do the raycast here since Physics can't run in the animation job.
            // This uses the last frame's foot position, though
            float rootHeight = this.transform.position.y;

            this.leftRay = CastFootRay(this.leftFoot, rootHeight);
            this.rightRay = CastFootRay(this.rightFoot, rootHeight);

            FootPelvisIkJob ikJob = this.ikPlayable.GetJobData<FootPelvisIkJob>();
            ikJob.rootHeight = rootHeight;
            ikJob.maxFootTilt = this.maxFootTilt; // Might be changed in editor
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
                leg.groundHitPoint = ray.hit.point;
                leg.groundHitNormal = ray.hit.normal;
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
            public TransformStreamHandle toe;

            public Vector3 groundHitNormal;
            public Vector3 groundHitPoint;
                
            public float weight;
        }
        
        [BurstCompile]
        private struct FootPelvisIkJob : IAnimationJob {
            public TransformStreamHandle pelvis;
            public Leg left;
            public Leg right;

            public float rootHeight;

            public float maxFootTilt;
            
            public void ProcessRootMotion(AnimationStream stream) {
            }
            
            public void ProcessAnimation(AnimationStream stream) {
                // Read the animated feet before moving the pelvis, since that moves them too
                Vector3 leftFootPosition = this.left.low.GetPosition(stream);
                Quaternion leftFootRotation = this.left.low.GetRotation(stream);

                Vector3 rightFootPosition = this.right.low.GetPosition(stream);
                Quaternion rightFootRotation = this.right.low.GetRotation(stream);

                (float leftOffset, Quaternion leftTargetRotation) = 
                    GroundOffset(stream, ref this.left, leftFootPosition, leftFootRotation);
                (float rightOffset, Quaternion rightTargetRotation) = 
                    GroundOffset(stream, ref this.right, rightFootPosition, rightFootRotation);
                
                // Lower the pelvis by the deepest drop so that foot can reach. Raised feet just bend the knee
                float pelvisOffset = Mathf.Min(0f, Mathf.Min(leftOffset, rightOffset));
                Vector3 pelvisPosition = this.pelvis.GetPosition(stream);
                this.pelvis.SetPosition(stream, pelvisPosition + new Vector3(0, pelvisOffset, 0));
                
                Solve(stream, ref this.left, leftFootPosition, leftTargetRotation, leftOffset);
                Solve(stream, ref this.right, rightFootPosition, rightTargetRotation, rightOffset);
            }
            
            // How far the ankle has to move up (or down) to keep its clip height above the ground under it,
            // measured along the ground normal so the sole doesn't sink on slopes. Zero on a miss so the foot
            // keeps its animated height
            private (float offset, Quaternion targetRotation) GroundOffset(AnimationStream stream, ref Leg leg, 
                Vector3 footPosition, Quaternion footRotation) {
                if (leg.weight <= 0f) {
                    return (0f, footRotation);
                }
                
                // Real ankles only bend so far, so limit the tilt. Matching steep slopes fully looks broken
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, leg.groundHitNormal);
                tilt = Quaternion.RotateTowards(Quaternion.identity, tilt, this.maxFootTilt);
                Quaternion targetRotation = tilt * footRotation;

                float ankleHeight = footPosition.y - this.rootHeight;
                float offset = leg.groundHitPoint.y + ankleHeight / leg.groundHitNormal.y - footPosition.y;
                
                // With the tilt clamped, the foot doesn't lie flat on steep slopes and the toes dig in.
                // Lift the foot until the toe is back on the surface, like standing on the ball of the foot
                Vector3 toeFromAnkle = tilt * (leg.toe.GetPosition(stream) - footPosition);
                Vector3 toe = footPosition + new Vector3(0, offset, 0) + toeFromAnkle;
                float toeDepth = Vector3.Dot(toe - leg.groundHitPoint, leg.groundHitNormal);
                if (toeDepth < 0f) {
                    offset -= toeDepth / leg.groundHitNormal.y;
                }

                return (offset, targetRotation);
            }

            private static void Solve(AnimationStream stream, ref Leg leg, Vector3 footPosition, 
                Quaternion targetRotation, float groundOffset) {
                // Keep the clip's ankle height, but measured from the ground under the foot instead of the root.
                // Always solve, even on a miss, since the pelvis may have moved the foot
                Vector3 target = footPosition + new Vector3(0, groundOffset, 0);
                TwoBoneIkSolver.Solve(stream, leg.top, leg.mid, leg.low, target);
                
                // The solver rotated the parents, so set the final world rotation computed in GroundOffset
                leg.low.SetRotation(stream, targetRotation);
            }
        }
    }
}