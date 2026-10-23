using UnityEngine;

namespace TechDemo {
    /// <summary>
    /// Just a simple movement of y position by interpolation
    /// </summary>
    public class YMovement : MonoBehaviour {
        [SerializeField]
        private float y1 = 0.05f;
        
        [SerializeField]
        private float y2 = -0.5f;

        [SerializeField]
        private float duration = 1.0f;

        private Timer timer;
        private float yFrom;
        private float yDest;

        private void Awake() {
            this.timer = new Timer(this.duration);
            this.yFrom = this.y1;
            this.yDest = this.y2;
        }

        private void Update() {
            this.timer.Update(Time.deltaTime);
            if (this.timer.HasElapsed) {
                // Snap to dest. Reset the timer then swap the positions.
                Vector3 position = this.transform.position;
                position.y = this.yDest;
                this.transform.position = position;
                
                this.timer.Reset();
                (yFrom, yDest) = (yDest, yFrom);
                return;
            }
            
            // Interpolate y positions
            Vector3 newPosition = this.transform.position;
            newPosition.y = Mathf.Lerp(this.yFrom, this.yDest, this.timer.Ratio);
            this.transform.position = newPosition;
        }
    }
}