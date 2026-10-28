using Unity.Burst;
using Unity.Collections;
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

        // Radius of the sweep under each heel, so the heel rests on the highest ground under it instead of only
        // the point under the ankle
        private const float HEEL_RADIUS = 0.05f;
        
        // Radius of the sweep under each toe. Crossing an edge, the sphere rolls over the corner, so the toe's
        // ground height eases down by up to this much instead of popping
        private const float TOE_RADIUS = 0.04f;

        private AnimationScriptPlayable ikPlayable;

        // For gizmos
        private FootRay leftHeelRay;
        private FootRay leftToeRay;

        private FootRay rightHeelRay;
        private FootRay rightToeRay;
        
        // Animated (pre-IK) ankle and toe positions written by the job, so the casts don't measure the IK's own
        // result and feed back into it. We raycast from the pose, not from the resulting IK.
        private NativeArray<Vector3> animatedPositions;
        
        // Indices into animatedPositions
        private const int LEFT_FOOT = 0;
        private const int LEFT_TOE = 1;
        private const int RIGHT_FOOT = 2;
        private const int RIGHT_TOE = 3;

        public Playable CreatePlayable(PlayableGraph graph, Animator animator, Playable input) {
            Assert.IsNotNull(this.leftFoot);
            Assert.IsNotNull(this.leftToe);
            Assert.IsNotNull(this.rightFoot);
            Assert.IsNotNull(this.rightToe);
            Assert.IsNotNull(this.pelvis);

            this.animatedPositions = new NativeArray<Vector3>(4, Allocator.Persistent);
            
            // Start with the bind pose so the first casts, before the job has run, have valid origins
            this.animatedPositions[LEFT_FOOT] = this.leftFoot.position;
            this.animatedPositions[LEFT_TOE] = this.leftToe.position;
            this.animatedPositions[RIGHT_FOOT] = this.rightFoot.position;
            this.animatedPositions[RIGHT_TOE] = this.rightToe.position;
            
            FootPelvisIkJob ikJob = new() {
                animatedPositions = this.animatedPositions,
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
                toeGroundNormal = Vector3.up
            };
        }

        private void OnDestroy() {
            if (this.animatedPositions.IsCreated) {
                this.animatedPositions.Dispose();
            }
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

            this.leftHeelRay = CastFootRay(this.animatedPositions[LEFT_FOOT], rootHeight, HEEL_RADIUS);
            this.leftToeRay = CastFootRay(this.animatedPositions[LEFT_TOE], rootHeight, TOE_RADIUS);

            this.rightHeelRay = CastFootRay(this.animatedPositions[RIGHT_FOOT], rootHeight, HEEL_RADIUS);
            this.rightToeRay = CastFootRay(this.animatedPositions[RIGHT_TOE], rootHeight, TOE_RADIUS);

            FootPelvisIkJob ikJob = this.ikPlayable.GetJobData<FootPelvisIkJob>();
            ikJob.rootHeight = rootHeight;
            ikJob.maxFootTilt = this.maxFootTilt; // Might be changed in editor
            ApplyRay(ref ikJob.left, ref this.leftHeelRay, ref this.leftToeRay);
            ApplyRay(ref ikJob.right, ref this.rightHeelRay, ref this.rightToeRay);
            this.ikPlayable.SetJobData(ikJob);
        }

        private FootRay CastFootRay(Vector3 origin, float rootHeight, float radius) {
            origin.y = rootHeight + this.rayStartHeight;

            FootRay ray = new() {
                origin = origin,
                rayLength = this.rayStartHeight + this.maxStepDown,
                radius = radius
            };

            if (radius > 0) {
                // The sphere's bottom reaches radius further than its centre, so shorten the cast to keep the same reach
                ray.hasHit = Physics.SphereCast(origin, radius, Vector3.down, out ray.hit,
                    ray.rayLength - radius, this.groundMask);
            } else {
                ray.hasHit = Physics.Raycast(origin, Vector3.down, out ray.hit, ray.rayLength, this.groundMask);
            }

            return ray;
        }

        private static void ApplyRay(ref Leg leg, ref FootRay heelRay, ref FootRay toeRay) {
            if (heelRay.hasHit) {
                leg.heelGroundY = heelRay.GroundY;
                leg.heelWeight = 1.0f;
            } else {
                leg.heelWeight = 0;
            }

            if (toeRay.hasHit) {
                leg.toeGroundY = toeRay.GroundY;
                leg.toeGroundNormal = toeRay.hit.normal;
                leg.toeWeight = 1.0f;
            } else {
                leg.toeWeight = 0;
            }
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) {
                return;
            }

            DrawFootRay(ref this.leftHeelRay);
            DrawFootRay(ref this.leftToeRay);

            DrawFootRay(ref this.rightHeelRay);
            DrawFootRay(ref this.rightToeRay);
        }

        private static void DrawFootRay(ref FootRay ray) {
            if (ray.hasHit) {
                // Green to where the cast stopped, with the swept sphere
                Gizmos.color = Color.green;
                Vector3 end = ray.origin + Vector3.down * ray.hit.distance;
                Gizmos.DrawLine(ray.origin, end);
                Gizmos.DrawWireSphere(end, Mathf.Max(ray.radius, 0.02f));

                Gizmos.color = Color.blue;
                Gizmos.DrawLine(ray.hit.point, ray.hit.point + ray.hit.normal * 0.3f);
            } else {
                // Red full length on a miss
                Gizmos.color = Color.red;
                Vector3 end = ray.origin + Vector3.down * (ray.rayLength - ray.radius);
                Gizmos.DrawLine(ray.origin, end);
                if (ray.radius > 0f) {
                    Gizmos.DrawWireSphere(end, ray.radius);
                }
            }
        }

        private struct FootRay {
            internal RaycastHit hit;
            internal Vector3 origin;
            internal float rayLength;
            internal float radius;
            internal bool hasHit;

            internal float GroundY => this.origin.y - this.hit.distance - radius;
        }

        private struct Leg {
            public TransformStreamHandle top;
            public TransformStreamHandle mid;
            public TransformStreamHandle low;
            public TransformStreamHandle toe;

            public Vector3 toeGroundNormal;
            public float heelGroundY;
            public float toeGroundY;

            public float heelWeight;    // 1 when the heel found ground. Without it the foot keeps its clip pose
            public float toeWeight;     // 1 when the toe found ground. Without it the foot isn't tilted
        }

        [BurstCompile]
        private struct FootPelvisIkJob : IAnimationJob {
            public NativeArray<Vector3> animatedPositions;
            
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
                
                // Store the positions to animatedPositions
                this.animatedPositions[LEFT_FOOT] = leftFootPosition;
                this.animatedPositions[LEFT_TOE] = this.left.toe.GetPosition(stream);
                
                this.animatedPositions[RIGHT_FOOT] = rightFootPosition;
                this.animatedPositions[RIGHT_TOE] = this.right.toe.GetPosition(stream);

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

            // How far the ankle has to move up (or down), and the foot's final rotation, to keep the clip's foot
            // pose on the ground measured under the heel and the toe. Zero and the clip rotation on a heel miss
            private (float offset, Quaternion targetRotation) GroundOffset(AnimationStream stream, ref Leg leg,
                Vector3 footPosition, Quaternion footRotation) {
                if (leg.heelWeight <= 0f) {
                    return (0f, footRotation);
                }

                Vector3 toePosition = leg.toe.GetPosition(stream);
                Vector3 toeFromAnkle = toePosition - footPosition;

                Quaternion tilt = leg.toeWeight > 0f
                    ? GroundTilt(ref leg, footPosition, toePosition)
                    : Quaternion.identity;

                // Real ankles only bend so far, so limit the tilt. Matching steep slopes fully looks broken
                tilt = Quaternion.RotateTowards(Quaternion.identity, tilt, this.maxFootTilt);
                Quaternion targetRotation = tilt * footRotation;

                // Keep the clip's ankle height above the heel's support, measured perpendicular to the tilted sole.
                // Straight above the support that's ankleHeight / cos(tilt), and cos(tilt) is the sole's up.y
                float ankleHeight = footPosition.y - this.rootHeight;
                Vector3 soleUp = tilt * Vector3.up;
                float offset = leg.heelGroundY + ankleHeight / soleUp.y - footPosition.y;

                // With the tilt clamped, the toe can end up below the ground measured under it. Lift the foot until
                // the toe keeps its clip height there, like standing on the ball of the foot
                if (leg.toeWeight > 0f) {
                    float toeHeight = toePosition.y - this.rootHeight;
                    float toeY = footPosition.y + offset + (tilt * toeFromAnkle).y;
                    float toeLift = leg.toeGroundY + toeHeight - toeY;
                    if (toeLift > 0f) {
                        offset += toeLift;
                    }
                }

                return (offset, targetRotation);
            }

            // Tilt that lays the clip's flat foot onto the measured ground. Pitch comes from the line between the
            // ground under the heel and under the toe, and roll from the side slope under the toe
            private static Quaternion GroundTilt(ref Leg leg, Vector3 footPosition, Vector3 toePosition) {
                Vector3 heelGround = new(footPosition.x, leg.heelGroundY, footPosition.z);
                Vector3 toeGround = new(toePosition.x, leg.toeGroundY, toePosition.z);
                Vector3 groundForward = toeGround - heelGround;
                Vector3 flatForward = new(groundForward.x, 0, groundForward.z);

                if (flatForward.sqrMagnitude < 0.000001f) {
                    // Toe straight above the heel, like at toe-off. No direction to pitch along
                    return Quaternion.identity;
                }

                Quaternion pitch = Quaternion.FromToRotation(flatForward, groundForward);

                // Roll around the pitched foot's forward axis so its up matches the side slope under the toe
                Vector3 forward = groundForward.normalized;
                Vector3 pitchedUp = Vector3.ProjectOnPlane(pitch * Vector3.up, forward);
                Vector3 groundUp = Vector3.ProjectOnPlane(leg.toeGroundNormal, forward);
                Quaternion roll = Quaternion.FromToRotation(pitchedUp, groundUp);

                return roll * pitch;
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
