using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

public class RoundUIHandler : MonoBehaviour
{
    [SerializeField] UIDocument finalScoreDocument;
    [SerializeField] TextMeshProUGUI timeElapsedText;

    [Header("Final Score Reveal")]
    [Tooltip("Total reveal time for a perfect score, including rank pauses. Lower scores finish sooner.")]
    [SerializeField, Min(1.4f)] float fullRevealDuration = 4.6f;
    [SerializeField, Min(0f)] float initialHold = 0.35f;
    [SerializeField, Min(0f)] float rankPause = 0.24f;
    [SerializeField, Min(0f)] float extraPausePerRank = 0.10f;

    [Header("Rank Letter Impact")]
    [SerializeField, Min(0.05f)] float rankImpactDuration = 0.28f;
    [SerializeField, Range(1f, 1.5f)] float rankImpactScale = 1.22f;
    [SerializeField, Range(0f, 15f)] float rankImpactAngle = 7f;

    RoundManager roundManager;
    Coroutine revealRoutine;
    Coroutine rankImpactRoutine;
    VisualElement scoreScreen;
    VisualElement unrevealedBar;
    VisualElement qualityMarker;
    VisualElement rankDisplay;
    VisualElement breakdown;
    Label qualityPercent;
    Label rankLetter;
    Label timeLabel;
    Label forgingLabel;
    Label grindingLabel;
    Label scoringNote;
    VisualElement forgingList;
    VisualElement grindingList;
    readonly VisualElement[] rankBands = new VisualElement[5];
    readonly Label[] rankLabels = new Label[5];
    readonly Label[] thresholdLabels = new Label[5];
    bool endScreen;

    void Start()
    {
        roundManager = GetComponent<RoundManager>();
        if (!endScreen && finalScoreDocument != null)
            finalScoreDocument.gameObject.SetActive(false);
        if (timeElapsedText != null)
            timeElapsedText.text = "0.00";
    }

    void Update()
    {
        if (!endScreen && roundManager != null && timeElapsedText != null)
            timeElapsedText.text = roundManager.TimeElapsed.ToString("F2");
    }

    void OnDisable()
    {
        StopReveal();
    }

    public void ResetFinalScores()
    {
        StopReveal();
        endScreen = false;
        if (finalScoreDocument != null)
            finalScoreDocument.gameObject.SetActive(false);
    }

    void StopReveal()
    {
        if (revealRoutine != null)
            StopCoroutine(revealRoutine);
        revealRoutine = null;
        StopRankImpact();
    }

    public void ShowFinalScores(HeatableMetal[] finishedParts, PartDefinition[] partDefinitions, PartTableLayout partLayout, float elapsedSeconds)
    {
        if (endScreen)
            return;
        if (finalScoreDocument == null || partLayout == null)
        {
            Debug.LogError("Final scores require a UIDocument and a PartTableLayout.", this);
            return;
        }

        finalScoreDocument.gameObject.SetActive(true);
        if (!BindScoreElements(finalScoreDocument.rootVisualElement))
            return;

        Label finishTitle = finalScoreDocument.rootVisualElement.Q<Label>(className: "finish-title");
        CustomerScenario scenario = GameManager.Instance != null ? GameManager.Instance.GetCurrentScenario() : null;
        string pieceTitle = scenario != null ? scenario.PieceTitle : null;
        if (finishTitle != null)
        {
            string title = string.IsNullOrWhiteSpace(pieceTitle) ? "PIECE" : pieceTitle.Trim().ToUpperInvariant();
            finishTitle.text = $"{title} FINISHED!";
        }

        endScreen = true;
        if (timeElapsedText != null)
            timeElapsedText.text = Mathf.Max(0f, elapsedSeconds).ToString("F2");

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
            float forgingScore = metal != null ? Mathf.Clamp01(metal.GetMatchPercent()) : 0f;
            forgingTotal += forgingScore;
            forgingCount++;
            AddScoreRow(forgingList, partName, FormatPercent(forgingScore));

            if (definition != null && definition.isBladed)
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
        bool hasGrinding = grindingCount > 0;
        timeLabel.text = $"Time: {FormatTime(elapsedSeconds)}   {partLayout.TimeRanks.Evaluate(elapsedSeconds)}";
        forgingLabel.text = $"Forging: {FormatPercent(forgingAverage)}   {partLayout.ForgingRanks.Evaluate(forgingAverage)}";
        grindingLabel.text = hasGrinding
            ? $"Grinding: {FormatPercent(grindingAverage)}   {partLayout.GrindingRanks.Evaluate(grindingAverage)}"
            : "Grinding: N/A";
        if (scoringNote != null)
            scoringNote.text = hasGrinding
                ? "Time, forging and grinding ranks count equally."
                : "Time and forging ranks count equally. Grinding is not required.";
        if (forgingCount == 0)
            AddScoreRow(forgingList, "No completed parts", "0.0%");

        float finalScore = partLayout.EvaluateScore(elapsedSeconds, forgingAverage, grindingAverage, hasGrinding);
        FinalRank finalRank = partLayout.EvaluateRank(elapsedSeconds, forgingAverage, grindingAverage, hasGrinding);

        ConfigureGauge();
        scoreScreen.EnableInClassList("is-complete", false);
        breakdown.AddToClassList("is-hidden");
        SetDisplayedScore(0f);
        SetRankTransform(1f, 0f);
        SetRankAppearance(FinalRank.D, false);
        revealRoutine = StartCoroutine(RevealScore(finalScore, finalRank));
    }

