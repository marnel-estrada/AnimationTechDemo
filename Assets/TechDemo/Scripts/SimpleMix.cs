using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace TechDemo {
    public class SimpleMix : MonoBehaviour {
        [SerializeField]
        private AnimationClip clip1;
        
        [SerializeField]
        private AnimationClip clip2;
        
        [SerializeField]
        [Range(0.0f, 1.0f)]
        private float weight;

        private NativeArray<TransformStreamHandle> transformHandles;
        private NativeArray<float> boneWeights;
        
        private PlayableGraph playableGraph;
        private AnimationScriptPlayable customPlayable;

        private AnimationClipPlayable clipPlayable1;
        private AnimationClipPlayable clipPlayable2;

        private void OnEnable() {
            Assert.IsNotNull(this.clip1);
            Assert.IsNotNull(this.clip2);

            Animator animator = GetComponent<Animator>();
            
            // Get all transforms in the heirarchy
            Transform[] transforms = animator.transform.GetComponentsInChildren<Transform>();
            int transformCount = transforms.Length - 1;
            
            // Fill native arrays
            this.transformHandles = new NativeArray<TransformStreamHandle>(transformCount, Allocator.Domain,
                NativeArrayOptions.UninitializedMemory);
            this.boneWeights = new NativeArray<float>(transformCount, Allocator.Domain);
            for (int i = 0; i < transformCount; i++) {
                this.transformHandles[i] = animator.BindStreamTransform(transforms[i + 1]);
                this.boneWeights[i] = 1.0f;
            }
            
            // Create job.
            MixerJob mixerJob = new() {
                handles = this.transformHandles,
                boneWeights = this.boneWeights,
                weight = 0f
            };
            
            // Create graph
            this.playableGraph = PlayableGraph.Create("SimpleMix");
            this.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            
            this.customPlayable = AnimationScriptPlayable.Create(this.playableGraph, mixerJob);
            this.customPlayable.SetProcessInputs(false);
            
            this.clipPlayable1 = AnimationClipPlayable.Create(this.playableGraph, this.clip1);
            this.clipPlayable2 = AnimationClipPlayable.Create(this.playableGraph, this.clip2);
            
            this.customPlayable.AddInput(this.clipPlayable1, 0, 1.0f);
            this.customPlayable.AddInput(this.clipPlayable2, 0, 1.0f);
            
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(this.playableGraph, "output", animator);
            output.SetSourcePlayable(this.customPlayable);
            
            this.playableGraph.Play();
        }

        private void OnDisable() {
            this.transformHandles.Dispose();
            this.boneWeights.Dispose();
            this.playableGraph.Destroy();
        }

        private float prevWeight;

        private void Update() {
            if (Mathf.Approximately(this.prevWeight, this.weight)) {
                return; // No change of weight
            }
            
            // Set the weight as it could be changed in the editor.
            MixerJob mixerJob = this.customPlayable.GetJobData<MixerJob>();
            mixerJob.weight = this.weight;
            this.customPlayable.SetJobData(mixerJob);
            
            // Play both clips at speeds that make their cycles last the same blended length,
            // so they stay at the same normalized time and don't drift apart.
            float length1 = this.clip1.length;
            float length2 = this.clip2.length;
            float blendedLength = Mathf.Lerp(length1, length2, this.weight);
            this.clipPlayable1.SetSpeed(length1 / blendedLength);
            this.clipPlayable2.SetSpeed(length2 / blendedLength);

            this.prevWeight = this.weight;
        }

        [BurstCompile]
        private struct MixerJob : IAnimationJob {
            public NativeArray<TransformStreamHandle> handles;
            public NativeArray<float> boneWeights;
            public float weight;
            
            public void ProcessRootMotion(AnimationStream stream) {
                AnimationStream streamA = stream.GetInputStream(0);
                AnimationStream streamB = stream.GetInputStream(1);

                stream.velocity = Vector3.Lerp(streamA.velocity, streamB.velocity, weight);
                stream.angularVelocity = Vector3.Lerp(streamA.angularVelocity, streamB.angularVelocity, weight);
            }

            public void ProcessAnimation(AnimationStream stream) {
                AnimationStream streamA = stream.GetInputStream(0);
                AnimationStream streamB = stream.GetInputStream(1);

                int handlesCount = this.handles.Length;
                for (int i = 0; i < handlesCount; i++) {
                    TransformStreamHandle handle = this.handles[i];

                    Vector3 posA = handle.GetLocalPosition(streamA);
                    Vector3 posB = handle.GetLocalPosition(streamB);
                    handle.SetLocalPosition(stream, Vector3.Lerp(posA, posB, weight * this.boneWeights[i]));

                    Quaternion rotA = handle.GetLocalRotation(streamA);
                    Quaternion rotB = handle.GetLocalRotation(streamB);
                    handle.SetLocalRotation(stream, Quaternion.Slerp(rotA, rotB, weight * this.boneWeights[i]));
                }
            }
        }
    }
}