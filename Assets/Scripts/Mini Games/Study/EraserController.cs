using UnityEngine;
using UnityEngine.InputSystem;

public class EraserController : MonoBehaviour {
    [Header("Box References")]
    [Tooltip("Assign the same box Transforms that letters aim for.")]
    public Transform[] boxPositions;  // We'll take their X, keep y = -1.55

    [Header("Movement Settings")]
    [Tooltip("How fast the eraser moves between positions.")]
    public float moveSpeed = 5f;

    [Tooltip("The Y value where the eraser should remain.")]
    public float eraserZ = 1f;

    [Header("Input")]
    [Tooltip("PlayerInput driving the MiniGame.inputactions asset. If left unassigned, resolved automatically at runtime.")]
    [SerializeField] private PlayerInput playerInput;
    private InputAction _left, _right;

    private Vector3[] eraserPositions;  // Will be generated from boxPositions
    private int targetIndex = 0;        // Which position we're moving toward

    /// <summary>Which column (box index) the eraser is currently set to intercept.</summary>
    public int CurrentColumnIndex => targetIndex;

    private void Awake() {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInput>();

        if (playerInput == null) {
            Debug.LogError("EraserController: No PlayerInput found in scene. Left/right input will not work.");
            return;
        }

        _left = playerInput.actions["PressLeft"];
        _right = playerInput.actions["PressRight"];
    }

    private void OnEnable() {
        if (playerInput == null) return;

        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Play");
        playerInput.actions.Enable();

        _left.performed += OnPressLeft;
        _right.performed += OnPressRight;
    }

    private void OnDisable() {
        if (_left != null) _left.performed -= OnPressLeft;
        if (_right != null) _right.performed -= OnPressRight;
    }

    private void OnPressLeft(InputAction.CallbackContext context) {
        if (eraserPositions != null && targetIndex > 0) targetIndex--;
    }

    private void OnPressRight(InputAction.CallbackContext context) {
        if (eraserPositions != null && targetIndex < eraserPositions.Length - 1) targetIndex++;
    }

    private void Start() {
        // Auto-generate eraserPositions from boxPositions
        if (boxPositions == null || boxPositions.Length == 0) {
            Debug.LogError("EraserController: No box positions assigned.");
            return;
        }

        eraserPositions = new Vector3[boxPositions.Length];

        // Use each box's X-value, but fix the Y at eraserY and Z at eraserZ
        for (int i = 0; i < boxPositions.Length; i++) {
            float boxX = boxPositions[i].position.x;
            eraserPositions[i] = new Vector3(boxX, transform.position.y, eraserZ);
        }

        // Initialize eraser at the first position
        transform.position = eraserPositions[targetIndex];
    }

    private void Update() {
        if (eraserPositions == null || eraserPositions.Length == 0) return;

        MoveToTarget();
    }

    /// <summary>
    /// Smoothly moves in a straight line from current position to eraserPositions[targetIndex].
    /// </summary>
    private void MoveToTarget() {
        Vector3 currentPos = transform.position;
        Vector3 goalPos = eraserPositions[targetIndex];

        // Move the eraser in a straight line at approximately 'moveSpeed' units/second
        transform.position = Vector3.MoveTowards(
            currentPos,
            goalPos,
            moveSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// Called when another object (e.g. a Letter) enters this collider.
    /// If it's tagged "Letter," we erase it.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Letter")) {
            LetterMovement letter = other.GetComponent<LetterMovement>();
            if (letter != null) letter.Erase();
            else Destroy(other.gameObject);
            // Optional: add SFX or other feedback here
        }
    }
}
