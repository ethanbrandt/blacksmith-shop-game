using System;
using DialogueSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Tooltip("ONLY FOR DEBUG PURPOSES")]
    [SerializeField] CustomerScenario testScenario;
    [SerializeField] CustomerScenario[] customerScenarios;

    [Header("Scene Transitions")]
    [SerializeField, Min(0f)] float sceneFadeSeconds = 0.45f;
    [SerializeField, Min(0f)] float laterThatDaySeconds = 2.5f;

    public static bool IsTransitioning => ScreenTransitionController.IsTransitioning;

    public static GameManager Instance;

    enum ScenarioPhase { Introduction, Workshop, CustomerReaction, ReturningToMenu }
    ScenarioPhase phase;
    int currentScenarioIndex;
    CustomerScenario completedScenario;
    FinalRank completedRank;
    DialogueManager dialogueManager;
    ScenarioOverviewController scenarioOverviewController;
    RoundManager roundManager;

    public bool HasCompletedResult => completedScenario != null;
    public FinalRank CompletedRank => completedRank;
    public bool IsCustomerReaction => phase == ScenarioPhase.CustomerReaction;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        currentScenarioIndex = 0;
        phase = ScenarioPhase.Introduction;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (IsTransitioning || Keyboard.current == null)
            return;
	    if (phase == ScenarioPhase.Introduction && Keyboard.current.rKey.wasPressedThisFrame && Keyboard.current.fKey.isPressed)
		    EndShopFrontScene();
	    
	    if (phase == ScenarioPhase.Workshop && Keyboard.current.fKey.isPressed)
	    {
		    if (Keyboard.current.digit1Key.wasPressedThisFrame)
		    {
			    RecordRoundResult(FinalRank.D);
			    EndWorkshopScene();
		    }
		    else if (Keyboard.current.digit2Key.wasPressedThisFrame)
		    {
			    RecordRoundResult(FinalRank.C);
			    EndWorkshopScene();
		    }
		    else if (Keyboard.current.digit3Key.wasPressedThisFrame)
		    {
			    RecordRoundResult(FinalRank.B);
			    EndWorkshopScene();
		    }
		    else if (Keyboard.current.digit4Key.wasPressedThisFrame)
		    {
			    RecordRoundResult(FinalRank.A);
			    EndWorkshopScene();
		    }
		    else if (Keyboard.current.digit5Key.wasPressedThisFrame)
		    {
			    RecordRoundResult(FinalRank.S);
			    EndWorkshopScene();
		    }
	    } 
    }

    public void RegisterRoundManager(RoundManager manager)
    {
        if (roundManager != null || manager == null)
        {
            Debug.LogError("Cannot register an invalid or duplicate RoundManager.", this);
            return;
        }
        CustomerScenario scenario = GetCurrentScenario();
        if (scenario == null || scenario.RequestedPiece == null)
        {
            Debug.LogError("The current customer scenario needs a requested piece.", this);
            return;
        }
        roundManager = manager;
        phase = ScenarioPhase.Workshop;
        completedScenario = null;
        roundManager.BeginRound(scenario.RequestedPiece);
    }

    public void UnregisterRoundManager()
    {
        roundManager = null;
    }

    public void RegisterDialogueManager(DialogueManager manager)
    {
        if (dialogueManager != null || manager == null)
        {
            Debug.LogError("Cannot register an invalid or duplicate DialogueManager.", this);
            return;
        }
        dialogueManager = manager;
    }

    public void UnregisterDialogueManager()
    {
        dialogueManager = null;
    }

    public void RegisterScenarioOverviewController(ScenarioOverviewController controller)
    {
        if (scenarioOverviewController != null || controller == null)
        {
            Debug.LogError("Cannot register an invalid or duplicate ScenarioOverviewController.", this);
            return;
        }
        scenarioOverviewController = controller;
    }

    public void UnregisterScenarioOverviewController()
    {
        scenarioOverviewController = null;
    }

    public void EndShopFrontScene()
    {
        if (IsTransitioning || phase != ScenarioPhase.Introduction)
            return;
        phase = ScenarioPhase.Workshop;
        completedScenario = null;
        ScreenTransitionController.LoadScene("_Scenes/WorkshopScene", sceneFadeSeconds);
    }

    public void RecordRoundResult(FinalRank rank)
    {
        if (phase != ScenarioPhase.Workshop)
            return;
        completedScenario = GetCurrentScenario();
        completedRank = rank;
    }

    public void EndWorkshopScene()
    {
        if (IsTransitioning || phase != ScenarioPhase.Workshop || !HasCompletedResult)
            return;
        phase = ScenarioPhase.CustomerReaction;
        ScreenTransitionController.LoadScene("_Scenes/ShopFrontScene", sceneFadeSeconds, ResumeDialogueAfterTransition);
    }

    public void StartCurrentDialogue()
    {
        if (dialogueManager == null)
            return;
        CustomerScenario scenario = IsCustomerReaction ? completedScenario : GetCurrentScenario();
        scenarioOverviewController?.HideScenarioOverview();
        if (scenario == null)
        {
            ReturnToMenu();
            return;
        }
        DialogueScene scene = IsCustomerReaction ? scenario.GetCompletionDialogue(completedRank) : scenario.Scene;
        // Rank reactions can share the introduction's actor without duplicating its assignment.
        GameObject fallbackActor = IsCustomerReaction && scenario.Scene != null ? scenario.Scene.actorPrefab : null;
        dialogueManager.SetDialogueScene(scene, fallbackActor, IsTransitioning);
    }

    public void EndOfDialogue()
    {
        if (IsTransitioning)
            return;
        if (IsCustomerReaction)
        {
            AdvanceAfterCustomerReaction();
            return;
        }
        if (phase == ScenarioPhase.Introduction)
        {
            CustomerScenario scenario = GetCurrentScenario();
            if (scenario != null)
                scenarioOverviewController?.ShowScenarioOverview(scenario);
        }
    }

    void AdvanceAfterCustomerReaction()
    {
        int completedIndex = testScenario != null && customerScenarios != null ? Array.IndexOf(customerScenarios, completedScenario) : currentScenarioIndex;
        testScenario = null;
        completedScenario = null;
        completedRank = FinalRank.D;
        if (customerScenarios == null || completedIndex < 0)
        {
            ReturnToMenu();
            return;
        }

        if (completedIndex + 1 >= customerScenarios.Length)
        {
	        phase = ScenarioPhase.ReturningToMenu;
	        completedScenario = null;
	        ScreenTransitionController.LoadScene("_Scenes/PromoscreenScene", sceneFadeSeconds, () => Destroy(gameObject));
	        return;
        }
        
        ScreenTransitionController.Interlude("Later that day", () =>
        {
            currentScenarioIndex = completedIndex + 1;
            phase = ScenarioPhase.Introduction;
            StartCurrentDialogue();
        }, ResumeDialogueAfterTransition, sceneFadeSeconds, laterThatDaySeconds);
    }

    void ReturnToMenu()
    {
        phase = ScenarioPhase.ReturningToMenu;
        completedScenario = null;
        ScreenTransitionController.LoadScene("_Scenes/StartScene", sceneFadeSeconds);
    }

    public void StartScenario(int scenarioIndex)
    {
        if (IsTransitioning)
            return;
        if (customerScenarios == null || scenarioIndex < 0 || scenarioIndex >= customerScenarios.Length)
        {
            Debug.LogError("Invalid Scenario Index", this);
            return;
        }
        SetScenarioIndex(scenarioIndex);
        ScreenTransitionController.LoadScene("_Scenes/ShopFrontScene", sceneFadeSeconds, ResumeDialogueAfterTransition);
    }

    void ResumeDialogueAfterTransition()
    {
        if (dialogueManager == null)
            return;
        if (dialogueManager.HasPreparedDialogue)
            dialogueManager.BeginPreparedDialogue();
        else
            StartCurrentDialogue();
    }

    public string ResolveDialogueText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        CustomerScenario scenario = IsCustomerReaction ? completedScenario : GetCurrentScenario();
        if (scenario == null)
            return text;
        return text.Replace("{piece}", scenario.PieceTitle ?? "piece").Replace("{customer}", scenario.CustomerName ?? "Customer").Replace("{rank}", HasCompletedResult ? completedRank.ToString() : string.Empty);
    }

    public CustomerScenario GetCurrentScenario()
    {
        if (testScenario != null)
            return testScenario;
        if (customerScenarios == null || currentScenarioIndex < 0 || currentScenarioIndex >= customerScenarios.Length)
            return null;
        return customerScenarios[currentScenarioIndex];
    }

    public void SetScenarioIndex(int scenarioIndex)
    {
        if (customerScenarios == null || scenarioIndex < 0 || scenarioIndex >= customerScenarios.Length)
        {
            Debug.LogError("Invalid Scenario Index", this);
            return;
        }
        testScenario = null;
        completedScenario = null;
        completedRank = FinalRank.D;
        phase = ScenarioPhase.Introduction;
        currentScenarioIndex = scenarioIndex;
    }
}
