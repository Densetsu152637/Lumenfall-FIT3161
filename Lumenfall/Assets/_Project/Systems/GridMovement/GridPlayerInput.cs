using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Accessibility;
using Lumenfall.Controls;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Converts a Vector2 input action into discrete eight-direction GridMover commands.
    /// One command always finishes its whole tile before another can begin.
    /// </summary>
    [RequireComponent(typeof(GridMover))]
    [DisallowMultipleComponent]
    public sealed class GridPlayerInput : MonoBehaviour
    {
        [Header("Input Actions")]
        [Tooltip("Usually Player/Move from Assets/_Project/Input/InputSystem_Actions.inputactions.")]
        [SerializeField] private InputActionReference moveAction;
        [Tooltip("Usually Player/Sprint from Assets/_Project/Input/InputSystem_Actions.inputactions.")]
        [SerializeField] private InputActionReference sprintAction;
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string sprintActionName = "Sprint";

        [Header("Tile Repeat")]
        [SerializeField, Range(0.1f, 0.95f)] private float directionThreshold = 0.5f;
        [SerializeField] private bool repeatWhileHeld = true;
        [Tooltip("Delay before retrying a held direction when movement is blocked.")]
        [SerializeField, Min(0.01f)] private float repeatInterval = 0.12f;

        private GridMover mover;
        private InputAction activeMoveAction;
        private InputAction activeSprintAction;
        private InputAction fallbackMoveAction;
        private InputAction fallbackSprintAction;
        private bool enabledMoveActionHere;
        private bool enabledSprintActionHere;
        private Vector2Int heldDirection;
        private float nextRepeatTime;
        private readonly SprintPreferenceState sprintPreference = new SprintPreferenceState();
        private bool sprinting;
        // A finished slide needs a fresh press, so a held key cannot launch an unintended second slide.
        private bool waitForRelease;

        private void Reset()
        {
            mover = GetComponent<GridMover>();
            playerInput = GetComponent<PlayerInput>();
        }

        private void Awake()
        {
            mover = GetComponent<GridMover>();
        }

        private void OnEnable()
        {
            mover.MoveCompleted += HandleMoveCompleted;
            AccessibilitySettings.Changed += OnPreferencesChanged;
            mover.SlideEnded += HandleSlideEnded;
            ResolveActions();
            if (activeMoveAction == null)
            {
                Debug.LogError($"{name} could not create or find a movement input action.", this);
                enabled = false;
                return;
            }

            if (!activeMoveAction.enabled)
            {
                activeMoveAction.Enable();
                enabledMoveActionHere = true;
            }

            if (activeSprintAction != null && !activeSprintAction.enabled)
            {
                activeSprintAction.Enable();
                enabledSprintActionHere = true;
            }
        }

        private void OnDisable()
        {
            mover.MoveCompleted -= HandleMoveCompleted;
            AccessibilitySettings.Changed -= OnPreferencesChanged;
            sprintPreference.Reset();
            sprinting = false;
            mover.SlideEnded -= HandleSlideEnded;
            waitForRelease = false;

            if (enabledMoveActionHere && activeMoveAction != null)
            {
                activeMoveAction.Disable();
            }

            if (enabledSprintActionHere && activeSprintAction != null)
            {
                activeSprintAction.Disable();
            }

            mover?.SetMovementMode(GridMovementMode.Walk);
            enabledMoveActionHere = false;
            enabledSprintActionHere = false;
            heldDirection = Vector2Int.zero;

            if (fallbackMoveAction != null)
            {
                fallbackMoveAction.Dispose();
                fallbackMoveAction = null;
            }

            if (fallbackSprintAction != null)
            {
                fallbackSprintAction.Dispose();
                fallbackSprintAction = null;
            }

            activeMoveAction = null;
            activeSprintAction = null;
        }

        private void Update()
        {
            if (AccessibilitySettingsOverlay.InputBlocked || activeMoveAction == null || Lumenfall.GameEvents.OverlayInputGate.IsBlocked)
            {
                heldDirection = Vector2Int.zero;
                return;
            }

            // The accessibility preference turns sprint into hold or toggle; the raw input ignores rebound keyboard keys.
            sprinting = sprintPreference.Resolve(AccessibilitySettings.Current.HoldToSprint, ReadSprint(), ReadSprintPressed(),
                mover.IsMovementLocked || Time.timeScale <= 0f);
            mover.SetMovementMode(sprinting ? GridMovementMode.Sprint : GridMovementMode.Walk);

            Vector2Int direction = QuantiseDirection(ReadMove());
            if (direction == Vector2Int.zero)
            {
                heldDirection = Vector2Int.zero;
                waitForRelease = false;
                return;
            }

            if (direction != heldDirection)
            {
                heldDirection = direction;
                waitForRelease = false;
                TryMoveHeldDirection();
                return;
            }

            if (waitForRelease || !repeatWhileHeld || Time.unscaledTime < nextRepeatTime || mover.IsMoving ||
                mover.IsMovementLocked)
            {
                return;
            }

            TryMoveHeldDirection();
        }

        private void HandleSlideEnded(GridMover movingActor, Vector2Int cell)
        {
            waitForRelease = true;
        }

        private void HandleMoveCompleted(GridMover movingActor, Vector2Int destination)
        {
            if (waitForRelease || AccessibilitySettingsOverlay.InputBlocked || !repeatWhileHeld || activeMoveAction == null || mover.IsMovementLocked || Lumenfall.GameEvents.OverlayInputGate.IsBlocked)
            {
                return;
            }

            // Start the next tile in the completion frame so held movement has no
            // stop/start frame for the follow camera to expose as visible jitter.
            Vector2Int direction = QuantiseDirection(ReadMove());
            if (direction == Vector2Int.zero)
            {
                heldDirection = Vector2Int.zero;
                return;
            }

            heldDirection = direction;
            mover.SetMovementMode(sprinting ? GridMovementMode.Sprint : GridMovementMode.Walk);
            TryMoveHeldDirection();
        }

        private void OnPreferencesChanged(AccessibilitySnapshot _) { sprintPreference.Reset(); sprinting = false; }

        private void TryMoveHeldDirection()
        {
            if (!mover.TryMove(heldDirection))
            {
                nextRepeatTime = Time.unscaledTime + repeatInterval;
            }
        }

        // Keyboard movement comes only from the player's key bindings, so a rebound key stops moving the player;
        // the input actions still supply gamepad input.
        private Vector2 ReadMove()
        {
            Vector2 keys = KeyBindings.MoveVector();
            if (keys != Vector2.zero) return keys;
            return IsKeyboardDriven(activeMoveAction) ? Vector2.zero : activeMoveAction.ReadValue<Vector2>();
        }

        private bool ReadSprint()
        {
            if (KeyBindings.IsPressed(GameAction.Sprint)) return true;
            return activeSprintAction != null && !IsKeyboardDriven(activeSprintAction) && activeSprintAction.IsPressed();
        }

        private bool ReadSprintPressed()
        {
            if (KeyBindings.WasPressed(GameAction.Sprint)) return true;
            return activeSprintAction != null && !IsKeyboardDriven(activeSprintAction) && activeSprintAction.WasPressedThisFrame();
        }

        private static bool IsKeyboardDriven(InputAction action)
        {
            InputControl control = action.activeControl;
            return control != null && control.device is Keyboard;
        }

        private Vector2Int QuantiseDirection(Vector2 value)
        {
            int x = Mathf.Abs(value.x) >= directionThreshold ? (int)Mathf.Sign(value.x) : 0;
            int y = Mathf.Abs(value.y) >= directionThreshold ? (int)Mathf.Sign(value.y) : 0;
            return new Vector2Int(x, y);
        }

        private void ResolveActions()
        {
            activeMoveAction = moveAction != null ? moveAction.action : null;
            activeSprintAction = sprintAction != null ? sprintAction.action : null;

            if (playerInput == null && (activeMoveAction == null || activeSprintAction == null))
            {
                playerInput = GetComponent<PlayerInput>();
            }

            if (activeMoveAction == null)
            {
                activeMoveAction = playerInput != null
                    ? playerInput.actions?.FindAction(moveActionName, false)
                    : null;
            }

            if (activeSprintAction == null)
            {
                activeSprintAction = playerInput != null
                    ? playerInput.actions?.FindAction(sprintActionName, false)
                    : null;
            }

            if (activeMoveAction == null)
            {
                // Keeps a newly added component usable before an InputAction reference is assigned.
                fallbackMoveAction = new InputAction(
                    "Grid Move",
                    InputActionType.Value,
                    expectedControlType: "Vector2");
                fallbackMoveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                fallbackMoveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/rightArrow");
                fallbackMoveAction.AddBinding("<Gamepad>/leftStick");
                fallbackMoveAction.AddBinding("<Gamepad>/dpad");
                activeMoveAction = fallbackMoveAction;
            }

            if (activeSprintAction == null)
            {
                fallbackSprintAction = new InputAction(
                    "Grid Sprint",
                    InputActionType.Button,
                    expectedControlType: "Button");
                fallbackSprintAction.AddBinding("<Keyboard>/leftShift");
                fallbackSprintAction.AddBinding("<Gamepad>/leftStickPress");
                activeSprintAction = fallbackSprintAction;
            }
        }

        private void OnValidate()
        {
            directionThreshold = Mathf.Clamp(directionThreshold, 0.1f, 0.95f);
            repeatInterval = Mathf.Max(0.01f, repeatInterval);
        }
    }
}
