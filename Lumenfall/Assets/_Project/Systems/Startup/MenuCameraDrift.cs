using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Slowly pans the gameplay camera around the player's surroundings while the startup menu is open, then eases it
    /// back to the follow camera's resting pose. The menu calls <see cref="Apply"/> from LateUpdate after
    /// <see cref="CameraFollow2_5D"/> has run, so this pose wins for the frame. Uses unscaled time because the menu pauses
    /// the world with <c>Time.timeScale = 0</c>.
    /// </summary>
    internal sealed class MenuCameraDrift
    {
        private const float EnterSeconds = 2.5f;

        private Camera camera;
        private CameraFollow2_5D follow;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float startedAt;
        private bool active;

        public void Begin(float now)
        {
            startedAt = now;
            active = Capture();
        }

        /// <summary>Positions the camera. <paramref name="returnBlend"/> goes from 0 (drifting) to 1 (back at rest).</summary>
        public void Apply(float now, Vector2 parallax, float returnBlend)
        {
            if (!active)
            {
                // The camera can appear after the menu does, so keep looking until one exists.
                active = Capture();
                if (!active) return;
                startedAt = now;
            }
            if (camera == null)
            {
                active = false;
                return;
            }

            GetRestPose(out Vector3 restPosition, out Vector3 focus);
            Vector3 arm = restPosition - focus;

            // A wide establishing view that sways a few degrees and wanders a few tiles around the focus.
            float yaw = Mathf.Sin(now * 0.07f) * 6f + parallax.x * 2.5f;
            float distance = 1.18f + 0.04f * Mathf.Sin(now * 0.11f);
            arm = Quaternion.Euler(0f, yaw, 0f) * arm * distance;
            arm.y += parallax.y * 0.35f;
            Vector3 pan = new Vector3(Mathf.Sin(now * 0.05f) * 3.2f, 0f, Mathf.Sin(now * 0.037f + 1.3f) * 2.4f);
            Vector3 driftFocus = focus + pan;
            Vector3 driftPosition = driftFocus + arm;
            Quaternion driftRotation = Quaternion.LookRotation(driftFocus - driftPosition);

            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - startedAt) / EnterSeconds));
            Vector3 position = Vector3.Lerp(startPosition, driftPosition, enter);
            Quaternion rotation = Quaternion.Slerp(startRotation, driftRotation, enter);
            if (returnBlend > 0f)
            {
                Quaternion restRotation = Quaternion.LookRotation(focus - restPosition);
                position = Vector3.Lerp(position, restPosition, returnBlend);
                rotation = Quaternion.Slerp(rotation, restRotation, returnBlend);
            }
            camera.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Snaps to the resting pose and clears follow smoothing so gameplay starts without a camera jump.</summary>
        public void Release()
        {
            if (!active || camera == null) return;
            GetRestPose(out Vector3 restPosition, out Vector3 focus);
            camera.transform.SetPositionAndRotation(restPosition, Quaternion.LookRotation(focus - restPosition));
            if (follow != null) follow.ResetSmoothing();
            active = false;
        }

        private bool Capture()
        {
            camera = Camera.main;
            if (camera == null) return false;
            follow = camera.GetComponent<CameraFollow2_5D>();
            startPosition = camera.transform.position;
            startRotation = camera.transform.rotation;
            return true;
        }

        private void GetRestPose(out Vector3 position, out Vector3 focus)
        {
            if (follow != null && follow.TryGetFollowPose(out position, out focus)) return;
            position = startPosition;
            focus = startPosition + startRotation * Vector3.forward * 12f;
        }
    }
}
