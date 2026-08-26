using System.Collections.Generic;
using ForgingPrototype;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class GrindSessionController : MonoBehaviour
{
	public static GrindSessionController Instance { get; private set; }

	[Header("Scene Refs")]
	[SerializeField] Camera grindCamera;
	[SerializeField] RawImage viewport;
	[SerializeField] Image frameImage;
	[SerializeField] Sprite frameSprite;
	[SerializeField] TextMeshProUGUI statusText;
	[SerializeField] TextMeshProUGUI qualityText;
	[SerializeField] RectTransform overlayRoot;
	[SerializeField] GrindBladeBody blade;
	[SerializeField] EdgeGrindEvaluator evaluator;
	[SerializeField] GrindMetalView metalView;
	[SerializeField] GrindstoneWheel wheel;
	[SerializeField] GrindSparks sparks;
	[SerializeField] Transform stageRoot;
	[SerializeField] Canvas overlayCanvas;
	[SerializeField] RenderTexture grindTexture;

	[Header("Stage")]
	[SerializeField] Vector3 stageWorldPosition = new Vector3(40f, 180f, 0f);
	[SerializeField] float cameraDistance = 8f;
	[SerializeField] int renderTextureSize = 1024;
	[SerializeField] Color stageBackground = new Color(0.22f, 0.08f, 0.18f, 1f);
	[SerializeField] Color plateColor = new Color(0.16f, 0.07f, 0.14f, 1f);
	[SerializeField] bool autoFitCamera = true;

	[Header("Controls")]
	[SerializeField] float moveSpeed = 3.2f;
	[SerializeField] float rotateSpeed = 140f;
	[SerializeField] float stickDeadzone = 0.25f;
	[SerializeField] float kickbackStrength = 4.2f;
	[SerializeField] float grindRate = 0.85f;
	[SerializeField] float grindRadius = 0.42f;
	[SerializeField] float sparkPressureThreshold = 0.03f;
	[SerializeField] float spawnClearanceBelowStone = 0.35f;

	[Header("Quality Text Colors")]
	[SerializeField] Color bluntColor = new Color(0.65f, 0.62f, 0.6f, 1f);
	[SerializeField] Color dullColor = new Color(0.75f, 0.7f, 0.55f, 1f);
	[SerializeField] Color fineColor = new Color(0.9f, 0.92f, 0.95f, 1f);
	[SerializeField] Color honedColor = new Color(0.75f, 0.9f, 1f, 1f);
	[SerializeField] Color keenColor = new Color(0.55f, 0.95f, 1f, 1f);

	readonly List<Vector2> vertexScratch = new List<Vector2>();
	readonly List<float> grindScratch = new List<float>();
	bool runtimeOwnedTexture;

	Grindstone activeStation;
	HeatableMetal activeMetal;
	Vector2 stageOrigin;
	float closeInputUnblockTime;

	public bool IsOpen { get; private set; }
	public static bool IsBlockingPlayer => Instance != null && Instance.IsOpen;

	public static GrindSessionController EnsureExists()
	{
		if (Instance != null)
			return Instance;

		var existing = FindFirstObjectByType<GrindSessionController>();
		if (existing != null)
			return existing;

		var go = new GameObject("GrindSession");
		return go.AddComponent<GrindSessionController>();
	}

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		EnsureStage();
		EnsureOverlay();
		SetOverlayVisible(false);
	}

	void OnDestroy()
	{
		if (Instance == this)
			Instance = null;

		if (runtimeOwnedTexture && grindTexture != null)
		{
			if (grindCamera != null)
				grindCamera.targetTexture = null;
			grindTexture.Release();
			Destroy(grindTexture);
		}
	}

	void Update()
	{
		if (!IsOpen)
			return;

		HandleGrindInput();
		TickContact();
		UpdateStatus();
	}

	public void BeginSession(Grindstone station, HeatableMetal metal)
	{
		if (station == null || metal == null)
			return;

		EnsureStage();
		EnsureOverlay();

		if (IsOpen)
			EndSession();

		activeStation = station;
		activeMetal = metal;
		stageOrigin = stageRoot != null
			? new Vector2(stageRoot.position.x, stageRoot.position.y)
			: new Vector2(stageWorldPosition.x, stageWorldPosition.y);

		wheel.Configure(stageOrigin, ForgingVisualUtility.ResolveForgeLayer());
		LoadBlade(metal);

		PartDefinition part = metal.PartDefinition;
		if (part != null)
			part.EnsureSharpeningFlagsMatchOutline();

		Vector2[] outline = part != null && part.HasValidOutline
			? part.outlineLocal
			: PartDefinition.CreateDefaultAxeOutlineLocal();
		bool[] edgeSharpenFlags = part != null && part.outlineEdgeNeedsSharpening != null
			? part.outlineEdgeNeedsSharpening
			: System.Array.Empty<bool>();

		evaluator.Configure(blade, outline, edgeSharpenFlags);
		metalView.Configure(blade, evaluator);
		if (sparks != null)
			sparks.Configure(stageRoot, ForgingVisualUtility.ResolveForgeLayer());

		ForgingVisualUtility.ApplyLayerRecursively(stageRoot.gameObject, ForgingVisualUtility.ResolveForgeLayer());

		if (autoFitCamera)
			FitCamera();
		if (grindCamera != null)
			grindCamera.enabled = true;

		closeInputUnblockTime = Time.unscaledTime + 0.25f;
		IsOpen = true;
		SetOverlayVisible(true);
		UpdateStatus();
	}

	public void EndSession()
	{
		if (!IsOpen && activeMetal == null)
			return;

		SaveActiveMetal();
		if (sparks != null)
			sparks.Stop();
		if (wheel != null)
			wheel.SetSpinning(false);

		activeStation = null;
		activeMetal = null;
		IsOpen = false;
		SetOverlayVisible(false);
		if (grindCamera != null)
			grindCamera.enabled = false;
	}

	public void NotifyStationEmptied(Grindstone station)
	{
		if (!IsOpen || activeStation != station)
			return;

		EndSession();
	}

	void LoadBlade(HeatableMetal metal)
	{
		vertexScratch.Clear();
		grindScratch.Clear();

		// Prefer the piece's actual forged silhouette (even if messy), then grind save, then authored outline.
		if (metal.HasGrindProgress)
		{
			for (int i = 0; i < metal.GroundVertices.Count; i++)
				vertexScratch.Add(metal.GroundVertices[i]);
			for (int i = 0; i < metal.GrindAmounts.Count; i++)
				grindScratch.Add(metal.GrindAmounts[i]);
		}
		else if (metal.HasForgeProgress)
		{
			for (int i = 0; i < metal.ForgedVertices.Count; i++)
				vertexScratch.Add(metal.ForgedVertices[i]);
		}
		else if (metal.PartDefinition != null && metal.PartDefinition.HasValidOutline)
		{
			for (int i = 0; i < metal.PartDefinition.outlineLocal.Length; i++)
				vertexScratch.Add(metal.PartDefinition.outlineLocal[i]);
		}
		else
		{
			Vector2[] fallback = PartDefinition.CreateDefaultAxeOutlineLocal();
			for (int i = 0; i < fallback.Length; i++)
				vertexScratch.Add(fallback[i]);
		}

		while (grindScratch.Count < vertexScratch.Count)
			grindScratch.Add(0f);

		// Finer perimeter so one edge can show multiple grind bands along its length.
		if (!metal.HasGrindProgress)
			GrindBladeBody.DensifyShape(vertexScratch, grindScratch, 0.18f, 72);

		// Spawn low and fully outside the stone's contact range.
		Vector2 spawn = stageOrigin + new Vector2(0f, -1.35f);
		blade.LoadShape(vertexScratch, grindScratch, spawn, -20f);
		EnsureBladeSpawnedBelowStone();
	}

	void EnsureBladeSpawnedBelowStone()
	{
		if (blade == null || wheel == null)
			return;

		Bounds bounds = blade.GetWorldBounds();
		float stoneBottom = wheel.Center.y - wheel.Radius;
		float targetTop = stoneBottom - spawnClearanceBelowStone;
		if (bounds.max.y <= targetTop)
			return;

		float shiftDown = bounds.max.y - targetTop;
		blade.Position = new Vector2(blade.Position.x, blade.Position.y - shiftDown);
		blade.Position = ClampToView(blade.Position);
	}

	void SaveActiveMetal()
	{
		if (activeMetal == null || blade == null || blade.VertexCount < 3)
			return;

		// Persist the pre-grind silhouette + amounts so reload does not double-apply inward carve.
		blade.CopyInitialLocalVerticesTo(vertexScratch);
		blade.CopyGrindAmountsTo(grindScratch);
		SharpnessQuality quality = evaluator != null ? evaluator.Quality : SharpnessQuality.Blunt;
		activeMetal.SaveGrindProgress(vertexScratch, grindScratch, quality);
	}

	void HandleGrindInput()
	{
		if (blade == null)
			return;

		if (HandleCloseInput())
			return;

		Gamepad pad = Gamepad.current;
		if (pad == null)
			return;

		Vector2 move = pad.leftStick.ReadValue();
		if (move.sqrMagnitude > stickDeadzone * stickDeadzone)
		{
			if (move.sqrMagnitude > 1f)
				move.Normalize();
			blade.Position += move * moveSpeed * Time.unscaledDeltaTime;
		}

		Vector2 rotStick = pad.rightStick.ReadValue();
		if (Mathf.Abs(rotStick.x) > stickDeadzone)
			blade.RotationDegrees += rotStick.x * rotateSpeed * Time.unscaledDeltaTime;

		blade.Position = ClampToView(blade.Position);
	}

	bool HandleCloseInput()
	{
		if (Time.unscaledTime < closeInputUnblockTime)
			return false;

		Gamepad pad = Gamepad.current;
		if (pad != null && (
			pad.buttonEast.wasPressedThisFrame ||
			pad.buttonNorth.wasPressedThisFrame ||
			pad.startButton.wasPressedThisFrame ||
			pad.selectButton.wasPressedThisFrame))
		{
			EndSession();
			return true;
		}

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
		{
			EndSession();
			return true;
		}

		return false;
	}

	void TickContact()
	{
		if (blade == null || wheel == null)
			return;

		bool contacting = blade.TryGetStoneContact(wheel.Center, wheel.Radius, out Vector2 contact, out _, out float penetration);
		wheel.SetSpinning(contacting);

		if (!contacting)
		{
			if (sparks != null)
				sparks.Stop();
			return;
		}

		// Kickback only pushes the part downward.
		blade.Position += Vector2.down * (penetration * kickbackStrength * Time.unscaledDeltaTime);
		blade.Position = ClampToView(blade.Position);

		float pressure = Mathf.Clamp01(penetration / Mathf.Max(0.05f, wheel.Radius * 0.45f));
		float applied = blade.ApplyGrind(contact, grindRadius, grindRate * pressure * Time.unscaledDeltaTime);

		if (sparks != null)
		{
			if (applied > 0f && pressure >= sparkPressureThreshold)
				sparks.EmitAt(contact, Vector2.down, pressure);
			else
				sparks.Stop();
		}
	}

	Vector2 ClampToView(Vector2 pos)
	{
		if (grindCamera == null)
			return pos;

		Vector3 camPos = grindCamera.transform.position;
		float halfH = grindCamera.orthographicSize * 0.9f;
		float halfW = halfH * Mathf.Max(0.1f, grindCamera.aspect);
		pos.x = Mathf.Clamp(pos.x, camPos.x - halfW, camPos.x + halfW);
		pos.y = Mathf.Clamp(pos.y, camPos.y - halfH, camPos.y + halfH);
		return pos;
	}

	void UpdateStatus()
	{
		if (statusText == null)
			return;

		string partName = activeMetal != null && activeMetal.PartDefinition != null
			? activeMetal.PartDefinition.DisplayLabel
			: "Blade";
		SharpnessQuality quality = evaluator != null ? evaluator.Quality : SharpnessQuality.Blunt;
		int match = evaluator != null ? Mathf.RoundToInt(evaluator.MatchPercent * 100f) : 0;
		int waste = evaluator != null ? Mathf.RoundToInt(evaluator.WastePercent * 100f) : 0;
		statusText.text =
			$"{partName}\n{quality}  {match}%\nWaste {waste}%\nLS move  ·  RS rotate  ·  B / Y finish";

		if (qualityText != null)
		{
			qualityText.text = quality.ToString().ToUpperInvariant();
			qualityText.color = quality switch
			{
				SharpnessQuality.Dull => dullColor,
				SharpnessQuality.Fine => fineColor,
				SharpnessQuality.Honed => honedColor,
				SharpnessQuality.Keen => keenColor,
				_ => bluntColor
			};
		}
	}

	void FitCamera()
	{
		if (grindCamera == null || blade == null)
			return;

		Bounds bounds = blade.GetWorldBounds();
		bounds.Encapsulate(wheel != null ? (Vector3)wheel.Center : stageOrigin);
		float extent = Mathf.Max(bounds.extents.x, bounds.extents.y) + 0.9f;
		grindCamera.orthographicSize = Mathf.Max(2.4f, extent);
		Rect viewRect = viewport != null ? viewport.rectTransform.rect : new Rect(0f, 0f, 1f, 1f);
		if (viewRect.height > 0.001f)
			grindCamera.aspect = viewRect.width / viewRect.height;
		grindCamera.transform.position = new Vector3(
			bounds.center.x,
			bounds.center.y,
			(stageRoot != null ? stageRoot.position.z : stageWorldPosition.z) - cameraDistance);
		grindCamera.transform.rotation = Quaternion.identity;
	}

	void SetOverlayVisible(bool visible)
	{
		if (overlayRoot != null)
			overlayRoot.gameObject.SetActive(visible);
		if (overlayCanvas != null)
			overlayCanvas.enabled = visible;
	}

	void EnsureStage()
	{
		int grindLayer = ForgingVisualUtility.ResolveForgeLayer();
		ExcludeFromShopCameras();

		if (stageRoot == null)
		{
			var stageGo = new GameObject("GrindStage");
			stageGo.transform.SetParent(transform, false);
			stageGo.transform.position = stageWorldPosition;
			stageRoot = stageGo.transform;
		}

		if (blade == null)
			blade = stageRoot.GetComponent<GrindBladeBody>();
		if (blade == null)
			blade = stageRoot.gameObject.AddComponent<GrindBladeBody>();

		if (metalView == null)
			metalView = stageRoot.GetComponent<GrindMetalView>();
		if (metalView == null)
			metalView = stageRoot.gameObject.AddComponent<GrindMetalView>();

		if (evaluator == null)
			evaluator = stageRoot.GetComponent<EdgeGrindEvaluator>();
		if (evaluator == null)
			evaluator = stageRoot.gameObject.AddComponent<EdgeGrindEvaluator>();

		if (wheel == null)
		{
			Transform existing = stageRoot.Find("GrindstoneWheel");
			if (existing != null)
				wheel = existing.GetComponent<GrindstoneWheel>();
		}

		if (wheel == null)
		{
			var wheelGo = new GameObject("GrindstoneWheel");
			wheelGo.transform.SetParent(stageRoot, false);
			wheel = wheelGo.AddComponent<GrindstoneWheel>();
		}

		if (sparks == null)
		{
			Transform existing = stageRoot.Find("GrindSparks");
			if (existing != null)
				sparks = existing.GetComponent<GrindSparks>();
		}

		if (sparks == null)
		{
			var sparksGo = new GameObject("GrindSparks");
			sparksGo.transform.SetParent(stageRoot, false);
			sparks = sparksGo.AddComponent<GrindSparks>();
		}

		EnsureBackgroundPlate(grindLayer);
		EnsureGrindCamera(grindLayer);
		ForgingVisualUtility.ApplyLayerRecursively(stageRoot.gameObject, grindLayer);
	}

	void ExcludeFromShopCameras()
	{
		var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
		for (int i = 0; i < cameras.Length; i++)
		{
			Camera cam = cameras[i];
			if (cam == null || cam == grindCamera)
				continue;
			ForgingVisualUtility.ExcludeForgeLayer(cam);
		}
	}

	void EnsureBackgroundPlate(int grindLayer)
	{
		Transform existing = stageRoot.Find("GrindPlate");
		if (existing != null)
			return;

		var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
		plate.name = "GrindPlate";
		plate.transform.SetParent(stageRoot, false);
		plate.transform.localPosition = new Vector3(0f, 0f, 0.45f);
		plate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
		plate.transform.localScale = new Vector3(8f, 8f, 1f);
		Object.Destroy(plate.GetComponent<Collider>());
		var renderer = plate.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = ForgingVisualUtility.CreateColorMaterial(plateColor);
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		renderer.sortingOrder = 0;
		plate.layer = grindLayer;
	}

	void EnsureGrindCamera(int grindLayer)
	{
		if (grindTexture == null)
		{
			grindTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16)
			{
				name = "GrindView",
				filterMode = FilterMode.Point,
				antiAliasing = 1
			};
			grindTexture.Create();
			runtimeOwnedTexture = true;
		}

		if (grindCamera == null)
		{
			var camGo = new GameObject("GrindCamera");
			camGo.transform.SetParent(transform, false);
			grindCamera = camGo.AddComponent<Camera>();
			var extra = camGo.GetComponent<UniversalAdditionalCameraData>();
			if (extra == null)
				extra = camGo.AddComponent<UniversalAdditionalCameraData>();
			extra.renderType = CameraRenderType.Base;
			extra.renderPostProcessing = false;
			extra.renderShadows = false;
			grindCamera.orthographic = true;
			grindCamera.clearFlags = CameraClearFlags.SolidColor;
			grindCamera.backgroundColor = stageBackground;
			grindCamera.nearClipPlane = 0.1f;
			grindCamera.farClipPlane = 40f;
			grindCamera.depth = -11;
			grindCamera.enabled = false;
		}

		grindCamera.cullingMask = 1 << grindLayer;
		grindCamera.targetTexture = grindTexture;
		grindCamera.transform.position = stageWorldPosition + new Vector3(0f, 0f, -cameraDistance);
		grindCamera.transform.rotation = Quaternion.identity;
	}

	void EnsureOverlay()
	{
		if (overlayRoot != null && viewport != null)
		{
			if (viewport.texture == null && grindTexture != null)
				viewport.texture = grindTexture;
			if (qualityText == null)
				CreateQualityText(overlayRoot.Find("GrindFrame") ?? overlayRoot);
			return;
		}

		var canvasGo = new GameObject("GrindOverlayCanvas");
		canvasGo.transform.SetParent(transform, false);
		canvasGo.layer = 5;
		overlayCanvas = canvasGo.AddComponent<Canvas>();
		overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
		overlayCanvas.sortingOrder = 85;
		var scaler = canvasGo.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.matchWidthOrHeight = 0.5f;
		canvasGo.AddComponent<GraphicRaycaster>();

		overlayRoot = canvasGo.GetComponent<RectTransform>();
		CreateFullRectImage(overlayRoot, "Dimmer", new Color(0.02f, 0.01f, 0.03f, 0.72f));

		var panel = CreatePanel(overlayRoot, "GrindFrame", new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f));
		frameImage = panel.GetComponent<Image>();
		frameImage.color = new Color(0.22f, 0.12f, 0.18f, 0.96f);
		if (frameSprite != null)
		{
			frameImage.sprite = frameSprite;
			frameImage.type = Image.Type.Sliced;
			frameImage.color = Color.white;
		}

		var inner = CreatePanel(panel.transform, "ViewportFrame", new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.9f));
		inner.GetComponent<Image>().color = new Color(0.08f, 0.04f, 0.07f, 1f);

		var viewportGo = new GameObject("GrindViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		viewportGo.transform.SetParent(inner.transform, false);
		viewport = viewportGo.GetComponent<RawImage>();
		viewport.texture = grindTexture;
		viewport.color = Color.white;
		Stretch(viewport.rectTransform, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f));
		var aspect = viewportGo.AddComponent<AspectRatioFitter>();
		aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
		aspect.aspectRatio = 1f;

		statusText = CreateTmp(panel.transform, "StatusText", 22f, TextAlignmentOptions.TopLeft);
		var statusRect = statusText.rectTransform;
		statusRect.anchorMin = new Vector2(0.08f, 0.02f);
		statusRect.anchorMax = new Vector2(0.55f, 0.16f);
		statusRect.offsetMin = Vector2.zero;
		statusRect.offsetMax = Vector2.zero;
		statusText.color = new Color(0.95f, 0.86f, 0.78f, 1f);

		CreateQualityText(panel.transform);
	}

	void CreateQualityText(Transform parent)
	{
		if (qualityText != null || parent == null)
			return;

		qualityText = CreateTmp(parent, "QualityText", 52f, TextAlignmentOptions.Center);
		var rect = qualityText.rectTransform;
		rect.anchorMin = new Vector2(0.55f, 0.02f);
		rect.anchorMax = new Vector2(0.92f, 0.16f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		qualityText.fontStyle = FontStyles.Bold;
		qualityText.color = fineColor;
		qualityText.text = "BLUNT";
	}

	static Image CreateFullRectImage(Transform parent, string name, Color color)
	{
		var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		go.transform.SetParent(parent, false);
		var image = go.GetComponent<Image>();
		image.color = color;
		Stretch(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
		return image;
	}

	static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
	{
		var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		go.transform.SetParent(parent, false);
		Stretch(go.GetComponent<RectTransform>(), anchorMin, anchorMax);
		return go;
	}

	static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
	{
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	static TextMeshProUGUI CreateTmp(Transform parent, string name, float size, TextAlignmentOptions align)
	{
		var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
		go.transform.SetParent(parent, false);
		var tmp = go.GetComponent<TextMeshProUGUI>();
		tmp.fontSize = size;
		tmp.alignment = align;
		tmp.textWrappingMode = TextWrappingModes.Normal;
		tmp.raycastTarget = false;
		if (TMP_Settings.defaultFontAsset != null)
			tmp.font = TMP_Settings.defaultFontAsset;
		return tmp;
	}
}