    bool BindScoreElements(VisualElement root)
    {
        scoreScreen = root.Q("FinalScoreScreen");
        unrevealedBar = root.Q("QualityUnrevealed");
        qualityMarker = root.Q("QualityMarker");
        rankDisplay = root.Q("RankDisplay");
        breakdown = root.Q("ScoreBreakdown");
        qualityPercent = root.Q<Label>("QualityPercent");
        rankLetter = root.Q<Label>("RankLetter");
        timeLabel = root.Q<Label>("TimeTaken");
        forgingLabel = root.Q<Label>("ForgingScore");
        grindingLabel = root.Q<Label>("GrindingScore");
        scoringNote = root.Q<Label>("ScoringNote");
        forgingList = root.Q("ForgingPartScores");
        grindingList = root.Q("GrindingPartScores");
        bool valid = scoreScreen != null && unrevealedBar != null && qualityMarker != null
            && rankDisplay != null && breakdown != null && qualityPercent != null
            && rankLetter != null
            && timeLabel != null && forgingLabel != null && grindingLabel != null
            && forgingList != null && grindingList != null;
        for (int i = 0; i < rankBands.Length; i++)
        {
            string rank = ((FinalRank)i).ToString();
            rankBands[i] = root.Q(rank + "RankBand");
            rankLabels[i] = root.Q<Label>(rank + "RankLabel");
            thresholdLabels[i] = root.Q<Label>(rank + "Threshold");
            valid &= rankBands[i] != null && rankLabels[i] != null && thresholdLabels[i] != null;
        }
        if (!valid)
            Debug.LogError("FinalScoreVisualTree is missing one or more score or gauge elements.", this);
        return valid;
    }

    void ConfigureGauge()
    {
        for (int i = 0; i < rankBands.Length; i++)
        {
            float lower = PartTableLayout.MinimumFinalScore((FinalRank)i);
            float upper = i == (int)FinalRank.S ? 1f : PartTableLayout.MinimumFinalScore((FinalRank)(i + 1));
            rankBands[i].style.bottom = Length.Percent(lower * 100f);
            rankBands[i].style.height = Length.Percent((upper - lower) * 100f);
            rankLabels[i].style.bottom = Length.Percent(lower * 100f);
            rankLabels[i].style.height = Length.Percent((upper - lower) * 100f);
            thresholdLabels[i].style.bottom = Length.Percent(lower * 100f);
            thresholdLabels[i].text = lower == 0f ? "0%" : FormatPercent(lower);
            rankLabels[i].RemoveFromClassList("rank-reached");
        }
    }

