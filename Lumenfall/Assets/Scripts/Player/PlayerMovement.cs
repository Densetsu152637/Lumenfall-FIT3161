using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

    [Header("Camera Reference")]
    [Tooltip("The camera used to convert input into screen-relative movement.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private CharacterController characterController;
    private Vector2 moveInput;
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    // Called by the Move action through the Player Input component.
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    private void Update()
    {
        MovePlayer();
        UpdateAnimation();
    }

    private void MovePlayer()
    {
        Vector3 moveDirection = GetCameraRelativeDirection();

        // Prevent diagonal movement from being faster than straight movement.
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        // Rotate the player toward the current movement direction.
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(moveDirection);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        // Apply a small downward force while grounded and simulate gravity.
        if (characterController.isGrounded &&
            verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            moveDirection * moveSpeed +
            Vector3.up * verticalVelocity;

        characterController.Move(
            velocity * Time.deltaTime
        );
    }

    /// <summary>
    /// Converts two-dimensional input into a direction aligned with the
    /// camera view. Pressing W therefore moves toward the top of the screen.
    /// </summary>
    private Vector3 GetCameraRelativeDirection()
    {
        if (cameraTransform == null)
        {
            return new Vector3(moveInput.x, 0f, moveInput.y);
        }

        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        return cameraRight * moveInput.x +
               cameraForward * moveInput.y;
    }

    private void UpdateAnimation()
    {
        if (animator == null)
        {
            return;
        }
        
        // Set falling state
        bool isGrounded = characterController.isGrounded;
        bool isFalling =
            !isGrounded && verticalVelocity < -0.1f;

        animator.SetFloat(
            "Speed",
            moveInput.magnitude,
            0.1f,
            Time.deltaTime
        );

        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsFalling", isFalling);
    }
}
