using ForgingPrototype;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[InitializeOnLoad]
static class ForgeRigAutoBuild
{
	static ForgeRigAutoBuild()
	{
		EditorApplication.delayCall += TryAutoBuild;
		EditorSceneManager.sceneOpened += (_, __) => TryAutoBuild();
	}

	static void TryAutoBuild()
	{
		var controller = Object.FindFirstObjectByType<ForgeSessionController>();
		if (controller == null)
			return;

		var so = new SerializedObject(controller);
		if (so.FindProperty("viewport").objectReferenceValue != null)
			return;

		ForgeRigBuilder.Build(controller);
	}
}

[CustomEditor(typeof(ForgeSessionController))]
public class ForgeSessionControllerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		EditorGUILayout.Space();
		if (GUILayout.Button("Build / Refresh Scene Rig", GUILayout.Height(32)))
			ForgeRigBuilder.Build((ForgeSessionController)target);
	}
}

public static class ForgeRigBuilder
{
	const string ArtFolder = "Assets/Art/Forging";

	[MenuItem("Forging/Build Forge Rig In Scene")]
	public static void BuildFromMenu()
	{
		var controller = Object.FindFirstObjectByType<ForgeSessionController>();
		if (controller == null)
		{
			var go = new GameObject("ForgeSession");
			Undo.RegisterCreatedObjectUndo(go, "Create Forge Session");
			controller = go.AddComponent<ForgeSessionController>();
		}

		Build(controller);
	}

