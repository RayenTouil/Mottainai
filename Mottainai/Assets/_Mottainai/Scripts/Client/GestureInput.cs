using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Mottainai.Client
{
    /// Swipe / tap recognition with EnhancedTouch (mouse simulated as touch on desktop) + keyboard dev shortcuts.
    public class GestureInput : MonoBehaviour
    {
        public event Action<float, bool> OnStroke;           // power, back
        public event Action<Vector2> OnTap;                  // screen pos
        public event Action<Vector2> OnSwipe;                // screen direction (normalized)
        public event Action OnToggleHud;
        public event Action<Role> OnDevStroke;               // keyboard: specific side (editor testing)
        public event Action OnDevThrow;

        public Role role;
        public bool active;
        float cooldownUntil, hudToggleAt;

        struct Finger { public Vector2 start, anchor; public float startTime, anchorTime; public bool moved, consumed; }
        readonly Finger[] fingers = new Finger[16];

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Enable();
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Disable();
#endif
            EnhancedTouchSupport.Disable();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            var touches = Touch.activeTouches;

            int began = 0;
            for (int i = 0; i < touches.Count; i++) if (touches[i].phase == TouchPhase.Began) began++;
            if (touches.Count >= 3 && began > 0 && now > hudToggleAt) { hudToggleAt = now + 0.6f; OnToggleHud?.Invoke(); return; }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame) OnToggleHud?.Invoke();
                if (active)
                {
                    if (kb.qKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) OnDevStroke?.Invoke(Role.LeftPaddler);
                    if (kb.eKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) OnDevStroke?.Invoke(Role.RightPaddler);
                    if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) Stroke(0.85f, false, now);
                    if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) Stroke(0.85f, true, now);
                    if (kb.spaceKey.wasPressedThisFrame) OnDevThrow?.Invoke();
                }
            }
            if (!active || touches.Count >= 3) return;

            float h = Mathf.Max(1, Screen.height);
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                int id = t.finger.index;
                if (id < 0 || id >= fingers.Length) continue;
                ref var f = ref fingers[id];
                var pos = t.screenPosition;
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        f = new Finger { start = pos, anchor = pos, startTime = now, anchorTime = now };
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if ((pos - f.start).magnitude > h * 0.03f) f.moved = true;
                        if (role == Role.LeftPaddler || role == Role.RightPaddler)
                        {
                            var d = pos - f.anchor;
                            if (Mathf.Abs(d.y) > h * 0.07f && Mathf.Abs(d.y) > Mathf.Abs(d.x) * 0.8f)
                            {
                                float speed = Mathf.Abs(d.y) / h / Mathf.Max(0.016f, now - f.anchorTime);
                                float power = Mathf.Lerp(GameTuning.Current.minStrokePower, GameTuning.Current.maxStrokePower, Mathf.InverseLerp(0.6f, 4f, speed));
                                if (Stroke(power, d.y > 0f, now)) { f.anchor = pos; f.anchorTime = now; f.consumed = true; }
                            }
                        }
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (role == Role.Collector && t.phase == TouchPhase.Ended)
                        {
                            var d = pos - f.start;
                            if (!f.moved && now - f.startTime < 0.45f) OnTap?.Invoke(pos);
                            else if (d.magnitude > h * 0.06f) OnSwipe?.Invoke(d.normalized);
                        }
                        break;
                }
            }
        }

        bool Stroke(float power, bool back, float now)
        {
            if (role != Role.LeftPaddler && role != Role.RightPaddler) return false;
            if (now < cooldownUntil) return false;
            cooldownUntil = now + GameTuning.Current.strokeCooldown;
            OnStroke?.Invoke(power, back);
            return true;
        }
    }
}
