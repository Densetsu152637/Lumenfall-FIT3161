using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Accessibility;
using Lumenfall.CameraEffects;
using Lumenfall.Cutscenes;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Startup;

/// <summary>
/// Makes the attached camera follow a target from a fixed 2.5D viewing angle.
/// The camera moves smoothly while continuously looking at the target.
/// During a conversation it eases in to frame a requested focus point instead (with Reduced Motion it cuts there).
/// Holding the left mouse button and dragging (or the gamepad right stick) swings the view a little around the
/// target to peek past walls and through doorways; it eases back to the fixed angle on release.
/// The options menu's Camera angle (<see cref="CameraAngleSetting"/>) turns the offset 45° to the original diagonal view.
/// </summary>
public class CameraFollow2_5D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The Transform that the camera follows, usually the Player root object.")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [Tooltip("The camera's world-space position relative to the target for the Straight camera angle. Keeping x at 0 " +
             "places the camera due south of the target, so the grid's axes line up with the screen and W/A/S/D move " +
             "up/left/down/right on screen. The Diagonal angle turns this offset 45° around the target.")]
    [SerializeField] private Vector3 offset =
        new Vector3(0f, 10f, -11.3f);

    [Tooltip("Offsets the camera's focus point from the target's origin.")]
    [SerializeField] private Vector3 focusOffset =
        new Vector3(0f, 1.5f, 0f);

    [Header("Smoothing")]
    [Tooltip("The approximate time required for the camera to reach its target position. Kept well under one walking " +
             "step (0.15 s) so the view does not trail the player and then swing after them.")]
    [Min(0f)]
    [SerializeField] private float smoothTime = 0.07f;

    [Tooltip("The approximate time taken to ease into and out of conversation framing.")]
    [Min(0f)]
    [SerializeField] private float conversationBlendTime = 0.45f;

    [Header("Peek")]
    [Tooltip("How far the view can swing left or right around the target, in degrees. Kept under 45 so W still reads as up.")]
    [Range(0f, 45f)]
    [SerializeField] private float peekYawLimit = 35f;

    [Tooltip("How far the camera can rise above (positive) or drop below (negative) its resting angle, in degrees.")]
    [SerializeField] private Vector2 peekPitchRange = new Vector2(-15f, 20f);

    [Tooltip("Degrees of swing per pixel of mouse drag.")]
    [Min(0f)]
    [SerializeField] private float peekMouseSensitivity = 0.25f;

    [Tooltip("Degrees of swing per second at full right-stick tilt.")]
    [Min(0f)]
    [SerializeField] private float peekStickSpeed = 120f;

    [Tooltip("The approximate time taken to ease back to the resting angle after letting go.")]
    [Min(0f)]
    [SerializeField] private float peekReturnTime = 0.35f;

    // Conversation framing requested by the dialogue box; shared because there is one gameplay camera.
    private static bool conversationActive;
    private static Vector3 conversationFocus;
    private static float conversationZoom = 1f;

    // A cutscene shake, added on top of the follow position each frame without disturbing its smoothing.
    private static Vector3 shakeOffset;
    private Vector3 appliedShake;

    // Stores the velocity used internally by SmoothDamp.
    private Vector3 currentVelocity;

    private bool reducedMotion;
    private void OnEnable()
    {
        AccessibilitySettings.Changed += OnPreferencesChanged;
        SetReducedMotion(AccessibilitySettings.Current.ReducedMotion);
    }
    private void OnDisable() => AccessibilitySettings.Changed -= OnPreferencesChanged;
    private void OnPreferencesChanged(AccessibilitySnapshot value) => SetReducedMotion(value.ReducedMotion);
    public void SetReducedMotion(bool value)
    {
        if (reducedMotion == value) return;
        reducedMotion = value;
        currentVelocity = Vector3.zero;
        if (target != null) transform.position = target.position + RestOffset;
    }

    // The offset for the chosen camera angle, before any peek.
    private Vector3 RestOffset => Quaternion.Euler(0f, CameraAngleSetting.Yaw, 0f) * offset;

    // The current turn of the offset in degrees, eased toward the chosen camera angle so switching it swings the view
    // round instead of cutting. NaN until the first frame, which starts at the chosen angle.
    private float angleYaw = float.NaN;
    private float angleYawVelocity;

    // How far the camera has eased into conversation framing, from 0 to 1.
    private float conversationBlend;
    private float conversationBlendVelocity;

    // The current peek swing in degrees (x yaw, y pitch) and the velocity used to ease it back.
    private Vector2 peek;
    private Vector2 peekVelocity;
    private GridMover targetMover;

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

        position = target.position + RestOffset;
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

        // Remove the previous shake before smoothing. Reduced motion cuts to and from conversation framing instead of
        // easing, so a line that points at a building still shows it.
        transform.position -= appliedShake;
        if (reducedMotion) conversationBlendVelocity = 0f;
        conversationBlend = reducedMotion ? (conversationActive ? 1f : 0f) : Mathf.SmoothDamp(
            conversationBlend,
            conversationActive ? 1f : 0f,
            ref conversationBlendVelocity,
            conversationBlendTime
        );

        // Blend the followed point and distance toward the conversation framing.
        Vector3 anchor = Vector3.Lerp(target.position, conversationFocus, conversationBlend);
        float distanceScale = Mathf.Lerp(1f, conversationZoom, conversationBlend);

        // Swing toward the chosen camera angle. Unscaled time lets the change show behind a paused settings menu.
        float restYaw = CameraAngleSetting.Yaw;
        if (float.IsNaN(angleYaw) || reducedMotion)
        {
            angleYaw = restYaw;
            angleYawVelocity = 0f;
        }
        else
        {
            angleYaw = Mathf.SmoothDampAngle(angleYaw, restYaw, ref angleYawVelocity, conversationBlendTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
        }
        Vector3 viewOffset = Quaternion.Euler(0f, angleYaw, 0f) * offset;

        UpdatePeek();

        // Calculate the camera's desired position relative to the target, swung around it by any peek.
        Vector3 desiredPosition =
            anchor + PeekRotation(viewOffset) * viewOffset * distanceScale;

        // Smoothly move the camera toward its desired position.
        transform.position = reducedMotion ? desiredPosition : Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            smoothTime
        );

        // Look slightly above the followed point, a little lower when framing faces up close.
        Vector3 focusPoint =
            anchor + focusOffset * Mathf.Lerp(1f, 0.8f, conversationBlend);

        transform.LookAt(focusPoint);

        appliedShake = reducedMotion ? Vector3.zero : shakeOffset;
        transform.position += appliedShake;
    }

    /// <summary>
    /// Swings the peek from a held left-mouse drag or the right stick, and eases it back to rest otherwise.
    /// Peeking is off whenever the player could not move: menus, dialogue, cutscenes, the spell wheel and the journal.
    /// </summary>
    private void UpdatePeek()
    {
        if (targetMover == null || targetMover.transform != target) targetMover = target.GetComponent<GridMover>();
        bool blocked = Time.timeScale <= 0f || conversationActive || StartupMenu.IsOpen || CutscenePlayer.BlocksInput ||
                       OverlayInputGate.IsBlocked || AccessibilitySettingsOverlay.InputBlocked || InventoryUiState.IsOpen ||
                       (targetMover != null && targetMover.IsMovementLocked);

        Vector2 swing = Vector2.zero;
        bool held = false;
        if (!blocked)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                held = true;
                // Dragging right looks right; dragging up raises the camera to see over walls.
                swing += mouse.delta.ReadValue() * peekMouseSensitivity;
            }

            Vector2 stick = Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
            if (stick.sqrMagnitude > 0.04f)
            {
                held = true;
                swing += stick * (peekStickSpeed * Time.unscaledDeltaTime);
            }
        }

        if (held)
        {
            peekVelocity = Vector2.zero;
            peek.x = Mathf.Clamp(peek.x + swing.x, -peekYawLimit, peekYawLimit);
            peek.y = Mathf.Clamp(peek.y + swing.y, peekPitchRange.x, peekPitchRange.y);
        }
        else if (reducedMotion)
        {
            peekVelocity = Vector2.zero;
            peek = Vector2.zero;
        }
        else
        {
            peek = Vector2.SmoothDamp(peek, Vector2.zero, ref peekVelocity, peekReturnTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }
    }

    // Yaw turns the offset around the world up axis; pitch tilts it around the horizontal axis across the view.
    private Quaternion PeekRotation(Vector3 viewOffset)
    {
        if (peek == Vector2.zero) return Quaternion.identity;
        Vector3 across = Vector3.Cross(viewOffset, Vector3.up);
        Quaternion pitch = across.sqrMagnitude > 0.0001f ? Quaternion.AngleAxis(peek.y, across.normalized) : Quaternion.identity;
        return Quaternion.Euler(0f, peek.x, 0f) * pitch;
    }
}
