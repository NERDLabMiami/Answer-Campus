using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class InputBasedUISelector : MonoBehaviour
{
    public GameObject firstSelected;

    private bool lastInputWasMouse = true;

    void Update()
    {
        bool mouseMoved = Mouse.current != null && Mouse.current.delta.ReadValue() != Vector2.zero;

        if (mouseMoved)
        {
            if (!lastInputWasMouse)
                SwitchToMouseMode();
        }
        else if (LegacyInputCompat.GetButtonDown("Submit") || HorizontalOrVerticalPressed())
        {
            if (lastInputWasMouse)
                SwitchToGamepadMode();
        }
    }

    // Mirrors the legacy "Horizontal"/"Vertical" axes: keyboard arrows + WASD, or gamepad left stick.
    private bool HorizontalOrVerticalPressed()
    {
        if (Keyboard.current != null &&
            (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed ||
             Keyboard.current.upArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed ||
             Keyboard.current.aKey.isPressed || Keyboard.current.dKey.isPressed ||
             Keyboard.current.wKey.isPressed || Keyboard.current.sKey.isPressed))
            return true;

        if (Gamepad.current != null && Gamepad.current.leftStick.ReadValue() != Vector2.zero)
            return true;

        return false;
    }

    void SwitchToMouseMode()
    {
        lastInputWasMouse = true;
        EventSystem.current.SetSelectedGameObject(null);
    }

    void SwitchToGamepadMode()
    {
        lastInputWasMouse = false;
        EventSystem.current.SetSelectedGameObject(firstSelected);
    }
}