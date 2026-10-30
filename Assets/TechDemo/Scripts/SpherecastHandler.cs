using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace TechDemo {
    /// <summary>
    /// Handles all the sphere casting required. Uses SpherecastCommand.
    /// </summary>
    public class SpherecastHandler : MonoBehaviour {
        [SerializeField]
        private int capacity = 1024;
        
        [SerializeField]
        private LayerMask groundMask = 0;

        // We return a handle to the foot ray set of an agent whenever one wants to register
        private int handleCount;

        private NativeArray<FootRay> footRays;

        private FootPlacementPostProcess[] footPlacements;

        private const int SPHERECAST_COUNT_PER_FOOT_PLACEMENT = 4;
            
        private void Awake() {
            this.footRays =
                new NativeArray<FootRay>(this.capacity * SPHERECAST_COUNT_PER_FOOT_PLACEMENT, Allocator.Domain);
            this.footPlacements = new FootPlacementPostProcess[this.capacity];
        }

        public int Register(FootPlacementPostProcess footPlacement) {
            if (this.handleCount >= this.capacity) {
                throw new Exception($"Reached capacity: {this.capacity}");
            }
            
            int index = this.handleCount;
            this.footPlacements[index] = footPlacement;
            ++this.handleCount;

            return index;
        }

        public void UpdateRays(int handle, ref FootRay leftHeel, ref FootRay leftToe, ref FootRay rightHeel,
            ref FootRay rightToe) {
            int startIndex = handle * SPHERECAST_COUNT_PER_FOOT_PLACEMENT;
            this.footRays[startIndex] = leftHeel;
            this.footRays[startIndex + 1] = leftToe;
            this.footRays[startIndex + 2] = rightHeel;
            this.footRays[startIndex + 3] = rightToe;
        }

        private void Update() {
            if (this.handleCount <= 0) {
                return; // Nothing to process
            }
            
            // Prepare the SpherecastCommands
            int commandCount = this.handleCount * SPHERECAST_COUNT_PER_FOOT_PLACEMENT;
            NativeArray<RaycastHit> results = new(commandCount, Allocator.TempJob); // Only 1 hit per command
            NativeArray<SpherecastCommand> commands = new(commandCount, Allocator.TempJob);

            PrepareCommandsJob prepareCommandsJob = new() {
                footRays = this.footRays,
                commands = commands,
                layerMask = this.groundMask.value
            };
            JobHandle handle = prepareCommandsJob.ScheduleParallelByRef(commandCount, 64, default);
            
            // Schedule the raycasts
            int commandsPerJob = Mathf.Max(commandCount / JobsUtility.JobWorkerCount, 1);
            handle = SpherecastCommand.ScheduleBatch(commands, results, commandsPerJob, handle);

            ProcessResultsJob processResultsJob = new() {
                hits = results,
                footRays = this.footRays
            };
            handle = processResultsJob.ScheduleParallelByRef(commandCount, 64, handle);
            
            // We complete here so we get the results
            handle.Complete();
            
            // Call ProcessCastResult() for each foot placement
            for (int i = 0; i < this.handleCount; i++) {
                int startIndex = i * SPHERECAST_COUNT_PER_FOOT_PLACEMENT;
                FootRay leftHeel = this.footRays[startIndex];
                FootRay leftToe = this.footRays[startIndex + 1];
                FootRay rightHeel = this.footRays[startIndex + 2];
                FootRay rightToe = this.footRays[startIndex + 3];
                
                this.footPlacements[i].ProcessCastResult(ref leftHeel, ref leftToe, ref rightHeel, ref rightToe);
            }
            
            // Don't forget to dispose
            results.Dispose();
            commands.Dispose();
        }

        private void OnDestroy() {
            if (this.footRays.IsCreated) {
                this.footRays.Dispose();
            }
        }
        
        [BurstCompile]
        private struct PrepareCommandsJob : IJobFor {
            [ReadOnly]
            public NativeArray<FootRay> footRays;

            [NativeDisableParallelForRestriction]
            public NativeArray<SpherecastCommand> commands;

            public int layerMask;
            
            public void Execute(int index) {
                FootRay ray = this.footRays[index];
                this.commands[index] = new SpherecastCommand(ray.origin, ray.radius, Vector3.down,
                    new QueryParameters(this.layerMask), ray.rayLength - ray.radius);
            }
        }
        
        [BurstCompile]
        private struct ProcessResultsJob : IJobFor {
            [ReadOnly]
            public NativeArray<RaycastHit> hits;

            [NativeDisableParallelForRestriction]
            public NativeArray<FootRay> footRays;
            
            public void Execute(int index) {
                RaycastHit hit = hits[index];
                
                FootRay footRay = this.footRays[index];
                footRay.hasHit = hit.colliderEntityId != EntityId.None;
                footRay.hitNormal = hit.normal;
                footRay.hitPoint = hit.point;
                footRay.hitDistance = hit.distance;
                this.footRays[index] = footRay; // Modify with the hit result
            }
        }
    }
}