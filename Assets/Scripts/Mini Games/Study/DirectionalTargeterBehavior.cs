using UnityEngine;
using UnityEngine.InputSystem;

public class DirectionalTargeterBehavior : MonoBehaviour {
    // Rotation angles for each direction
    private Quaternion upRotation = Quaternion.Euler(0, 0, 0);
    private Quaternion downRotation = Quaternion.Euler(0, 0, 180);
    private Quaternion leftRotation = Quaternion.Euler(0, 0, 90);
    private Quaternion rightRotation = Quaternion.Euler(0, 0, 270);

    [SerializeField] private float rotationSpeed = 5f; // Speed of rotation
    private Quaternion targetRotation; // Target rotation

    [Header("Detach Settings")]
    [SerializeField] private float maxDist = 3.5f;       // Max distance to move forward
    [SerializeField] private float moveSpeed = 5f;      // Speed to move forward
    [SerializeField] private float returnSpeed = 5f;    // Speed to return to original position

    [Header("Input")]
    [Tooltip("PlayerInput driving the MiniGame.inputactions asset. If left unassigned, resolved automatically at runtime.")]
    [SerializeField] private PlayerInput playerInput;
    private InputAction _up, _down, _left, _right, _detach;

    private Vector3 originalPosition; // To store the original position

    private void Awake() {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInput>();

        if (playerInput == null) {
            Debug.LogError("DirectionalTargeterBehavior: No PlayerInput found in scene. Input will not work.");
            return;
        }

        _up = playerInput.actions["PressUp"];
        _down = playerInput.actions["PressDown"];
        _left = playerInput.actions["PressLeft"];
        _right = playerInput.actions["PressRight"];
        _detach = playerInput.actions["Detach"];
    }

    private void OnEnable() {
        if (playerInput == null) return;

        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Play");
        playerInput.actions.Enable();

        _up.performed += OnPressUp;
        _down.performed += OnPressDown;
        _left.performed += OnPressLeft;
        _right.performed += OnPressRight;
    }

    private void OnDisable() {
        if (_up != null) _up.performed -= OnPressUp;
        if (_down != null) _down.performed -= OnPressDown;
        if (_left != null) _left.performed -= OnPressLeft;
        if (_right != null) _right.performed -= OnPressRight;
    }

    private void OnPressUp(InputAction.CallbackContext context) => targetRotation = upRotation;
    private void OnPressDown(InputAction.CallbackContext context) => targetRotation = downRotation;
    private void OnPressLeft(InputAction.CallbackContext context) => targetRotation = leftRotation;
    private void OnPressRight(InputAction.CallbackContext context) => targetRotation = rightRotation;

    private void Start() {
        // Set the initial target rotation to the current rotation
        targetRotation = transform.rotation;
        // Store the original position
        originalPosition = transform.position;
    }

    private void Update() {
        // Smoothly interpolate towards the target rotation
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

        DetachPencil();
    }

    /// <summary>
    /// Handles the detach and return behavior while the Detach action is held/released.
    /// </summary>
    private void DetachPencil() {
        bool detachHeld = _detach != null && _detach.IsPressed();

        if (detachHeld) {
            // Calculate the forward direction based on current rotation
            Vector3 direction = transform.up; // up is forward direction in 2D
            Vector3 targetPosition = originalPosition + direction * maxDist;

            // Move towards the target position
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
        } else {
            // Return to the original position smoothly
            transform.position = Vector3.MoveTowards(transform.position, originalPosition, returnSpeed * Time.deltaTime);
        }
    }
}
