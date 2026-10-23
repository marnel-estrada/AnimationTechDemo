using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class TwoBoneIk : MonoBehaviour {
        [SerializeField]
        private Transform endJoint;

        [SerializeField]
        private AnimationClip animationClip;

        [SerializeField]
        private GameObject effectorPrefab;

        private Transform topJoint;
        private Transform midJoint;
        private GameObject effector;

        private PlayableGraph playableGraph;
        private AnimationScriptPlayable ikPlayable;

        private void OnEnable() {
            Assert.IsNotNull(this.endJoint);
            Assert.IsNotNull(this.animationClip);
            Assert.IsNotNull(this.effectorPrefab);

            this.midJoint = this.endJoint.parent;
            Assert.IsNotNull(this.midJoint);

            this.topJoint = this.midJoint.parent;
            Assert.IsNotNull(this.topJoint);

            this.effector = Instantiate(this.effectorPrefab, this.endJoint.position, this.endJoint.rotation);
            Assert.IsNotNull(this.effector);
            
            this.playableGraph = PlayableGraph.Create("TwoBoneIK");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            Animator animator = GetComponent<Animator>();
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", animator);

            TwoBoneIkJob ikJob = new() {
                effector = animator.BindSceneTransform(this.effector.transform),
                top = animator.BindStreamTransform(this.topJoint),
                mid = animator.BindStreamTransform(this.midJoint),
                low = animator.BindStreamTransform(this.endJoint)
            };
            
            // We call SetApplyFootIK(false) so that our custom IK works on the foot
            AnimationClipPlayable animationClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.animationClip);
            animationClipPlayable.SetApplyFootIK(false);
            
            this.ikPlayable = AnimationScriptPlayable.Create(this.playableGraph, ikJob);
            this.ikPlayable.AddInput(animationClipPlayable, 0, 1f);
            
            output.SetSourcePlayable(this.ikPlayable);
            this.playableGraph.Play();
        }

        private void OnDisable() {
            this.playableGraph.Destroy();
        }
        
        [BurstCompile]
        private struct TwoBoneIkJob : IAnimationJob {
            public TransformSceneHandle effector;

            public TransformStreamHandle top;
            public TransformStreamHandle mid;
            public TransformStreamHandle low;
            
            public void ProcessRootMotion(AnimationStream stream) {
            }
            
            public void ProcessAnimation(AnimationStream stream) {
                // Solve
                Quaternion aRotation = this.top.GetRotation(stream);
                Quaternion bRotation = this.mid.GetRotation(stream);

                Vector3 aPosition = this.top.GetPosition(stream);
                Vector3 bPosition = this.mid.GetPosition(stream);
                Vector3 cPosition = this.low.GetPosition(stream);
                Vector3 ePosition = this.effector.GetPosition(stream);

                Vector3 ab = bPosition - aPosition;
                Vector3 bc = cPosition - bPosition;
                Vector3 ac = cPosition - aPosition;
                Vector3 ae = ePosition - aPosition;

                float abcAngle = TriangleAngle(ac.magnitude, ab, bc);
                float abeAngle = TriangleAngle(ae.magnitude, ab, bc);
                float angle = (abcAngle - abeAngle) * Mathf.Rad2Deg;
                Vector3 axis = Vector3.Cross(ab, bc).normalized;

                Quaternion targetMidRotation = Quaternion.AngleAxis(angle, axis);

                Quaternion worldQ = targetMidRotation * bRotation;
                this.mid.SetRotation(stream, worldQ);

                cPosition = this.low.GetPosition(stream);
                ac = cPosition - aPosition;
                Quaternion targetTopRotation = Quaternion.FromToRotation(ac, ae);
                this.top.SetRotation(stream, targetTopRotation * aRotation);
            }

            // Law of cosines
            private static float TriangleAngle(float desiredLen, Vector3 v1, Vector3 v2) {
                float v1Len = v1.magnitude;
                float v2Len = v2.magnitude;
                float value = (v1Len * v1Len + v2Len * v2Len - desiredLen * desiredLen) / (v1Len * v2Len * 2.0f);
                float c = Mathf.Clamp(value, -1f, 1f);
                return Mathf.Acos(c);
            }
        }
    }
}