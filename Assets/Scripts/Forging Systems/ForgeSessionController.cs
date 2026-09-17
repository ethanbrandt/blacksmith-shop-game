using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public class ForgeSessionController : MonoBehaviour
{
	const float CloseInputDelay = 0.25f;
	const float ViewClampFraction = 0.9f;
	const float MinimumCameraAspect = 0.1f;
	const float CameraBoundsPadding = 0.65f;
	const float MinimumCameraHalfHeight = 2.1f;
	const int RenderTextureDepthBits = 16;
	const float CameraNearClip = 0.1f;
	const float CameraFarClip = 40f;
	const int CameraRenderDepth = -10;
	const float BackgroundPlateDepth = 0.45f;
	const float BackgroundPlateFacingAngle = 180f;
	const float BackgroundPlateSize = 8f;
	const int MinimumRenderTextureSize = 16;
	const float MinimumSquaredAimMagnitude = 0.0001f;
	public static ForgeSessionController Instance { get; private set; }

	[Header("Optional Overrides")]
	[SerializeField] Camera forgeCamera;
	[SerializeField] MetalDeformer2D deformer;
	[SerializeField] ShapeMatchEvaluator evaluator;
	[SerializeField] ForgeMetalView metalView;
	[SerializeField] ForgeHammerPreview hammerPreview;
	[SerializeField] ForgeTargetView targetView;
	[SerializeField] Transform stageRoot;
	[SerializeField] RenderTexture forgeTexture;
	[SerializeField] UIDocument document;

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
	Vector2 hammerPosition;
	Vector2 lastAimDirection;
	bool hasStickAim;
	bool charging;
	float chargeStartTime;
	float closeInputUnblockTime;
	
	private VisualElement quenchTempVisualElement;
	private VisualElement normalTempVisualElement;
	private VisualElement coldTempVisualElement;
	private Slider heatSlider;

	private Label qualityLabel;
	private VisualElement dLight;
	private VisualElement cLight;
	private VisualElement bLight;
	private VisualElement aLight;
	private VisualElement sLight;

	public bool CanBegin(HeatableMetal metal)
	{
		bool canOwnSession = !IsOpen && StationSessionCoordinator.CanAcquire(this);
		if (!canOwnSession)
			return false;
		
		bool hasMetalType = metal != null && metal.MetalType != null;
		if (!hasMetalType)
			return false;
		
		bool hasValidPart = metal.PartDefinition != null && metal.PartDefinition.HasValidOutline;
		
		return hasValidPart;
	}

	public bool IsOpen { get; private set; }
	public static bool IsBlockingPlayer => Instance != null && Instance.IsOpen;

	public static ForgeSessionController EnsureExists()
	{
		if (Instance != null)
			return Instance;
		
		var existing = FindFirstObjectByType<ForgeSessionController>();
		
		if (existing != null)
			return existing;
		
		Debug.LogError("Forge requires a scene ForgeSessionController with a child UIDocument.");
		return null;
	}

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		
		SetHidden(false);
		EnsureDocumentElements();
		EnsureStage();
		SetHidden(true);
	}

	void EnsureDocumentElements()
	{
		var heatElement = document.rootVisualElement.Q<VisualElement>("HeatElement");

		heatSlider = heatElement.Q<Slider>("HeatSlider");
		
		var heatGaugeBackground = heatElement.Q<VisualElement>("HeatGaugeBackground");
		quenchTempVisualElement = heatGaugeBackground.Q<VisualElement>("QuenchTemp");
		normalTempVisualElement = heatGaugeBackground.Q<VisualElement>("NormalTemp");
		coldTempVisualElement = heatGaugeBackground.Q<VisualElement>("ColdTemp");

		var qualityElement = document.rootVisualElement.Q<VisualElement>("QualityElement");
		
		qualityLabel = qualityElement.Q<Label>("QualityLabel");
		
		dLight = qualityElement.Q<VisualElement>("LightD");
		cLight = qualityElement.Q<VisualElement>("LightC");
		bLight = qualityElement.Q<VisualElement>("LightB");
		aLight = qualityElement.Q<VisualElement>("LightA");
		sLight = qualityElement.Q<VisualElement>("LightS");
	}

	void OnDestroy()
	{
		StationSessionCoordinator.Release(this);
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

	private void SetHidden(bool isHidden)
	{
		if (document == null)
			return;
		
		if (!isHidden && !document.gameObject.activeSelf)
			document.gameObject.SetActive(true);

		if (document.rootVisualElement == null)
			return;
		
		document.rootVisualElement.style.display = isHidden ? DisplayStyle.None : DisplayStyle.Flex;
	}

	void OnDisable() => EndSession();

	private float statusTimer = 0f;
	void Update()
	{
		if (!IsOpen)
			return;
		
		if (activeMetal == null || activeAnvil == null)
		{
			EndSession();
			return;
		}

		if (deformer != null)
			deformer.Heat = activeMetal.Heat01;

		statusTimer -= Time.deltaTime;
		if (statusTimer <= 0f)
		{
			statusTimer = 0.25f;
			UpdateStatus();
		}
		
		HandleForgeInput();
	}

	public void BeginSession(Anvil anvil, HeatableMetal metal)
	{
		if (anvil == null || !CanBegin(metal))
			return;
		
		SetHidden(false);
		EnsureStage();
		SetHidden(true);
		
		if (!StationSessionCoordinator.TryAcquire(this))
			return;

		metal.ShapeChanged += OnMetalShapeChanged;
		
		activeAnvil = anvil;
		activeMetal = metal;
		forgeOrigin = stageRoot != null ? new Vector2(stageRoot.position.x, stageRoot.position.y) : new Vector2(stageWorldPosition.x, stageWorldPosition.y);

		deformer.SetMetalType(metal.MetalType);
		deformer.Heat = metal.Heat01;
		
		LoadLocalVertices(metal.ShapeVertices);

		IReadOnlyList<Vector2> targetOutlineList = metal.PartDefinition.BuildForgeOutline(forgeOrigin);
		Vector2[] targetOutline = targetOutlineList.ToArray();
		deformer.SetTargetOutline(targetOutline);
		targetView.Configure(targetOutline);
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
		lastAimDirection = Vector2.right;
		hammerPosition = deformer != null ? deformer.ComputeCentroid() : forgeOrigin;
		closeInputUnblockTime = Time.unscaledTime + CloseInputDelay;
		IsOpen = true;
		
		UpdateStatus();
		UpdateHammerPreview();

		SetHidden(false);
	}

	public void EndSession()
	{
		if (!IsOpen && activeMetal == null)
			return;

		activeMetal.ShapeChanged -= OnMetalShapeChanged;
		
		activeAnvil = null;
		activeMetal = null;
		charging = false;
		hasStickAim = false;
		
		if (hammerPreview != null)
			hammerPreview.Hide();
		
		IsOpen = false;
		
		StationSessionCoordinator.Release(this);
		
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

	void OnMetalShapeChanged()
	{
		LoadLocalVertices(activeMetal.ShapeVertices);
	}

	void SaveActiveMetal()
	{
		bool hasSaveTarget = activeMetal != null && deformer != null;
		if (!hasSaveTarget)
			return;
		
		bool hasValidShape = deformer.MetalVertices.Count >= PolygonGeometry.MinimumVertexCount;
		if (!hasValidShape)
			return;
		
		vertexScratch.Clear();
		PolygonGeometry.CopyVertices(deformer.MetalVertices, vertexScratch);
		for (int i = 0; i < vertexScratch.Count; i++)
			vertexScratch[i] -= forgeOrigin;
		
		activeMetal.TryCommitShapeVertices(vertexScratch);
	}

	void LoadLocalVertices(IReadOnlyList<Vector2> local)
	{
		vertexScratch.Clear();
		for (int i = 0; i < local.Count; i++)
			vertexScratch.Add(local[i] + forgeOrigin);
		
		deformer.LoadVertices(vertexScratch);
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
		bool finishPressed = pad != null && pad.buttonEast.wasPressedThisFrame;
		bool alternateFinishPressed = pad != null && pad.buttonNorth.wasPressedThisFrame;
		if (finishPressed || alternateFinishPressed)
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
		
		if (move.sqrMagnitude > MinimumSquaredAimMagnitude)
			hammerPosition += move * cursorSpeed * Time.unscaledDeltaTime;
		
		hammerPosition = ClampToForgeView(hammerPosition);
	}

	Vector2 ClampToForgeView(Vector2 position)
	{
		if (forgeCamera == null)
			return position;
		
		Vector3 cameraPosition = forgeCamera.transform.position;
		float halfHeight = forgeCamera.orthographicSize * ViewClampFraction;
		float halfWidth = halfHeight * Mathf.Max(MinimumCameraAspect, forgeCamera.aspect);
		
		position.x = Mathf.Clamp(position.x, cameraPosition.x - halfWidth, cameraPosition.x + halfWidth);
		position.y = Mathf.Clamp(position.y, cameraPosition.y - halfHeight, cameraPosition.y + halfHeight);
		return position;
	}

	void HandleStrikeButtons()
	{
		bool pressed = StrikePressed();
		bool released = StrikeReleased();

		if (pressed)
		{
			charging = true;
			chargeStartTime = Time.unscaledTime;
		}

		if (!charging || !released)
			return;
		
		float charge01 = CurrentCharge01();
		ResolveStrike(out Vector2 impact, out Vector2 direction, out _);
		float radius = deformer.ImpactRadius(impact, direction, charge01);
		charging = false;
		
		LoadLocalVertices(activeMetal.ShapeVertices);
		bool accepted = deformer.TryStrike(impact, direction, charge01);

		if (accepted)
		{
			SaveActiveMetal();
			UpdateStatus();
		}
		
		if (hammerPreview != null)
			hammerPreview.PlayStrikeFlash(impact, direction, radius, accepted, deformer.LastStrikeLimited);
	}

	bool StrikePressed()
	{
		Gamepad pad = Gamepad.current;
		bool triggerPressed = pad != null && pad.rightTrigger.wasPressedThisFrame;
		bool bumperPressed = pad != null && pad.rightShoulder.wasPressedThisFrame;
		
		if (triggerPressed || bumperPressed)
			return true;
		
		return false;
	}

	bool StrikeReleased()
	{
		Gamepad pad = Gamepad.current;
		bool triggerReleased = pad != null && pad.rightTrigger.wasReleasedThisFrame;
		bool bumperReleased = pad != null && pad.rightShoulder.wasReleasedThisFrame;
		
		if (triggerReleased || bumperReleased)
			return true;
		
		return false;
	}

	void UpdateHammerPreview()
	{
		if (hammerPreview == null || deformer == null)
			return;
		
		ResolveStrike(out Vector2 impact, out Vector2 direction, out float charge);
		hammerPreview.ShowAim(impact, direction, deformer.ImpactRadius(impact, direction, charge), charge, deformer);
	}

	float CurrentCharge01()
	{
		if (!charging)
			return 0f;
		
		return Mathf.InverseLerp(minChargeTime, maxChargeTime, Time.unscaledTime - chargeStartTime);
	}

	void ResolveStrike(out Vector2 impact, out Vector2 direction, out float charge01)
	{
		impact = hammerPosition;
		charge01 = CurrentCharge01();
		Vector2 requested = Vector2.zero;

		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			Vector2 aimStick = pad.rightStick.ReadValue();
			if (aimStick.sqrMagnitude > stickDeadzone * stickDeadzone)
			{
				requested = aimStick;
				lastAimDirection = aimStick;
				hasStickAim = true;
			}

			else if (hasStickAim)
				requested = lastAimDirection;
		}

		direction = requested.sqrMagnitude >= MinimumSquaredAimMagnitude ? requested.normalized : Vector2.zero;
	}

	void UpdateStatus()
	{
		if (document == null || activeMetal == null)
			return;

		UpdateHeatStatusElements();
		UpdateQualityStatusElements();
	}

	void UpdateHeatStatusElements()
	{
		heatSlider.value = activeMetal.Heat01;
		MetalType metalType = activeMetal.MetalType;
		
		//! FIX float quenchPercent = (1f - metalType.minHeatToQuench);
		float quenchPercent = 1f;
		//! FIX float coldPercent = metalType.minHeatToForge;
		float coldPercent = 0f;
		float normalPercent = 1f - (quenchPercent + coldPercent);
		
		quenchTempVisualElement.style.height = new Length(quenchPercent * 100f, LengthUnit.Percent);
		coldTempVisualElement.style.height = new Length(coldPercent * 100f, LengthUnit.Percent);
		normalTempVisualElement.style.height = new Length(normalPercent * 100f, LengthUnit.Percent);
	}

	void UpdateQualityStatusElements()
	{
		IReadOnlyList<Vector2> localTarget = activeMetal.PartDefinition.BuildForgeOutline(Vector2.zero);
		ShapeQuality shapeQuality = evaluator.EvaluateQuality(activeMetal.ShapeVertices, localTarget);
		uint quality = (uint)shapeQuality;
		
		dLight.EnableInClassList("quality-light-on", true);
		qualityLabel.text = "AWFUL";
		
		cLight.EnableInClassList("quality-light-on", false);
		bLight.EnableInClassList("quality-light-on", false);
		aLight.EnableInClassList("quality-light-on", false);
		sLight.EnableInClassList("quality-light-on", false);

		if (quality >= (uint)ShapeQuality.Flawed)
		{
			qualityLabel.text = "FLAWED";
			cLight.EnableInClassList("quality-light-on", true);
		}
		
		if (quality >= (uint)ShapeQuality.Good)
		{
			qualityLabel.text = "GOOD";
			bLight.EnableInClassList("quality-light-on", true);
		}
		
		if (quality >= (uint)ShapeQuality.Excellent)
		{
			qualityLabel.text = "EXCELLENT";
			aLight.EnableInClassList("quality-light-on", true);
		}
		
		if (quality >= (uint)ShapeQuality.Perfect)
		{
			qualityLabel.text = "PERFECT";
			sLight.EnableInClassList("quality-light-on", true);
		}
	}

	void FitCamera()
	{
		if (forgeCamera == null || deformer == null)
			return;
		
		Bounds bounds = PolygonGeometry.ComputeBounds(deformer.MetalVertices);
		bool hasTargetVertices = targetView != null && targetView.TargetVertices != null;
		bool hasTargetOutline = hasTargetVertices && targetView.TargetVertices.Count >= PolygonGeometry.MinimumVertexCount;
		if (hasTargetOutline)
		{
			var targetBounds = new Bounds(targetView.TargetVertices[0], Vector3.zero);
			for (int i = 1; i < targetView.TargetVertices.Count; i++)
				targetBounds.Encapsulate(targetView.TargetVertices[i]);
			bounds.Encapsulate(targetBounds);
		}

		forgeCamera.aspect = (float)forgeTexture.width / forgeTexture.height;
		float extent = Mathf.Max(bounds.extents.x / forgeCamera.aspect, bounds.extents.y) + CameraBoundsPadding;
		forgeCamera.orthographicSize = Mathf.Max(MinimumCameraHalfHeight, extent);
		forgeCamera.aspect = (float)forgeTexture.width / (float)forgeTexture.height;
		forgeCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, (stageRoot != null ? stageRoot.position.z : stageWorldPosition.z) - cameraDistance);
		forgeCamera.transform.rotation = Quaternion.identity;
	}

	void EnsureStage()
	{
		int forgeLayer = ForgingVisualUtility.ResolveForgeLayer();
		if (stageRoot == null)
		{
			var stageObject = new GameObject("ForgeStage");
			stageObject.transform.SetParent(transform, false);
			stageObject.transform.position = stageWorldPosition;
			stageRoot = stageObject.transform;
		}

		if (deformer == null)
			deformer = stageRoot.GetComponent<MetalDeformer2D>();
		if (deformer == null)
			deformer = stageRoot.gameObject.AddComponent<MetalDeformer2D>();

		if (metalView == null)
			metalView = stageRoot.GetComponent<ForgeMetalView>();
		if (metalView == null)
			metalView = stageRoot.gameObject.AddComponent<ForgeMetalView>();

		if (targetView == null)
			targetView = stageRoot.GetComponent<ForgeTargetView>();
		if (targetView == null)
			targetView = stageRoot.gameObject.AddComponent<ForgeTargetView>();

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

	void EnsureBackgroundPlate(int forgeLayer)
	{
		Transform existing = stageRoot.Find("AnvilPlate");
		if (existing != null)
			return;
		var backgroundPlate = GameObject.CreatePrimitive(PrimitiveType.Quad);
		backgroundPlate.name = "AnvilPlate";
		backgroundPlate.transform.SetParent(stageRoot, false);
		backgroundPlate.transform.localPosition = new Vector3(0f, 0f, BackgroundPlateDepth);
		backgroundPlate.transform.localRotation = Quaternion.Euler(0f, BackgroundPlateFacingAngle, 0f);
		backgroundPlate.transform.localScale = new Vector3(BackgroundPlateSize, BackgroundPlateSize, 1f);
		Destroy(backgroundPlate.GetComponent<Collider>());
		var renderer = backgroundPlate.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		ForgingVisualUtility.SetTint(renderer, plateColor);
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		renderer.sortingOrder = 0;
		backgroundPlate.layer = forgeLayer;
	}

	void EnsureForgeCamera(int forgeLayer)
	{
		if (forgeTexture == null)
		{
			forgeTexture = new RenderTexture(Mathf.Max(MinimumRenderTextureSize, renderTextureSize), Mathf.Max(MinimumRenderTextureSize, renderTextureSize), RenderTextureDepthBits)
			{
				name = "ForgeView"
			};
			forgeTexture.Create();
			runtimeOwnedTexture = true;
		}


		var image = document != null ? document.rootVisualElement.Q<Image>("Image") : null;
		if (image != null)
			image.image = forgeTexture;
		if (forgeCamera == null)
		{
			var cameraObject = new GameObject("ForgeCamera");
			cameraObject.transform.SetParent(transform, false);
			forgeCamera = cameraObject.AddComponent<Camera>();
			var cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
			if (cameraData == null)
				cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
			cameraData.renderType = CameraRenderType.Base;
			cameraData.renderPostProcessing = false;
			cameraData.renderShadows = false;
			forgeCamera.orthographic = true;
			forgeCamera.clearFlags = CameraClearFlags.SolidColor;
			forgeCamera.backgroundColor = stageBackground;
			forgeCamera.nearClipPlane = CameraNearClip;
			forgeCamera.farClipPlane = CameraFarClip;
			forgeCamera.depth = CameraRenderDepth;
			forgeCamera.enabled = false;
		}

		forgeCamera.cullingMask = 1 << forgeLayer;
		forgeCamera.enabled = IsOpen;
		forgeCamera.targetTexture = forgeTexture;
		forgeCamera.transform.position = stageWorldPosition + new Vector3(0f, 0f, -cameraDistance);
		forgeCamera.transform.rotation = Quaternion.identity;
	}
}
