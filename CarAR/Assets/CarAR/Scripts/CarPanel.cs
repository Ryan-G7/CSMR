using UnityEngine;
namespace CarAR
{
    public class CarPanel : MonoBehaviour
    {
        public Vector3 localAxis = Vector3.up;
        public float openAngle = 65f;
        public float duration = 0.65f;
        Quaternion closedRotation;
        float progress, target;
        public bool IsOpen => target > 0.5f;
        public bool IsClosed => progress < 0.001f && target == 0;
        void Awake() { closedRotation = transform.localRotation; }
        public void Toggle() { if (!GetComponentInParent<ExplodeController>().IsBusy) target = 1f - target; }
        public void Close() { target = 0f; }
        public void ResetImmediate() { target = progress = 0; transform.localRotation = closedRotation; }
        void Update()
        {
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(.01f, duration));
            transform.localRotation = closedRotation * Quaternion.AngleAxis(Mathf.SmoothStep(0f, 1f, progress) * openAngle, localAxis);
        }
    }
}
