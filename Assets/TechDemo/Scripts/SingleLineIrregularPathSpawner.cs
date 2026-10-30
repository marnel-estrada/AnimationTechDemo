using UnityEngine;
using UnityEngine.Serialization;

namespace TechDemo {
    public class SingleLineIrregularPathSpawner : MonoBehaviour {
        [SerializeField]
        private GameObject platformPrefab;

        [SerializeField]
        private int count = 5;

        [FormerlySerializedAs("minX")]
        [SerializeField]
        private float minXOffset = -0.25f;
        
        [FormerlySerializedAs("maxX")]
        [SerializeField]
        private float maxXOffset = 0.25f;
        
        [SerializeField]
        private float minYOffset = -0.25f;
        
        [SerializeField]
        private float maxYOffset = 0.25f;

        [SerializeField]
        private float minForward = 0.15f;
        
        [SerializeField]
        private float maxForward = 0.25f;

        [SerializeField]
        private float minXRotation = -15f;
        
        [SerializeField]
        private float maxXRotation = 15f;

        private void Awake() {
            Vector3 position = this.transform.position;

            for (int i = 0; i < this.count; i++) {
                Spawn(position);
                
                // Next
                position.z += Random.Range(this.minForward, this.maxForward);
            }
        }

        private void Spawn(Vector3 pos) {
            pos.x += Random.Range(this.minXOffset, this.maxXOffset);
            pos.y += Random.Range(this.minYOffset, this.maxYOffset);

            float angle = Random.Range(this.minXRotation, this.maxXRotation);
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.right);
            
            GameObject instance = Instantiate(this.platformPrefab, pos, rotation);
            instance.transform.SetParent(this.transform);
        }
    }
}