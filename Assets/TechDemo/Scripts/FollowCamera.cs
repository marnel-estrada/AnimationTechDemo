using UnityEngine;
using UnityEngine.Assertions;

namespace TechDemo {
    // Follows the target at a fixed offset without rotating with it, so camera-relative input stays stable
    public class FollowCamera : MonoBehaviour {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset = new(0f, 2.5f, -4.0f);

        [SerializeField]
        private float lookAtHeight = 1.2f;

        private void Awake() {
            Assert.IsNotNull(this.target);
        }

        private void LateUpdate() {
            this.transform.position = this.target.position + this.offset;
            this.transform.LookAt(this.target.position + Vector3.up * this.lookAtHeight);
        }
    }
}