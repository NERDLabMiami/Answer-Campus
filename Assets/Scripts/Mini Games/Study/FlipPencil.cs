using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class FlipPencil : MonoBehaviour {
    [SerializeField] private GameObject eraserCollider; // Reference to the eraser collider
    [SerializeField] private GameObject tipCollider;    // Reference to the tip collider

    private bool isEraserMode = true;                   // Tracks the current mode
    private float erasingRotation = 45f;                // Rotation angle for eraser mode
    private float writingRotation = 226f;               // Rotation angle for writing mode

    [SerializeField] private float flipDuration = 0.05f; // Duration for the flip animation (faster)
    [SerializeField] private float minFlipDuration = 0.02f; // Minimum flip duration to prevent instant flips

    [Header("Input")]
    [Tooltip("PlayerInput driving the MiniGame.inputactions asset. If left unassigned, resolved automatically at runtime.")]
    [SerializeField] private PlayerInput playerInput;
    private InputAction _flip;

    private void Awake() {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInput>();

        if (playerInput == null) {
            Debug.LogError("FlipPencil: No PlayerInput found in scene. Flip input will not work.");
            return;
        }

        _flip = playerInput.actions["Flip"];
    }

    private void OnEnable() {
        if (playerInput == null) return;

        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Play");
        playerInput.actions.Enable();

        _flip.performed += OnFlip;
    }

    private void OnDisable() {
        if (_flip != null) _flip.performed -= OnFlip;
    }

    private void OnFlip(InputAction.CallbackContext context) => Flip();

    private void Start() {
        SetRotation(isEraserMode ? erasingRotation : writingRotation);
        UpdateColliders();
    }

    /// <summary>
    /// Toggles the pencil's mode and starts the flipping coroutine.
    /// </summary>
    private void Flip() {
        isEraserMode = !isEraserMode; // Toggle current mode

        float targetAngle = isEraserMode ? erasingRotation : writingRotation; // Determine which angle to set

        // Clamp flipDuration to a minimum value to prevent extremely fast flips
        float duration = Mathf.Max(flipDuration, minFlipDuration);

        // Start the flipping coroutine for smooth transition
        StartCoroutine(RotateOverTime(targetAngle, duration));

        UpdateColliders();
    }

    /// <summary>
    /// Sets the pencil's rotation to the specified angle instantly.
    /// </summary>
    /// <param name="angle">The Z-axis rotation angle in degrees.</param>
    private void SetRotation(float angle) {
        transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// Activates/deactivates colliders based on the current mode.
    /// </summary>
    private void UpdateColliders() {
        if (isEraserMode) {
            eraserCollider.SetActive(true);
            tipCollider.SetActive(false);
        } else {
            eraserCollider.SetActive(false);
            tipCollider.SetActive(true);
        }
    }

    /// <summary>
    /// Smoothly rotates the pencilSprite to the target angle over the specified duration.
    /// </summary>
    /// <param name="targetAngle">The target Z-axis rotation angle in degrees.</param>
    /// <param name="duration">Duration of the rotation in seconds.</param>
    /// <returns></returns>
    private IEnumerator RotateOverTime(float targetAngle, float duration) {
        Quaternion initialRotation = transform.localRotation;
        Quaternion finalRotation = Quaternion.Euler(0f, 0f, targetAngle);
        float elapsed = 0f;

        while (elapsed < duration) {
            transform.localRotation = Quaternion.Slerp(initialRotation, finalRotation, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localRotation = finalRotation;
    }
}
