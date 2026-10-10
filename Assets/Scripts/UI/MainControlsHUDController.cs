using UnityEngine;
using UnityEngine.UIElements;

public class MainControlsHUDController : MonoBehaviour
{
    [Header("Prompt Offsets")]
    [SerializeField] float stationPromptYOffset;
    [SerializeField] float pickablePromptYOffset;

    private readonly HatchetTutorialProgress tutorialProgress = new HatchetTutorialProgress();
    private HatchetTutorialStep displayedTutorialStep;
    private VisualElement tutorialPanel;
    private VisualElement controlsVisual;
    private bool displayedForgeOpen;
    private bool displayedGrindOpen;
    private bool showTutorial;
    private float normalSortingOrder;
    private RoundManager roundManager;
    private bool tutorialInitialized;
    
    private UIDocument document;
    
    private VisualElement xButton;
    private VisualElement yButton;
    private VisualElement buttonPrompt;

    private Label yLabel;
    private Label xLabel;
    
    private PlayerController player;

    private Transform promptTarget;
    private float currentPromptOffset;
    
    void Awake()
    {
        document = GetComponent<UIDocument>();
        normalSortingOrder = document.sortingOrder;
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        controlsVisual = document.rootVisualElement.Q<VisualElement>("ControlsVisual");

        xButton = document.rootVisualElement.Q<VisualElement>("XButton");
        yButton = document.rootVisualElement.Q<VisualElement>("YButton");
        
        yLabel = yButton.Q<Label>("ActionLabel");
        xLabel = xButton.Q<Label>("ActionLabel");
        
        buttonPrompt = document.rootVisualElement.Q<VisualElement>("PromptVisual");
        buttonPrompt.EnableInClassList("is-hidden", true);

        
        player = FindFirstObjectByType<PlayerController>();
        roundManager = FindFirstObjectByType<RoundManager>();
    }

    void Start()
    {
        tutorialPanel = document.rootVisualElement.Q<VisualElement>("WorkshopTutorial");
        RefreshTutorial();
    }

    private void RefreshTutorial()
    {
        bool forgeOpen = ForgeSessionController.IsBlockingPlayer;
        bool grindOpen = GrindSessionController.IsBlockingPlayer;
        var scenario = GameManager.Instance != null ? GameManager.Instance.GetCurrentScenario() : null;
        bool visible = scenario != null && scenario.IsTutorial && roundManager != null
            && roundManager.SpawnedParts.Count > 0 && !roundManager.HasEnded;
        HatchetTutorialStep step = HatchetTutorialStep.PickUpMetal;
        if (visible)
        {
            HeatableMetal heldPart = player != null && player.Held != null ? player.Held.GetComponent<HeatableMetal>() : null;
            HeatableMetal forgePart = forgeOpen ? ForgeSessionController.Instance.ActiveMetal : null;
            HeatableMetal grindPart = grindOpen ? GrindSessionController.Instance.ActiveMetal : null;
            step = tutorialProgress.Update(roundManager.SpawnedParts, heldPart, forgePart, grindPart, roundManager.HasEnded);
            visible = step != HatchetTutorialStep.Complete;
        }

        if (!tutorialInitialized || displayedTutorialStep != step || displayedForgeOpen != forgeOpen
            || displayedGrindOpen != grindOpen || showTutorial != visible)
        {
            tutorialInitialized = true;
            displayedTutorialStep = step;
            displayedForgeOpen = forgeOpen;
            displayedGrindOpen = grindOpen;
            showTutorial = visible;
            if (forgeOpen)
                HatchetTutorialUI.ShowForge(tutorialPanel, visible);
            else if (grindOpen)
                HatchetTutorialUI.ShowGrind(tutorialPanel, visible);
            else
                HatchetTutorialUI.ShowWorkshop(tutorialPanel, visible, step);
        }

        bool isBlocking = forgeOpen || grindOpen;
        document.rootVisualElement.EnableInClassList("is-hidden", isBlocking && !showTutorial);
        controlsVisual.EnableInClassList("is-hidden", isBlocking);
        buttonPrompt.style.display = isBlocking ? new StyleEnum<DisplayStyle>(DisplayStyle.None) : new StyleEnum<DisplayStyle>(StyleKeyword.Null);
        // Station windows render at 100. Keep the usual tutorial above them.
        document.sortingOrder = isBlocking && showTutorial ? Mathf.Max(normalSortingOrder, 101f) : normalSortingOrder;
    }

    private void LateUpdate()
    {
        RefreshTutorial();
        RefreshInteractionPrompts();
        UpdatePromptPosition();
    }

    private void RefreshInteractionPrompts()
    {
        if (!player || !player.CanShowInteractionPrompts)
        {
            xButton.EnableInClassList("is-hidden", true);
            yButton.EnableInClassList("is-hidden", true);
            buttonPrompt.EnableInClassList("is-hidden", true);
            promptTarget = null;
            return;
        }

        Pickable held = player.Held;
        Component target = player.InteractionTarget;
        promptTarget = target ? target.transform : null;
        currentPromptOffset = target is Pickable ? pickablePromptYOffset : stationPromptYOffset;
        buttonPrompt.EnableInClassList("is-hidden", !promptTarget);

        string actionText = held ? "Drop" + GetPickableName(held) : null;
        if (target is Pickable pickable)
            actionText = "Pick Up" + GetPickableName(pickable);
        else if (target is Bellows)
            actionText = "Pump bellows";
        else if (target is Station station)
            actionText = held && station.CanInteract(held) ? "Place" + GetPickableName(held) : "Interact";
        else if (target is Interactable)
            actionText = "Interact";

        yButton.EnableInClassList("is-hidden", actionText == null);
        yLabel.text = actionText ?? string.Empty;

        Furnace fuelFurnace = held ? null : player.GetNearbyFuelFurnace();
        bool canAddFuel = fuelFurnace && fuelFurnace.Fuel < fuelFurnace.MaxFuel - 0.0001f;
        xButton.EnableInClassList("is-hidden", !held && !canAddFuel);
        xLabel.text = held ? "Throw" : "Add fuel";

    }

    private string GetPickableName(Pickable _pickable)
    {
        if (!_pickable)
            return "";
        
        if (_pickable.TryGetComponent(out HeatableMetal metal) && metal.PartDefinition != null)
            return ' ' + metal.PartDefinition.displayName;

        return "";
    }

    private void UpdatePromptPosition()
    {
        Camera cam = Camera.main;
        if (!cam)
            return;

        if (promptTarget)
            PositionPrompt(buttonPrompt, promptTarget, currentPromptOffset, cam);
    }

    private static void PositionPrompt(VisualElement element, Transform target, float offset, Camera cam)
    {
        Vector2 screenPos = cam.WorldToScreenPoint(target.position);
        element.style.left = new Length(100f * (screenPos.x / cam.pixelWidth), LengthUnit.Percent);
        element.style.bottom = new Length(100f * (screenPos.y / cam.pixelHeight) + offset, LengthUnit.Percent);
    }
}
