using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

public static class ClipperMeltGeometry
{
	private const int BOUNDARY_SEGMENTS = 64;
	private const int PRECISION = 6;
	private const double SIMPLIFY_TOLERANCE = 0.001;
	private const double ARC_TOLERANCE = 0.01;

	private static readonly Dictionary<float, PathsD> cachedBoundaries = new Dictionary<float, PathsD>();
	
	public static bool TryExpand(IReadOnlyList<Vector2> _source, float _distance, float _maxRadius, List<Vector2> _destination)
	{
		if (ReferenceEquals(_source, _destination))
			return false;
		
		_destination.Clear();

		if (!IsValidAndPositive(_distance) || !IsValidAndPositive(_maxRadius) || !PolygonGeometry.IsSimple(_source))
			return false;

		var path = new PathD();
		
		for (int i = 0; i < _source.Count; i++)
			path.Add(new PointD(_source[i].x, _source[i].y));
		
		if (Clipper.Area(path) < 0)
			path.Reverse();

		var original = new PathsD { path };
		PathsD expanded = Clipper.InflatePaths(original, _distance, JoinType.Round, EndType.Polygon, 2.0, PRECISION, ARC_TOLERANCE);
		expanded = Clipper.SimplifyPaths(expanded, SIMPLIFY_TOLERANCE);
		
		PathsD bounded = Clipper.Intersect(expanded, GetBoundary(_maxRadius), FillRule.NonZero, PRECISION);
		
		PathsD resolved = Clipper.Union(original, bounded, FillRule.NonZero, PRECISION);
		
		PathD outer = null;

		foreach (var contour in resolved)
		{
			if (Clipper.Area(contour) <= 0)
				continue;

			if (outer != null)
				return false;

			outer = contour;
		}

		if (outer == null)
			return false;

		outer = Clipper.SimplifyPath(outer, SIMPLIFY_TOLERANCE);

		foreach (var point in outer)
			_destination.Add(new Vector2((float)point.x, (float)point.y));
		
		if (PolygonGeometry.SignedArea(_source) < 0f)
			_destination.Reverse();

		if (!PolygonGeometry.IsSimple(_destination))
		{
			_destination.Clear();
			return false;
		}

		return !PolygonGeometry.IsSameVertices(_source, _destination);
	}

	private static PathsD GetBoundary(float _radius)
	{
		if (cachedBoundaries.TryGetValue(_radius, out var boundary))
			return boundary;

		var circle = new PathD();

		for (int i = 0; i < BOUNDARY_SEGMENTS; i++)
		{
			double angle = i * Math.PI * 2.0 / BOUNDARY_SEGMENTS;
			
			circle.Add(new PointD(Math.Cos(angle) * _radius, Math.Sin(angle) * _radius));
		}

		var createdBoundary = new PathsD { circle };
		
		cachedBoundaries.Add(_radius, createdBoundary);
		
		return createdBoundary;
	}

	private static bool IsValidAndPositive(float _value)
	{
		return !float.IsNaN(_value) && !float.IsInfinity(_value) && _value > 0f;
	}
}
