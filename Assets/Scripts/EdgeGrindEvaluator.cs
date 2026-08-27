using System.Collections.Generic;
using UnityEngine;

namespace ForgingPrototype
{
	/// <summary>
	/// Maps forged silhouette edges to PartDefinition outline edges marked for sharpening
	/// and scores grind on those edges.
	/// </summary>
	public class EdgeGrindEvaluator : MonoBehaviour
	{
		[Header("Score Weights")]
		[SerializeField, Range(0f, 1f)] float wastePenaltyWeight = 0.4f;
		[SerializeField, Range(0f, 1f)] float overgrindPenaltyWeight = 0.45f;

		[Header("Overgrind Scoring")]
		[Tooltip("Grind amount that counts as fully sharp. Past this is overgrind for scoring.")]
		[SerializeField] float idealGrindAmount = 1f;
		[Tooltip("Average overgrind at which quality collapses to Blunt.")]
		[SerializeField, Range(0f, 1f)] float overgrindBluntThreshold = 0.55f;
		[Tooltip("Average overgrind that starts softening the match score.")]
		[SerializeField, Range(0f, 1f)] float overgrindSoftPenaltyThreshold = 0.35f;

		[Header("Thresholds")]
		[SerializeField, Range(0f, 1f)] float keenThreshold = 0.88f;
		[SerializeField, Range(0f, 1f)] float honedThreshold = 0.74f;
		[SerializeField, Range(0f, 1f)] float fineThreshold = 0.55f;
		[SerializeField, Range(0f, 1f)] float dullThreshold = 0.28f;

		GrindBladeBody blade;
		Vector2[] outlineLocal = System.Array.Empty<Vector2>();
		bool[] outlineEdgeNeedsSharpening = System.Array.Empty<bool>();
		readonly List<bool> silhouetteEdgeNeedsSharpening = new List<bool>();

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
			outlineLocal = outline != null ? outline : System.Array.Empty<Vector2>();
			outlineEdgeNeedsSharpening = edgeSharpenFlags != null ? edgeSharpenFlags : System.Array.Empty<bool>();
			RemapSilhouette();
			Evaluate();
		}

		public bool EdgeNeedsSharpening(int silhouetteEdgeIndex)
		{
			return silhouetteEdgeIndex >= 0 && silhouetteEdgeIndex < silhouetteEdgeNeedsSharpening.Count && silhouetteEdgeNeedsSharpening[silhouetteEdgeIndex];
		}

		void LateUpdate()
		{
			if (blade == null)
				return;

			RemapSilhouette();
			Evaluate();
		}

		void RemapSilhouette()
		{
			silhouetteEdgeNeedsSharpening.Clear();
			if (blade == null || blade.VertexCount < 3 || outlineLocal == null || outlineLocal.Length < 3)
				return;

			var local = blade.LocalVertices;
			int n = local.Count;
			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				int nearestEdge = FindNearestOutlineEdge(local[i], local[j]);
				bool needs = nearestEdge >= 0
					&& nearestEdge < outlineEdgeNeedsSharpening.Length
					&& outlineEdgeNeedsSharpening[nearestEdge];
				silhouetteEdgeNeedsSharpening.Add(needs);
			}
		}

		int FindNearestOutlineEdge(Vector2 edgeStart, Vector2 edgeEnd)
		{
			Vector2 mid = (edgeStart + edgeEnd) * 0.5f;
			int best = 0;
			float bestDist = float.MaxValue;
			for (int i = 0; i < outlineLocal.Length; i++)
			{
				int next = (i + 1) % outlineLocal.Length;
				Vector2 a = outlineLocal[i];
				Vector2 b = outlineLocal[next];
				float d = DistancePointToSegment(mid, a, b);
				if (d < bestDist)
				{
					bestDist = d;
					best = i;
				}
			}

			return best;
		}

		public void Evaluate()
		{
			if (blade == null || blade.VertexCount < 3 || silhouetteEdgeNeedsSharpening.Count != blade.VertexCount)
			{
				MatchPercent = 0f;
				EdgeCoverage = 0f;
				EdgeAccuracy = 0f;
				WastePercent = 0f;
				OvergrindPercent = 0f;
				Quality = SharpnessQuality.Blunt;
				return;
			}

			float coverageSum = 0f;
			float overSum = 0f;
			int sharpenCount = 0;
			float wasteSum = 0f;
			int wasteCount = 0;

			float ideal = Mathf.Max(0.05f, blade != null ? blade.IdealGrindAmount : idealGrindAmount);
			float overRange = Mathf.Max(0.05f, (blade != null ? blade.MaxGrindAmount : ideal + 0.75f) - ideal);
			int n = blade.VertexCount;

			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				float grind = (blade.GrindAmounts[i] + blade.GrindAmounts[j]) * 0.5f;

				if (silhouetteEdgeNeedsSharpening[i])
				{
					sharpenCount++;
					coverageSum += Mathf.Clamp01(grind / ideal);
					if (grind > ideal)
						overSum += Mathf.Clamp01((grind - ideal) / overRange);
				}
				else if (!IsSharpenTransitionEdge(i, n) && TryGetWasteGrind(i, n, out float wasteGrind) && wasteGrind > 0.001f)
				{
					wasteCount++;
					wasteSum += Mathf.Clamp01(wasteGrind / ideal);
				}
			}

			float edgeCoverage = sharpenCount > 0 ? coverageSum / sharpenCount : 0f;
			float overgrind = sharpenCount > 0 ? overSum / sharpenCount : 0f;
			float wastePercent = wasteCount > 0 ? wasteSum / wasteCount : 0f;

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

		bool VertexTouchesSharpenEdge(int vertexIndex, int vertexCount)
		{
			int prevEdge = (vertexIndex - 1 + vertexCount) % vertexCount;
			return silhouetteEdgeNeedsSharpening[prevEdge] || silhouetteEdgeNeedsSharpening[vertexIndex];
		}

		bool IsSharpenTransitionEdge(int edgeIndex, int edgeCount)
		{
			if (silhouetteEdgeNeedsSharpening[edgeIndex])
				return false;

			int nextVertex = (edgeIndex + 1) % edgeCount;
			return VertexTouchesSharpenEdge(edgeIndex, edgeCount)
				|| VertexTouchesSharpenEdge(nextVertex, edgeCount);
		}

		bool TryGetWasteGrind(int edgeIndex, int edgeCount, out float wasteGrind)
		{
			int nextVertex = (edgeIndex + 1) % edgeCount;
			float start = VertexTouchesSharpenEdge(edgeIndex, edgeCount) ? 0f : blade.GrindAmounts[edgeIndex];
			float end = VertexTouchesSharpenEdge(nextVertex, edgeCount) ? 0f : blade.GrindAmounts[nextVertex];
			wasteGrind = (start + end) * 0.5f;
			return true;
		}

		SharpnessQuality ResolveQuality(float match, float overgrind)
		{
			if (overgrind > overgrindBluntThreshold)
				return SharpnessQuality.Blunt;
			if (overgrind > overgrindSoftPenaltyThreshold && match < keenThreshold)
				match *= 0.75f;

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

		static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float denom = Vector2.Dot(ab, ab);
			if (denom < 0.000001f)
				return Vector2.Distance(p, a);

			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
			return Vector2.Distance(p, a + ab * t);
		}
	}
}
