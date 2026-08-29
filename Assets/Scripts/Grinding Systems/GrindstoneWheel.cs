using UnityEngine;

public class GrindstoneWheel : MonoBehaviour
{
	[SerializeField] float radius = 0.5f;
	[SerializeField] float height = 1.45f;
	[SerializeField] float stageOffsetY = 0.42f;
	[Tooltip("Positive Z puts the stone behind the blade (camera looks +Z).")]
	[SerializeField] float stoneDepthZ = 0.35f;
	[SerializeField] float visualThicknessZ = 0.06f;
	[SerializeField] Color stoneColor = new Color(0.62f, 0.72f, 0.82f, 1f);
	[SerializeField] MeshRenderer wheelRenderer;
	[SerializeField] Transform wheelVisual;

	Vector2 center;

	public float Radius => radius;
	public Vector2 Center => center;

	public void Configure(Vector2 stageCenter, int layer)
	{
		center = stageCenter + new Vector2(0f, stageOffsetY);
		EnsureVisual(layer);
		transform.position = new Vector3(center.x, center.y, stoneDepthZ);
		SetVisible(true);
	}

	public void SetSpinning(bool _) { }

	public void SetVisible(bool visible)
	{
		if (wheelVisual != null)
			wheelVisual.gameObject.SetActive(visible);
	}

	void EnsureVisual(int layer)
	{
		if (wheelVisual == null)
		{
			Transform existing = transform.Find("WheelVisual");
			if (existing != null)
				wheelVisual = existing;
		}

		if (wheelVisual == null)
		{
			var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			cylinder.name = "WheelVisual";
			Destroy(cylinder.GetComponent<Collider>());
			wheelVisual = cylinder.transform;
			wheelVisual.SetParent(transform, false);
			wheelRenderer = cylinder.GetComponent<MeshRenderer>();
		}

		wheelVisual.localPosition = Vector3.zero;
		wheelVisual.localRotation = Quaternion.identity;
		wheelVisual.localScale = new Vector3(radius * 2f, height * 0.5f, visualThicknessZ);

		if (wheelRenderer == null)
			wheelRenderer = wheelVisual.GetComponent<MeshRenderer>();

		if (wheelRenderer != null)
		{
			wheelRenderer.sharedMaterial = ForgingVisualUtility.CreateColorMaterial(stoneColor);
			wheelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			wheelRenderer.receiveShadows = false;
			wheelRenderer.sortingOrder = 1;
		}

		ForgingVisualUtility.ApplyLayerRecursively(gameObject, layer);
	}
}
