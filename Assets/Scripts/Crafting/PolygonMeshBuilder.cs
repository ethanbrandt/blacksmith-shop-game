using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable buffers for a simple polygon fill, including concave outlines.</summary>
public sealed class PolygonMeshBuilder
{
	readonly List<Vector3> vertices = new List<Vector3>();
	readonly List<Vector2> uvs = new List<Vector2>();
	readonly List<Color> colors = new List<Color>();
	readonly List<int> triangles = new List<int>();
	readonly List<int> remainingVertexIndices = new List<int>();

	public void Build(Mesh mesh, IReadOnlyList<Vector2> polygon, float depth, Color color, Transform worldToLocal = null)
	{
		mesh.Clear();
		if (!PolygonGeometry.Triangulate(polygon, triangles, remainingVertexIndices))
			return;

		vertices.Clear();
		uvs.Clear();
		colors.Clear();
		for (int i = 0; i < polygon.Count; i++)
		{
			var point = new Vector3(polygon[i].x, polygon[i].y, depth);
			vertices.Add(worldToLocal != null ? worldToLocal.InverseTransformPoint(point) : point);
			uvs.Add(Vector2.one * 0.5f);
			colors.Add(color);
		}

		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.SetColors(colors);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();
	}
}
