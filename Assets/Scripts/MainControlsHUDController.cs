using UnityEngine;
using UnityEngine.UIElements;

public class MainControlsHUDController : MonoBehaviour
{
    [Header("Prompt Offsets")]
    [SerializeField] float stationPromptYOffset;
    [SerializeField] float pickablePromptYOffset;
    
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

        leftStick = document.rootVisualElement.Q<VisualElement>("LeftStick");
        xButton = document.rootVisualElement.Q<VisualElement>("XButton");
        yButton = document.rootVisualElement.Q<VisualElement>("YButton");
        
        yLabel = yButton.Q<Label>("ActionLabel");
        
        buttonPrompt = document.rootVisualElement.Q<VisualElement>("PromptVisual");
        buttonPrompt.EnableInClassList("is-hidden", true);

        
        player = FindFirstObjectByType<PlayerController>();
    }

    private void Update()
    {
        bool isBlocking = ForgeSessionController.IsBlockingPlayer || GrindSessionController.IsBlockingPlayer;
        document.rootVisualElement.EnableInClassList("is-hidden", isBlocking);
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
        
        if (_pickable.Type == Pickable.PickableType.Fuel)
            return " Fuel";
        
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
