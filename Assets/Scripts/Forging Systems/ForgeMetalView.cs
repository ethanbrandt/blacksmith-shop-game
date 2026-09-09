using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MetalDeformer2D))]
public class ForgeMetalView : MonoBehaviour
{
	const float MinimumFlashDuration = 0.0001f;
	const float MetalColorBlend = 0.65f;
	const float OutlineDarkening = 0.35f;
	[SerializeField] MetalDeformer2D deformer;
	[SerializeField] float fillZ;
	[SerializeField] float outlineZ = -0.02f;
	[SerializeField] float outlineWidth = 0.045f;
	[SerializeField] int fillSortingOrder = 8;
	[SerializeField] int outlineSortingOrder = 9;
	[SerializeField] Color coldTint = new Color(0.45f, 0.48f, 0.55f, 1f);
	[SerializeField] Color hotTint = new Color(1f, 0.45f, 0.12f, 1f);
	[SerializeField] Color hitFlashColor = Color.white;
	[Min(0f)]
	[SerializeField] float hitFlashDuration = 0.1f;

	[Header("Scene Objects")]
	[SerializeField] MeshFilter fillFilter;
	[SerializeField] MeshRenderer fillRenderer;
	[SerializeField] LineRenderer outline;
	[SerializeField] Material fillMaterial;

	float hitFlashStartTime = float.NegativeInfinity;
	Mesh fillMesh;
	readonly PolygonMeshBuilder fillBuilder = new PolygonMeshBuilder();

	void OnDestroy()
	{
		if (fillMesh != null)
			ForgingVisualUtility.DestroyGenerated(fillMesh);
	}

	MaterialPropertyBlock tintBlock;

	public MetalDeformer2D Deformer => deformer;

	void Awake()
	{
		if (deformer == null)
			deformer = GetComponent<MetalDeformer2D>();

		EnsureVisuals();
	}

	void OnEnable()
	{
		if (deformer != null)
		{
			deformer.VerticesChanged += Rebuild;
			deformer.Struck += OnStruck;
		}

		Rebuild();
	}

	void OnDisable()
	{
		if (deformer != null)
		{
			deformer.VerticesChanged -= Rebuild;
			deformer.Struck -= OnStruck;
		}
	}

	void LateUpdate()
	{
		RefreshTint();
	}

	public void Configure(MetalDeformer2D source)
	{
		if (deformer != null)
		{
			deformer.VerticesChanged -= Rebuild;
			deformer.Struck -= OnStruck;
		}

		deformer = source;
		EnsureVisuals();

		if (deformer != null)
		{
			deformer.VerticesChanged -= Rebuild;
			deformer.VerticesChanged += Rebuild;

			deformer.Struck -= OnStruck;
			deformer.Struck += OnStruck;
		}

		Rebuild();
	}

	void EnsureVisuals()
	{
		if (fillFilter == null)
		{
			Transform existing = transform.Find("MetalFill");
			if (existing != null)
			{
				fillFilter = existing.GetComponent<MeshFilter>();
				fillRenderer = existing.GetComponent<MeshRenderer>();
			}
		}

		if (fillFilter == null)
		{
			var fillGo = new GameObject("MetalFill");
			fillGo.transform.SetParent(transform, false);
			fillFilter = fillGo.AddComponent<MeshFilter>();
			fillRenderer = fillGo.AddComponent<MeshRenderer>();
			fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
			fillRenderer.receiveShadows = false;
			fillRenderer.sortingOrder = fillSortingOrder;
		}

		if (fillMesh == null)
		{
			fillMesh = new Mesh
			{
				name = "ForgeMetalFill"
			};
			fillMesh.MarkDynamic();
		}

		fillFilter.sharedMesh = fillMesh;
		fillMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		if (fillRenderer != null)
		{
			fillRenderer.sharedMaterial = fillMaterial;
			fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
			fillRenderer.receiveShadows = false;
			fillRenderer.sortingOrder = fillSortingOrder;
			fillRenderer.enabled = true;
		}

		if (outline == null)
		{
			Transform existing = transform.Find("MetalOutline");
			if (existing != null)
				outline = existing.GetComponent<LineRenderer>();
		}

		if (outline == null)
		{
			var outlineGo = new GameObject("MetalOutline");
			outlineGo.transform.SetParent(transform, false);
			outline = outlineGo.AddComponent<LineRenderer>();
			outline.useWorldSpace = true;
			outline.loop = true;
			ForgingVisualUtility.ApplyLineRendererDefaults(outline, Color.white, outlineWidth, outlineSortingOrder);
		}

		ForgingVisualUtility.ApplyLayerRecursively(gameObject, gameObject.layer);
	}

	void OnStruck(Vector2 _, float __)
	{
		hitFlashStartTime = Time.unscaledTime;
	}

	void Rebuild()
	{
		EnsureVisuals();
		if (deformer == null || deformer.VertexCount < 3)
		{
			if (fillMesh != null)
				fillMesh.Clear();
			if (outline != null)
				outline.positionCount = 0;
			return;
		}

		var source = deformer.Vertices;
		int count = source.Count;
		fillBuilder.Build(fillMesh, source, fillFilter.transform.position.z + fillZ, Color.white, fillFilter.transform);
		outline.positionCount = count;
		for (int i = 0; i < count; i++)
		{
			Vector2 vertex = source[i];
			outline.SetPosition(i, new Vector3(vertex.x, vertex.y, outlineZ));
		}

		RefreshTint();
	}

	void RefreshTint()
	{
		if (deformer == null)
			return;

		Color baseColor = deformer.MetalType != null ? deformer.MetalType.metalColor : Color.white;
		float heat01 = deformer.Heat;
		Color heatedColor = Color.Lerp(Color.Lerp(coldTint, baseColor, MetalColorBlend), hotTint, heat01);
		float flash01 = 1f - Mathf.Clamp01((Time.unscaledTime - hitFlashStartTime) / Mathf.Max(MinimumFlashDuration, hitFlashDuration));
		flash01 *= flash01;
		heatedColor = (heatedColor * 0.5f) + (0.5f * Color.Lerp(heatedColor, hitFlashColor, flash01));
		if (fillRenderer != null)
		{
			tintBlock ??= new MaterialPropertyBlock();
			fillRenderer.GetPropertyBlock(tintBlock);
			tintBlock.SetColor("_Color", heatedColor);
			tintBlock.SetColor("_BaseColor", heatedColor);
			tintBlock.SetColor("_RendererColor", Color.white);
			if (fillMaterial != null && fillMaterial.HasProperty("_MainTex"))
				tintBlock.SetTexture("_MainTex", Texture2D.whiteTexture);
			fillRenderer.SetPropertyBlock(tintBlock);
		}

		if (outline != null)
		{
			Color edge = Color.Lerp(heatedColor, Color.black, OutlineDarkening);
			outline.startColor = edge;
			outline.endColor = edge;
		}
	}
}
