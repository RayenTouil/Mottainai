using UnityEngine;

namespace Mottainai.Client
{
    /// v3 visual-only motion for floating waste/obstacles: bob, yaw drift, roll/tilt/spin, ring pulse. Root transform is untouched.
    public class WasteFloat : MonoBehaviour
    {
        public Transform vis, ring;
        public float bobAmp = 0.08f, bobHz = 0.8f, pulseHz = 1.2f, yawDrift = 10f, spin, roll, tilt;
        float phase, yaw;
        Vector3 ringScale;

        void OnEnable()
        {
            phase = Random.value * 10f;
            yaw = Random.value * 360f;

        }

        void Update()
        {
            float t = Time.time + phase;
            float bob = Mathf.Sin(t * bobHz * 6.2832f) * bobAmp;
            yaw += (yawDrift + spin) * Time.deltaTime;
            if (vis != null)
            {
                vis.localPosition = new Vector3(0f, bob, 0f);
                vis.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * roll, yaw, Mathf.Sin(t * 0.9f) * tilt);
            }
            if (ring != null && ringScale == Vector3.zero) ringScale = ring.localScale;
            if (ring != null)
            {
                ring.localPosition = new Vector3(0f, 0.14f + bob, 0f); // above the 0.12 m wave crests
                float p = 1f + 0.04f + 0.04f * Mathf.Sin(t * pulseHz * 6.2832f);
                ring.localScale = ringScale * p;
            }
        }
    }
}
