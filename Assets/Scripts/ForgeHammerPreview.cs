using UnityEngine;
using UnityEngine.Rendering;

namespace ForgingPrototype
{
	public class ForgeHammerPreview : MonoBehaviour
	{
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
		Vector3[] ringPoints;
		Vector3[] discVerts;
		int[] discTris;
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
			Color color = flashing
				? flashTint
				: Color.Lerp(idleColor, chargeColor, Mathf.Clamp01(charge01));

			DrawRing(impact, radius, color);
			DrawDisc(impact, radius, color);
			DrawArrow(impact, direction, radius, Mathf.Clamp01(charge01), color);
			ApplyLayer();
		}

		public void PlayStrikeFlash(Vector2 impact, Vector2 direction, float radius)
		{
			flashUntil = Time.unscaledTime + 0.16f;
			flashTint = strikeColor;
			ShowAim(impact, direction, radius, 1f);
		}

		void LateUpdate()
		{
			if (Time.unscaledTime >= flashUntil)
				return;
			if (ring == null || !ring.enabled)
				return;

			float t = 1f - Mathf.InverseLerp(flashUntil - 0.16f, flashUntil, Time.unscaledTime);
			Color color = Color.Lerp(idleColor, flashTint, t);
			ring.startColor = color;
			ring.endColor = color;
			arrow.startColor = color;
			arrow.endColor = color;
			SetDiscColor(new Color(color.r, color.g, color.b, color.a * 0.22f));
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
				var discGo = new GameObject("HammerRadiusFill");
				discGo.transform.SetParent(transform, false);
				discFilter = discGo.AddComponent<MeshFilter>();
				discRenderer = discGo.AddComponent<MeshRenderer>();
				discRenderer.shadowCastingMode = ShadowCastingMode.Off;
				discRenderer.receiveShadows = false;
				discRenderer.sortingOrder = 18;
			}

			if (discMesh == null)
			{
				discMesh = new Mesh { name = "HammerRadiusDisc" };
				discMesh.MarkDynamic();
				discFilter.sharedMesh = discMesh;
			}

			if (discMaterial == null)
				discMaterial = ForgingVisualUtility.CreateColorMaterial(idleColor);
			if (discRenderer != null && discRenderer.sharedMaterial == null)
				discRenderer.sharedMaterial = discMaterial;

			if (ring == null)
			{
				Transform existing = transform.Find("HammerRadiusRing");
				if (existing != null)
					ring = existing.GetComponent<LineRenderer>();
			}

			if (ring == null)
			{
				ring = CreateLine("HammerRadiusRing", ringWidth, 20);
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
				arrow = CreateLine("HammerAimArrow", arrowWidth, 21);
				arrow.loop = false;
			}

			int segs = Mathf.Max(12, ringSegments);
			if (ringPoints == null || ringPoints.Length != segs)
			{
				ringPoints = new Vector3[segs];
				discVerts = new Vector3[segs + 1];
				discTris = new int[segs * 3];
			}

			ApplyLayer();
		}

		LineRenderer CreateLine(string name, float width, int sortingOrder)
		{
			var go = new GameObject(name);
			go.transform.SetParent(transform, false);
			var line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			ForgingVisualUtility.ApplyLineRendererDefaults(line, idleColor, width, sortingOrder);
			return line;
		}

		void DrawRing(Vector2 center, float radius, Color color)
		{
			int segs = ringPoints.Length;
			for (int i = 0; i < segs; i++)
			{
				float ang = (i / (float)segs) * Mathf.PI * 2f;
				ringPoints[i] = new Vector3(
					center.x + Mathf.Cos(ang) * radius,
					center.y + Mathf.Sin(ang) * radius,
					previewZ);
			}

			ring.positionCount = segs;
			ring.SetPositions(ringPoints);
			ring.startColor = color;
			ring.endColor = color;
			ring.widthMultiplier = ringWidth;
		}

		void DrawDisc(Vector2 center, float radius, Color color)
		{
			int segs = ringPoints.Length;
			discVerts[0] = new Vector3(center.x, center.y, previewZ + 0.02f);
			for (int i = 0; i < segs; i++)
			{
				float ang = (i / (float)segs) * Mathf.PI * 2f;
				discVerts[i + 1] = new Vector3(
					center.x + Mathf.Cos(ang) * radius,
					center.y + Mathf.Sin(ang) * radius,
					previewZ + 0.02f);

				int t = i * 3;
				discTris[t] = 0;
				discTris[t + 1] = (i + 1) % segs + 1;
				discTris[t + 2] = i + 1;
			}

			discMesh.Clear();
			discMesh.SetVertices(discVerts);
			discMesh.SetTriangles(discTris, 0);
			discMesh.RecalculateBounds();
			SetDiscColor(new Color(color.r, color.g, color.b, color.a * 0.2f));
		}

		void DrawArrow(Vector2 origin, Vector2 direction, float radius, float charge01, Color color)
		{
			if (direction.sqrMagnitude < 0.0001f)
			{
				arrow.positionCount = 0;
				return;
			}

			Vector2 dir = direction.normalized;
			float length = radius * Mathf.Lerp(0.7f, 1.15f, charge01);
			Vector2 tip = origin + dir * length;
			Vector2 side = new Vector2(-dir.y, dir.x);
			float head = Mathf.Max(0.12f, radius * 0.28f);
			Vector2 left = tip - dir * head + side * head * 0.55f;
			Vector2 right = tip - dir * head - side * head * 0.55f;

			arrow.positionCount = 5;
			arrow.SetPosition(0, new Vector3(origin.x, origin.y, previewZ));
			arrow.SetPosition(1, new Vector3(tip.x, tip.y, previewZ));
			arrow.SetPosition(2, new Vector3(left.x, left.y, previewZ));
			arrow.SetPosition(3, new Vector3(tip.x, tip.y, previewZ));
			arrow.SetPosition(4, new Vector3(right.x, right.y, previewZ));
			arrow.startColor = color;
			arrow.endColor = color;
			arrow.widthMultiplier = Mathf.Lerp(arrowWidth * 0.85f, arrowWidth * 1.25f, charge01);
		}

		void SetDiscColor(Color color)
		{
			if (discMaterial == null)
				return;

			discMaterial.color = color;
			if (discMaterial.HasProperty("_BaseColor"))
				discMaterial.SetColor("_BaseColor", color);
			if (discMaterial.HasProperty("_Color"))
				discMaterial.SetColor("_Color", color);
		}

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
}
