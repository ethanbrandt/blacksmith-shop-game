using UnityEngine;
using UnityEngine.Rendering;

public class ForgeHammerPreview : MonoBehaviour
{
	const float StrikeFlashDuration = 0.16f;
	const float FlashDiscOpacity = 0.22f;
	const float AimDiscOpacity = 0.2f;
	const float DiscDepthOffset = 0.02f;
	const int DiscSortingOrder = 18;
	const int RingSortingOrder = 20;
	const int ArrowSortingOrder = 21;
	const int MinimumRingSegments = 12;
	const int TriangleIndexCount = 3;
	const float MinimumSquaredAimMagnitude = 0.0001f;
	const float MinimumArrowLengthScale = 0.7f;
	const float MaximumArrowLengthScale = 1.15f;
	const float MinimumArrowHeadLength = 0.12f;
	const float ArrowHeadRadiusFraction = 0.28f;
	const float ArrowHeadWidthFraction = 0.55f;
	const float MinimumArrowWidthScale = 0.85f;
	const float MaximumArrowWidthScale = 1.25f;
	[SerializeField] int ringSegments = 48;
	[SerializeField] float previewZ = -0.92f;
	[SerializeField] float ringWidth = 0.035f;
	[SerializeField] float arrowWidth = 0.055f;
	[SerializeField] Color idleColor = new Color(1f, 0.92f, 0.45f, 0.7f);
	[SerializeField] Color chargeColor = new Color(1f, 0.55f, 0.12f, 0.95f);
	[SerializeField] Color strikeColor = new Color(1f, 0.95f, 0.7f, 1f);

	[Header("Scene Objects")]
	[SerializeField] LineRenderer ring;
	[SerializeField] LineRenderer arrow;
	[SerializeField] MeshFilter discFilter;
	[SerializeField] MeshRenderer discRenderer;
	[SerializeField] Material discMaterial;
	Mesh discMesh;
	Color[] discColors;

	void OnDestroy()
	{
		if (discMesh != null)
			ForgingVisualUtility.DestroyGenerated(discMesh);
	}

	Vector3[] ringPoints;
	Vector3[] discVertices;
	int[] discTriangles;
	float flashUntil;
	Color flashTint;

	public bool IsFlashing => Time.unscaledTime < flashUntil;

	public void Hide()
	{
		SetVisible(false);
	}

	public void ShowAim(Vector2 impact, Vector2 direction, float radius, float charge01)
	{
		EnsureVisuals();
		SetVisible(true);

		bool flashing = Time.unscaledTime < flashUntil;
		Color color = flashing ? flashTint : Color.Lerp(idleColor, chargeColor, Mathf.Clamp01(charge01));
		DrawRing(impact, radius, color);
		DrawDisc(impact, radius, color);
		DrawArrow(impact, direction, radius, Mathf.Clamp01(charge01), color);
		ApplyLayer();
	}

	public void PlayStrikeFlash(Vector2 impact, Vector2 direction, float radius)
	{
		flashUntil = Time.unscaledTime + StrikeFlashDuration;
		flashTint = strikeColor;
		ShowAim(impact, direction, radius, 1f);
	}

	void LateUpdate()
	{
		if (Time.unscaledTime >= flashUntil)
			return;
		if (ring == null || !ring.enabled)
			return;
		float flashProgress = 1f - Mathf.InverseLerp(flashUntil - StrikeFlashDuration, flashUntil, Time.unscaledTime);
		Color color = Color.Lerp(idleColor, flashTint, flashProgress);
		ring.startColor = color;
		ring.endColor = color;
		arrow.startColor = color;
		arrow.endColor = color;
		SetDiscColor(new Color(color.r, color.g, color.b, color.a * FlashDiscOpacity));
	}

	void EnsureVisuals()
	{
		if (discFilter == null)
		{
			Transform existing = transform.Find("HammerRadiusFill");
			if (existing != null)
			{
				discFilter = existing.GetComponent<MeshFilter>();
				discRenderer = existing.GetComponent<MeshRenderer>();
			}
		}

		if (discFilter == null)
		{
			var discObject = new GameObject("HammerRadiusFill");
			discObject.transform.SetParent(transform, false);
			discFilter = discObject.AddComponent<MeshFilter>();
			discRenderer = discObject.AddComponent<MeshRenderer>();
			discRenderer.shadowCastingMode = ShadowCastingMode.Off;
			discRenderer.receiveShadows = false;
			discRenderer.sortingOrder = DiscSortingOrder;
		}

		if (discMesh == null)
		{
			discMesh = new Mesh
			{
				name = "HammerRadiusDisc"
			};
			discMesh.MarkDynamic();
			discFilter.sharedMesh = discMesh;
		}

		discMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		discRenderer.sharedMaterial = discMaterial;
		discRenderer.sortingOrder = DiscSortingOrder;
		if (ring == null)
		{
			Transform existing = transform.Find("HammerRadiusRing");
			if (existing != null)
				ring = existing.GetComponent<LineRenderer>();
		}

		if (ring == null)
		{
			ring = CreateLine("HammerRadiusRing", ringWidth, RingSortingOrder);
			ring.loop = true;
		}

		if (arrow == null)
		{
			Transform existing = transform.Find("HammerAimArrow");
			if (existing != null)
				arrow = existing.GetComponent<LineRenderer>();
		}

		if (arrow == null)
		{
			arrow = CreateLine("HammerAimArrow", arrowWidth, ArrowSortingOrder);
			arrow.loop = false;
		}

		ring.sharedMaterial = ForgingVisualUtility.GetSpritesDefaultMaterial();
		arrow.sharedMaterial = ForgingVisualUtility.GetSpritesDefaultMaterial();
		int segmentCount = Mathf.Max(MinimumRingSegments, ringSegments);
		if (ringPoints == null || ringPoints.Length != segmentCount)
		{
			ringPoints = new Vector3[segmentCount];
			discVertices = new Vector3[segmentCount + 1];
			discColors = new Color[segmentCount + 1];
			for (int i = 0; i < discColors.Length; i++)
				discColors[i] = Color.white;
			discTriangles = new int[segmentCount * TriangleIndexCount];
		}

		ApplyLayer();
	}