	public static void Build(ForgeSessionController controller)
	{
		if (controller == null)
			return;

		Undo.RegisterCompleteObjectUndo(controller.gameObject, "Build Forge Rig");

		int forgeLayer = ForgingVisualUtility.ResolveForgeLayer();
		EnsureArtFolder();
		Material metalMat = EnsureMaterial("ForgeMetal", new Color(0.72f, 0.45f, 0.28f, 1f));
		Material plateMat = EnsureMaterial("ForgeAnvilPlate", new Color(0.22f, 0.18f, 0.16f, 1f));
		Material hammerMat = EnsureMaterial("ForgeHammerPreview", new Color(1f, 0.92f, 0.45f, 0.7f));
		Material ghostMat = EnsureMaterial("ForgeGhostFill", new Color(0.55f, 0.8f, 1f, 0.12f));
		RenderTexture rt = EnsureRenderTexture();

		Transform root = controller.transform;
		Transform stage = FindOrCreate(root, "ForgeStage").transform;
		stage.position = new Vector3(0f, 180f, 0f);

		var deformer = GetOrAdd<MetalDeformer2D>(stage.gameObject);
		var metalView = GetOrAdd<ForgeMetalView>(stage.gameObject);
		var evaluator = GetOrAdd<ShapeMatchEvaluator>(stage.gameObject);
		var preview = GetOrAdd<ForgeHammerPreview>(stage.gameObject);

		GameObject plate = FindOrCreate(stage, "AnvilPlate");
		if (plate.GetComponent<MeshFilter>() == null)
		{
			var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Undo.RegisterCreatedObjectUndo(quad, "Build Forge Rig");
			quad.name = "AnvilPlate";
			quad.transform.SetParent(stage, false);
			Object.DestroyImmediate(plate);
			plate = quad;
		}

		plate.transform.localPosition = new Vector3(0f, 0f, 0.45f);
		plate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
		plate.transform.localScale = new Vector3(8f, 8f, 1f);
		Collider plateCol = plate.GetComponent<Collider>();
		if (plateCol != null)
			Object.DestroyImmediate(plateCol);
		var plateRenderer = plate.GetComponent<MeshRenderer>();
		if (plateRenderer != null)
		{
			plateRenderer.sharedMaterial = plateMat;
			plateRenderer.shadowCastingMode = ShadowCastingMode.Off;
			plateRenderer.receiveShadows = false;
		}

		GameObject metalFill = FindOrCreate(stage, "MetalFill");
		var fillFilter = GetOrAdd<MeshFilter>(metalFill);
		var fillRenderer = GetOrAdd<MeshRenderer>(metalFill);
		fillRenderer.sharedMaterial = metalMat;
		fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
		fillRenderer.receiveShadows = false;
		fillRenderer.sortingOrder = 8;

		LineRenderer metalOutline = GetOrAddLine(FindOrCreate(stage, "MetalOutline"), true, 9);
		LineRenderer targetOutline = GetOrAddLine(FindOrCreate(stage, "TargetOutline"), true, 2);
		LineRenderer ghostOutline = GetOrAddLine(FindOrCreate(stage, "TargetGhostOutline"), true, 12);
		GameObject ghostFill = FindOrCreate(stage, "TargetGhostFill");
		var ghostFilter = GetOrAdd<MeshFilter>(ghostFill);
		var ghostRenderer = GetOrAdd<MeshRenderer>(ghostFill);
		ghostRenderer.sharedMaterial = ghostMat;
		ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
		ghostRenderer.receiveShadows = false;
		ghostRenderer.sortingOrder = 3;

		GameObject discGo = FindOrCreate(stage, "HammerRadiusFill");
		var discFilter = GetOrAdd<MeshFilter>(discGo);
		var discRenderer = GetOrAdd<MeshRenderer>(discGo);
		discRenderer.sharedMaterial = hammerMat;
		discRenderer.shadowCastingMode = ShadowCastingMode.Off;
		discRenderer.receiveShadows = false;
		discRenderer.sortingOrder = 18;
		LineRenderer ring = GetOrAddLine(FindOrCreate(stage, "HammerRadiusRing"), true, 20);
		LineRenderer arrow = GetOrAddLine(FindOrCreate(stage, "HammerAimArrow"), false, 21);

		ForgingVisualUtility.ApplyLayerRecursively(stage.gameObject, forgeLayer);

		GameObject camGo = FindOrCreate(root, "ForgeCamera");
		Camera forgeCam = GetOrAdd<Camera>(camGo);
		var extra = camGo.GetComponent<UniversalAdditionalCameraData>();
		if (extra == null)
			extra = Undo.AddComponent<UniversalAdditionalCameraData>(camGo);
		extra.renderType = CameraRenderType.Base;
		extra.renderPostProcessing = false;
		extra.renderShadows = false;
		forgeCam.orthographic = true;
		forgeCam.orthographicSize = 2.6f;
		forgeCam.clearFlags = CameraClearFlags.SolidColor;
		forgeCam.backgroundColor = new Color(0.12f, 0.09f, 0.08f, 1f);
		forgeCam.nearClipPlane = 0.1f;
		forgeCam.farClipPlane = 40f;
		forgeCam.depth = -10;
		forgeCam.cullingMask = 1 << forgeLayer;
		forgeCam.targetTexture = rt;
		forgeCam.enabled = true;
		camGo.transform.position = stage.position + new Vector3(0f, 0f, -8f);
		camGo.transform.rotation = Quaternion.identity;
		AudioListener listener = camGo.GetComponent<AudioListener>();
		if (listener != null)
			Object.DestroyImmediate(listener);

		GameObject canvasGo = FindOrCreate(root, "ForgeOverlayCanvas");
		canvasGo.layer = 5;
		Canvas canvas = GetOrAdd<Canvas>(canvasGo);
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 80;
		var scaler = GetOrAdd<CanvasScaler>(canvasGo);
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.matchWidthOrHeight = 0.5f;
		GetOrAdd<GraphicRaycaster>(canvasGo);
		RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();

		Image dimmer = GetOrAddImage(FindOrCreateUi(canvasGo.transform, "Dimmer"), new Color(0.02f, 0.01f, 0.01f, 0.72f));
		Stretch(dimmer.rectTransform, Vector2.zero, Vector2.one);

		Image frame = GetOrAddImage(FindOrCreateUi(canvasGo.transform, "ForgeFrame"), new Color(0.28f, 0.16f, 0.1f, 0.96f));
		Stretch(frame.rectTransform, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f));

