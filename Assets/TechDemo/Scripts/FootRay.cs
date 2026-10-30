using UnityEngine;

namespace TechDemo {
    public struct FootRay {
        public Vector3 origin;
        public Vector3 hitNormal;
        public Vector3 hitPoint;
        
        public float rayLength;
        public float radius;
        public float hitDistance;
        
        public bool hasHit;

        public float GroundY => this.origin.y - this.hitDistance - radius;
    }
}