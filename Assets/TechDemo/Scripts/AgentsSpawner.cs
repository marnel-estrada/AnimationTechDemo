using UnityEngine;
using UnityEngine.Assertions;

namespace TechDemo {
    public class AgentsSpawner : MonoBehaviour {
        [SerializeField]
        private SpherecastHandler spherecastHandler;

        [SerializeField]
        private AnimatorsMoveRaycastHandler animatorsMoveHandler;
        
        [SerializeField]
        private GameObject agentPrefab;

        [SerializeField]
        private int spawnCount = 10;

        [SerializeField]
        private Vector3 minPos = new(-25f, 0, -25f);

        [SerializeField]
        private Vector3 maxPos = new(25f, 0, 25f);

        private void Start() {
            Assert.IsNotNull(this.spherecastHandler);
            Assert.IsNotNull(this.animatorsMoveHandler);
            Assert.IsNotNull(this.agentPrefab);
            
            for (int i = 0; i < this.spawnCount; i++) {
                Vector3 position = new() {
                    x = Random.Range(this.minPos.x, this.maxPos.x),
                    y = 0,
                    z = Random.Range(this.minPos.z, this.maxPos.z)
                };
                
                GameObject go = Instantiate(this.agentPrefab, position, Quaternion.identity);
                
                // The sphere cast handler must be set first before preparing the playable graph
                FootPlacementPostProcess footPlacement = go.GetComponent<FootPlacementPostProcess>();
                Assert.IsNotNull(footPlacement);
                footPlacement.Init(this.spherecastHandler);

                AgentWalker walker = go.GetComponent<AgentWalker>();
                Assert.IsNotNull(walker);
                walker.Prepare(this.animatorsMoveHandler);
            }
        }
    }
}