    IEnumerator RevealScore(float finalScore, FinalRank finalRank)
    {
        var stops = new List<float>();
        float pauseTotal = 0f;
        for (int i = (int)FinalRank.C; i <= (int)FinalRank.S; i++)
        {
            float threshold = PartTableLayout.MinimumFinalScore((FinalRank)i);
            if (threshold > finalScore)
                break;
            stops.Add(threshold);
            if (threshold < finalScore)
                pauseTotal += PauseForRank((FinalRank)i);
        }
        if (stops.Count == 0 || stops[stops.Count - 1] < finalScore)
            stops.Add(finalScore);

        float duration = Mathf.Lerp(1.4f, Mathf.Max(1.4f, fullRevealDuration), finalScore);
        float lead = Mathf.Min(initialHold, duration * 0.15f);
        float settle = 0.22f;
        float pauseScale = pauseTotal > 0f ? Mathf.Min(1f, (duration - lead - settle) * 0.6f / pauseTotal) : 1f;
        float movementBudget = Mathf.Max(0.1f, duration - lead - settle - pauseTotal * pauseScale);
        yield return HoldUnscaled(lead);

        float current = 0f;
        foreach (float stop in stops)
        {
            float segmentDuration = finalScore > 0f ? movementBudget * (stop - current) / finalScore : 0f;
            float elapsed = 0f;
            while (elapsed < segmentDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / segmentDuration);
                // Slow into each boundary and the final score; never overshoot an unearned rank.
                SetDisplayedScore(Mathf.Lerp(current, stop, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }
            SetDisplayedScore(stop);
            current = stop;
            FinalRank reached = PartTableLayout.FinalScoreRanks.Evaluate(stop);
            SetRankAppearance(reached);
            if (stop < finalScore)
            {
                yield return HoldUnscaled(PauseForRank(reached) * pauseScale);
            }
        }

        yield return HoldUnscaled(settle);
        SetDisplayedScore(finalScore);
        SetRankAppearance(finalRank);
        breakdown.RemoveFromClassList("is-hidden");
        scoreScreen.AddToClassList("is-complete");
        revealRoutine = null;
    }

    float PauseForRank(FinalRank rank)
    {
        return Mathf.Max(0f, rankPause) + Mathf.Max(0f, extraPausePerRank) * Mathf.Max(0, (int)rank - 1);
    }

    static IEnumerator HoldUnscaled(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    void SetDisplayedScore(float score)
    {
        score = Mathf.Clamp01(score);
        unrevealedBar.style.height = Length.Percent((1f - score) * 100f);
        qualityMarker.style.bottom = Length.Percent(score * 100f);
        qualityPercent.text = FormatPercent(score);
    }

    void SetRankAppearance(FinalRank rank, bool animate = true)
    {
        bool changed = rankLetter.text != rank.ToString();
        rankLetter.text = rank.ToString();
        for (int i = 0; i < rankLabels.Length; i++)
        {
            rankLabels[i].EnableInClassList("rank-reached", i <= (int)rank);
            rankDisplay.EnableInClassList("rank-" + ((FinalRank)i).ToString().ToLowerInvariant(), i == (int)rank);
        }
        if (animate && changed)
        {
            StopRankImpact();
            float direction = (int)rank % 2 == 0 ? -1f : 1f;
            rankImpactRoutine = StartCoroutine(PlayRankImpact(direction));
        }
    }

    void StopRankImpact()
    {
        if (rankImpactRoutine != null)
            StopCoroutine(rankImpactRoutine);
        rankImpactRoutine = null;
        SetRankTransform(1f, 0f);
    }

    IEnumerator PlayRankImpact(float direction)
    {
        float duration = Mathf.Max(0.05f, rankImpactDuration);
        float elapsed = 0f;
        const float attackFraction = 0.28f;
        while (elapsed < duration && rankLetter != null)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            // A quick kick outward, then a longer eased return to the resting pose.
            float impact = t < attackFraction
                ? Mathf.SmoothStep(0f, 1f, t / attackFraction)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - attackFraction) / (1f - attackFraction));
            float scale = Mathf.Lerp(1f, Mathf.Clamp(rankImpactScale, 1f, 1.5f), impact);
            float angle = Mathf.Clamp(rankImpactAngle, 0f, 15f) * direction * impact;
            SetRankTransform(scale, angle);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        SetRankTransform(1f, 0f);
        rankImpactRoutine = null;
    }

    void SetRankTransform(float scale, float angle)
    {
        if (rankLetter == null)
            return;
        rankLetter.style.scale = new Scale(new Vector3(scale, scale, 1f));
        rankLetter.style.rotate = new Rotate(Angle.Degrees(angle));
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
        return $"{Mathf.Clamp01(value) * 100f:0.0}%";
    }

    static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        return $"{Mathf.FloorToInt(seconds / 60f):00}:{seconds % 60f:00.0}";
    }
}
