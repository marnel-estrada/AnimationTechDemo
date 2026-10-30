using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class FootPlacementPostProcess : MonoBehaviour, IAnimationPostProcess {
        [SerializeField]
        private SpherecastHandler spherecastHandler;
        
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
        private float rayStartHeight = 0.5f;

        // How far below the root a foot may reach. The pelvis drops by up to this much
        [SerializeField]
        private float maxStepDown = 0.4f;

        [SerializeField]
        private float maxFootTilt = 30.0f;
        
        // How fast the IK eases toward its targets, roughly 1 / time constant (20 = about 50 ms). Feet rise faster
        // than they drop, since a late rise sinks into a step while a late drop just hovers briefly
        [SerializeField]
        private float footUpSpeed = 20f;

        [SerializeField]
        private float footDownSpeed = 10f;

        [SerializeField]
        private float footTiltSpeed = 15f;
        
        // Slower than the feet so the body doesn't bob
        [SerializeField]
        private float pelvisSpeed = 8f;

        // Radius of the sweep under each heel, so the heel rests on the highest ground under it instead of only
        // the point under the ankle
        private const float HEEL_RADIUS = 0.05f;

        // Radius of the sweep under each toe. Crossing an edge, the sphere rolls over the corner, so the toe's
        // ground height eases down by up to this much instead of popping
        private const float TOE_RADIUS = 0.04f;

        private AnimationScriptPlayable ikPlayable;

        // For gizmos
        private FootRay leftHeelGizmoRay;
        private FootRay leftToeGizmoRay;

        private FootRay rightHeelGizmoRay;
        private FootRay rightToeGizmoRay;

        // Animated (pre-IK) ankle and toe positions written by the job, so the casts don't measure the IK's own
        // result and feed back into it. We raycast from the pose, not from the resulting IK.
        private NativeArray<Vector3> animatedPositions;

        // The IK result from the last frame, eased toward the new targets in the job
        private NativeReference<SmoothedPose> smoothedPose;

        // Indices into animatedPositions
        private const int LEFT_FOOT = 0;
        private const int LEFT_TOE = 1;
        private const int RIGHT_FOOT = 2;
        private const int RIGHT_TOE = 3;

        private int sphereCastHandle = -1;

        public Playable CreatePlayable(PlayableGraph graph, Animator animator, Playable input) {
            Assert.IsNotNull(this.spherecastHandler);
            Assert.IsNotNull(this.leftFoot);
            Assert.IsNotNull(this.leftToe);
            Assert.IsNotNull(this.rightFoot);
            Assert.IsNotNull(this.rightToe);
            Assert.IsNotNull(this.pelvis);

            // Register so we know the handle to update
            this.sphereCastHandle = this.spherecastHandler.Register(this);
            Assert.IsTrue(this.sphereCastHandle >= 0); // Must be a valid index

            this.animatedPositions = new NativeArray<Vector3>(4, Allocator.Domain);

            // Start with the bind pose so the first casts, before the job has run, have valid origins
            this.animatedPositions[LEFT_FOOT] = this.leftFoot.position;
            this.animatedPositions[LEFT_TOE] = this.leftToe.position;
            this.animatedPositions[RIGHT_FOOT] = this.rightFoot.position;
            this.animatedPositions[RIGHT_TOE] = this.rightToe.position;

            // Start with no correction. Quaternion's default is all zeroes, so the tilts need identity
            this.smoothedPose = new NativeReference<SmoothedPose>(new SmoothedPose() {
                leftTilt = Quaternion.identity,
                rightTilt = Quaternion.identity
            }, Allocator.Domain);

            FootPelvisIkJob ikJob = new() {
                animatedPositions = this.animatedPositions,
                smoothedPose = this.smoothedPose,
                pelvis = animator.BindStreamTransform(this.pelvis),
                left = CreateLeg(animator, this.leftFoot, this.leftToe),
                right = CreateLeg(animator, this.rightFoot, this.rightToe),
                maxFootTilt = this.maxFootTilt,
                footUpSpeed = this.footUpSpeed,
                footDownSpeed = this.footDownSpeed,
                footTiltSpeed = this.footTiltSpeed,
                pelvisSpeed = this.pelvisSpeed
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

            if (this.smoothedPose.IsCreated) {
                this.smoothedPose.Dispose();
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

            FootRay leftHeelRay = PrepareRay(this.animatedPositions[LEFT_FOOT], rootHeight, HEEL_RADIUS);
            FootRay leftToeRay = PrepareRay(this.animatedPositions[LEFT_TOE], rootHeight, TOE_RADIUS);
            FootRay rightHeelRay = PrepareRay(this.animatedPositions[RIGHT_FOOT], rootHeight, HEEL_RADIUS);
            FootRay rightToeRay = PrepareRay(this.animatedPositions[RIGHT_TOE], rootHeight, TOE_RADIUS);
            
            // Update the sphere cast handler
            this.spherecastHandler.UpdateRays(this.sphereCastHandle, ref leftHeelRay, ref leftToeRay, 
                ref rightHeelRay, ref rightToeRay);
        }

        private FootRay PrepareRay(Vector3 origin, float rootHeight, float radius) {
            Assert.IsTrue(radius > 0f);
            origin.y = rootHeight + this.rayStartHeight;

            FootRay ray = new() {
                origin = origin,
                rayLength = this.rayStartHeight + this.maxStepDown,
                radius = radius
            };

            return ray;
        }

        public void ProcessCastResult(ref FootRay leftHeel, ref FootRay leftToe, ref FootRay rightHeel,
            ref FootRay rightToe) {
            // Update gizmo copies so we could render gizmos
            this.leftHeelGizmoRay = leftHeel;
            this.leftToeGizmoRay = leftToe;
            this.rightHeelGizmoRay = rightHeel;
            this.rightToeGizmoRay = rightToe;
            
            FootPelvisIkJob ikJob = this.ikPlayable.GetJobData<FootPelvisIkJob>();
            ikJob.rootHeight = this.transform.position.y;
            ikJob.maxFootTilt = this.maxFootTilt; // Might be changed in editor
            ikJob.footUpSpeed = this.footUpSpeed;
            ikJob.footDownSpeed = this.footDownSpeed;
            ikJob.footTiltSpeed = this.footTiltSpeed;
            ikJob.pelvisSpeed = this.pelvisSpeed;
            ApplyRay(ref ikJob.left, ref leftHeel, ref leftToe);
            ApplyRay(ref ikJob.right, ref rightHeel, ref rightToe);
            this.ikPlayable.SetJobData(ikJob);
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
                leg.toeGroundNormal = toeRay.hitNormal;
                leg.toeWeight = 1.0f;
            } else {
                leg.toeWeight = 0;
            }
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) {
                return;
            }
        
            DrawFootRay(ref this.leftHeelGizmoRay);
            DrawFootRay(ref this.leftToeGizmoRay);
        
            DrawFootRay(ref this.rightHeelGizmoRay);
            DrawFootRay(ref this.rightToeGizmoRay);
        }
        
        private static void DrawFootRay(ref FootRay ray) {
            if (ray.hasHit) {
                // Green to where the cast stopped, with the swept sphere
                Gizmos.color = Color.green;
                Vector3 end = ray.origin + Vector3.down * ray.hitDistance;
                Gizmos.DrawLine(ray.origin, end);
                Gizmos.DrawWireSphere(end, Mathf.Max(ray.radius, 0.02f));
        
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(ray.hitPoint, ray.hitPoint + ray.hitNormal * 0.3f);
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

        private struct SmoothedPose {
            public float leftOffset;
            public Quaternion leftTilt;

            public float rightOffset;
            public Quaternion rightTilt;

            public float pelvisOffset;
        }

        [BurstCompile]
        private struct FootPelvisIkJob : IAnimationJob {
            public NativeArray<Vector3> animatedPositions;
            public NativeReference<SmoothedPose> smoothedPose;

            public TransformStreamHandle pelvis;
            public Leg left;
            public Leg right;

            public float rootHeight;

            public float maxFootTilt;

            public float footUpSpeed;
            public float footDownSpeed;
            public float footTiltSpeed;
            public float pelvisSpeed;

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

                (float leftOffset, Quaternion leftTilt) =
                    GroundOffset(stream, ref this.left, leftFootPosition);
                (float rightOffset, Quaternion rightTilt) =
                    GroundOffset(stream, ref this.right, rightFootPosition);
                
                // Ease the result instead of the casts, so that targets stay exact and only the pose lags
                float deltaTime = stream.deltaTime;
                SmoothedPose smoothed = this.smoothedPose.Value;
                
                // The pelvis target comes from the raw offsets, so the pelvis isn't held back by the feet's
                // own easing
                float pelvisTarget = Mathf.Min(0, Mathf.Min(leftOffset, rightOffset));
                smoothed.pelvisOffset = Ease(smoothed.pelvisOffset, pelvisTarget, this.pelvisSpeed, deltaTime);

                smoothed.leftOffset = EaseFootOffset(smoothed.leftOffset, leftOffset, deltaTime);
                smoothed.rightOffset = EaseFootOffset(smoothed.rightOffset, rightOffset, deltaTime);

                float tiltT = 1f - Mathf.Exp(-this.footTiltSpeed * deltaTime);
                smoothed.leftTilt = Quaternion.Slerp(smoothed.leftTilt, leftTilt, tiltT);
                smoothed.rightTilt = Quaternion.Slerp(smoothed.rightTilt, rightTilt, tiltT);

                this.smoothedPose.Value = smoothed; // Modify the value

                // Lower the pelvis by the deepest drop so that foot can reach. Raised feet just bend the knee
                Vector3 pelvisPosition = this.pelvis.GetPosition(stream);
                this.pelvis.SetPosition(stream, pelvisPosition + new Vector3(0, smoothed.pelvisOffset, 0));

                Solve(stream, ref this.left, leftFootPosition, 
                    smoothed.leftTilt * leftFootRotation, smoothed.leftOffset);
                Solve(stream, ref this.right, rightFootPosition, 
                    smoothed.rightTilt * rightFootRotation, smoothed.rightOffset);
            }

            private float EaseFootOffset(float current, float target, float deltaTime) {
                float speed = target > current ? this.footUpSpeed : this.footDownSpeed;
                return Ease(current, target, speed, deltaTime);
            }

            // Frame-rate independent exponential ease, speed is roughly 1 / time constant
            private static float Ease(float current, float target, float speed, float deltaTime) {
                return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * deltaTime));
            }

            // How far the ankle has to move up (or down), and the tilt that lays the foot on the ground measured under
            // the heel and the toe. No correction on a heel miss, so the smoothing eases the foot back to its clip pose
            private (float offset, Quaternion tilt) GroundOffset(AnimationStream stream, ref Leg leg,
                Vector3 footPosition) {
                if (leg.heelWeight <= 0f) {
                    return (0f, Quaternion.identity);
                }

                Vector3 toePosition = leg.toe.GetPosition(stream);
                Vector3 toeFromAnkle = toePosition - footPosition;

                Quaternion tilt = leg.toeWeight > 0f
                    ? GroundTilt(ref leg, footPosition, toePosition)
                    : Quaternion.identity;

                // Real ankles only bend so far, so limit the tilt. Matching steep slopes fully looks broken
                tilt = Quaternion.RotateTowards(Quaternion.identity, tilt, this.maxFootTilt);

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

                return (offset, tilt);
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

                // The solver rotated the parents, so set the final world rotation: the smoothed tilt on the clip's
                // foot rotation
                leg.low.SetRotation(stream, targetRotation);
            }
        }
    }
}
