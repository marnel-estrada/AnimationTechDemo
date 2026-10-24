using UnityEngine;

namespace TechDemo {
    public class LerpZRotation : MonoBehaviour {
        [SerializeField]
        private float z1 = 45f;

        [SerializeField]
        private float z2 = -45;
        
        [SerializeField]
        private float duration = 2.0f;

        private Timer timer;
        private float zFrom;
        private float zDest;

        private void Awake() {
            this.timer = new Timer(this.duration);
            this.zFrom = this.z1;
            this.zDest = this.z2;
        }

        private void Update() {
            this.timer.Update(Time.deltaTime);
            if (this.timer.HasElapsed) {
                // Snap to dest. Reset the timer then swap the positions.
                Vector3 euler = this.transform.localEulerAngles;
                euler.z = this.zDest;
                this.transform.localEulerAngles = euler;
                
                this.timer.Reset();
                (zFrom, zDest) = (zDest, zFrom);
                return;
            }
            
            // Interpolate z rotation
            Vector3 newEuler = this.transform.localEulerAngles;
            newEuler.z = Mathf.Lerp(this.zFrom, this.zDest, this.timer.Ratio);
            this.transform.localEulerAngles = newEuler;
        }
    }
}