		Image viewportFrame = GetOrAddImage(FindOrCreateUi(frame.transform, "ViewportFrame"), new Color(0.08f, 0.06f, 0.05f, 1f));
		Stretch(viewportFrame.rectTransform, new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.9f));

		GameObject viewportGo = FindOrCreateUi(viewportFrame.transform, "ForgeViewport");
		RawImage viewport = GetOrAdd<RawImage>(viewportGo);
		viewport.texture = rt;
		viewport.color = Color.white;
		Stretch(viewport.rectTransform, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f));
		var aspect = GetOrAdd<AspectRatioFitter>(viewportGo);
		aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
		aspect.aspectRatio = 1f;

		TextMeshProUGUI status = GetOrAdd<TextMeshProUGUI>(FindOrCreateUi(frame.transform, "StatusText"));
		status.fontSize = 22f;
		status.alignment = TextAlignmentOptions.TopLeft;
		status.color = new Color(0.95f, 0.86f, 0.7f, 1f);
		status.raycastTarget = false;
		if (TMP_Settings.defaultFontAsset != null)
			status.font = TMP_Settings.defaultFontAsset;
		Stretch(status.rectTransform, new Vector2(0.08f, 0.02f), new Vector2(0.7f, 0.15f));
		if (string.IsNullOrWhiteSpace(status.text))
			status.text = "Forge";

		GameObject doneGo = FindOrCreateUi(frame.transform, "DoneButton");
		Image doneImage = GetOrAddImage(doneGo, new Color(0.45f, 0.28f, 0.14f, 1f));
		Button done = GetOrAdd<Button>(doneGo);
		Stretch(doneImage.rectTransform, new Vector2(0.74f, 0.035f), new Vector2(0.92f, 0.13f));
		TextMeshProUGUI doneLabel = GetOrAdd<TextMeshProUGUI>(FindOrCreateUi(doneGo.transform, "Label"));
		doneLabel.text = "Done";
		doneLabel.fontSize = 26f;
		doneLabel.alignment = TextAlignmentOptions.Center;
		doneLabel.fontStyle = FontStyles.Bold;
		doneLabel.color = new Color(1f, 0.92f, 0.78f, 1f);
		doneLabel.raycastTarget = false;
		if (TMP_Settings.defaultFontAsset != null)
			doneLabel.font = TMP_Settings.defaultFontAsset;
		Stretch(doneLabel.rectTransform, Vector2.zero, Vector2.one);

		var so = new SerializedObject(controller);
		so.FindProperty("forgeCamera").objectReferenceValue = forgeCam;
		so.FindProperty("viewport").objectReferenceValue = viewport;
		so.FindProperty("frameImage").objectReferenceValue = frame;
		so.FindProperty("statusText").objectReferenceValue = status;
		so.FindProperty("doneButton").objectReferenceValue = done;
		so.FindProperty("overlayRoot").objectReferenceValue = canvasRect;
		so.FindProperty("deformer").objectReferenceValue = deformer;
		so.FindProperty("evaluator").objectReferenceValue = evaluator;
		so.FindProperty("metalView").objectReferenceValue = metalView;
		so.FindProperty("hammerPreview").objectReferenceValue = preview;
		so.FindProperty("stageRoot").objectReferenceValue = stage;
		so.FindProperty("overlayCanvas").objectReferenceValue = canvas;
		so.FindProperty("forgeTexture").objectReferenceValue = rt;
		so.ApplyModifiedProperties();

		var metalSo = new SerializedObject(metalView);
		metalSo.FindProperty("deformer").objectReferenceValue = deformer;
		metalSo.FindProperty("fillFilter").objectReferenceValue = fillFilter;
		metalSo.FindProperty("fillRenderer").objectReferenceValue = fillRenderer;
		metalSo.FindProperty("outline").objectReferenceValue = metalOutline;
		metalSo.FindProperty("fillMaterial").objectReferenceValue = metalMat;
		metalSo.ApplyModifiedProperties();

		var previewSo = new SerializedObject(preview);
		previewSo.FindProperty("ring").objectReferenceValue = ring;
		previewSo.FindProperty("arrow").objectReferenceValue = arrow;
		previewSo.FindProperty("discFilter").objectReferenceValue = discFilter;
		previewSo.FindProperty("discRenderer").objectReferenceValue = discRenderer;
		previewSo.FindProperty("discMaterial").objectReferenceValue = hammerMat;
		previewSo.ApplyModifiedProperties();

		var evalSo = new SerializedObject(evaluator);
		evalSo.FindProperty("metal").objectReferenceValue = deformer;
		evalSo.FindProperty("targetOutline").objectReferenceValue = targetOutline;
		evalSo.FindProperty("targetGhostOutline").objectReferenceValue = ghostOutline;
		evalSo.FindProperty("ghostFillFilter").objectReferenceValue = ghostFilter;
		evalSo.FindProperty("ghostFillRenderer").objectReferenceValue = ghostRenderer;
		evalSo.ApplyModifiedProperties();

		EnsureAnvilSocket();
		canvasGo.SetActive(false);

		EditorUtility.SetDirty(controller);
		EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
		Selection.activeGameObject = controller.gameObject;
		Debug.Log("Forge rig is in the scene. Edit ForgeSession children, then assign frame art on ForgeFrame.");
	}

	static void EnsureAnvilSocket()
	{
		var anvils = Object.FindObjectsByType<Anvil>(FindObjectsSortMode.None);
		for (int i = 0; i < anvils.Length; i++)
		{
			Anvil anvil = anvils[i];
			var so = new SerializedObject(anvil);
			SerializedProperty socketProp = so.FindProperty("metalSocket");
			if (socketProp.objectReferenceValue != null)
				continue;

			GameObject socket = FindOrCreate(anvil.transform, "MetalSocket");
			socket.transform.localPosition = new Vector3(0f, 0.78f, 0f);
			socketProp.objectReferenceValue = socket.transform;
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(anvil);
		}
	}

	static void EnsureArtFolder()
	{
		if (!AssetDatabase.IsValidFolder("Assets/Art"))
			AssetDatabase.CreateFolder("Assets", "Art");
		if (!AssetDatabase.IsValidFolder(ArtFolder))
			AssetDatabase.CreateFolder("Assets/Art", "Forging");
	}

	static Material EnsureMaterial(string name, Color color)
	{
		string path = $"{ArtFolder}/{name}.mat";
		var material = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (material == null)
		{
			material = ForgingVisualUtility.CreateColorMaterial(color);
			material.name = name;
			AssetDatabase.CreateAsset(material, path);
		}

		return material;
	}

	static RenderTexture EnsureRenderTexture()
	{
		string path = $"{ArtFolder}/ForgeView.renderTexture";
		var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
		if (rt != null)
			return rt;

		rt = new RenderTexture(1024, 1024, 16)
		{
			name = "ForgeView",
			filterMode = FilterMode.Point,
			antiAliasing = 1
		};
		rt.Create();
		AssetDatabase.CreateAsset(rt, path);
		return rt;
	}

	static GameObject FindOrCreate(Transform parent, string name)
	{
		Transform existing = parent.Find(name);
		if (existing != null)
			return existing.gameObject;

		var go = new GameObject(name);
		Undo.RegisterCreatedObjectUndo(go, "Build Forge Rig");
		go.transform.SetParent(parent, false);
		return go;
	}

	static GameObject FindOrCreateUi(Transform parent, string name)
	{
		Transform existing = parent.Find(name);
		if (existing != null)
			return existing.gameObject;

		var go = new GameObject(name, typeof(RectTransform));
		Undo.RegisterCreatedObjectUndo(go, "Build Forge Rig");
		go.transform.SetParent(parent, false);
		go.layer = 5;
		return go;
	}

	static T GetOrAdd<T>(GameObject go) where T : Component
	{
		T component = go.GetComponent<T>();
		if (component == null)
			component = Undo.AddComponent<T>(go);
		return component;
	}

	static LineRenderer GetOrAddLine(GameObject go, bool loop, int sortingOrder)
	{
		LineRenderer line = GetOrAdd<LineRenderer>(go);
		line.useWorldSpace = true;
		line.loop = loop;
		ForgingVisualUtility.ApplyLineRendererDefaults(line, Color.white, 0.045f, sortingOrder);
		return line;
	}

	static Image GetOrAddImage(GameObject go, Color color)
	{
		Image image = GetOrAdd<Image>(go);
		if (image.sprite == null)
			image.color = color;
		return image;
	}

	static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
	{
		rect.anchorMin = min;
		rect.anchorMax = max;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}
}
