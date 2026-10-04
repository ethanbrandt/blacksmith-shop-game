using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StartMenu : MonoBehaviour
{
	[SerializeField] string[] scenarioNames;
	
	private const float HOLD_TIME_TO_START = 2.0f;
	
	private PlayerInput playerInput;
	private InputAction submit;

	private UIDocument document;
	private RadialProgress startRadialProgress;
	private Label scenarioSelectLabel;

	private float holdTimer = 0;
	private float lastXInput = 0;

	private int scenarioIndex = 0;
	
	void Awake()
	{
		playerInput = GetComponent<PlayerInput>();

		document = GetComponent<UIDocument>();
	}

	private void OnEnable()
	{
		submit = playerInput.actions.FindAction("UI/Submit", true);
		playerInput.actions.FindAction("UI/Navigate").performed += OnNavigate;
		
		startRadialProgress = document.rootVisualElement.Q<RadialProgress>();
		startRadialProgress.Progress = 0f;

		scenarioSelectLabel = document.rootVisualElement.Q<Label>("ScenarioSelectLabel");
		scenarioSelectLabel.text = scenarioNames[scenarioIndex];
	}

	private void OnDisable()
	{
		startRadialProgress = null;
		scenarioSelectLabel = null;
		
		submit = null;
		playerInput.actions.FindAction("UI/Navigate").performed -= OnNavigate;
	}

	void Update()
	{
		if (submit.IsPressed())
		{
			holdTimer += Time.unscaledDeltaTime;

			if (holdTimer >= HOLD_TIME_TO_START)
			{
				GameManager.Instance.SetScenarioIndex(scenarioIndex);
				SceneManager.LoadScene("_Scenes/ShopFrontScene", LoadSceneMode.Single);
			}
		}
		else
			holdTimer = 0f;
		
		if (startRadialProgress != null)
			startRadialProgress.Progress = Mathf.InverseLerp(0f, HOLD_TIME_TO_START, holdTimer);
	}

	void OnNavigate(InputAction.CallbackContext _context)
	{
		Vector2 inputDir = _context.ReadValue<Vector2>();

		float tempLast = lastXInput;
		lastXInput = inputDir.x;
		
		if (inputDir.x == 0f || Mathf.Abs(inputDir.x - tempLast) < 0.7f)
			return;

		if (inputDir.x > 0)
			scenarioIndex++;
		else
			scenarioIndex--;

		if (scenarioIndex < 0)
			scenarioIndex = scenarioNames.Length - 1;
		else if (scenarioIndex >= scenarioNames.Length)
			scenarioIndex = 0;

		scenarioSelectLabel.text = scenarioNames[scenarioIndex];
	}
}
