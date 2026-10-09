using UnityEngine;
using UnityEngine.InputSystem;

public class EndScreenHandler : MonoBehaviour
{
    float resetTime = -1f;

    void Update()
    {
        if (ScreenTransitionController.IsTransitioning)
            return;
        if (resetTime < 0f)
            resetTime = Time.unscaledTime + 20f;
        bool resetPressed = Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;
        if (resetTime <= Time.unscaledTime || resetPressed)
        {
            if (ScreenTransitionController.LoadScene("_Scenes/StartScene"))
                resetTime = -1f;
        }
    }
}
