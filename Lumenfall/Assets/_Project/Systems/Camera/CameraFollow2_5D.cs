using UnityEngine;

/// <summary>
/// Makes the attached camera follow a target from a fixed 2.5D viewing angle.
/// The camera moves smoothly while continuously looking at the target.
/// During a conversation it eases in to frame a requested focus point instead.
/// </summary>
public class CameraFollow2_5D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The Transform that the camera follows, usually the Player root object.")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [Tooltip("The camera's world-space position relative to the target.")]
    [SerializeField] private Vector3 offset =
        new Vector3(8f, 10f, -8f);

    [Tooltip("Offsets the camera's focus point from the target's origin.")]
    [SerializeField] private Vector3 focusOffset =
        new Vector3(0f, 1.5f, 0f);

    [Header("Smoothing")]
    [Tooltip("The approximate time required for the camera to reach its target position.")]
    [Min(0f)]
    [SerializeField] private float smoothTime = 0.15f;

    [Tooltip("The approximate time taken to ease into and out of conversation framing.")]
    [Min(0f)]
    [SerializeField] private float conversationBlendTime = 0.45f;

    // Conversation framing requested by the dialogue box; shared because there is one gameplay camera.
    private static bool conversationActive;
    private static Vector3 conversationFocus;
    private static float conversationZoom = 1f;

    // A cutscene shake, added on top of the follow position each frame without disturbing its smoothing.
    private static Vector3 shakeOffset;
    private Vector3 appliedShake;

    // Stores the velocity used internally by SmoothDamp.
    private Vector3 currentVelocity;

    // How far the camera has eased into conversation framing, from 0 to 1.
    private float conversationBlend;
    private float conversationBlendVelocity;

    /// <summary>
    /// Frames a conversation: the camera centres on <paramref name="focus"/> and moves closer by
    /// <paramref name="zoom"/> (a fraction of the normal offset). Call every frame while it should hold.
    /// </summary>
    public static void FrameConversation(Vector3 focus, float zoom)
    {
        conversationActive = true;
        conversationFocus = focus;
        conversationZoom = Mathf.Clamp(zoom, 0.3f, 1f);
    }

    /// <summary>Returns the camera to following its target.</summary>
    public static void ClearConversationFrame()
    {
        conversationActive = false;
    }

    /// <summary>Offsets the camera by <paramref name="offset"/> until it is set back to zero; used for cutscene shakes.</summary>
    public static void SetShake(Vector3 offset)
    {
        shakeOffset = offset;
    }

    /// <summary>
    /// Reports where the camera would rest when fully caught up with its target. Overlays such as the
    /// startup menu use this to move the camera themselves and hand it back without a jump.
    /// </summary>
    public bool TryGetFollowPose(out Vector3 position, out Vector3 focusPoint)
    {
        position = transform.position;
        focusPoint = transform.position + transform.forward;
        if (target == null)
        {
            return false;
        }

        position = target.position + offset;
        focusPoint = target.position + focusOffset;
        return true;
    }

    /// <summary>Clears smoothing velocity so following resumes cleanly after another script moved the camera.</summary>
    public void ResetSmoothing()
    {
        currentVelocity = Vector3.zero;
    }

    /// <summary>
    /// Updates the camera after the target has completed its movement for the
    /// current frame. This helps reduce visible camera jitter.
    /// </summary>
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // Remove last frame's shake first, so smoothing works from the unshaken position.
        transform.position -= appliedShake;

        conversationBlend = Mathf.SmoothDamp(
            conversationBlend,
            conversationActive ? 1f : 0f,
            ref conversationBlendVelocity,
            conversationBlendTime
        );

        // Blend the followed point and distance toward the conversation framing.
        Vector3 anchor = Vector3.Lerp(target.position, conversationFocus, conversationBlend);
        float distanceScale = Mathf.Lerp(1f, conversationZoom, conversationBlend);

        // Calculate the camera's desired position relative to the target.
        Vector3 desiredPosition =
            anchor + offset * distanceScale;

        // Smoothly move the camera toward its desired position.
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            smoothTime
        );

        // Look slightly above the followed point, a little lower when framing faces up close.
        Vector3 focusPoint =
            anchor + focusOffset * Mathf.Lerp(1f, 0.8f, conversationBlend);

        transform.LookAt(focusPoint);

        appliedShake = shakeOffset;
        transform.position += appliedShake;
    }
}
