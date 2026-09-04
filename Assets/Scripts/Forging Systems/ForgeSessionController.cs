using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class ForgeSessionController : MonoBehaviour
{
	public static ForgeSessionController Instance { get; private set; }

	[Header("Optional Overrides")]
	[SerializeField] Camera forgeCamera;
	[SerializeField] RawImage viewport;
	[SerializeField] MetalDeformer2D deformer;
	[SerializeField] ShapeMatchEvaluator evaluator;
	[SerializeField] ForgeMetalView metalView;
	[SerializeField] ForgeHammerPreview hammerPreview;
	[SerializeField] Transform stageRoot;
	[SerializeField] RenderTexture forgeTexture;

	[Header("Stage")]
	[SerializeField] Vector3 stageWorldPosition = new Vector3(0f, 180f, 0f);
	[SerializeField] float cameraDistance = 8f;
	[SerializeField] int renderTextureSize = 1024;
	[SerializeField] Color stageBackground = new Color(0.12f, 0.09f, 0.08f, 1f);
	[SerializeField] Color plateColor = new Color(0.22f, 0.18f, 0.16f, 1f);
	[SerializeField] bool autoFitCamera = true;

	[Header("Hammer")]
	[SerializeField] float minChargeTime = 0.08f;
	[SerializeField] float maxChargeTime = 0.45f;

	[Header("Controller")]
	[SerializeField] float cursorSpeed = 5.5f;
	[SerializeField] float stickDeadzone = 0.25f;

	readonly List<Vector2> vertexScratch = new List<Vector2>();
	bool runtimeOwnedTexture;

	Anvil activeAnvil;
	HeatableMetal activeMetal;
	Vector2 forgeOrigin;
	Vector2 hammerPos;
	Vector2 lastAimDir;
	bool hasStickAim;
	bool charging;
	Vector2 pressScreen;
	float pressTime;
	float closeInputUnblockTime;
	private UIDocument document;

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


		document = GetComponentInChildren<UIDocument>();
		SetHidden(true);
		
		EnsureStage();
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

	private void SetHidden(bool _isHidden)
	{
		document.rootVisualElement.EnableInClassList("is-hidden", _isHidden);
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
		hammerPos = deformer != null ? deformer.GetCentroid() : forgeOrigin;
		closeInputUnblockTime = Time.unscaledTime + 0.25f;
		IsOpen = true;
		UpdateStatus();
		UpdateHammerPreview();
		
		SetHidden(false);
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
		if (forgeCamera != null)
			forgeCamera.enabled = false;
		
		SetHidden(true);
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

		if (HandleCloseInput())
			return;

		TickHammerCursor();
		UpdateHammerPreview();
		HandleStrikeButtons();
	}

	bool HandleCloseInput()
	{
		if (Time.unscaledTime < closeInputUnblockTime)
			return false;

		Gamepad pad = Gamepad.current;
		if (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame))
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

		if (move.sqrMagnitude > 1f)
			move.Normalize();
		if (move.sqrMagnitude > 0.0001f)
			hammerPos += move * cursorSpeed * Time.unscaledDeltaTime;

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
		bool pressed = StrikePressed();
		bool released = StrikeReleased();

		if (pressed)
		{
			charging = true;
			pressTime = Time.unscaledTime;
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
	}

	bool StrikeReleased()
	{
		Gamepad pad = Gamepad.current;
		if (pad != null && (pad.rightTrigger.wasReleasedThisFrame || pad.buttonWest.wasReleasedThisFrame))
			return true;
		
		return false;
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
		
		direction = requested.sqrMagnitude >= 0.0001f ? requested.normalized : Vector2.zero;
	}

	void UpdateStatus()
	{
		
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
		Rect viewRect = viewport != null ? viewport.rectTransform.rect : new Rect(0f, 0f, forgeTexture.width, forgeTexture.height);
		forgeCamera.aspect = (float)forgeTexture.width / (float)forgeTexture.height;
		forgeCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, (stageRoot != null ? stageRoot.position.z : stageWorldPosition.z) - cameraDistance);
		forgeCamera.transform.rotation = Quaternion.identity;
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
		Destroy(plate.GetComponent<Collider>());
		var renderer = plate.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = ForgingVisualUtility.CreateColorMaterial(plateColor);
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		renderer.sortingOrder = 0;
		plate.layer = forgeLayer;
	}

	void EnsureForgeCamera(int forgeLayer)
	{
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
}
