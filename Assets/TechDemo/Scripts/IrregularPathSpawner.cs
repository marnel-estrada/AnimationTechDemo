using UnityEngine;
using UnityEngine.Assertions;

namespace TechDemo {
    public class IrregularPathSpawner : MonoBehaviour {
        [SerializeField]
        private GameObject singleLineSpawner;

        [SerializeField]
        private float startX = -24f;

        [SerializeField]
        private int spawnCount = 24;

        private void Awake() {
            Assert.IsNotNull(this.singleLineSpawner);

            Vector3 position = this.transform.position;
            position.x = this.startX;

            for (int i = 0; i < this.spawnCount; i++) {
                Instantiate(this.singleLineSpawner, position, Quaternion.identity);
                position.x += 1f;
            }
        }
    }
}