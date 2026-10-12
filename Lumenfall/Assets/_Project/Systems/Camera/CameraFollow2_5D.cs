using UnityEngine;

/// <summary>
/// Makes the attached camera follow a target from a fixed 2.5D viewing angle.
/// The camera moves smoothly while continuously looking at the target.
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

    // Stores the velocity used internally by SmoothDamp.
    private Vector3 currentVelocity;

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

        // Calculate the camera's desired position relative to the target.
        Vector3 desiredPosition =
            target.position + offset;

        // Smoothly move the camera toward its desired position.
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            smoothTime
        );

        // Look slightly above the target's origin.
        Vector3 focusPoint =
            target.position + focusOffset;

        transform.LookAt(focusPoint);
    }
}