using UnityEngine;

public class MetalWorldViewMeshHandler : MonoBehaviour
{
	[SerializeField] float scale = 0.35f;
	[SerializeField, Range(0f, 1f)] float thinningStrength = 1f;
	[Header("Grinding")]
	[Tooltip("Requested taper width in local mesh units. The angle setting can widen it; the thick core limits its available run.")]
	[SerializeField, Min(0.001f)] float bevelWidth = 0.12f;
	[Tooltip("Preferred maximum taper angle in degrees. Small parts may need a steeper taper to retain their thick core.")]
	[SerializeField, Range(5f, 80f)] float maximumBevelAngle = 30f;
	[SerializeField, Range(0.01f, 1f)] float minEdgeThicknessRatio = 0.05f;
	[Tooltip("Curve detail. Higher values refine the surface more closely around the bevel.")]
	[SerializeField, Range(1, 8)] int bevelSegments = 4;
	
	private HeatableMetal metal;
	private MeshFilter meshFilter;
	private BoxCollider boxCollider;
	private ExtrudePolygonMeshBuilder builder = new ExtrudePolygonMeshBuilder();
	private float initialBoundsArea;
	private Mesh runtimeMesh;
	private bool initialized;
	private bool rebuildRequested;
	
	void Awake()
	{
		metal = GetComponent<HeatableMetal>();
		meshFilter = GetComponent<MeshFilter>();
		boxCollider = GetComponent<BoxCollider>();
		runtimeMesh = new Mesh { name = "MetalWorldViewMesh" };
	}

	private void Start()
	{
		if (metal.ShapeVertices.Count < PolygonGeometry.MinimumVertexCount)
			return;
		Vector3 size = PolygonGeometry.ComputeBounds(metal.ShapeVertices).size;
		initialBoundsArea = Mathf.Max(size.x * size.y, 0.01f);
		initialized = true;
		OnShapeChanged();
	}

	void OnEnable()
	{
		metal.ShapeChanged += OnShapeChanged;
		metal.GrindChanged += OnShapeChanged;
		if (initialized)
			OnShapeChanged();
	}

	void OnDisable()
	{
		metal.ShapeChanged -= OnShapeChanged;
		metal.GrindChanged -= OnShapeChanged;
	}

	void OnDestroy()
	{
		if (runtimeMesh != null)
			Destroy(runtimeMesh);
	}

	void OnValidate()
	{
		// OnValidate can run while loading; rebuild on the main thread instead.
		rebuildRequested = true;
	}

	void Update()
	{
		if (initialized && rebuildRequested)
			OnShapeChanged();
	}

	void OnShapeChanged()
	{
		if (!initialized)
			return;
		rebuildRequested = false;

		var polygon = metal.HasGrindProgress ? metal.GroundVertices : metal.ShapeVertices;
		if (polygon.Count < PolygonGeometry.MinimumVertexCount)
			return;
		Vector3 size = PolygonGeometry.ComputeBounds(polygon).size;
		float area = Mathf.Max(size.x * size.y, 0.01f);
		float thickness = metal.PartDefinition.thickness;
		float actualThickness = thickness * Mathf.Pow(initialBoundsArea / area, thinningStrength);
		float bodyThickness = Mathf.Clamp(actualThickness, Mathf.Min(0.1f, thickness), thickness);

		bool built; 
		if (metal.HasGrindProgress)
			built = builder.TryBuildGround(runtimeMesh, polygon, metal.GrindAmounts, bodyThickness, Color.white, scale, bevelWidth, minEdgeThicknessRatio, bevelSegments, metal.GrindForSharpEdge, maximumBevelAngle);
		else
			built = builder.TryBuild(runtimeMesh, polygon, bodyThickness, Color.white, scale);
		
		if (built)
		{
			meshFilter.sharedMesh = runtimeMesh;
			if (boxCollider != null)
			{
				boxCollider.center = runtimeMesh.bounds.center;
				boxCollider.size = runtimeMesh.bounds.size;
			}
		}
	}
}
