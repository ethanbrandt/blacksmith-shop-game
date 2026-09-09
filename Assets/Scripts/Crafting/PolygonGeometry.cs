using System.Collections.Generic;
using UnityEngine;

public static class PolygonGeometry
{
	public const int MinimumVertexCount = 3;
	const float IntersectionTolerance = 0.000001f;
	const float SquaredDistanceTolerance = IntersectionTolerance * IntersectionTolerance;

	public static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;

	public static Vector2 ClosestOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		Vector2 segment = segmentEnd - segmentStart;
		bool isDegenerate = segment.sqrMagnitude <= SquaredDistanceTolerance;
		if (isDegenerate)
			return segmentStart;

		float projectionFraction = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / segment.sqrMagnitude);
		return segmentStart + segment * projectionFraction;
	}

	public static bool Contains(Vector2 point, IReadOnlyList<Vector2> polygon)
	{
		if (polygon == null || polygon.Count < MinimumVertexCount)
			return false;

		bool isInside = false;
		int previousIndex = polygon.Count - 1;
		for (int vertexIndex = 0; vertexIndex < polygon.Count; vertexIndex++)
		{
			Vector2 edgeStart = polygon[vertexIndex];
			Vector2 edgeEnd = polygon[previousIndex];
			if (IsOnSegment(point, edgeStart, edgeEnd))
				return true;

			bool crossesPointHeight = (edgeStart.y > point.y) != (edgeEnd.y > point.y);
			if (crossesPointHeight)
			{
				float intersectionX = (edgeEnd.x - edgeStart.x) * (point.y - edgeStart.y) / (edgeEnd.y - edgeStart.y) + edgeStart.x;
				bool crossesRightwardRay = point.x < intersectionX;
				if (crossesRightwardRay)
					isInside = !isInside;
			}

			previousIndex = vertexIndex;
		}

		return isInside;
	}

	public static float SignedArea(IReadOnlyList<Vector2> polygon)
	{
		if (polygon == null || polygon.Count < MinimumVertexCount)
			return 0f;

		Vector2 origin = polygon[0];
		float twiceArea = 0f;
		for (int vertexIndex = 1; vertexIndex < polygon.Count - 1; vertexIndex++)
			twiceArea += Cross(polygon[vertexIndex] - origin, polygon[vertexIndex + 1] - origin);
		return twiceArea * 0.5f;
	}

	public static bool IsSimple(IReadOnlyList<Vector2> polygon)
	{
		if (polygon == null || polygon.Count < MinimumVertexCount)
			return false;

		bool hasNegligibleArea = Mathf.Abs(SignedArea(polygon)) <= IntersectionTolerance;
		if (hasNegligibleArea)
			return false;

		for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
		{
			Vector2 edgeStart = polygon[edgeIndex];
			Vector2 edgeEnd = polygon[(edgeIndex + 1) % polygon.Count];
			bool hasFiniteCoordinates = IsFinite(edgeStart.x) && IsFinite(edgeStart.y);
			bool isDegenerateEdge = (edgeStart - edgeEnd).sqrMagnitude <= SquaredDistanceTolerance;
			if (!hasFiniteCoordinates || isDegenerateEdge)
				return false;

			for (int otherEdgeIndex = edgeIndex + 1; otherEdgeIndex < polygon.Count; otherEdgeIndex++)
			{
				bool isNextEdge = otherEdgeIndex == edgeIndex + 1;
				bool sharesClosingVertex = edgeIndex == 0 && otherEdgeIndex == polygon.Count - 1;
				if (isNextEdge || sharesClosingVertex)
					continue;

				Vector2 otherStart = polygon[otherEdgeIndex];
				Vector2 otherEnd = polygon[(otherEdgeIndex + 1) % polygon.Count];
				if (SegmentsIntersect(edgeStart, edgeEnd, otherStart, otherEnd))
					return false;
			}
		}

		return true;
	}

	static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

	static bool IsOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		Vector2 closestPoint = ClosestOnSegment(point, segmentStart, segmentEnd);
		return (closestPoint - point).sqrMagnitude <= SquaredDistanceTolerance;
	}

	static bool HaveOppositeSigns(float first, float second)
	{
		bool positiveThenNegative = first > IntersectionTolerance && second < -IntersectionTolerance;
		bool negativeThenPositive = first < -IntersectionTolerance && second > IntersectionTolerance;
		return positiveThenNegative || negativeThenPositive;
	}

	static bool SegmentsIntersect(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
	{
		Vector2 firstSegment = firstEnd - firstStart;
		Vector2 secondSegment = secondEnd - secondStart;
		float secondStartSide = Cross(firstSegment, secondStart - firstStart);
		float secondEndSide = Cross(firstSegment, secondEnd - firstStart);
		float firstStartSide = Cross(secondSegment, firstStart - secondStart);
		float firstEndSide = Cross(secondSegment, firstEnd - secondStart);
		bool straddlesFirstSegment = HaveOppositeSigns(secondStartSide, secondEndSide);
		bool straddlesSecondSegment = HaveOppositeSigns(firstStartSide, firstEndSide);
		if (straddlesFirstSegment && straddlesSecondSegment)
			return true;

		bool secondTouchesFirst = IsOnSegment(secondStart, firstStart, firstEnd) || IsOnSegment(secondEnd, firstStart, firstEnd);
		bool firstTouchesSecond = IsOnSegment(firstStart, secondStart, secondEnd) || IsOnSegment(firstEnd, secondStart, secondEnd);
		return secondTouchesFirst || firstTouchesSecond;
	}

	/// <summary>Ear clipping with original vertex indices; accepts either winding and collinear perimeter samples.</summary>
	public static bool Triangulate(IReadOnlyList<Vector2> polygon, List<int> triangles, List<int> remainingIndices)
	{
		triangles.Clear();
		remainingIndices.Clear();
		if (!IsSimple(polygon))
			return false;

		bool isCounterClockwise = SignedArea(polygon) > 0f;
		float windingSign = isCounterClockwise ? 1f : -1f;
		for (int vertexIndex = 0; vertexIndex < polygon.Count; vertexIndex++)
			remainingIndices.Add(vertexIndex);

		while (remainingIndices.Count > MinimumVertexCount)
		{
			bool removedVertex = false;
			for (int candidateIndex = 0; candidateIndex < remainingIndices.Count; candidateIndex++)
			{
				int previousIndex = remainingIndices[(candidateIndex + remainingIndices.Count - 1) % remainingIndices.Count];
				int currentIndex = remainingIndices[candidateIndex];
				int nextIndex = remainingIndices[(candidateIndex + 1) % remainingIndices.Count];
				Vector2 previous = polygon[previousIndex];
				Vector2 current = polygon[currentIndex];
				Vector2 next = polygon[nextIndex];
				float signedTurn = Cross(current - previous, next - current) * windingSign;
				bool isCollinear = Mathf.Abs(signedTurn) <= IntersectionTolerance;
				if (isCollinear)
				{
					remainingIndices.RemoveAt(candidateIndex);
					removedVertex = true;
					break;
				}

				if (signedTurn < 0f)
					continue;

				bool containsOtherVertex = false;
				for (int remainingIndex = 0; remainingIndex < remainingIndices.Count; remainingIndex++)
				{
					int pointIndex = remainingIndices[remainingIndex];
					bool isEarEndpoint = pointIndex == previousIndex || pointIndex == nextIndex;
					bool isEarVertex = isEarEndpoint || pointIndex == currentIndex;
					if (isEarVertex)
						continue;

					Vector2 point = polygon[pointIndex];
					bool insideFirstEdge = Cross(current - previous, point - previous) * windingSign >= -IntersectionTolerance;
					bool insideSecondEdge = Cross(next - current, point - current) * windingSign >= -IntersectionTolerance;
					bool insideThirdEdge = Cross(previous - next, point - next) * windingSign >= -IntersectionTolerance;
					bool insideFirstTwoEdges = insideFirstEdge && insideSecondEdge;
					if (insideFirstTwoEdges && insideThirdEdge)
					{
						containsOtherVertex = true;
						break;
					}
				}

				if (containsOtherVertex)
					continue;

				triangles.Add(previousIndex);
				triangles.Add(isCounterClockwise ? nextIndex : currentIndex);
				triangles.Add(isCounterClockwise ? currentIndex : nextIndex);
				remainingIndices.RemoveAt(candidateIndex);
				removedVertex = true;
				break;
			}

			if (!removedVertex)
			{
				triangles.Clear();
				return false;
			}
		}

		triangles.Add(remainingIndices[0]);
		triangles.Add(remainingIndices[isCounterClockwise ? 2 : 1]);
		triangles.Add(remainingIndices[isCounterClockwise ? 1 : 2]);
		return true;
	}
}
