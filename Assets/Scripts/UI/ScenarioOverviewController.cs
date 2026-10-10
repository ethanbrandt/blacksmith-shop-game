using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ScenarioOverviewController : MonoBehaviour
{
    [SerializeField] float holdTimeToConfirm;
    [SerializeField] PlayerInput playerInput;

    private InputAction submit;
    private InputAction navigate;
    private UIDocument document;
    private readonly List<PartInfoElement> partInfoElements = new List<PartInfoElement>();
    private RadialProgress continueRadialProgress;
    private Image piecePreviewImage;
    private Label forgePieceTitleLabel;
    private Label customerNameLabel;
    private Label customerNoteLabel;
    private ScrollView overviewContent;
    private VisualElement materialGuideArea;
    private float confirmTimer;

    void Awake()
    {
        submit = playerInput.actions.FindAction("UI/Submit", true);
        navigate = playerInput.actions.FindAction("UI/Navigate", true);
        document = GetComponent<UIDocument>();
        document.enabled = true;
        document.rootVisualElement.AddToClassList("is-hidden");
        confirmTimer = -1f;
    }

    void OnEnable()
    {
        GameManager.Instance.RegisterScenarioOverviewController(this);
        if (document == null)
            return;

        var root = document.rootVisualElement;
        continueRadialProgress = root.Q<RadialProgress>("ContinueRadialProgress");
        continueRadialProgress.Progress = 0f;
        piecePreviewImage = root.Q<Image>("PiecePreviewImage");
        piecePreviewImage.scaleMode = ScaleMode.ScaleToFit;
        forgePieceTitleLabel = root.Q<Label>("PieceTitleLabel");
        customerNameLabel = root.Q<Label>("CustomerNameLabel");
        customerNoteLabel = root.Q<Label>("CustomerNoteLabel");
        overviewContent = root.Q<ScrollView>("OverviewContent");
        materialGuideArea = root.Q<VisualElement>("MaterialGuideArea");

        partInfoElements.Clear();
        foreach (var partRoot in root.Q<VisualElement>("PartInfoArea").Query<VisualElement>("PartInfoElement").ToList())
        {
            partInfoElements.Add(new PartInfoElement {
                rootElement = partRoot,
                partNameLabel = partRoot.Q<Label>("PartNameLabel"),
                materialLabel = partRoot.Q<Label>("MaterialLabel")
            });
        }
    }

    void OnDisable()
    {
        GameManager.Instance?.UnregisterScenarioOverviewController();
        partInfoElements.Clear();
        continueRadialProgress = null;
        piecePreviewImage = null;
        forgePieceTitleLabel = null;
        customerNameLabel = null;
        customerNoteLabel = null;
        overviewContent = null;
        materialGuideArea = null;
        confirmTimer = -1f;
    }

    void Update()
    {
        if (GameManager.IsTransitioning || confirmTimer < 0)
            return;

        Vector2 navigation = navigate.ReadValue<Vector2>();
        overviewContent.scrollOffset += Vector2.down * (navigation.y * 160f * Time.unscaledDeltaTime);

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

    public void HideScenarioOverview()
    {
        confirmTimer = -1f;
        if (continueRadialProgress != null)
            continueRadialProgress.Progress = 0f;
        if (document != null)
            document.rootVisualElement.AddToClassList("is-hidden");
    }

    public void ShowScenarioOverview(CustomerScenario _scenario)
    {
        if (document == null || _scenario == null || _scenario.RequestedPiece == null)
        {
            Debug.LogError("Scenario overview requires a UI document and a requested piece.", this);
            return;
        }

        ScenarioPart[] parts = _scenario.RequestedPiece.ScenarioParts;
        if (parts == null || parts.Length > partInfoElements.Count)
        {
            Debug.LogError("Invalid number of parts to display on Scenario Overview", this);
            return;
        }

        SetConfirmTimer(0f);
        document.rootVisualElement.RemoveFromClassList("is-hidden");
        piecePreviewImage.sprite = _scenario.PiecePreviewSprite;
        forgePieceTitleLabel.text = _scenario.PieceTitle;
        customerNameLabel.text = $"Customer: {_scenario.CustomerName}";
        customerNoteLabel.text = $"Customer Note: {_scenario.CustomerNote}";

        for (int i = 0; i < partInfoElements.Count; i++)
        {
            var info = partInfoElements[i];
            info.rootElement.EnableInClassList("is-hidden", i >= parts.Length);
            if (i >= parts.Length)
                continue;

            info.partNameLabel.text = GetPartName(parts[i]);
            info.materialLabel.text = $"Material: {GetMaterialName(parts[i].metalType)}";
        }

        BuildMaterialGuides(parts);
        HatchetTutorialMockup.ShowWelcome(document.rootVisualElement.Q<VisualElement>("OverviewTutorial"), _scenario.IsTutorial);
        overviewContent.scrollOffset = Vector2.zero;
    }

    private void BuildMaterialGuides(ScenarioPart[] _parts)
    {
        materialGuideArea.Clear();
        var materials = new List<MetalType>();
        var partNamesByMaterial = new Dictionary<MetalType, List<string>>();
        foreach (var part in _parts)
        {
            if (part.metalType == null)
                continue;
            if (!partNamesByMaterial.TryGetValue(part.metalType, out var names))
            {
                names = new List<string>();
                materials.Add(part.metalType);
                partNamesByMaterial.Add(part.metalType, names);
            }
            string partName = GetPartName(part);
            if (!names.Contains(partName))
                names.Add(partName);
        }

        materialGuideArea.EnableInClassList("is-hidden", materials.Count == 0);
        for (int i = 0; i < materials.Count; i++)
        {
            var material = materials[i];
            var guide = CreateMaterialGuide(material, partNamesByMaterial[material]);
            guide.EnableInClassList("material-guide-separated", i > 0);
            materialGuideArea.Add(guide);
        }
    }

    private static VisualElement CreateMaterialGuide(MetalType _material, List<string> _partNames)
    {
        var guide = CreateElement("material-guide", "MaterialGuideElement");
        var heading = CreateElement("guide-heading-row");
        var materialHeading = CreateElement("guide-material-heading");
        materialHeading.Add(CreateLabel(GetMaterialName(_material).ToUpperInvariant(), "guide-heading", "GuideMaterialLabel"));
        materialHeading.Add(CreateLabel(string.Join(", ", _partNames).ToUpperInvariant(), "guide-used-for", "GuidePartsLabel"));
        heading.Add(materialHeading);
        heading.Add(CreateLabel("COLD  >  HOT", "temperature-endpoint"));
        guide.Add(heading);

        var bar = CreateElement("temperature-bar", "GuideTemperatureBar");
        var connectors = CreateElement("region-connectors");
        var explanations = CreateElement("region-explanations");
        List<HeatGaugeRegion> regions = _material.GetHeatGaugeRegions();
        if (regions != null)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                HeatGaugeRegion region = regions[i];
                float start = Mathf.Clamp01(region.startHeat);
                float end = i + 1 < regions.Count ? Mathf.Clamp01(regions[i + 1].startHeat) : 1f;
                if (end <= start)
                    continue;

                var band = CreateElement("temperature-region", "HeatGaugeRegion");
                band.style.left = Length.Percent(start * 100f);
                band.style.width = Length.Percent((end - start) * 100f);
                band.style.backgroundColor = region.regionColor;
                bar.Add(band);

                var tick = CreateElement("region-tick");
                tick.style.left = Length.Percent((start + end) * 50f);
                connectors.Add(tick);

                var caption = CreateElement("region-explanation");
                caption.style.width = Length.Percent((end - start) * 100f);
                caption.Add(CreateLabel(region.label, "region-title", "RegionLabel"));
                explanations.Add(caption);
            }
        }

        // Keep the outside labels within the guide when a material has narrow end regions.
        if (explanations.childCount > 2)
        {
            explanations[0].style.alignItems = Align.FlexStart;
            explanations[explanations.childCount - 1].style.alignItems = Align.FlexEnd;
        }
        guide.Add(bar);
        guide.Add(connectors);
        guide.Add(explanations);
        if (!string.IsNullOrWhiteSpace(_material.note))
            guide.Add(CreateLabel($"Material Note: {_material.note}", "material-note", "MaterialNoteLabel"));
        return guide;
    }

    private static VisualElement CreateElement(string _className, string _name = "")
    {
        var element = new VisualElement { name = _name };
        element.AddToClassList(_className);
        return element;
    }

    private static Label CreateLabel(string _text, string _className, string _name = "")
    {
        var label = new Label(_text) { name = _name };
        label.AddToClassList(_className);
        return label;
    }

    private static string GetPartName(ScenarioPart _part)
    {
        return _part.partDefinition != null ? _part.partDefinition.DisplayLabel : "Part";
    }

    private static string GetMaterialName(MetalType _material)
    {
        if (_material == null)
            return "Unknown";
        return string.IsNullOrWhiteSpace(_material.displayName) ? _material.name : _material.displayName;
    }

    private struct PartInfoElement
    {
        public VisualElement rootElement;
        public Label partNameLabel;
        public Label materialLabel;
    }
}
