using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StartMenu : MonoBehaviour
{
	private const float HOLD_TIME_TO_START = 3.0f;
	
	private PlayerInput playerInput;
	private InputAction submit;

	private UIDocument document;
	private RadialProgress startRadialProgress;

	private float holdTimer = 0;
	
	void Awake()
	{
		playerInput = GetComponent<PlayerInput>();
		
		submit = playerInput.actions.FindAction("UI/Submit", true);

		document = GetComponent<UIDocument>();
	}

	private void OnEnable()
	{
		startRadialProgress = document.rootVisualElement.Q<RadialProgress>();
		startRadialProgress.Progress = 0f;
	}

	private void OnDisable()
	{
		startRadialProgress = null;
	}

	void Update()
	{
		if (submit.IsPressed())
		{
			holdTimer += Time.unscaledDeltaTime;

			if (holdTimer >= HOLD_TIME_TO_START)
				SceneManager.LoadScene("Scenes/ShopFrontScene", LoadSceneMode.Single);
		}
		else
			holdTimer = 0f;
		
		if (startRadialProgress != null)
			startRadialProgress.Progress = Mathf.InverseLerp(0f, HOLD_TIME_TO_START, holdTimer);
	}
}
