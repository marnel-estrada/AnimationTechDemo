using UnityEngine;
using UnityEngine.Animations;

namespace TechDemo {
    public static class TwoBoneIkSolver {
        /// <summary>
        /// Solves a two bone IK. The specified low tries to reach the specified effector position.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="top"></param>
        /// <param name="mid"></param>
        /// <param name="low"></param>
        /// <param name="effectorPosition"></param>
        public static void Solve(AnimationStream stream, TransformStreamHandle top, TransformStreamHandle mid,
            TransformStreamHandle low, Vector3 effectorPosition) {
            Quaternion aRotation = top.GetRotation(stream);
            Quaternion bRotation = mid.GetRotation(stream);

            Vector3 aPosition = top.GetPosition(stream);
            Vector3 bPosition = mid.GetPosition(stream);
            Vector3 cPosition = low.GetPosition(stream);

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;
            Vector3 ae = effectorPosition - aPosition;

            float abcAngle = TriangleAngle(ac.magnitude, ab, bc);
            float abeAngle = TriangleAngle(ae.magnitude, ab, bc);
            Debug.Log($"abcAngle {abcAngle} abeAngle {abeAngle}");
            float angle = (abcAngle - abeAngle) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(ab, bc).normalized;

            Quaternion targetMidRotation = Quaternion.AngleAxis(angle, axis);

            Quaternion worldQ = targetMidRotation * bRotation;
            mid.SetRotation(stream, worldQ);

            cPosition = low.GetPosition(stream);
            ac = cPosition - aPosition;
            Quaternion targetTopRotation = Quaternion.FromToRotation(ac, ae);
            top.SetRotation(stream, targetTopRotation * aRotation);
        }
        
        // Law of cosines
        private static float TriangleAngle(float desiredLen, Vector3 v1, Vector3 v2) {
            float v1Len = v1.magnitude;
            float v2Len = v2.magnitude;
            float value = (v1Len * v1Len + v2Len * v2Len - desiredLen * desiredLen) / (v1Len * v2Len * 2.0f);
            float c = Mathf.Clamp(value, -1f, 1f);
            return Mathf.Acos(c);
        }
    }
}