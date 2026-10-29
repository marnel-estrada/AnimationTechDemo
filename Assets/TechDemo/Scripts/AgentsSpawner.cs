using UnityEngine;

namespace TechDemo {
    public class AgentsSpawner : MonoBehaviour {
        [SerializeField]
        private GameObject agentPrefab;

        [SerializeField]
        private int spawnCount = 10;

        [SerializeField]
        private Vector3 minPos = new(-25f, 0, -25f);

        [SerializeField]
        private Vector3 maxPos = new(25f, 0, 25f);

        private void Awake() {
            for (int i = 0; i < this.spawnCount; i++) {
                Vector3 position = new() {
                    x = Random.Range(this.minPos.x, this.maxPos.x),
                    y = 0,
                    z = Random.Range(this.minPos.z, this.maxPos.z)
                };
                Instantiate(this.agentPrefab, position, Quaternion.identity);
            }
        }
    }
}