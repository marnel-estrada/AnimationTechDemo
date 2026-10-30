using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Jobs;

namespace TechDemo {
    /// <summary>
    /// Handles the multiple raycasts needed to reposition agents' y position based on the hit ground object.
    /// </summary>
    public class AnimatorsMoveRaycastHandler : MonoBehaviour {
        [SerializeField]
        private int capacity = 1024;
        
        [SerializeField]
        private LayerMask groundMask;

        private TransformAccessArray transforms;
        private List<Animator> animators;
        private NativeList<EntryData> entries;

        private void Awake() {
            this.transforms = new TransformAccessArray(this.capacity, JobsUtility.JobWorkerCount);
            this.animators = new List<Animator>(this.capacity);
            this.entries = new NativeList<EntryData>(this.capacity, Allocator.Domain);
        }

        private void OnDestroy() {
            this.transforms.Dispose();
            this.entries.Dispose();
        }

        public void Register(Animator animator, float groundRayHeight, float groundRayLength, 
            float groundSmoothTime) {
            this.transforms.Add(animator.transform.GetEntityId());
            this.animators.Add(animator);
            this.entries.Add(new EntryData(groundRayHeight, groundRayLength, groundSmoothTime));
        }

        private void LateUpdate() {
            int animatorCount = this.animators.Count;

            if (animatorCount == 0) {
                return; // Nothing to process
            }
            
            // Collect animator delta position
            NativeArray<Vector3> deltaPositions = new(animatorCount, Allocator.TempJob);
            for (int i = 0; i < animatorCount; i++) {
                deltaPositions[i] = this.animators[i].deltaPosition;
            }
            
            // Prepare the commands
            NativeArray<RaycastHit> results = new(animatorCount, Allocator.TempJob); // Only 1 hit per command
            NativeArray<RaycastCommand> commands = new(animatorCount, Allocator.TempJob);

            PrepareCommandsJob prepareCommandsJob = new() {
                deltaPositions = deltaPositions,
                entries = this.entries,
                commands = commands,
                groundMask = this.groundMask.value
            };
            JobHandle handle = prepareCommandsJob.ScheduleByRef(this.transforms);
            
            // Schedule the raycasts
            int commandsPerJob = Mathf.Max(animatorCount / JobsUtility.JobWorkerCount, 1);
            handle = RaycastCommand.ScheduleBatch(commands, results, commandsPerJob, handle);

            ProcessResultsJob processResultsJob = new() {
                deltaPositions = deltaPositions,
                hits = results,
                entries = this.entries,
                deltaTime = Time.deltaTime
            };
            handle = processResultsJob.ScheduleByRef(this.transforms, handle);
            
            handle.Complete();
            
            // Don't forget to dispose
            deltaPositions.Dispose();
            results.Dispose();
            commands.Dispose();
        }
        
        [BurstCompile]
        private struct PrepareCommandsJob : IJobParallelForTransform {
            [ReadOnly]
            public NativeArray<Vector3> deltaPositions;
            
            [ReadOnly]
            public NativeList<EntryData> entries;
            
            [NativeDisableParallelForRestriction]
            public NativeArray<RaycastCommand> commands;

            public int groundMask;
            
            public void Execute(int index, TransformAccess transform) {
                EntryData entry = this.entries[index];
                
                Vector3 position = transform.position + this.deltaPositions[index];
                Vector3 origin = position + new Vector3(0, entry.groundRayHeight, 0);
                this.commands[index] = new RaycastCommand(origin, Vector3.down, 
                    new QueryParameters(this.groundMask), entry.groundRayLength);
            }
        }
        
        [BurstCompile]
        private struct ProcessResultsJob : IJobParallelForTransform {
            [ReadOnly]
            public NativeArray<Vector3> deltaPositions;
            
            [ReadOnly]
            public NativeArray<RaycastHit> hits;
            
            [NativeDisableParallelForRestriction]
            public NativeList<EntryData> entries;

            public float deltaTime;

            public void Execute(int index, TransformAccess transform) {
                Vector3 position = transform.position + this.deltaPositions[index];
                RaycastHit hit = hits[index];
                if (hit.colliderEntityId == EntityId.None) {
                    // No hit. Just set the position.
                    transform.position = position;
                    return;
                }
                
                // Has a hit
                EntryData entry = this.entries[index];
                position.y = Mathf.SmoothDamp(position.y, hit.point.y, ref entry.groundVelocity,
                    entry.groundSmoothTime, Mathf.Infinity, this.deltaTime);
                transform.position = position;
                
                // Set the entry back as the groundVelocity changed
                this.entries[index] = entry;
            }
        }

        private struct EntryData {
            public readonly float groundRayHeight;
            public readonly float groundRayLength;
            public readonly float groundSmoothTime;
            
            public float groundVelocity;

            public EntryData(float groundRayHeight, float groundRayLength, float groundSmoothTime) {
                this.groundRayHeight = groundRayHeight;
                this.groundRayLength = groundRayLength;
                this.groundSmoothTime = groundSmoothTime;
                
                this.groundVelocity = 0f;
            }
        }
    }
}