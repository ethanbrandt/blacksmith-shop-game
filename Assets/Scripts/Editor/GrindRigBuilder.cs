using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[InitializeOnLoad]
static class GrindRigAutoBuild
{
	static GrindRigAutoBuild()
	{
		EditorApplication.delayCall += TryAutoBuild;
		EditorSceneManager.sceneOpened += (_, __) => TryAutoBuild();
	}

	static void TryAutoBuild()
	{
		var controller = Object.FindFirstObjectByType<GrindSessionController>();
		if (controller == null)
			return;

		var so = new SerializedObject(controller);
		if (so.FindProperty("viewport").objectReferenceValue != null)
			return;

		GrindRigBuilder.Build(controller);
	}
}

[CustomEditor(typeof(GrindSessionController))]
public class GrindSessionControllerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		EditorGUILayout.Space();
		if (GUILayout.Button("Build / Refresh Scene Rig", GUILayout.Height(32)))
			GrindRigBuilder.Build((GrindSessionController)target);
	}
}

public static class GrindRigBuilder
{
	const string ArtFolder = "Assets/Art/Forging";

	[MenuItem("Forging/Build Grind Rig In Scene")]
	public static void BuildFromMenu()
	{
		var controller = Object.FindFirstObjectByType<GrindSessionController>();
		if (controller == null)
		{
			var go = new GameObject("GrindSession");
			Undo.RegisterCreatedObjectUndo(go, "Create Grind Session");
			controller = go.AddComponent<GrindSessionController>();
		}

		Build(controller);
	}

	public static void Build(GrindSessionController controller)
	{
		if (controller == null)
			return;

		Undo.RegisterCompleteObjectUndo(controller.gameObject, "Build Grind Rig");

		int grindLayer = ForgingVisualUtility.ResolveForgeLayer();
		EnsureArtFolder();
		Material plateMat = EnsureMaterial("GrindPlate", new Color(0.16f, 0.07f, 0.14f, 1f));
		Material stoneMat = EnsureMaterial("GrindStone", new Color(0.62f, 0.72f, 0.82f, 1f));
		Material vertexColorMat = EnsureVertexColorMaterial("GrindVertexColor");
		RenderTexture rt = EnsureRenderTexture();

		Transform root = controller.transform;
		Transform stage = FindOrCreate(root, "GrindStage").transform;
		stage.position = new Vector3(40f, 180f, 0f);

		var blade = GetOrAdd<GrindBladeBody>(stage.gameObject);
		var metalView = GetOrAdd<GrindMetalView>(stage.gameObject);
		var evaluator = GetOrAdd<EdgeGrindEvaluator>(stage.gameObject);

		GameObject plate = FindOrCreate(stage, "GrindPlate");
		if (plate.GetComponent<MeshFilter>() == null)
		{
			var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Undo.RegisterCreatedObjectUndo(quad, "Build Grind Rig");
			quad.name = "GrindPlate";
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
			plateRenderer.sortingOrder = 0;
		}

		GameObject metalFill = FindOrCreate(stage, "GrindMetalFill");
		var fillFilter = GetOrAdd<MeshFilter>(metalFill);
		var fillRenderer = GetOrAdd<MeshRenderer>(metalFill);
		fillRenderer.sharedMaterial = vertexColorMat;
		fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
		fillRenderer.receiveShadows = false;
		fillRenderer.sortingOrder = 8;

		GameObject metalOutlineGo = FindOrCreate(stage, "GrindMetalOutline");
		LineRenderer metalOutline = GetOrAddLine(metalOutlineGo, true, 11);

		GameObject bevelGo = FindOrCreate(stage, "GrindEdgeBevel");
		var bevelFilter = GetOrAdd<MeshFilter>(bevelGo);
		var bevelRenderer = GetOrAdd<MeshRenderer>(bevelGo);
		bevelRenderer.sharedMaterial = vertexColorMat;
		bevelRenderer.shadowCastingMode = ShadowCastingMode.Off;
		bevelRenderer.receiveShadows = false;
		bevelRenderer.sortingOrder = 10;

		Transform oldHighlight = stage.Find("SharpenHighlight");
		if (oldHighlight != null)
			Object.DestroyImmediate(oldHighlight.gameObject);

		GameObject wheelGo = FindOrCreate(stage, "GrindstoneWheel");
		var wheel = GetOrAdd<GrindstoneWheel>(wheelGo);
		GameObject wheelVisual = FindOrCreate(wheelGo.transform, "WheelVisual");
		if (wheelVisual.GetComponent<MeshFilter>() == null)
		{
			var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Undo.RegisterCreatedObjectUndo(cylinder, "Build Grind Rig");
			cylinder.name = "WheelVisual";
			cylinder.transform.SetParent(wheelGo.transform, false);
			Object.DestroyImmediate(wheelVisual);
			wheelVisual = cylinder;
			Object.DestroyImmediate(wheelVisual.GetComponent<Collider>());
		}

		Collider wheelCol = wheelVisual.GetComponent<Collider>();
		if (wheelCol != null)
			Object.DestroyImmediate(wheelCol);
		wheelVisual.transform.localPosition = Vector3.zero;
		wheelVisual.transform.localRotation = Quaternion.identity;
		wheelVisual.transform.localScale = new Vector3(1f, 0.725f, 0.06f);
		var wheelRenderer = wheelVisual.GetComponent<MeshRenderer>();
		if (wheelRenderer != null)
		{
			wheelRenderer.sharedMaterial = stoneMat;
			wheelRenderer.shadowCastingMode = ShadowCastingMode.Off;
			wheelRenderer.receiveShadows = false;
			wheelRenderer.sortingOrder = 1;
		}

		GameObject sparksGo = FindOrCreate(stage, "GrindSparks");
		var sparks = GetOrAdd<GrindSparks>(sparksGo);

		ForgingVisualUtility.ApplyLayerRecursively(stage.gameObject, grindLayer);

		GameObject camGo = FindOrCreate(root, "GrindCamera");
		Camera grindCam = GetOrAdd<Camera>(camGo);
		var extra = camGo.GetComponent<UniversalAdditionalCameraData>();
		if (extra == null)
			extra = Undo.AddComponent<UniversalAdditionalCameraData>(camGo);
		extra.renderType = CameraRenderType.Base;
		extra.renderPostProcessing = false;
		extra.renderShadows = false;
		grindCam.orthographic = true;
		grindCam.orthographicSize = 2.6f;
		grindCam.clearFlags = CameraClearFlags.SolidColor;
		grindCam.backgroundColor = new Color(0.22f, 0.08f, 0.18f, 1f);
		grindCam.nearClipPlane = 0.1f;
		grindCam.farClipPlane = 40f;
		grindCam.depth = -11;
		grindCam.cullingMask = 1 << grindLayer;
		grindCam.targetTexture = rt;
		grindCam.enabled = false;
		camGo.transform.position = stage.position + new Vector3(0f, 0f, -8f);
		camGo.transform.rotation = Quaternion.identity;
		AudioListener listener = camGo.GetComponent<AudioListener>();
		if (listener != null)
			Object.DestroyImmediate(listener);

		GameObject canvasGo = FindOrCreate(root, "GrindOverlayCanvas");
		canvasGo.layer = 5;
		Canvas canvas = GetOrAdd<Canvas>(canvasGo);
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 85;
		var scaler = GetOrAdd<CanvasScaler>(canvasGo);
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.matchWidthOrHeight = 0.5f;
		GetOrAdd<GraphicRaycaster>(canvasGo);
		RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();

		Image dimmer = GetOrAddImage(FindOrCreateUi(canvasGo.transform, "Dimmer"), new Color(0.02f, 0.01f, 0.03f, 0.72f));
		Stretch(dimmer.rectTransform, Vector2.zero, Vector2.one);

		Image frame = GetOrAddImage(FindOrCreateUi(canvasGo.transform, "GrindFrame"), new Color(0.22f, 0.12f, 0.18f, 0.96f));
		Stretch(frame.rectTransform, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.94f));

