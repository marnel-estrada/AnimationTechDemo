using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TechDemo {
    public class SimpleFootIk : MonoBehaviour {
        [SerializeField]
        private Transform footJoint;

        [SerializeField]
        private AnimationClip animationClip;

        [SerializeField]
        private LayerMask groundMask = 0;

        [SerializeField]
        private float rayStartHeight = 0.5f;

        [SerializeField]
        private float rayLength = 1.5f;

        private PlayableGraph playableGraph;
        private AnimationScriptPlayable ikPlayable;

        // Last raycast, for gizmos
        private Vector3 rayOrigin;
        private bool hasHit;
        private RaycastHit lastHit;

        private void OnEnable() {
            Assert.IsNotNull(this.footJoint);
            Assert.IsNotNull(this.animationClip);

            this.playableGraph = PlayableGraph.Create("FootIk");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            Animator animator = GetComponent<Animator>();
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", animator);
            
            Transform knee = this.footJoint.parent;
            Assert.IsNotNull(knee);
            
            Transform hip = knee.parent;
            Assert.IsNotNull(hip);

            FootIkJob ikJob = new() {
                top = animator.BindStreamTransform(hip),
                mid = animator.BindStreamTransform(knee),
                low = animator.BindStreamTransform(this.footJoint),
                groundNormal = Vector3.up
            };
            
            // We call SetApplyFootIK(false) so that our custom IK works on the foot
            AnimationClipPlayable animationClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.animationClip);
            animationClipPlayable.SetApplyFootIK(false);
            
            this.ikPlayable = AnimationScriptPlayable.Create(this.playableGraph, ikJob);
            this.ikPlayable.AddInput(animationClipPlayable, 0, 1f);
            
            output.SetSourcePlayable(this.ikPlayable);
            this.playableGraph.Play();
        }

        private void Update() {
            // Do the raycast here since Physics can't run in the animation job.
            // This uses the last frame's foot position, though
            float rootHeight = this.transform.position.y;
            Vector3 origin = this.footJoint.position;
            origin.y = rootHeight + this.rayStartHeight;
            this.rayOrigin = origin;

            FootIkJob ikJob = this.ikPlayable.GetJobData<FootIkJob>();
            ikJob.rootHeight = rootHeight;

            this.hasHit = Physics.Raycast(origin, Vector3.down, out this.lastHit, this.rayLength, this.groundMask);
            if (this.hasHit) {
                ikJob.groundHeight = this.lastHit.point.y;
                ikJob.groundNormal = this.lastHit.normal;
                ikJob.weight = 1.0f;
            } else {
                ikJob.weight = 0;
            }

            this.ikPlayable.SetJobData(ikJob);
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) {
                return;
            }

            if (this.hasHit) {
                // Green up to the hit, then the hit point and its normal
                Gizmos.color = Color.green;
                Gizmos.DrawLine(this.rayOrigin, this.lastHit.point);
                Gizmos.DrawWireSphere(this.lastHit.point, 0.03f);

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(this.lastHit.point, this.lastHit.point + this.lastHit.normal * 0.3f);
            } else {
                // Red full length on a miss
                Gizmos.color = Color.red;
                Gizmos.DrawLine(this.rayOrigin, this.rayOrigin + Vector3.down * this.rayLength);
            }
        }
        
        [BurstCompile]
        private struct FootIkJob : IAnimationJob {
            public TransformStreamHandle top;
            public TransformStreamHandle mid;
            public TransformStreamHandle low;

            public float rootHeight;
            public float groundHeight;
            public Vector3 groundNormal;
            public float weight;
            
            public void ProcessRootMotion(AnimationStream stream) {
            }
            
            public void ProcessAnimation(AnimationStream stream) {
                if (this.weight <= 0f) {
                    return;
                }

                Vector3 footPosition = this.low.GetPosition(stream);
                Quaternion footRotation = this.low.GetRotation(stream);
                
                // Keep the clip's ankle height, but measure it from the hit ground instead of the root
                float ankleHeight = footPosition.y - this.rootHeight;
                Vector3 target = new(footPosition.x, this.groundHeight + ankleHeight, footPosition.z);
                Debug.Log($"groundHeight {this.groundHeight} footY {footPosition.y} targetY {target.y}");
                TwoBoneIkSolver.Solve(stream, this.top, this.mid, this.low, target);
                
                Debug.Log($"solvedFootY {this.low.GetPosition(stream).y}");
                
                // The solver rotated the parents, so put back the animation world rotation, then tilt it to the ground
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, this.groundNormal);
                this.low.SetRotation(stream, Quaternion.Slerp(footRotation, tilt * footRotation, this.weight));
            }
        }
    }
}