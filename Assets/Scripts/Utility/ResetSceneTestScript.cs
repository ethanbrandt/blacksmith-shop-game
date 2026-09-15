using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

//! This script exists to help with testing and should be removed before release
public class ResetSceneTestScript : MonoBehaviour
{
    [SerializeField] GameObject resetObject;
    [SerializeField] Slider resetSlider;
    [SerializeField] float resetTime = 3.0f;
    
    private InputAction resetAction;
    
    void Start()
    {
        resetAction = new InputAction(
            name: "ResetScene",
            type: InputActionType.Button,
            binding: "<Gamepad>/start",
            interactions: $"hold(duration={resetTime})"
        );

        resetAction.performed += ResetScene;
        resetAction.Enable();
    }

    private void Update()
    {
        if (resetAction.GetTimeoutCompletionPercentage() > 0.01f)
        {
            if (!resetObject.activeInHierarchy)
            {
                print("RESETTING");
                resetObject.SetActive(true);
            }
            
            resetSlider.value = resetAction.GetTimeoutCompletionPercentage();
        }
        else if (resetObject.activeInHierarchy)
        {
            print("STOPPING RESET");
            resetObject.SetActive(false);
        }
    }

    public void ResetScene(InputAction.CallbackContext _context)
    {
        SceneManager.LoadScene(0, LoadSceneMode.Single);
    }
}
