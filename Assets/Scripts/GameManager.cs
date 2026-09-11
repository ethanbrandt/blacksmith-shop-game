using System;
using DialogueSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
	[Tooltip("ONLY FOR DEBUG PURPOSES")]
	[SerializeField] CustomerScenario testScenario;
	[SerializeField] CustomerScenario[] customerScenarios;
	
	public static GameManager Instance;

	private int currentScenarioIndex;

	private DialogueManager dialogueManager;
	private ScenarioOverviewController scenarioOverviewController;
	private RoundManager roundManager;

	void Awake()
	{
		if (Instance != null)
			Destroy(this.gameObject);

		Instance = this;
		DontDestroyOnLoad(this.gameObject);

		currentScenarioIndex = 0;
	}

	public void RegisterRoundManager(RoundManager _roundManager)
	{
		if (roundManager != null)
		{
			Debug.LogError("Attempted to register RoundManager when it is already registered");
			return;
		}

		if (_roundManager == null)
		{
			Debug.LogError("Invalid RoundManager");
			return;
		}

		roundManager = _roundManager;
		
		roundManager.BeginRound(GetCurrentScenario().RequestedPiece);
	}

	public void UnregisterRoundManager()
	{
		roundManager = null;
	}

	public void RegisterDialogueManager(DialogueManager _dialogueManager)
	{
		if (dialogueManager != null)
		{
			Debug.LogError("Attempted to register DialogueManager when it is already registered");
			return;
		}

		if (_dialogueManager == null)
		{
			Debug.LogError("Invalid DialogueManager");
			return;
		}

		dialogueManager = _dialogueManager;
	}

	public void UnregisterDialogueManager()
	{
		dialogueManager = null;
	}

	public void RegisterScenarioOverviewController(ScenarioOverviewController _scenarioOverviewController)
	{
		if (scenarioOverviewController != null)
		{
			Debug.LogError("Attempted to register ScenarioOverviewController when it is already registered");
			return;
		}

		if (_scenarioOverviewController == null)
		{
			Debug.LogError("Invalid ScenarioOverviewController");
			return;
		}

		scenarioOverviewController = _scenarioOverviewController;
	}

	public void UnregisterScenarioOverviewController()
	{
		scenarioOverviewController = null;
	}

	public void EndShopFrontScene()
	{
		Debug.Log("ENDING SHOP FRONT SCENE");
		SceneManager.LoadScene("Scenes/WorkshopScene", LoadSceneMode.Single);
	}

	public void EndOfDialogue()
	{
		if (!scenarioOverviewController)
			return;
		
		scenarioOverviewController.ShowScenarioOverview(GetCurrentScenario());
	}

	public void EndWorkshopScene()
	{
		Debug.Log("ENDING WORKSHOP SCENE");
	}

	public CustomerScenario GetCurrentScenario()
	{
		if (testScenario != null)
			return testScenario;

		return customerScenarios[currentScenarioIndex];
	}
}
