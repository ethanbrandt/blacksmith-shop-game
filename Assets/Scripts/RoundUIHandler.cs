using ForgingPrototype;
using UnityEngine;
using UnityEngine.UIElements;

public class RoundUIHandler : MonoBehaviour
{
    [SerializeField] Sprite[] rankSprites;
    [SerializeField] UIDocument document;
    [SerializeField] UIDocument finalScoreDocument;
    
    RoundManager roundManager;
    Button button;
    Image rankImage;
    
    void Start()
    {
        roundManager = GetComponent<RoundManager>();

        if (finalScoreDocument != null)
            finalScoreDocument.gameObject.SetActive(false);
        
        document.gameObject.SetActive(true);
        button = document.rootVisualElement.Q<Button>("StartGameButton");
        button.clicked += OnStartGame;

        document.rootVisualElement.schedule.Execute(() => button.Focus());
        
        rankImage = document.rootVisualElement.Q<Image>("RankImage");
    }

    void OnDisable()
    {
        if (button != null)
            button.clicked -= OnStartGame;
    }

    void OnStartGame()
    {
        print("CLICKED START");
        roundManager.BeginRound();
        document.gameObject.SetActive(false);
    }

    public void ShowFinalScores(HeatableMetal[] finishedParts, PartDefinition[] partDefinitions, float elapsedSeconds)
    {
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

        if (forgingAverage < 0.5f)
            rankImage.sprite = rankSprites[0];
        else if (forgingAverage < 0.65)
            rankImage.sprite = rankSprites[1];
        else if (forgingAverage < 0.8)
            rankImage.sprite = rankSprites[2];
        else if (forgingAverage < 0.925)
            rankImage.sprite = rankSprites[3];
        else
            rankImage.sprite = rankSprites[4];
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
}
