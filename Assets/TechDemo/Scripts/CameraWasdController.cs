using UnityEngine;
using UnityEngine.InputSystem;

namespace TechDemo {
    // FPS-style free camera for the object it's on. Holding the right mouse button turns it, like the Scene view, so
    // the cursor stays free for the Editor otherwise. WASD flies it where it faces: W forward, S back, A and D sideways
    public class CameraWasdController : MonoBehaviour {
        [SerializeField]
        private float moveSpeed = 2; // Units per second

        [SerializeField]
        private float lookSensitivity = 0.1f; // Degrees per pixel of mouse movement

        // Short of 90 so forward never points straight up or down
        [SerializeField]
        private float maxPitch = 89f;

        private float yaw;
        private float pitch;

        private void OnEnable() {
            // Continue from the rotation placed in the scene. Roll is dropped
            Vector3 euler = this.transform.eulerAngles;
            this.yaw = euler.y;
            this.pitch = Mathf.DeltaAngle(0f, euler.x); // 0..360 to -180..180, so the clamp works
        }

        private void OnDisable() {
            // Don't leave the cursor locked if disabled mid-drag
            LockCursor(false);
        }

        private void Update() {
            UpdateLook();
            UpdateMove();
        }

        private static void LockCursor(bool locked) {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void UpdateLook() {
            Mouse mouse = Mouse.current;
            if (mouse == null) {
                return;
            }

            // Locked only while dragging, so turning doesn't stop at the screen edge
            if (mouse.rightButton.wasPressedThisFrame) {
                LockCursor(true);
            } else if (mouse.rightButton.wasReleasedThisFrame) {
                LockCursor(false);
            }

            if (!mouse.rightButton.isPressed) {
                return;
            }

            // The delta is already this frame's movement, so no deltaTime
            Vector2 delta = mouse.delta.ReadValue();
            this.yaw += delta.x * this.lookSensitivity;
            this.pitch = Mathf.Clamp(this.pitch - delta.y * this.lookSensitivity, -this.maxPitch, this.maxPitch);

            this.transform.rotation = Quaternion.Euler(this.pitch, this.yaw, 0f);
        }

        private void UpdateMove() {
            Vector3 direction = ReadMoveDirection();
            if (direction.sqrMagnitude <= 0f) {
                return;
            }

            this.transform.position += direction * (this.moveSpeed * Time.deltaTime);
        }

        // Direction from WASD, relative to where the camera faces, pitch included. Zero without input
        private Vector3 ReadMoveDirection() {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) {
                return Vector3.zero;
            }

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) {
                // Nothing pressed
                return Vector3.zero;
            }

            // Normalized so diagonals aren't faster
            return (this.transform.forward * z + this.transform.right * x).normalized;
        }
    }
}
