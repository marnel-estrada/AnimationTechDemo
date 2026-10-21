using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class LookAt : MonoBehaviour {
        [SerializeField]
        private Transform target; // The target to look at

        [SerializeField]
        private AnimationClip animationClip;
        
        [SerializeField]
        private Transform joint;
        
        [SerializeField]
        private Axis axis = Axis.Forward;

        [SerializeField]
        private float minAngle = -70.0f;
        
        [SerializeField]
        private float maxAngle = 70.0f;

        private PlayableGraph playableGraph;
        private AnimationScriptPlayable lookAtPlayable;

        private void OnEnable() {
            Assert.IsNotNull(this.target);
            Assert.IsNotNull(this.animationClip);
            Assert.IsNotNull(this.joint);

            Animator animator = GetComponent<Animator>();
            animator.fireEvents = false;
            
            this.playableGraph = PlayableGraph.Create("LookAt");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", animator);

            LookAtJob lookAtJob = new() {
                joint = animator.BindStreamTransform(this.joint),
                target = animator.BindSceneTransform(this.target),
                jointAxis = GetAxisVector(this.axis),
                minAngle = Mathf.Min(this.minAngle, this.maxAngle),
                maxAngle = Mathf.Max(this.minAngle, this.maxAngle)
            };
            
            this.lookAtPlayable = AnimationScriptPlayable.Create(this.playableGraph, lookAtJob);
            AnimationClipPlayable animationClipPlayable = AnimationClipPlayable.Create(this.playableGraph, this.animationClip);
            this.lookAtPlayable.AddInput(animationClipPlayable, 0, 1.0f);
            
            output.SetSourcePlayable(this.lookAtPlayable);
            this.playableGraph.Play();
        }

        private static Vector3 GetAxisVector(Axis axis)
        {
            switch (axis)
            {
                case Axis.Forward:
                    return Vector3.forward;
                case Axis.Back:
                    return Vector3.back;
                case Axis.Up:
                    return Vector3.up;
                case Axis.Down:
                    return Vector3.down;
                case Axis.Left:
                    return Vector3.left;
                case Axis.Right:
                    return Vector3.right;
            }

            return Vector3.forward;
        }

        private enum Axis
        {
            Forward,
            Back,
            Up,
            Down,
            Left,
            Right
        }
        
        [BurstCompile]
        private struct LookAtJob : IAnimationJob {
            public TransformStreamHandle joint;
            public TransformSceneHandle target;
            public Vector3 jointAxis;
            public float minAngle;
            public float maxAngle;
            
            public void ProcessRootMotion(AnimationStream stream) {
            }
            
            public void ProcessAnimation(AnimationStream stream) {
                // Solve
                Vector3 jointPosition = this.joint.GetPosition(stream);
                Quaternion jointRotation = this.joint.GetRotation(stream);
                Vector3 targetPosition = this.target.GetPosition(stream);

                Vector3 fromDir = jointRotation * this.jointAxis;
                Vector3 toDir = targetPosition - jointPosition;

                Vector3 axis = Vector3.Cross(fromDir, toDir).normalized;
                float angle = Vector3.Angle(fromDir, toDir);
                angle = Mathf.Clamp(angle, this.minAngle, this.maxAngle);
                Quaternion jointToTargetRotation = Quaternion.AngleAxis(angle, axis);
                jointRotation = jointToTargetRotation * jointRotation;
                this.joint.SetRotation(stream, jointRotation);
            }
        }
    }
}