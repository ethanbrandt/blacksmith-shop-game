using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ScenarioOverviewController : MonoBehaviour
{
	[SerializeField] float holdTimeToConfirm;
	[SerializeField] PlayerInput playerInput;
	
	private InputAction submit;
	
	private UIDocument document;
	private List<PartInfoElement> partInfoElements;

	private RadialProgress continueRadialProgress;
	
	private Image piecePreviewImage;

	private Label forgePieceTitleLabel;
	private Label customerNameLabel;
	private Label rewardEstimateLabel;
	private Label customerNoteLabel;

	private float confirmTimer;
	
	void Awake()
	{
		submit = playerInput.actions.FindAction("UI/Submit", true);
		
		document = GetComponent<UIDocument>();
		document.enabled = true;
		document.rootVisualElement.EnableInClassList("is-hidden", true);
		partInfoElements = new List<PartInfoElement>();

		confirmTimer = -1f;
	}

	void OnEnable()
	{
		GameManager.Instance.RegisterScenarioOverviewController(this);
		
		if (document == null)
			return;

		continueRadialProgress = document.rootVisualElement.Q<RadialProgress>("ContinueRadialProgress");
		continueRadialProgress.Progress = 0f;
		
		piecePreviewImage = document.rootVisualElement.Q<Image>("PiecePreviewImage");
		
		forgePieceTitleLabel = document.rootVisualElement.Q<Label>("PieceTitleLabel");
		customerNameLabel = document.rootVisualElement.Q<Label>("CustomerNameLabel");
		rewardEstimateLabel = document.rootVisualElement.Q<Label>("RewardEstimateLabel");
		customerNoteLabel = document.rootVisualElement.Q<Label>("CustomerNoteLabel");
		
		var partInfoArea = document.rootVisualElement.Q<VisualElement>("PartInfoArea");
		List<VisualElement> partInfoRoots = partInfoArea.Query<VisualElement>("PartInfoElement").ToList();

		foreach (var root in partInfoRoots)
		{
			Label partNameLabel = root.Q<Label>("PartNameLabel");
			Label materialLabel = root.Q<Label>("MaterialLabel");
			Label forgeHeatLabel = root.Q<Label>("ForgeHeatLabel");
			Label meltHeatLabel = root.Q<Label>("MeltHeatLabel");
			
			partInfoElements.Add(new PartInfoElement {
				rootElement = root,
				partNameLabel = partNameLabel,
				materialLabel = materialLabel,
				forgeHeatLabel = forgeHeatLabel, 
				meltHeatLabel = meltHeatLabel
			});
		}
	}

	void OnDisable()
	{
		GameManager.Instance.UnregisterScenarioOverviewController();
		partInfoElements.Clear();

		continueRadialProgress = null;
		
		piecePreviewImage = null;

		forgePieceTitleLabel = null;
		customerNameLabel = null;
		rewardEstimateLabel = null;
		customerNoteLabel = null;
		
		confirmTimer = -1f;
	}

	void Update()
	{
		if (confirmTimer < 0)
			return;
		
		if (submit.IsPressed())
		{
			SetConfirmTimer(confirmTimer + Time.deltaTime);
			
			if (confirmTimer >= holdTimeToConfirm)
				GameManager.Instance.EndShopFrontScene();
		}
		else
			SetConfirmTimer(0f);
	}

	private void SetConfirmTimer(float _value)
	{
		confirmTimer = _value;
			
		if (continueRadialProgress != null)
			continueRadialProgress.Progress = Mathf.InverseLerp(0f, holdTimeToConfirm, confirmTimer);
	}

	public void ShowScenarioOverview(CustomerScenario _scenario)
	{
		if (document == null)
		{
			Debug.LogError("UI Document not found");
			return;
		}

		confirmTimer = 0f;
		
		document.rootVisualElement.EnableInClassList("is-hidden", false);

		var requestedPiece = _scenario.RequestedPiece;
		
		ScenarioPart[] parts = requestedPiece.ScenarioParts;
		if (parts.Length > 3)
		{
			Debug.LogError("Too many parts to display on Scenario Overview");
			return;
		}

		if (piecePreviewImage != null)
			piecePreviewImage.sprite = _scenario.PiecePreviewSprite;

		if (forgePieceTitleLabel != null)
			forgePieceTitleLabel.text = _scenario.PieceTitle;

		if (customerNameLabel != null)
			customerNameLabel.text = $"Customer: {_scenario.CustomerName}";

		if (rewardEstimateLabel != null)
			rewardEstimateLabel.text = $"Reward Estimate: {_scenario.EstimatedReward}";
		
		if (customerNoteLabel != null)
			customerNoteLabel.text = $"Customer Note: {_scenario.CustomerNote}";

		for (int i = 0; i < partInfoElements.Count; i++)
		{
			if (i >= parts.Length)
			{
				partInfoElements[i].rootElement.EnableInClassList("is-hidden", true);
				continue;
			}

			var partInfo = ExtractPartInfo(parts[i]);
			
			partInfoElements[i].rootElement.EnableInClassList("is-hidden", false);
			partInfoElements[i].partNameLabel.text = partInfo.partName;
			partInfoElements[i].materialLabel.text = $"Material: {partInfo.materialName}";
			partInfoElements[i].forgeHeatLabel.text = $"Forging Heat: {partInfo.forgingHeat}";
			partInfoElements[i].meltHeatLabel.text = $"Melting Heat: {partInfo.meltingHeat}";
		}
	}

    private ScenarioScreenPartInfo ExtractPartInfo(ScenarioPart _part)
    {
	    string partName = _part.partDefinition.displayName;
	    string matName = _part.metalType.displayName;

	    string forgeHeat = MetalHeatHeuristicToString(_part.metalType.forgeHeatHeuristic);
	    string meltHeat = MetalHeatHeuristicToString(_part.metalType.meltHeatHeuristic);
	    
	    if (string.IsNullOrEmpty(forgeHeat))
		    Debug.LogError("Invalid Forge Heat Heuristic");

	    if (string.IsNullOrEmpty(meltHeat))
			Debug.LogError("Invalid Melt Heat Heuristic");

	    return new ScenarioScreenPartInfo { partName = partName, materialName = matName, forgingHeat = forgeHeat, meltingHeat = meltHeat};
    }

    private string MetalHeatHeuristicToString(MetalHeatHeuristic _heatHeuristic)
    {
	    switch (_heatHeuristic)
	    {
		    case MetalHeatHeuristic.LOW:
			    return "Low";
		    case MetalHeatHeuristic.MEDIUM:
			    return "Medium";
		    case MetalHeatHeuristic.HIGH:
			    return "High";
	    }

	    return "";
    }

    private struct ScenarioScreenPartInfo
    {
	    public string partName;
	    public string materialName;
	    public string forgingHeat;
	    public string meltingHeat;
    }

    private struct PartInfoElement
    {
	    public VisualElement rootElement;
	    public Label partNameLabel;
	    public Label materialLabel;
	    public Label forgeHeatLabel;
	    public Label meltHeatLabel;
    }
}