	LineRenderer CreateLine(string name, float width, int sortingOrder)
	{
		var lineObject = new GameObject(name);
		lineObject.transform.SetParent(transform, false);
		var line = lineObject.AddComponent<LineRenderer>();
		line.useWorldSpace = true;
		ForgingVisualUtility.ApplyLineRendererDefaults(line, idleColor, width, sortingOrder);
		return line;
	}

	void DrawRing(Vector2 center, float radius, Color color)
	{
		int segmentCount = ringPoints.Length;
		for (int i = 0; i < segmentCount; i++)
		{
			float angleRadians = (i / (float)segmentCount) * Mathf.PI * 2f;
			ringPoints[i] = new Vector3(center.x + Mathf.Cos(angleRadians) * radius, center.y + Mathf.Sin(angleRadians) * radius, previewZ);
		}

		ring.positionCount = segmentCount;
		ring.SetPositions(ringPoints);
		ring.startColor = color;
		ring.endColor = color;
		ring.widthMultiplier = ringWidth;
	}

	void DrawDisc(Vector2 center, float radius, Color color)
	{
		int segmentCount = ringPoints.Length;
		discVertices[0] = new Vector3(center.x, center.y, previewZ + DiscDepthOffset);
		for (int i = 0; i < segmentCount; i++)
		{
			float angleRadians = (i / (float)segmentCount) * Mathf.PI * 2f;
			discVertices[i + 1] = new Vector3(center.x + Mathf.Cos(angleRadians) * radius, center.y + Mathf.Sin(angleRadians) * radius, previewZ + DiscDepthOffset);
			int triangleIndex = i * TriangleIndexCount;
			discTriangles[triangleIndex] = 0;
			discTriangles[triangleIndex + 1] = (i + 1) % segmentCount + 1;
			discTriangles[triangleIndex + 2] = i + 1;
		}

		for (int i = 0; i < discVertices.Length; i++)
			discVertices[i] = discFilter.transform.InverseTransformPoint(discVertices[i]);
		discMesh.Clear();
		discMesh.SetVertices(discVertices);
		discMesh.SetColors(discColors);
		discMesh.SetTriangles(discTriangles, 0);
		discMesh.RecalculateBounds();
		SetDiscColor(new Color(color.r, color.g, color.b, color.a * AimDiscOpacity));
	}

	void DrawArrow(Vector2 origin, Vector2 direction, float radius, float charge01, Color color)
	{
		if (direction.sqrMagnitude < MinimumSquaredAimMagnitude)
		{
			arrow.positionCount = 0;
			return;
		}

		Vector2 aimDirection = direction.normalized;
		float length = radius * Mathf.Lerp(MinimumArrowLengthScale, MaximumArrowLengthScale, charge01);
		Vector2 tip = origin + aimDirection * length;
		Vector2 side = new Vector2(-aimDirection.y, aimDirection.x);
		float arrowHeadLength = Mathf.Max(MinimumArrowHeadLength, radius * ArrowHeadRadiusFraction);
		Vector2 left = tip - aimDirection * arrowHeadLength + side * arrowHeadLength * ArrowHeadWidthFraction;
		Vector2 right = tip - aimDirection * arrowHeadLength - side * arrowHeadLength * ArrowHeadWidthFraction;
		arrow.positionCount = 5;
		arrow.SetPosition(0, new Vector3(origin.x, origin.y, previewZ));
		arrow.SetPosition(1, new Vector3(tip.x, tip.y, previewZ));
		arrow.SetPosition(2, new Vector3(left.x, left.y, previewZ));
		arrow.SetPosition(3, new Vector3(tip.x, tip.y, previewZ));
		arrow.SetPosition(4, new Vector3(right.x, right.y, previewZ));
		arrow.startColor = color;
		arrow.endColor = color;
		arrow.widthMultiplier = Mathf.Lerp(arrowWidth * MinimumArrowWidthScale, arrowWidth * MaximumArrowWidthScale, charge01);
	}

	void SetDiscColor(Color color) => ForgingVisualUtility.SetTint(discRenderer, color);

	void SetVisible(bool visible)
	{
		EnsureVisuals();
		if (ring != null)
			ring.enabled = visible;
		if (arrow != null)
			arrow.enabled = visible;
		if (discRenderer != null)
			discRenderer.enabled = visible;
	}

	void ApplyLayer()
	{
		ForgingVisualUtility.ApplyLayerRecursively(gameObject, gameObject.layer);
	}
}
