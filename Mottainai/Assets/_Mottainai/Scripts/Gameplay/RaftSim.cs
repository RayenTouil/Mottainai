using UnityEngine;

namespace Mottainai.Gameplay
{
    /// Kinematic raft: velocity relative to water + river flow field. Heading 0 = +Z, positive = turning toward +X.
    public class RaftSim
    {
        public float x, z, heading, vx, vz, yawRate;
        float lastLeft = -99f, lastRight = -99f;
        bool lastLeftBack, lastRightBack;
        public float worldVx, worldVz;

        public float ForwardSpeed => vx * Mathf.Sin(heading) + vz * Mathf.Cos(heading);
        public float LastStroke(bool left) => left ? lastLeft : lastRight;

        public bool Stroke(bool left, float power, bool back, float now, TuningData t)
        {
            float other = left ? lastRight : lastLeft;
            bool otherBack = left ? lastRightBack : lastLeftBack;
            bool sync = now - other <= t.syncWindow && otherBack == back;

            float imp = t.strokeImpulse * power * (back ? -t.backStrokeFactor : 1f) * (sync ? t.syncBoost : 1f);
            vx += Mathf.Sin(heading) * imp;
            vz += Mathf.Cos(heading) * imp;

            float kick = t.yawKickDeg * Mathf.Deg2Rad * power * (left ? 1f : -1f) * (back ? -1f : 1f);
            yawRate += kick;
            if (sync) yawRate *= 0.3f;

            if (left) { lastLeft = now; lastLeftBack = back; } else { lastRight = now; lastRightBack = back; }
            if (sync) { lastLeft = -99f; lastRight = -99f; }
            return sync;
        }

        public void Step(float dt, int seed, TuningData t)
        {
            yawRate *= Mathf.Exp(-t.yawDamping * dt);
            heading += yawRate * dt;
            float maxH = t.maxHeadingDeg * Mathf.Deg2Rad;
            if (heading > maxH) { heading = maxH; if (yawRate > 0) yawRate = 0; }
            if (heading < -maxH) { heading = -maxH; if (yawRate < 0) yawRate = 0; }

            float fx = Mathf.Sin(heading), fz = Mathf.Cos(heading);
            float rx = fz, rz = -fx;
            float fwd = vx * fx + vz * fz;
            float lat = vx * rx + vz * rz;
            fwd *= Mathf.Exp(-t.waterDrag * dt);
            lat *= Mathf.Exp(-t.lateralDrag * dt);
            vx = fwd * fx + lat * rx;
            vz = fwd * fz + lat * rz;

            Vector2 flow = FlowField.Sample(seed, x, z, t);
            float wx = vx + flow.x, wz = vz + flow.y;
            float sp = Mathf.Sqrt(wx * wx + wz * wz);
            if (sp > t.maxSpeed)
            {
                float k = t.maxSpeed / sp;
                wx *= k; wz *= k;
                vx = wx - flow.x; vz = wz - flow.y;
            }
            worldVx = wx; worldVz = wz;
            x += wx * dt;
            z += wz * dt;

            float limit = t.riverWidth * 0.5f - t.raftRadius;
            if (x > limit) { x = limit; if (vx > 0) vx = -vx * 0.3f; heading *= 0.9f; }
            if (x < -limit) { x = -limit; if (vx < 0) vx = -vx * 0.3f; heading *= 0.9f; }
        }
    }
}
