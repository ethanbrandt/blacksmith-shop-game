using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps forged silhouette edges to PartDefinition outline edges marked for sharpening
/// and scores grind on those edges.
/// </summary>
public class EdgeGrindEvaluator : MonoBehaviour
{
	const float MinimumGrindRange = 0.05f;
	const float DefaultOvergrindRange = 0.75f;
	const float NearlyEqualGrindTolerance = 0.000001f;
	const float MinimumSquaredSegmentLength = 0.000001f;
	const float SoftPenaltyMultiplier = 0.75f;

	[Header("Score Weights")]
	[Range(0f, 1f)]
	[SerializeField] float wastePenaltyWeight = 0.4f;
	[Range(0f, 1f)]
	[SerializeField] float overgrindPenaltyWeight = 0.45f;

	[Header("Overgrind Scoring")]
	[Tooltip("Grind amount that counts as fully sharp. Past this is overgrind for scoring.")]
	[SerializeField] float idealGrindAmount = 1f;
	[Tooltip("Average overgrind at which quality collapses to Blunt.")]
	[Range(0f, 1f)]
	[SerializeField] float overgrindBluntThreshold = 0.55f;
	[Tooltip("Average overgrind that starts softening the match score.")]
	[Range(0f, 1f)]
	[SerializeField] float overgrindSoftPenaltyThreshold = 0.35f;

	[Header("Thresholds")]
	[Range(0f, 1f)]
	[SerializeField] float keenThreshold = 0.88f;
	[Range(0f, 1f)]
	[SerializeField] float honedThreshold = 0.74f;
	[Range(0f, 1f)]
	[SerializeField] float fineThreshold = 0.55f;
	[Range(0f, 1f)]
	[SerializeField] float dullThreshold = 0.28f;
	
	GrindBladeBody blade;
	Vector2[] outlineLocal = System.Array.Empty<Vector2>();
	bool[] outlineEdgeNeedsSharpening = System.Array.Empty<bool>();
	readonly List<bool> silhouetteEdgeNeedsSharpening = new List<bool>();
	
	int mappedTopologyVersion = -1;
	public float MatchPercent { get; private set; }
	public float EdgeCoverage { get; private set; }
	public float EdgeAccuracy { get; private set; }
	public float WastePercent { get; private set; }
	public float OvergrindPercent { get; private set; }
	public SharpnessQuality Quality { get; private set; } = SharpnessQuality.Blunt;
	public IReadOnlyList<bool> SilhouetteEdgeNeedsSharpening => silhouetteEdgeNeedsSharpening;

	public void Configure(GrindBladeBody source, Vector2[] outline, bool[] edgeSharpenFlags)
	{
		blade = source;
		outlineLocal = outline != null ? (Vector2[])outline.Clone() : System.Array.Empty<Vector2>();
		outlineEdgeNeedsSharpening = edgeSharpenFlags != null ? (bool[])edgeSharpenFlags.Clone() : System.Array.Empty<bool>();
		RemapSilhouette();
		Evaluate();
	}

	public bool EdgeNeedsSharpening(int silhouetteEdgeIndex)
	{
		return silhouetteEdgeIndex >= 0 && silhouetteEdgeIndex < silhouetteEdgeNeedsSharpening.Count && silhouetteEdgeNeedsSharpening[silhouetteEdgeIndex];
	}

	void RemapSilhouette()
	{
		mappedTopologyVersion = blade != null ? blade.TopologyVersion : -1;
		silhouetteEdgeNeedsSharpening.Clear();
		
		bool hasBladeOutline = blade != null && blade.VertexCount >= PolygonGeometry.MinimumVertexCount;
		bool hasTargetOutline = outlineLocal != null && outlineLocal.Length >= PolygonGeometry.MinimumVertexCount;
		
		if (!hasBladeOutline || !hasTargetOutline)
			return;
		var silhouetteVertices = blade.LocalVertices;
		int vertexCount = silhouetteVertices.Count;
		
		for (int i = 0; i < vertexCount; i++)
		{
			int nextVertexIndex = (i + 1) % vertexCount;
			int nearestEdge = FindNearestOutlineEdge(silhouetteVertices[i], silhouetteVertices[nextVertexIndex]);
			bool needsSharpening = nearestEdge >= 0 && nearestEdge < outlineEdgeNeedsSharpening.Length && outlineEdgeNeedsSharpening[nearestEdge];
			silhouetteEdgeNeedsSharpening.Add(needsSharpening);
		}
	}

	int FindNearestOutlineEdge(Vector2 edgeStart, Vector2 edgeEnd)
	{
		Vector2 midpoint = (edgeStart + edgeEnd) * 0.5f;
		int nearestEdgeIndex = 0;
		float nearestDistance = float.MaxValue;
		for (int i = 0; i < outlineLocal.Length; i++)
		{
			int next = (i + 1) % outlineLocal.Length;
			Vector2 targetEdgeStart = outlineLocal[i];
			Vector2 targetEdgeEnd = outlineLocal[next];
			float distance = DistancePointToSegment(midpoint, targetEdgeStart, targetEdgeEnd);
			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				nearestEdgeIndex = i;
			}
		}

