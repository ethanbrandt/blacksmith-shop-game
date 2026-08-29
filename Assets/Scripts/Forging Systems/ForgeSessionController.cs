using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class ForgeSessionController : MonoBehaviour
{
	public static ForgeSessionController Instance { get; private set; }

	[Header("Optional Overrides")]
	[SerializeField] Camera forgeCamera;
	[SerializeField] RawImage viewport;
	[SerializeField] Image frameImage;
	[Tooltip("Assign your stylized forge frame art here. Leave empty to use the placeholder panel.")]
	[SerializeField] Sprite frameSprite;
	[SerializeField] TextMeshProUGUI statusText;
	[SerializeField] TextMeshProUGUI qualityText;
	[SerializeField] RectTransform overlayRoot;
	[SerializeField] MetalDeformer2D deformer;
	[SerializeField] ShapeMatchEvaluator evaluator;
	[SerializeField] ForgeMetalView metalView;
	[SerializeField] ForgeHammerPreview hammerPreview;
	[SerializeField] Transform stageRoot;
	[SerializeField] Canvas overlayCanvas;
	[SerializeField] RenderTexture forgeTexture;

	[Header("Stage")]
	[SerializeField] Vector3 stageWorldPosition = new Vector3(0f, 180f, 0f);
	[SerializeField] float cameraDistance = 8f;
	[SerializeField] int renderTextureSize = 1024;
	[SerializeField] Color stageBackground = new Color(0.12f, 0.09f, 0.08f, 1f);
	[SerializeField] Color plateColor = new Color(0.22f, 0.18f, 0.16f, 1f);
	[SerializeField] bool autoFitCamera = true;

	[Header("Quality Text Colors")]
	[SerializeField] Color incompleteColor;
	[SerializeField] Color flawedColor;
	[SerializeField] Color goodColor;
	[SerializeField] Color excellentColor;
	[SerializeField] Color perfectColor;
	
	[Header("Hammer")]
	[SerializeField] float minChargeTime = 0.08f;
	[SerializeField] float maxChargeTime = 0.45f;
	//[SerializeField] float minDragForAim = 12f;

	[Header("Controller")]
	[SerializeField] float cursorSpeed = 5.5f;
	[SerializeField] float stickDeadzone = 0.25f;
	[SerializeField] float mouseMovePixels = 1.5f;

	readonly List<Vector2> vertexScratch = new List<Vector2>();
	bool runtimeOwnedTexture;

	Anvil activeAnvil;
	HeatableMetal activeMetal;
	Vector2 forgeOrigin;
	Vector2 hammerPos;
	Vector2 lastAimDir;
	bool hasStickAim;
	bool usingGamepad;
	bool charging;
	Vector2 pressScreen;
	float pressTime;
	float closeInputUnblockTime;

	public bool IsOpen { get; private set; }
	public static bool IsBlockingPlayer => Instance != null && Instance.IsOpen;

	public static ForgeSessionController EnsureExists()
	{
		if (Instance != null)
			return Instance;

		var existing = FindFirstObjectByType<ForgeSessionController>();
		if (existing != null)
			return existing;

		var go = new GameObject("ForgeSession");
		return go.AddComponent<ForgeSessionController>();
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

		if (runtimeOwnedTexture && forgeTexture != null)
		{
			if (forgeCamera != null)
				forgeCamera.targetTexture = null;
			forgeTexture.Release();
			Destroy(forgeTexture);
		}
	}

	void Update()
	{
		if (!IsOpen)
			return;

		if (deformer != null)
			deformer.Heat = activeMetal.Heat01;
		
		UpdateStatus();
		HandleForgeInput();
	}

	public void BeginSession(Anvil anvil, HeatableMetal metal)
	{
		if (anvil == null || metal == null)
			return;

		EnsureStage();
		EnsureOverlay();

		if (IsOpen)
			EndSession();

		activeAnvil = anvil;
		activeMetal = metal;
		forgeOrigin = stageRoot != null ? new Vector2(stageRoot.position.x, stageRoot.position.y) : new Vector2(stageWorldPosition.x, stageWorldPosition.y);

		deformer.ShapeCenter = forgeOrigin;
		deformer.SetMetalType(metal.MetalType);
		if (metal.HasForgeProgress)
			LoadLocalVertices(metal.ForgedVertices);
		else
			deformer.InitializeShape();

		Vector2[] outline = metal.PartDefinition != null ? metal.PartDefinition.BuildForgeOutline(forgeOrigin) : System.Array.Empty<Vector2>();
		evaluator.Configure(deformer, outline.Length >= 3 ? outline : null);
		metalView.Configure(deformer);
		if (hammerPreview != null)
			hammerPreview.Hide();
		ForgingVisualUtility.ApplyLayerRecursively(stageRoot.gameObject, ForgingVisualUtility.ResolveForgeLayer());

		if (autoFitCamera)
			FitCamera();
		if (forgeCamera != null)
			forgeCamera.enabled = true;

		charging = false;
		hasStickAim = false;
		lastAimDir = Vector2.right;
		usingGamepad = Gamepad.current != null;
		hammerPos = deformer != null ? deformer.GetCentroid() : forgeOrigin;
		closeInputUnblockTime = Time.unscaledTime + 0.25f;
		IsOpen = true;
		SetOverlayVisible(true);
		UpdateStatus();
		UpdateHammerPreview();
	}

	public void EndSession()
	{
		if (!IsOpen && activeMetal == null)
			return;

		SaveActiveMetal();
		
		activeAnvil = null;
		activeMetal = null;
		charging = false;
		hasStickAim = false;
		if (hammerPreview != null)
			hammerPreview.Hide();
		IsOpen = false;
		SetOverlayVisible(false);
		if (forgeCamera != null)
			forgeCamera.enabled = false;
	}

	public void NotifyAnvilEmptied(Anvil anvil)
	{
		if (!IsOpen || activeAnvil != anvil)
			return;

		EndSession();
	}

	void SaveActiveMetal()
	{
		if (activeMetal == null || deformer == null || deformer.VertexCount < 3)
			return;

		vertexScratch.Clear();
		deformer.CopyVerticesTo(vertexScratch);
		for (int i = 0; i < vertexScratch.Count; i++)
			vertexScratch[i] -= forgeOrigin;

		ShapeQuality quality = evaluator != null ? evaluator.Quality : ShapeQuality.Incomplete;
		activeMetal.SaveForgeProgress(vertexScratch, deformer.Heat, quality, evaluator.MatchPercent, activeMetal.PartDefinition);
	}

	void LoadLocalVertices(IReadOnlyList<Vector2> local)
	{
		vertexScratch.Clear();
		for (int i = 0; i < local.Count; i++)
			vertexScratch.Add(local[i] + forgeOrigin);

		deformer.LoadVertices(vertexScratch, true);
	}

	void HandleForgeInput()
	{
		if (deformer == null)
			return;

		DetectInputDevice();
		if (HandleCloseInput())
			return;

		TickHammerCursor();
		UpdateHammerPreview();
		HandleStrikeButtons();
	}

	void DetectInputDevice()
	{
		Gamepad pad = Gamepad.current;
		Mouse mouse = Mouse.current;
		if (pad != null && (
			pad.leftStick.ReadValue().sqrMagnitude > stickDeadzone * stickDeadzone ||
			pad.rightStick.ReadValue().sqrMagnitude > stickDeadzone * stickDeadzone ||
			pad.rightTrigger.isPressed ||
			pad.buttonWest.isPressed ||
			pad.dpad.ReadValue().sqrMagnitude > 0.25f))
		{
			usingGamepad = true;
			return;
		}

		if (mouse != null && (
			mouse.delta.ReadValue().sqrMagnitude > mouseMovePixels * mouseMovePixels ||
			mouse.leftButton.isPressed))
		{
			usingGamepad = false;
		}
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

	void TickHammerCursor()
	{
		Vector2 move = Vector2.zero;
		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			Vector2 stick = pad.leftStick.ReadValue();
			if (stick.sqrMagnitude > stickDeadzone * stickDeadzone)
				move += stick;
			move += pad.dpad.ReadValue();
		}

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null)
		{
			if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
				move.y += 1f;
			if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
				move.y -= 1f;
			if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
				move.x -= 1f;
			if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
				move.x += 1f;
		}

		if (move.sqrMagnitude > 1f)
			move.Normalize();
		if (move.sqrMagnitude > 0.0001f)
			hammerPos += move * cursorSpeed * Time.unscaledDeltaTime;

		Mouse mouse = Mouse.current;
		if (!usingGamepad && mouse != null)
		{
			// TODO Fix mouse input
			/*
			Vector2 screen = mouse.position.ReadValue();
			if (IsScreenOver(viewport != null ? viewport.rectTransform : null, screen) && TryScreenToForgePoint(screen, out Vector2 mouseForge))
				hammerPos = mouseForge;
			*/
		}

		hammerPos = ClampToForgeView(hammerPos);
	}

	Vector2 ClampToForgeView(Vector2 pos)
	{
		if (forgeCamera == null)
			return pos;

		Vector3 camPos = forgeCamera.transform.position;
		float halfH = forgeCamera.orthographicSize * 0.9f;
		float halfW = halfH * Mathf.Max(0.1f, forgeCamera.aspect);
		pos.x = Mathf.Clamp(pos.x, camPos.x - halfW, camPos.x + halfW);
		pos.y = Mathf.Clamp(pos.y, camPos.y - halfH, camPos.y + halfH);
		return pos;
	}

	void HandleStrikeButtons()
	{
		Mouse mouse = Mouse.current;
		Vector2 mouseScreen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

		bool pressed = StrikePressed();
		bool released = StrikeReleased();

		if (pressed)
		{
			charging = true;
			pressTime = Time.unscaledTime;
			/*
			 TODO fix mouse input
			if (mouse != null)
				pressScreen = mouseScreen;
			*/	
		}

		if (!charging || !released)
			return;

		float charge01 = CurrentCharge01();
		charging = false;
		ResolveStrike(out Vector2 impact, out Vector2 direction, out _);
		deformer.TryStrike(impact, direction, charge01);
		if (hammerPreview != null)
			hammerPreview.PlayStrikeFlash(impact, direction, deformer.ImpactRadius);
	}

	bool StrikePressed()
	{
		Gamepad pad = Gamepad.current;
		if (pad != null && (pad.rightTrigger.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame))
			return true;
		
		return false;
		
		//TODO Fix mouse input
		/*
		Mouse mouse = Mouse.current;
		if (mouse != null && mouse.leftButton.wasPressedThisFrame)
			return true;

		Keyboard keyboard = Keyboard.current;
		return keyboard != null && keyboard.enterKey.wasPressedThisFrame;
		*/
	}

	bool StrikeReleased()
	{
		Gamepad pad = Gamepad.current;
		if (pad != null && (pad.rightTrigger.wasReleasedThisFrame || pad.buttonWest.wasReleasedThisFrame))
			return true;
		
		return false;
		
		//TODO Fix mouse input	
		/*
		Mouse mouse = Mouse.current;
		if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
			return true;

		Keyboard keyboard = Keyboard.current;
		return keyboard != null && keyboard.enterKey.wasReleasedThisFrame;
		*/
	}

	void UpdateHammerPreview()
	{
		if (hammerPreview == null || deformer == null)
			return;

		if (!charging && hammerPreview.IsFlashing)
			return;

		ResolveStrike(out Vector2 impact, out Vector2 direction, out _);
		hammerPreview.ShowAim(impact, direction, deformer.ImpactRadius, CurrentCharge01());
	}

	float CurrentCharge01()
	{
		if (!charging)
			return 0f;

		return Mathf.InverseLerp(minChargeTime, maxChargeTime, Time.unscaledTime - pressTime);
	}

	void ResolveStrike(out Vector2 impact, out Vector2 direction, out float charge01)
	{
		impact = hammerPos;
		charge01 = CurrentCharge01();
		Vector2 requested = Vector2.zero;

		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			Vector2 aimStick = pad.rightStick.ReadValue();
			if (aimStick.sqrMagnitude > stickDeadzone * stickDeadzone)
			{
				requested = aimStick;
				lastAimDir = aimStick;
				hasStickAim = true;
			}
			else if (hasStickAim)
				requested = lastAimDir;
		}

		if (!usingGamepad && charging)
		{
			// TODO Fix mouse forging controls
			/*
			Mouse mouse = Mouse.current;
			if (mouse != null)
			{
				Vector2 screen = mouse.position.ReadValue();
				Vector2 drag = screen - pressScreen;
				if (drag.sqrMagnitude >= minDragForAim * minDragForAim && TryScreenToForgePoint(pressScreen, out Vector2 pressForge) && TryScreenToForgePoint(screen, out Vector2 aimForge))
				{
					impact = pressForge;
					requested = aimForge - pressForge;
				}
			}
			*/
		}
		
		direction = requested.sqrMagnitude >= 0.0001f ? requested.normalized : Vector2.zero;
	}

	void UpdateStatus()
	{
		if (statusText == null)
			return;

		string partName = activeMetal != null && activeMetal.PartDefinition != null ? activeMetal.PartDefinition.DisplayLabel : "Billet";
		string metalName = deformer != null ? deformer.MetalDisplayName : "Metal";
		float heat01 = deformer != null ? deformer.Heat : 0f;
		bool canForge = deformer == null || deformer.CurrentFeel.canForge;
		ShapeQuality quality = evaluator != null ? evaluator.Quality : ShapeQuality.Incomplete;
		int match = evaluator != null ? Mathf.RoundToInt(evaluator.MatchPercent * 100f) : 0;
		string heatLabel = canForge ? "Working" : "Too cold";
		statusText.text = $"{partName}  ·  {metalName}\nHeat {Mathf.RoundToInt(heat01 * 100f)}%  {heatLabel}\n{quality.ToString()}  {match}%\nLS move  ·  RS aim  ·  RT / X swing  ·  B / Y finish";
		qualityText.text = quality.ToString().ToUpper();
		if (quality == ShapeQuality.Flawed)
			qualityText.color = flawedColor;
		else if (quality == ShapeQuality.Good)
    			qualityText.color = goodColor;
		else if (quality == ShapeQuality.Excellent)
        			qualityText.color = excellentColor;
		else if (quality == ShapeQuality.Perfect)
			qualityText.color = perfectColor;
		else
			qualityText.color = incompleteColor;
	}

	void FitCamera()
	{
		if (forgeCamera == null || deformer == null)
			return;

		Bounds bounds = deformer.GetBounds();
		if (evaluator != null && evaluator.TargetVertices != null && evaluator.TargetVertices.Count >= 3)
		{
			var targetBounds = new Bounds(evaluator.TargetVertices[0], Vector3.zero);
			for (int i = 1; i < evaluator.TargetVertices.Count; i++)
				targetBounds.Encapsulate(evaluator.TargetVertices[i]);
			bounds.Encapsulate(targetBounds);
		}

		float extent = Mathf.Max(bounds.extents.x, bounds.extents.y) + 0.65f;
		forgeCamera.orthographicSize = Mathf.Max(2.1f, extent);
		Rect viewRect = viewport != null ? viewport.rectTransform.rect : new Rect(0f, 0f, 1f, 1f);
		if (viewRect.height > 0.001f)
			forgeCamera.aspect = viewRect.width / viewRect.height;
		forgeCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, (stageRoot != null ? stageRoot.position.z : stageWorldPosition.z) - cameraDistance);
		forgeCamera.transform.rotation = Quaternion.identity;
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
		int forgeLayer = ForgingVisualUtility.ResolveForgeLayer();
		ExcludeForgeFromShopCameras();

		if (stageRoot == null)
		{
			var stageGo = new GameObject("ForgeStage");
			stageGo.transform.SetParent(transform, false);
			stageGo.transform.position = stageWorldPosition;
			stageRoot = stageGo.transform;
		}

		if (deformer == null)
			deformer = stageRoot.GetComponent<MetalDeformer2D>();
		if (deformer == null)
			deformer = stageRoot.gameObject.AddComponent<MetalDeformer2D>();

		if (metalView == null)
			metalView = stageRoot.GetComponent<ForgeMetalView>();
		if (metalView == null)
			metalView = stageRoot.gameObject.AddComponent<ForgeMetalView>();

		if (evaluator == null)
			evaluator = stageRoot.GetComponent<ShapeMatchEvaluator>();
		if (evaluator == null)
			evaluator = stageRoot.gameObject.AddComponent<ShapeMatchEvaluator>();

		if (hammerPreview == null)
			hammerPreview = stageRoot.GetComponent<ForgeHammerPreview>();
		if (hammerPreview == null)
			hammerPreview = stageRoot.gameObject.AddComponent<ForgeHammerPreview>();

		EnsureBackgroundPlate(forgeLayer);
		EnsureForgeCamera(forgeLayer);
		ForgingVisualUtility.ApplyLayerRecursively(stageRoot.gameObject, forgeLayer);
	}

	void ExcludeForgeFromShopCameras()
	{
		var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
		for (int i = 0; i < cameras.Length; i++)
		{
			Camera cam = cameras[i];
			if (cam == null || cam == forgeCamera)
				continue;
			ForgingVisualUtility.ExcludeForgeLayer(cam);
		}
	}

	void EnsureBackgroundPlate(int forgeLayer)
	{
		Transform existing = stageRoot.Find("AnvilPlate");
		if (existing != null)
			return;

		var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
		plate.name = "AnvilPlate";
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
		plate.layer = forgeLayer;
	}

	void EnsureForgeCamera(int forgeLayer)
	{
		if (forgeTexture == null)
		{
			forgeTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16)
			{
				name = "ForgeView",
				filterMode = FilterMode.Point,
				antiAliasing = 1
			};
			forgeTexture.Create();
			runtimeOwnedTexture = true;
		}

		if (forgeCamera == null)
		{
			var camGo = new GameObject("ForgeCamera");
			camGo.transform.SetParent(transform, false);
			forgeCamera = camGo.AddComponent<Camera>();
			var extra = camGo.GetComponent<UniversalAdditionalCameraData>();
			if (extra == null)
				extra = camGo.AddComponent<UniversalAdditionalCameraData>();
			extra.renderType = CameraRenderType.Base;
			extra.renderPostProcessing = false;
			extra.renderShadows = false;
			forgeCamera.orthographic = true;
			forgeCamera.clearFlags = CameraClearFlags.SolidColor;
			forgeCamera.backgroundColor = stageBackground;
			forgeCamera.nearClipPlane = 0.1f;
			forgeCamera.farClipPlane = 40f;
			forgeCamera.depth = -10;
			forgeCamera.enabled = false;
		}

		forgeCamera.cullingMask = 1 << forgeLayer;
		forgeCamera.targetTexture = forgeTexture;
		forgeCamera.transform.position = stageWorldPosition + new Vector3(0f, 0f, -cameraDistance);
		forgeCamera.transform.rotation = Quaternion.identity;
	}

	void EnsureOverlay()
	{
		if (overlayRoot != null && viewport != null)
		{
			if (viewport.texture == null && forgeTexture != null)
				viewport.texture = forgeTexture;
			return;
		}

		var canvasGo = new GameObject("ForgeOverlayCanvas");
		canvasGo.transform.SetParent(transform, false);
		canvasGo.layer = 5;
		overlayCanvas = canvasGo.AddComponent<Canvas>();
		overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
		overlayCanvas.sortingOrder = 80;
		var scaler = canvasGo.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.matchWidthOrHeight = 0.5f;
		canvasGo.AddComponent<GraphicRaycaster>();

		overlayRoot = canvasGo.GetComponent<RectTransform>();

		CreateFullRectImage(overlayRoot, "Dimmer", new Color(0.02f, 0.01f, 0.01f, 0.72f));

		var panel = CreatePanel(overlayRoot, "ForgeFrame", new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f));
		frameImage = panel.GetComponent<Image>();
		frameImage.color = new Color(0.28f, 0.16f, 0.1f, 0.96f);
		if (frameSprite != null)
		{
			frameImage.sprite = frameSprite;
			frameImage.type = Image.Type.Sliced;
			frameImage.color = Color.white;
		}

		var inner = CreatePanel(panel.transform, "ViewportFrame", new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.9f));
		inner.GetComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 1f);

		var viewportGo = new GameObject("ForgeViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		viewportGo.transform.SetParent(inner.transform, false);
		viewport = viewportGo.GetComponent<RawImage>();
		viewport.texture = forgeTexture;
		viewport.color = Color.white;
		Stretch(viewport.rectTransform, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f));
		var aspect = viewportGo.AddComponent<AspectRatioFitter>();
		aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
		aspect.aspectRatio = 1f;

		statusText = CreateTmp(panel.transform, "StatusText", 22f, TextAlignmentOptions.TopLeft);
		var statusRect = statusText.rectTransform;
		statusRect.anchorMin = new Vector2(0.08f, 0.02f);
		statusRect.anchorMax = new Vector2(0.7f, 0.15f);
		statusRect.offsetMin = Vector2.zero;
		statusRect.offsetMax = Vector2.zero;
		statusText.color = new Color(0.95f, 0.86f, 0.7f, 1f);
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
