using UnityEngine;
using UnityEngine.UIElements;

public class MainControlsHUDController : MonoBehaviour
{
    [Header("Prompt Offsets")]
    [SerializeField] float stationPromptYOffset;
    [SerializeField] float pickablePromptYOffset;

    [Header("Tutorial Visual Mockup")]
    [Tooltip("Manual preview only. Changing this in Play mode swaps Ethan's text and pose; player actions do not advance it.")]
    [SerializeField] TutorialPreviewStep tutorialPreviewStep = TutorialPreviewStep.PickUpMetal;
    private TutorialPreviewStep displayedTutorialStep;
    private VisualElement tutorialPanel;
    private VisualElement controlsVisual;
    private bool displayedForgeOpen;
    private bool displayedGrindOpen;
    private bool showTutorial;
    private float normalSortingOrder;
    
    private UIDocument document;
    
    private VisualElement leftStick;
    private VisualElement xButton;
    private VisualElement yButton;
    private VisualElement buttonPrompt;

    private Label yLabel;
    
    private PlayerController player;

    private Transform promptTarget;
    private float currentPromptOffset;
    
    void Awake()
    {
        document = GetComponent<UIDocument>();
        normalSortingOrder = document.sortingOrder;
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        controlsVisual = document.rootVisualElement.Q<VisualElement>("ControlsVisual");

        leftStick = document.rootVisualElement.Q<VisualElement>("LeftStick");
        xButton = document.rootVisualElement.Q<VisualElement>("XButton");
        yButton = document.rootVisualElement.Q<VisualElement>("YButton");
        
        yLabel = yButton.Q<Label>("ActionLabel");
        
        buttonPrompt = document.rootVisualElement.Q<VisualElement>("PromptVisual");
        buttonPrompt.EnableInClassList("is-hidden", true);

        
        player = FindFirstObjectByType<PlayerController>();
    }

    void Start()
    {
        tutorialPanel = document.rootVisualElement.Q<VisualElement>("WorkshopTutorial");
        RefreshTutorialPreview();
    }

    private void Update()
    {
        bool forgeOpen = ForgeSessionController.IsBlockingPlayer;
        bool grindOpen = GrindSessionController.IsBlockingPlayer;
        if (displayedTutorialStep != tutorialPreviewStep || displayedForgeOpen != forgeOpen || displayedGrindOpen != grindOpen)
            RefreshTutorialPreview();

        bool isBlocking = forgeOpen || grindOpen;
        document.rootVisualElement.EnableInClassList("is-hidden", isBlocking && !showTutorial);
        controlsVisual.EnableInClassList("is-hidden", isBlocking);
        buttonPrompt.style.display = isBlocking ? new StyleEnum<DisplayStyle>(DisplayStyle.None) : new StyleEnum<DisplayStyle>(StyleKeyword.Null);
        // Station windows render at 100. Keep the usual tutorial above them.
        document.sortingOrder = isBlocking && showTutorial ? Mathf.Max(normalSortingOrder, 101f) : normalSortingOrder;
    }

    private void RefreshTutorialPreview()
    {
        displayedTutorialStep = tutorialPreviewStep;
        displayedForgeOpen = ForgeSessionController.IsBlockingPlayer;
        displayedGrindOpen = GrindSessionController.IsBlockingPlayer;
        var scenario = GameManager.Instance != null ? GameManager.Instance.GetCurrentScenario() : null;
        showTutorial = scenario != null && scenario.IsTutorial;
        if (displayedForgeOpen)
            HatchetTutorialMockup.ShowForge(tutorialPanel, showTutorial);
        else if (displayedGrindOpen)
            HatchetTutorialMockup.ShowGrind(tutorialPanel, showTutorial);
        else
            HatchetTutorialMockup.ShowWorkshop(tutorialPanel, showTutorial, tutorialPreviewStep);
    }

    private void LateUpdate()
    {
        UpdatePromptPosition();
    }

    void OnEnable()
    {
        player.OnPickUp += OnPickUp;
        player.OnHighlight += OnHighlight;
    }

    void OnDisable()
    {
        if (!player)
            return;
        
        player.OnPickUp -= OnPickUp;
        player.OnHighlight -= OnHighlight;
    }

    private void OnPickUp(Transform _transform)
    {
        if (!_transform)
        {
            xButton.EnableInClassList("is-hidden", true);
            yButton.EnableInClassList("is-hidden", true);
            return;
        }
        
        xButton.EnableInClassList("is-hidden", false);
        yButton.EnableInClassList("is-hidden", false);
        
        yLabel.text = "Drop" + GetPickableName(player.Held);
    }

    private void OnHighlight(Transform _transform)
    {
        promptTarget = _transform;
        
        if (!_transform)
        {
            buttonPrompt.EnableInClassList("is-hidden", true);
            yButton.EnableInClassList("is-hidden", !player.Held);
            yLabel.text = "Drop" + GetPickableName(player.Held);
            return;
        }
        
        buttonPrompt.EnableInClassList("is-hidden", false);
        UpdatePromptPosition();

        
        if (_transform.TryGetComponent(out Pickable pickable))
        {
            yButton.EnableInClassList("is-hidden", false);

            string actionText = "Pick Up" + GetPickableName(pickable);
            yLabel.text = actionText;
            
            currentPromptOffset = pickablePromptYOffset;
        }
        else if (_transform.TryGetComponent(out Station station))
        {
            yButton.EnableInClassList("is-hidden", false);
            
            string actionText = (player.Held && station.CanAccept(player.Held)) ? "Place" + GetPickableName(player.Held) : "Interact";
            yLabel.text = actionText;

            currentPromptOffset = stationPromptYOffset;
        }
    }

    private string GetPickableName(Pickable _pickable)
    {
        if (!_pickable)
            return "";
        
        if (_pickable.TryGetComponent(out HeatableMetal metal))
            return ' ' + metal.PartDefinition.displayName;

        return "";
    }

    private void UpdatePromptPosition()
    {
        if (!promptTarget)
            return;
        
        Camera cam = Camera.main;
        Vector2 buttonPromptScreenPos = cam.WorldToScreenPoint(promptTarget.position);
        buttonPrompt.style.left = new Length(100f * (buttonPromptScreenPos.x / cam.pixelWidth), LengthUnit.Percent);
        buttonPrompt.style.bottom = new Length((100f * (buttonPromptScreenPos.y / cam.pixelHeight)) + currentPromptOffset, LengthUnit.Percent);
    }
}
