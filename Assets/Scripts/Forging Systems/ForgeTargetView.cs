using System.Collections.Generic;
using UnityEngine;

public class ForgeTargetView : MonoBehaviour
{
	const int MinimumGhostFillSortingOrder = 10;
	const float GhostFillDepthOffset = 0.05f;
	
	[Header("Target")]
	[SerializeField] Color targetOutlineColor = new Color(0.75f, 0.9f, 1f, 0.55f);
	[SerializeField] Color targetGhostOutlineColor = new Color(0.7f, 0.9f, 1f, 0.7f);
	[SerializeField] Color targetGhostFillColor = new Color(0.55f, 0.8f, 1f, 0.12f);
	[SerializeField] float targetLineWidth = 0.05f;
	[SerializeField] float ghostLineWidth = 0.04f;
	[SerializeField] int outlineSortingOrder = 2;
	[SerializeField] int ghostOutlineSortingOrder = 12;
	[SerializeField] int ghostFillSortingOrder = 10;
	[SerializeField] bool showGhostOverMetal = true;
	[SerializeField] bool showGhostFill = true;
	
	[Header("References")]
	[SerializeField] LineRenderer targetOutline;
	[SerializeField] LineRenderer targetGhostOutline;
	[SerializeField] MeshFilter ghostFillFilter;
	[SerializeField] MeshRenderer ghostFillRenderer;

	Mesh _ghostFillMesh;
	Material _ghostFillMaterial;
	Vector2[] targetVertices;
	
	readonly PolygonMeshBuilder ghostBuilder = new PolygonMeshBuilder();
	public IReadOnlyList<Vector2> TargetVertices => targetVertices;

	void OnDestroy()
	{
		if (_ghostFillMesh != null)
			ForgingVisualUtility.DestroyGenerated(_ghostFillMesh);
	}

	public void Configure(Vector2[] _outline)
	{
		targetVertices = _outline;
		
		EnsureOutline();
		EnsureGhostOutline();
		EnsureGhostFill();
		RebuildTargetVisuals();
	}
	
	void EnsureOutline()
	{
		if (targetOutline != null)
			return;

		var go = new GameObject("TargetOutline");
		go.transform.SetParent(transform, false);
		go.layer = gameObject.layer;
		targetOutline = go.AddComponent<LineRenderer>();
		ConfigureOutlineLine(targetOutline, targetOutlineColor, targetLineWidth, outlineSortingOrder, -0.05f);
	}

	void EnsureGhostOutline()
	{
		if (targetGhostOutline != null)
			return;

		var go = new GameObject("TargetGhostOutline");
		go.transform.SetParent(transform, false);
		go.layer = gameObject.layer;
		targetGhostOutline = go.AddComponent<LineRenderer>();
		ConfigureOutlineLine(targetGhostOutline, targetGhostOutlineColor, ghostLineWidth, ghostOutlineSortingOrder, -0.9f);
	}

	void EnsureGhostFill()
	{
		if (ghostFillFilter == null)
		{
			Transform existing = transform.Find("TargetGhostFill");
			if (existing != null)
			{
				ghostFillFilter = existing.GetComponent<MeshFilter>();
				ghostFillRenderer = existing.GetComponent<MeshRenderer>();
			}
		}

		if (ghostFillFilter == null)
		{
			var go = new GameObject("TargetGhostFill");
			go.transform.SetParent(transform, false);
			go.layer = gameObject.layer;
			ghostFillFilter = go.AddComponent<MeshFilter>();
			ghostFillRenderer = go.AddComponent<MeshRenderer>();
		}

		if (_ghostFillMesh == null)
		{
			_ghostFillMesh = new Mesh
			{
				name = "TargetGhostFill"
			};
			_ghostFillMesh.MarkDynamic();
		}

		ghostFillFilter.sharedMesh = _ghostFillMesh;
		_ghostFillMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		if (ghostFillRenderer != null)
		{
			ghostFillRenderer.sharedMaterial = _ghostFillMaterial;
			ghostFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			ghostFillRenderer.receiveShadows = false;
			ghostFillRenderer.sortingOrder = Mathf.Max(MinimumGhostFillSortingOrder, ghostFillSortingOrder);
		}
	}

	static void ConfigureOutlineLine(LineRenderer line, Color color, float width, int sortingOrder, float z)
	{
		line.useWorldSpace = true;
		line.loop = true;
		line.widthMultiplier = width;
		ForgingVisualUtility.ApplyLineRendererDefaults(line, color, width, sortingOrder);
		line.startColor = color;
		line.endColor = color;
		line.sortingOrder = sortingOrder;
		line.numCornerVertices = 4;
		line.numCapVertices = 4;
		line.positionCount = 0;
	}

	void RebuildTargetVisuals()
	{
		EnsureOutline();
		EnsureGhostOutline();
		EnsureGhostFill();

		if (targetVertices == null || targetVertices.Length < 2)
		{
			targetOutline.positionCount = 0;
			targetGhostOutline.positionCount = 0;
			if (_ghostFillMesh != null)
			{
				_ghostFillMesh.Clear();
			}

			return;
		}

		ApplyLine(targetOutline, targetOutlineColor, targetLineWidth, outlineSortingOrder, 0.15f);
		if (showGhostOverMetal)
		{
			targetGhostOutline.enabled = true;
			ApplyLine(targetGhostOutline, targetGhostOutlineColor, ghostLineWidth, ghostOutlineSortingOrder, -0.85f);
		}
		else
			targetGhostOutline.enabled = false;

		RebuildGhostFill();
	}

	void ApplyLine(LineRenderer line, Color color, float width, int sortingOrder, float z)
	{
		line.sharedMaterial = ForgingVisualUtility.GetSpritesDefaultMaterial();
		line.positionCount = targetVertices.Length;
		for (int i = 0; i < targetVertices.Length; i++)
		{
			Vector2 v = targetVertices[i];
			line.SetPosition(i, new Vector3(v.x, v.y, z));
		}

		line.startColor = color;
		line.endColor = color;
		line.widthMultiplier = width;
		line.sortingOrder = sortingOrder;
	}

	void RebuildGhostFill()
	{
		bool hasGhostMesh = ghostFillFilter != null && _ghostFillMesh != null;
		bool hasVisibleGhostMesh = showGhostFill && hasGhostMesh;
		bool hasTargetOutline = hasVisibleGhostMesh && targetVertices.Length >= PolygonGeometry.MinimumVertexCount;
		if (!hasVisibleGhostMesh || !hasTargetOutline)
		{
			if (_ghostFillMesh != null)
				_ghostFillMesh.Clear();

			if (ghostFillRenderer != null)
				ghostFillRenderer.enabled = false;

			return;
		}

		ghostFillRenderer.enabled = true;
		ForgingVisualUtility.SetTint(ghostFillRenderer, targetGhostFillColor);
		ghostFillRenderer.sortingOrder = Mathf.Max(MinimumGhostFillSortingOrder, ghostFillSortingOrder);
		ghostBuilder.Build(_ghostFillMesh, targetVertices, ghostFillFilter.transform.position.z - GhostFillDepthOffset, Color.white, ghostFillFilter.transform);
	}
}
