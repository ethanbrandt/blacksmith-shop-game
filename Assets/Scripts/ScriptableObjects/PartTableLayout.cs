using System;
using UnityEngine;

[Serializable]
public struct PartTableSlot
{
	public PartDefinition part;
	public Vector2 positionOffset;
}

[Serializable]
public struct PartToolSlot
{
	public Vector3 positionOffset;
	public Vector3 rotationOffset;
}

public enum FinalRank
{
	D,
	C,
	B,
	A,
	S
}

[Serializable]
public struct TimeRankBenchmarks
{
	[Min(0f)] public float c;
	[Min(0f)] public float b;
	[Min(0f)] public float a;
	[Min(0f)] public float s;

	public TimeRankBenchmarks(float c, float b, float a, float s)
	{
		this.c = c;
		this.b = b;
		this.a = a;
		this.s = s;
	}

	public FinalRank Evaluate(float elapsedSeconds)
	{
		elapsedSeconds = Mathf.Max(0f, elapsedSeconds);
		if (elapsedSeconds <= Mathf.Max(0f, s))
			return FinalRank.S;
		if (elapsedSeconds <= Mathf.Max(0f, a))
			return FinalRank.A;
		if (elapsedSeconds <= Mathf.Max(0f, b))
			return FinalRank.B;
		if (elapsedSeconds <= Mathf.Max(0f, c))
			return FinalRank.C;
		return FinalRank.D;
	}
}

[Serializable]
public struct ScoreRankBenchmarks
{
	[Range(0f, 1f)] public float c;
	[Range(0f, 1f)] public float b;
	[Range(0f, 1f)] public float a;
	[Range(0f, 1f)] public float s;

	public ScoreRankBenchmarks(float c, float b, float a, float s)
	{
		this.c = c;
		this.b = b;
		this.a = a;
		this.s = s;
	}

	public FinalRank Evaluate(float score)
	{
		score = Mathf.Clamp01(score);
		if (score >= Mathf.Clamp01(s))
			return FinalRank.S;
		if (score >= Mathf.Clamp01(a))
			return FinalRank.A;
		if (score >= Mathf.Clamp01(b))
			return FinalRank.B;
		if (score >= Mathf.Clamp01(c))
			return FinalRank.C;
		return FinalRank.D;
	}
}

[CreateAssetMenu(fileName = "PartTableLayout", menuName = "Forging/PartTableLayout", order = 3)]
public class PartTableLayout : ScriptableObject
{
	[SerializeField] PartTableSlot[] partTableSlots;
	[SerializeField] PartToolSlot[] partToolSlots;

	[Header("Final Rank Benchmarks")]
	[Tooltip("Maximum completion time in seconds for each rank. Lower is better.")]
	[SerializeField] TimeRankBenchmarks timeRankSeconds = new TimeRankBenchmarks(720f, 540f, 420f, 300f);
	[Tooltip("Minimum average forging score for each rank. Values range from 0 to 1.")]
	[SerializeField] ScoreRankBenchmarks forgingRankScores = new ScoreRankBenchmarks(0.70f, 0.80f, 0.925f, 0.95f);
	[Tooltip("Minimum average grinding score for each rank. Ignored when the layout has no bladed parts.")]
	[SerializeField] ScoreRankBenchmarks grindingRankScores = new ScoreRankBenchmarks(0.65f, 0.75f, 0.85f, 0.90f);

	public PartTableSlot[] GetPartTableSlots()
	{
		return partTableSlots;
	}

	public PartToolSlot[] GetPartToolSlots()
	{
		return partToolSlots;
	}

	public TimeRankBenchmarks TimeRanks => timeRankSeconds;
	public ScoreRankBenchmarks ForgingRanks => forgingRankScores;
	public ScoreRankBenchmarks GrindingRanks => grindingRankScores;

	// Normalize the original D..S rank average to 0..1. The half-rank boundaries
	// stay identical to the original rounding rule and also position the gauge.
	public static ScoreRankBenchmarks FinalScoreRanks => new ScoreRankBenchmarks(
		MinimumFinalScore(FinalRank.C), MinimumFinalScore(FinalRank.B),
		MinimumFinalScore(FinalRank.A), MinimumFinalScore(FinalRank.S));

	public static float MinimumFinalScore(FinalRank rank)
	{
		return rank == FinalRank.D ? 0f : ((int)rank - 0.5f) / (int)FinalRank.S;
	}

	public FinalRank EvaluateRank(float elapsedSeconds, float forgingAverage, float grindingAverage, bool hasGrindingScore)
	{
		return FinalScoreRanks.Evaluate(EvaluateScore(elapsedSeconds, forgingAverage, grindingAverage, hasGrindingScore));
	}

	public float EvaluateScore(float elapsedSeconds, float forgingAverage, float grindingAverage, bool hasGrindingScore)
	{
		int rankTotal = (int)timeRankSeconds.Evaluate(elapsedSeconds) + (int)forgingRankScores.Evaluate(forgingAverage);
		int rankCount = 2;

		if (hasGrindingScore)
		{
			rankTotal += (int)grindingRankScores.Evaluate(grindingAverage);
			rankCount++;
		}

		return (float)rankTotal / (rankCount * (int)FinalRank.S);
	}
}
