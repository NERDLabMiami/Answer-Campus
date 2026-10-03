using UnityEngine.InputSystem;

// Reproduces the handful of legacy UnityEngine.Input string-named button checks
// (Input.GetButtonDown/GetButton by Input Manager axis name, Input.anyKeyDown) that this
// project relied on, using the new Input System's direct device reads. Only the legacy
// button names actually configured in this project (ProjectSettings/InputManager.asset)
// are supported: "Cancel" (Escape) and "Submit" (Enter/Space/gamepad South).
public static class LegacyInputCompat
{
    public static bool GetButtonDown(string legacyButtonName)
    {
        switch (legacyButtonName)
        {
            case "Cancel":
                return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            case "Submit":
                return (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
                    || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
            default:
                return false;
        }
    }

    public static bool GetButton(string legacyButtonName)
    {
        switch (legacyButtonName)
        {
            case "Cancel":
                return Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            case "Submit":
                return (Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.spaceKey.isPressed))
                    || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);
            default:
                return false;
        }
    }

    // Mirrors legacy Input.anyKeyDown: any keyboard key or mouse button pressed this frame.
    public static bool AnyKeyDown()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        if (Mouse.current != null &&
            (Mouse.current.leftButton.wasPressedThisFrame ||
             Mouse.current.rightButton.wasPressedThisFrame ||
             Mouse.current.middleButton.wasPressedThisFrame))
            return true;

        return false;
    }
}
