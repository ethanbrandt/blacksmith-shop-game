using System;
using UnityEngine;
using UnityEngine.Rendering;

public class RoundEndVignetteController : MonoBehaviour
{
	[SerializeField] Transform focusTarget;
	[SerializeField] float zoom;
	[SerializeField, Range(0f, 1f)] float radius = 0.15f;
	[SerializeField, Range(0f, 1f)] float feather = 0.08f;
	[SerializeField, Range(0f, 1f)] float fadeDuration = 0.4f;
	[SerializeField, Range(0f, 1f)] float darkness = 0.65f;
	[SerializeField, Range(0f, 1f)] float desaturation = 1f;

	private static readonly int CenterID = Shader.PropertyToID("_VignetteCenter");
	private static readonly int RadiusID = Shader.PropertyToID("_VignetteRadius");
	private static readonly int FeatherID = Shader.PropertyToID("_VignetteFeather");
	private static readonly int StrengthID = Shader.PropertyToID("_VignetteStrength");
	private static readonly int DarknessID = Shader.PropertyToID("_VignetteDarkness");
	private static readonly int DesaturateID = Shader.PropertyToID("_VignetteDesaturation");

	private PixelRendererFeature rendererFeature;
	private Camera worldCam;
	private IsometricCameraController camController;
	private Material originalMat;
	private Material runtimeMat;

	private float fadeProgress;
	private float targetProgress;

	void Awake()
	{
		camController = GetComponent<IsometricCameraController>();
		rendererFeature = camController.pixelRendererFeature;
		worldCam = GetComponentInChildren<Camera>();

		originalMat = rendererFeature.settings.compositeMaterial;
		runtimeMat = new Material(originalMat)
		{
			name = "RoundEndComposite",
			hideFlags = HideFlags.DontSave
		};
		
		runtimeMat.SetFloat(StrengthID, 0f);
		rendererFeature.settings.compositeMaterial = runtimeMat;
	}

	private void OnEnable()
	{
		RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
	}
	
	private void OnDisable()
	{
		RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
	}

	void Update()
	{
		float fadeStep = Time.unscaledDeltaTime / fadeDuration;
		fadeProgress = Mathf.MoveTowards(fadeProgress, targetProgress, fadeStep);
	}

	public void BeginEnding()
	{
		targetProgress = 1f;
		camController.SetZoom(zoom);
	}

	public void ResetVignette()
	{
		fadeProgress = 0f;
		targetProgress = 0f;
		
		if (runtimeMat)
			runtimeMat.SetFloat(StrengthID, 0f);
	}

	void BeforeCameraRendering(ScriptableRenderContext _context, Camera _renderCam)
	{
		if (!runtimeMat)
			return;

		if (_renderCam != worldCam || focusTarget == null)
		{
			runtimeMat.SetFloat(StrengthID, 0f);
			return;
		}
		
		Vector3 viewportPoint = worldCam.WorldToViewportPoint(focusTarget.position);
		if (viewportPoint.z <= 0f)
		{
			runtimeMat.SetFloat(StrengthID, 0f);
			return;
		}

		Vector4 center = new Vector4(viewportPoint.x, viewportPoint.y, 0f, 0f);
		float strength = Mathf.SmoothStep(0f, 1f, fadeProgress);

		runtimeMat.SetVector(CenterID, center);
		runtimeMat.SetFloat(RadiusID, radius);
		runtimeMat.SetFloat(FeatherID, feather);
		runtimeMat.SetFloat(StrengthID, strength);
		runtimeMat.SetFloat(DarknessID, darkness);
		runtimeMat.SetFloat(DesaturateID, desaturation);
	}

	void OnDestroy()
	{
		if (rendererFeature && rendererFeature.settings != null && rendererFeature.settings.compositeMaterial == runtimeMat)
			rendererFeature.settings.compositeMaterial = originalMat;
		
		if (runtimeMat)
			Destroy(runtimeMat);
	}
}
