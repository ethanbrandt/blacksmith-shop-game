using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

public class RoundUIHandler : MonoBehaviour
{
    [Tooltip("Rank icons in D, C, B, A, S order.")]
    [SerializeField] Sprite[] rankSprites;
    [SerializeField] ForgePiece[] scenarioOptions;
    [SerializeField] UIDocument document;
    [SerializeField] UIDocument finalScoreDocument;
    [SerializeField] TextMeshProUGUI timeElapsedText;
    
    RoundManager roundManager;
    Button button;
    Image rankImage;
    RadioButtonGroup scenarioSelector;

    bool endScreen;
    
    void Start()
    {
        roundManager = GetComponent<RoundManager>();

        if (finalScoreDocument != null)
            finalScoreDocument.gameObject.SetActive(false);
        
        document.gameObject.SetActive(true);
        button = document.rootVisualElement.Q<Button>("StartGameButton");
        button.clicked += OnStartGame;

        scenarioSelector = document.rootVisualElement.Q<RadioButtonGroup>("ScenarioSelector");
        
        document.rootVisualElement.schedule.Execute(() => scenarioSelector.Focus());
        
        foreach (var scenario in scenarioOptions)
            scenario.IsValid();
    }

    void OnDisable()
    {
        if (button != null)
            button.clicked -= OnStartGame;
    }

    void OnStartGame()
    {
        int selectedScenarioIndex = scenarioSelector.value;
        if (selectedScenarioIndex < 0 || selectedScenarioIndex >= scenarioSelector.choices.Count() || !scenarioOptions[selectedScenarioIndex].IsValid())
            return;
        
        roundManager.BeginRound(scenarioOptions[selectedScenarioIndex]);
        document.gameObject.SetActive(false);
        timeElapsedText.text = "0.00";

    }

    void Update()
    {
        if (endScreen)
            return;
        
        float timeElapsed = roundManager.TimeElapsed;
        string timeString = timeElapsed.ToString("F2");
        timeElapsedText.text = timeString;
    }

    public void ShowFinalScores(HeatableMetal[] finishedParts, PartDefinition[] partDefinitions, PartTableLayout partLayout, float elapsedSeconds)
    {
        endScreen = true;
        
        if (finalScoreDocument == null)
        {
            Debug.LogError("RoundUIHandler is missing its Final Score UIDocument reference.", this);
            return;
        }

        finalScoreDocument.gameObject.SetActive(true);
        VisualElement root = finalScoreDocument.rootVisualElement;

        Label timeLabel = root.Q<Label>("TimeTaken");
        Label forgingLabel = root.Q<Label>("ForgingScore");
        Label grindingLabel = root.Q<Label>("GrindingScore");
        VisualElement forgingList = root.Q<VisualElement>("ForgingPartScores");
        VisualElement grindingList = root.Q<VisualElement>("GrindingPartScores");
        rankImage = root.Q<Image>("RankImage");

        if (timeLabel == null || forgingLabel == null || grindingLabel == null || forgingList == null || grindingList == null)
        {
            Debug.LogError("FinalScoreVisualTree is missing one or more score elements.", this);
            return;
        }

        forgingList.Clear();
        grindingList.Clear();

        float forgingTotal = 0f;
        int forgingCount = 0;
        float grindingTotal = 0f;
        int grindingCount = 0;
        int finishedCount = finishedParts != null ? finishedParts.Length : 0;
        int definitionCount = partDefinitions != null ? partDefinitions.Length : 0;
        int partCount = Mathf.Max(finishedCount, definitionCount);

        for (int i = 0; i < partCount; i++)
        {
            HeatableMetal metal = i < finishedCount ? finishedParts[i] : null;
            PartDefinition definition = i < definitionCount ? partDefinitions[i] : null;
            if (definition == null && metal != null)
                definition = metal.PartDefinition;

            string partName = definition != null ? definition.DisplayLabel : $"Part {i + 1}";
            float forgingScore = metal != null ? Mathf.Clamp01(metal.ForgeMatchPercent) : 0f;

            forgingTotal += forgingScore;
            forgingCount++;
            AddScoreRow(forgingList, partName, FormatPercent(forgingScore));

            bool requiresGrinding = definition != null && definition.isBladed;
            if (requiresGrinding)
            {
                float grindingScore = metal != null ? Mathf.Clamp01(metal.GrindMatchPercent) : 0f;
                grindingTotal += grindingScore;
                grindingCount++;
                AddScoreRow(grindingList, partName, FormatPercent(grindingScore));
            }
            else
                AddScoreRow(grindingList, partName, "N/A");
        }

        float forgingAverage = forgingCount > 0 ? forgingTotal / forgingCount : 0f;
        float grindingAverage = grindingCount > 0 ? grindingTotal / grindingCount : 0f;

        timeLabel.text = $"Time Taken: {Mathf.Max(0f, elapsedSeconds):0.0}s";
        forgingLabel.text = $"Forging Score: {FormatPercent(forgingAverage)}";
        grindingLabel.text = grindingCount > 0 ? $"Grinding Score: {FormatPercent(grindingAverage)}" : "Grinding Score: N/A";

        FinalRank rank = partLayout.EvaluateRank(elapsedSeconds, forgingAverage, grindingAverage, grindingCount > 0);

        SetRankIcon(rank);
    }

    static void AddScoreRow(VisualElement container, string partName, string score)
    {
        var row = new VisualElement();
        row.AddToClassList("part-score-row");

        var nameLabel = new Label(partName);
        nameLabel.AddToClassList("part-score-name");

        var scoreLabel = new Label(score);
        scoreLabel.AddToClassList("part-score-value");

        row.Add(nameLabel);
        row.Add(scoreLabel);
        container.Add(row);
    }

    static string FormatPercent(float value)
    {
        return $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
    }

    void SetRankIcon(FinalRank rank)
    {
        int spriteIndex = (int)rank;
        if (rankImage == null)
        {
            Debug.LogError("FinalScoreVisualTree is missing the RankImage element.", this);
            return;
        }

        if (rankSprites == null || spriteIndex < 0 || spriteIndex >= rankSprites.Length || rankSprites[spriteIndex] == null)
        {
            Debug.LogError($"RoundUIHandler is missing the {rank} rank icon.", this);
            return;
        }

        rankImage.sprite = rankSprites[spriteIndex];
    }
}