		Image viewportFrame = GetOrAddImage(FindOrCreateUi(frame.transform, "ViewportFrame"), new Color(0.08f, 0.04f, 0.07f, 1f));
		Stretch(viewportFrame.rectTransform, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.9f));

		GameObject viewportGo = FindOrCreateUi(viewportFrame.transform, "GrindViewport");
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
		status.color = new Color(0.95f, 0.86f, 0.78f, 1f);
		status.raycastTarget = false;
		if (TMP_Settings.defaultFontAsset != null)
			status.font = TMP_Settings.defaultFontAsset;
		Stretch(status.rectTransform, new Vector2(0.08f, 0.02f), new Vector2(0.55f, 0.16f));
		if (string.IsNullOrWhiteSpace(status.text))
			status.text = "Grind";

		TextMeshProUGUI quality = GetOrAdd<TextMeshProUGUI>(FindOrCreateUi(frame.transform, "QualityText"));
		quality.fontSize = 52f;
		quality.alignment = TextAlignmentOptions.Center;
		quality.fontStyle = FontStyles.Bold;
		quality.color = new Color(0.9f, 0.92f, 0.95f, 1f);
		quality.raycastTarget = false;
		if (TMP_Settings.defaultFontAsset != null)
			quality.font = TMP_Settings.defaultFontAsset;
		Stretch(quality.rectTransform, new Vector2(0.55f, 0.02f), new Vector2(0.92f, 0.16f));
		if (string.IsNullOrWhiteSpace(quality.text))
			quality.text = "BLUNT";

		var so = new SerializedObject(controller);
		so.FindProperty("grindCamera").objectReferenceValue = grindCam;
		so.FindProperty("viewport").objectReferenceValue = viewport;
		so.FindProperty("frameImage").objectReferenceValue = frame;
		so.FindProperty("statusText").objectReferenceValue = status;
		so.FindProperty("qualityText").objectReferenceValue = quality;
		so.FindProperty("overlayRoot").objectReferenceValue = canvasRect;
		so.FindProperty("blade").objectReferenceValue = blade;
		so.FindProperty("evaluator").objectReferenceValue = evaluator;
		so.FindProperty("metalView").objectReferenceValue = metalView;
		so.FindProperty("wheel").objectReferenceValue = wheel;
		so.FindProperty("sparks").objectReferenceValue = sparks;
		so.FindProperty("stageRoot").objectReferenceValue = stage;
		so.FindProperty("overlayCanvas").objectReferenceValue = canvas;
		so.FindProperty("grindTexture").objectReferenceValue = rt;
		so.ApplyModifiedProperties();

		var metalSo = new SerializedObject(metalView);
		metalSo.FindProperty("blade").objectReferenceValue = blade;
		metalSo.FindProperty("fillFilter").objectReferenceValue = fillFilter;
		metalSo.FindProperty("fillRenderer").objectReferenceValue = fillRenderer;
		metalSo.FindProperty("bevelFilter").objectReferenceValue = bevelFilter;
		metalSo.FindProperty("bevelRenderer").objectReferenceValue = bevelRenderer;
		metalSo.FindProperty("outline").objectReferenceValue = metalOutline;
		metalSo.FindProperty("fillMaterial").objectReferenceValue = vertexColorMat;
		metalSo.FindProperty("bevelMaterial").objectReferenceValue = vertexColorMat;
		metalSo.FindProperty("edgeEvaluator").objectReferenceValue = evaluator;
		metalSo.ApplyModifiedProperties();

		var wheelSo = new SerializedObject(wheel);
		wheelSo.FindProperty("wheelVisual").objectReferenceValue = wheelVisual.transform;
		wheelSo.FindProperty("wheelRenderer").objectReferenceValue = wheelRenderer;
		wheelSo.ApplyModifiedProperties();

		EnsureGrindstoneStation();
		canvasGo.SetActive(false);

		EditorUtility.SetDirty(controller);
		EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
		Selection.activeGameObject = controller.gameObject;
		Debug.Log("Grind rig is in the scene. Edit GrindSession children (GrindOverlayCanvas, GrindStage, GrindCamera).");
	}

	static void EnsureGrindstoneStation()
	{
		if (Object.FindFirstObjectByType<Grindstone>() != null)
			return;

		var go = new GameObject("Grindstone");
		Undo.RegisterCreatedObjectUndo(go, "Create Grindstone");
		go.transform.position = new Vector3(6.2f, 0f, 3.9f);
		go.AddComponent<Grindstone>();

		var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Undo.RegisterCreatedObjectUndo(visual, "Create Grindstone Visual");
		visual.name = "Visual";
		visual.transform.SetParent(go.transform, false);
		visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
		visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
		Object.DestroyImmediate(visual.GetComponent<Collider>());
		var renderer = visual.GetComponent<MeshRenderer>();
		if (renderer != null)
			renderer.sharedMaterial = ForgingVisualUtility.CreateColorMaterial(new Color(0.55f, 0.65f, 0.75f, 1f));
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

		ForgingVisualUtility.EnsureMeshFillMaterial(material, color);
		EditorUtility.SetDirty(material);
		return material;
	}

	static Material EnsureVertexColorMaterial(string name)
	{
		string path = $"{ArtFolder}/{name}.mat";
		var material = AssetDatabase.LoadAssetAtPath<Material>(path);
		Shader shader = Shader.Find("ForgingPrototype/UnlitVertexColor");
		if (shader == null)
			return EnsureMaterial(name, Color.white);

		if (material == null)
		{
			material = new Material(shader) { name = name };
			ForgingVisualUtility.EnsureMeshFillMaterial(material, Color.white);
			AssetDatabase.CreateAsset(material, path);
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
			ForgingVisualUtility.EnsureMeshFillMaterial(material, Color.white);
		}

		EditorUtility.SetDirty(material);
		return material;
	}

	static RenderTexture EnsureRenderTexture()
	{
		string path = $"{ArtFolder}/GrindView.renderTexture";
		var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
		if (rt != null)
			return rt;

		rt = new RenderTexture(1024, 1024, 16)
		{
			name = "GrindView",
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
		Undo.RegisterCreatedObjectUndo(go, "Build Grind Rig");
		go.transform.SetParent(parent, false);
		return go;
	}

	static GameObject FindOrCreateUi(Transform parent, string name)
	{
		Transform existing = parent.Find(name);
		if (existing != null)
			return existing.gameObject;

		var go = new GameObject(name, typeof(RectTransform));
		Undo.RegisterCreatedObjectUndo(go, "Build Grind Rig");
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
