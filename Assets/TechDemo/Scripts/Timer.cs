using UnityEngine;
using UnityEngine.Assertions;

namespace TechDemo {
    public struct Timer {
        private float polled;
        public readonly float duration;

        public Timer(float duration) {
            this.duration = duration;
            Assert.IsFalse(Mathf.Approximately(this.duration, 0)); // Must not be zero
            
            this.polled = 0;
        }

        public void Update(float deltaTime) {
            this.polled += deltaTime;
        }

        public void Reset() {
            this.polled = 0;
        }

        public float Ratio => Mathf.Clamp01(this.polled / this.duration);

        public bool HasElapsed => this.polled >= this.duration;
    }
}