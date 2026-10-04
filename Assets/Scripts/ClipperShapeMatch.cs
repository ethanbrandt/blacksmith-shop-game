using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

public struct ShapeMatchScores
{
	public float coverage;
	public float overflow;
}

public static class ClipperShapeMatch
{
	private const int PRECISION = 6;
	private const double AREA_MIN = 0.000001;
	public static ShapeMatchScores CalculateCoverageAndOverflow(IReadOnlyList<Vector2> _metalVertices, IReadOnlyList<Vector2> _targetVertices)
	{
		PathsD pathA = ListToPathsD(_metalVertices);
		PathsD pathB = ListToPathsD(_targetVertices);

		double metalArea = Clipper.Area(pathA);
		double targetArea = Clipper.Area(pathB);

		if (targetArea <= AREA_MIN || metalArea <= AREA_MIN)
		{
			return new ShapeMatchScores
			{
				coverage = 0f,
				overflow = 0f
			};
		}
		
		PathsD intersect = Clipper.Intersect(pathA, pathB, FillRule.NonZero, PRECISION);
		double overlapArea = Clipper.Area(intersect);

		double coverage = overlapArea / targetArea;
		double overflow = (metalArea - overlapArea) / metalArea;

		return new ShapeMatchScores
		{
			coverage = Mathf.Clamp01((float)coverage),
			overflow = 1f - Mathf.Clamp01((float)overflow)
		};
	}

	private static PathsD ListToPathsD(IReadOnlyList<Vector2> _list)
	{
		var path = new PathD();
		
		for (int i = 0; i < _list.Count; i++)
			path.Add(new PointD(_list[i].x, _list[i].y));
		
		if (Clipper.Area(path) < 0)
			path.Reverse();
		
		return new PathsD{ path };
	}
}