		return nearestEdgeIndex;
	}

	public void Evaluate()
	{
		if (blade != null && mappedTopologyVersion != blade.TopologyVersion)
			RemapSilhouette();
		bool hasBladeOutline = blade != null && blade.VertexCount >= PolygonGeometry.MinimumVertexCount;
		bool hasMatchingEdgeMap = hasBladeOutline && silhouetteEdgeNeedsSharpening.Count == blade.VertexCount;
		if (!hasMatchingEdgeMap)
		{
			MatchPercent = 0f;
			EdgeCoverage = 0f;
			EdgeAccuracy = 0f;
			WastePercent = 0f;
			OvergrindPercent = 0f;
			Quality = SharpnessQuality.Blunt;
			return;
		}

		float weightedCoverage = 0f;
		float weightedOvergrind = 0f;
		float sharpeningEdgeLength = 0f;
		float weightedWaste = 0f;
		float nonSharpeningEdgeLength = 0f;
		float idealAmount = Mathf.Max(MinimumGrindRange, blade != null ? blade.IdealGrindAmount : idealGrindAmount);
		float overgrindRange = Mathf.Max(MinimumGrindRange, (blade != null ? blade.MaxGrindAmount : idealAmount + DefaultOvergrindRange) - idealAmount);
		int vertexCount = blade.VertexCount;
		for (int i = 0; i < vertexCount; i++)
		{
			int nextVertexIndex = (i + 1) % vertexCount;
			float edgeLength = Vector2.Distance(blade.LocalVertices[i], blade.LocalVertices[nextVertexIndex]);
			float startGrind = blade.GrindAmounts[i], endGrind = blade.GrindAmounts[nextVertexIndex];
			if (silhouetteEdgeNeedsSharpening[i])
			{
				sharpeningEdgeLength += edgeLength;
				weightedCoverage += edgeLength * ClampedLinearAverage(startGrind / idealAmount, endGrind / idealAmount);
				weightedOvergrind += edgeLength * ClampedLinearAverage((startGrind - idealAmount) / overgrindRange, (endGrind - idealAmount) / overgrindRange);
			}
			else
			{
				nonSharpeningEdgeLength += edgeLength;
				weightedWaste += edgeLength * ClampedLinearAverage(startGrind / idealAmount, endGrind / idealAmount);
			}
		}

		float edgeCoverage = sharpeningEdgeLength > 0f ? weightedCoverage / sharpeningEdgeLength : 0f;
		float overgrind = sharpeningEdgeLength > 0f ? weightedOvergrind / sharpeningEdgeLength : 0f;
		float wastePercent = nonSharpeningEdgeLength > 0f ? weightedWaste / nonSharpeningEdgeLength : 0f;
		EdgeCoverage = edgeCoverage;
		EdgeAccuracy = edgeCoverage;
		WastePercent = Mathf.Clamp01(wastePercent);
		OvergrindPercent = overgrind;

		float score = edgeCoverage;
		score *= 1f - WastePercent * wastePenaltyWeight;
		score *= 1f - overgrind * overgrindPenaltyWeight;
		MatchPercent = Mathf.Clamp01(score);
		Quality = ResolveQuality(MatchPercent, overgrind);
	}

	public static float ClampedLinearAverage(float startAmount, float endAmount)
	{
		float amountDifference = endAmount - startAmount;
		bool hasNearlyConstantAmount = Mathf.Abs(amountDifference) < NearlyEqualGrindTolerance;
		if (hasNearlyConstantAmount)
			return Mathf.Clamp01(startAmount);
		return (ClampIntegral(endAmount) - ClampIntegral(startAmount)) / amountDifference;
	}

	static float ClampIntegral(float amount)
	{
		if (amount <= 0f)
			return 0f;
		if (amount < 1f)
			return amount * amount * 0.5f;
		return amount - 0.5f;
	}

	SharpnessQuality ResolveQuality(float match, float overgrind)
	{
		if (overgrind > overgrindBluntThreshold)
			return SharpnessQuality.Blunt;
		if (overgrind > overgrindSoftPenaltyThreshold && match < keenThreshold)
			match *= SoftPenaltyMultiplier;
		if (match >= keenThreshold)
			return SharpnessQuality.Keen;
		if (match >= honedThreshold)
			return SharpnessQuality.Honed;
		if (match >= fineThreshold)
			return SharpnessQuality.Fine;
		if (match >= dullThreshold)
			return SharpnessQuality.Dull;
		return SharpnessQuality.Blunt;
	}

	static float DistancePointToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		Vector2 segment = segmentEnd - segmentStart;
		float squaredSegmentLength = Vector2.Dot(segment, segment);
		if (squaredSegmentLength < MinimumSquaredSegmentLength)
			return Vector2.Distance(point, segmentStart);
		float projectionFraction = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / squaredSegmentLength);
		return Vector2.Distance(point, segmentStart + segment * projectionFraction);
	}
}
