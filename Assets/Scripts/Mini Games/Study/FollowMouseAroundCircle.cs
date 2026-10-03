/*
 * FollowMouseAroundCircle - allows player to control the targeter's rotation according to their mouse's position  
 */

using UnityEngine;
using UnityEngine.InputSystem;

public class FollowMouseAroundCircle : MonoBehaviour
{
    public Transform mainCircle;           // Center Circle to rotate around
    public float distanceFromCenter = 0f;  // Radius of the circle, set to 0 because center circle and targeter are aligned

    [Header("Input")]
    [Tooltip("PlayerInput driving the MiniGame.inputactions asset. If left unassigned, resolved automatically at runtime.")]
    [SerializeField] private PlayerInput playerInput;
    private InputAction _pointerPosition;

    private void Awake()
    {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInput>();

        if (playerInput == null)
        {
            Debug.LogError("FollowMouseAroundCircle: No PlayerInput found in scene. Pointer input will not work.");
            return;
        }

        _pointerPosition = playerInput.actions["PointerPosition"];
    }

    private void OnEnable()
    {
        if (playerInput == null) return;

        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Play");
        playerInput.actions.Enable();
    }

    void Update()
    {
        if (_pointerPosition == null) return;

        // Get the mouse position in world space
        Vector2 screenPos = _pointerPosition.ReadValue<Vector2>();
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        mousePosition.z = 0f;  // Set z to 0 for 2D

        // Calculate the direction from the main circle to the mouse
        Vector3 direction = (mousePosition - mainCircle.position).normalized;

        // Position the targeter at the correct distance along the calculated direction
        transform.position = mainCircle.position + direction * distanceFromCenter;

        // Rotate the targeter to face outward from the center
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle - 90); 
    }
}
