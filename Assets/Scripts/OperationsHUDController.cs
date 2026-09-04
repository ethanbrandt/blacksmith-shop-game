using UnityEngine;
using UnityEngine.UIElements;

public enum OperationStep
{
    FORGING,
    QUENCH,
    FINISH
}

public class OperationsHUDController : MonoBehaviour
{
    [Header("Status Icons")]
    [SerializeField] Sprite lockIcon;
    [SerializeField] Sprite checkMarkIcon;
    [SerializeField] Sprite dRankIcon;
    [SerializeField] Sprite cRankIcon;
    [SerializeField] Sprite bRankIcon;
    [SerializeField] Sprite aRankIcon;
    [SerializeField] Sprite sRankIcon;
    
    private UIDocument document;
    private VisualElement furnaceStep;
    private VisualElement anvilStep;
    private VisualElement quenchStep;
    private VisualElement grindStep;
    private VisualElement finishStep;

    private Image furnaceStatusIcon;
    private Image anvilStatusIcon;
    private Image quenchStatusIcon;
    private Image grindStatusIcon;
    private Image finishStatusIcon;

    private HeatableMetal currentMetal;

    private void Awake()
    {
        document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        furnaceStep = document.rootVisualElement.Q<VisualElement>("FurnaceStep");
        anvilStep = document.rootVisualElement.Q<VisualElement>("AnvilStep");
        quenchStep = document.rootVisualElement.Q<VisualElement>("QuenchStep");
        grindStep = document.rootVisualElement.Q<VisualElement>("GrindStep");
        finishStep = document.rootVisualElement.Q<VisualElement>("FinishStep");

        furnaceStatusIcon = furnaceStep.Q<Image>("StatusIcon");
        anvilStatusIcon = anvilStep.Q<Image>("StatusIcon");
        quenchStatusIcon = quenchStep.Q<Image>("StatusIcon");
        grindStatusIcon = grindStep.Q<Image>("StatusIcon");
        finishStatusIcon = finishStep.Q<Image>("StatusIcon");
        
        PlayerController player = FindFirstObjectByType<PlayerController>();
        player.OnPickUp += OnPickUpEvent;
        EnableUI(false);
    }

    private void OnDisable()
    {
        PlayerController player = FindFirstObjectByType<PlayerController>();
        
        if (player != null)
            player.OnPickUp -= OnPickUpEvent;
    }

    void FixedUpdate()
    {
        if (!currentMetal || currentMetal.Pickable.Type == Pickable.PickableType.QuenchedMetal)
            return;
        
        quenchStatusIcon.sprite = currentMetal.IsQuenchTemp ? null : lockIcon;
    }

    private void OnPickUpEvent(Transform _transform)
    {
        if (_transform == null)
        {
            EnableUI(false);
            currentMetal = null;
            return;
        }
        
        if (_transform.TryGetComponent(out HeatableMetal metal))
        {
            EnableUI(true);
            
            currentMetal = metal;
            UpdateUIOnPickup(metal);
        }
    }
    
    public void EnableUI(bool _enable)
    {
        document.rootVisualElement.EnableInClassList("is-hidden", !_enable);
    }

    private void UpdateUIOnPickup(HeatableMetal _metal)
    {
        grindStep.EnableInClassList("is-hidden", !_metal.PartDefinition.isBladed);

        if (_metal.Pickable.Type == Pickable.PickableType.QuenchedMetal)
        {
            ChangeOperationClassState(furnaceStep, furnaceStatusIcon, ClassState.COMPLETE);
            ChangeOperationClassState(anvilStep, anvilStatusIcon, ClassState.COMPLETE);
            ChangeOperationClassState(quenchStep, quenchStatusIcon, ClassState.COMPLETE);
            ChangeOperationClassState(grindStep, grindStatusIcon, ClassState.CURRENT);
            ChangeOperationClassState(finishStep, finishStatusIcon, ClassState.CURRENT);
            
            ChangeOperationRankStatusIcon(grindStatusIcon, (uint)_metal.SharpnessQuality);
        }
        else
        {
            ChangeOperationClassState(furnaceStep, furnaceStatusIcon, ClassState.CURRENT);
            ChangeOperationClassState(anvilStep, furnaceStatusIcon, ClassState.CURRENT);
            ChangeOperationClassState(quenchStep, quenchStatusIcon, ClassState.CURRENT);
            ChangeOperationClassState(grindStep, grindStatusIcon, ClassState.FUTURE);
            ChangeOperationClassState(finishStep, finishStatusIcon, ClassState.FUTURE);
            
            ChangeOperationRankStatusIcon(anvilStatusIcon, (uint)_metal.ForgeQuality);
        }
    }

    private void ChangeOperationClassState(VisualElement _visualElement, Image _statusIcon, ClassState _state)
    {
        _visualElement.EnableInClassList("operation--complete", false);
        _visualElement.EnableInClassList("operation--current", false);
        _visualElement.EnableInClassList("operation--future", false);
        _statusIcon.sprite = null;
        
        switch (_state)
        {
            case ClassState.CURRENT:
                _visualElement.EnableInClassList("operation--current", true);
                break;
            case ClassState.COMPLETE:
                _visualElement.EnableInClassList("operation--complete", true);
                _statusIcon.sprite = checkMarkIcon;
                break;
            case ClassState.FUTURE:
                _visualElement.EnableInClassList("operation--future", true);
                break;
        }
    }

    private void ChangeOperationRankStatusIcon(Image _statusIcon, uint _rank)
    {
        switch (_rank)
        {
            case 0:
                _statusIcon.sprite = dRankIcon;
                break;
            case 1:
                _statusIcon.sprite = cRankIcon;
                break;
            case 2:
                _statusIcon.sprite = bRankIcon;
                break;
            case 3:
                _statusIcon.sprite = aRankIcon;
                break;
            case 4:
                _statusIcon.sprite = sRankIcon;
                break;
        }
    }

    private enum ClassState
    {
        CURRENT,
        COMPLETE,
        FUTURE
    }
}