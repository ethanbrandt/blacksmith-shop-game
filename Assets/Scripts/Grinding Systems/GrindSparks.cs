using UnityEngine;

public class GrindSparks : MonoBehaviour
{
	const float SparkDepth = -0.06f;
	const float SparkLifetime = 0.35f;
	const float MinimumSparkSpeed = 1.2f;
	const float MaximumSparkSpeed = 2.8f;
	const float MinimumSparkSize = 0.04f;
	const float MaximumSparkSize = 0.09f;
	const int MaximumParticleCount = 64;
	const float SparkGravityMultiplier = 0.35f;
	const float EmissionConeAngle = 28f;
	const float EmissionRadius = 0.02f;
	const int SparkSortingOrder = 30;
	const float WarmColorTime = 0.45f;
	const float MidlifeOpacity = 0.8f;
	const float MidlifeTime = 0.5f;
	static readonly Color EmberColor = new Color(0.4f, 0.1f, 0.05f);

	[SerializeField] ParticleSystem sparks;
	[SerializeField] Color sparkColorA = new Color(1f, 0.72f, 0.15f, 1f);
	[SerializeField] Color sparkColorB = new Color(1f, 0.92f, 0.45f, 1f);
	[SerializeField] float emissionRate = 55f;
	bool isEmitting;
	public void Configure(Transform parent, int layer)
	{
		transform.SetParent(parent, false);
		gameObject.layer = layer;
		EnsureParticles();
		SetLayerRecursive(transform, layer);
		Stop();
	}

	public void EmitAt(Vector2 worldPoint, Vector2 awayNormal, float intensity01)
	{
		EnsureParticles();
		if (sparks == null)
			return;
		transform.position = new Vector3(worldPoint.x, worldPoint.y, SparkDepth);
		transform.rotation = Quaternion.FromToRotation(Vector3.forward, new Vector3(awayNormal.x, awayNormal.y, 0f));
		var emission = sparks.emission;
		emission.rateOverTime = emissionRate * Mathf.Clamp01(intensity01);
		if (!isEmitting)
		{
			sparks.Play();
			isEmitting = true;
		}
	}

	public void Stop()
	{
		if (sparks == null)
			return;

		var emission = sparks.emission;
		emission.rateOverTime = 0f;
		if (isEmitting)
		{
			sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
			isEmitting = false;
		}
	}

	void EnsureParticles()
	{
		if (sparks != null)
			return;

		sparks = gameObject.GetComponent<ParticleSystem>();
		if (sparks == null)
			sparks = gameObject.AddComponent<ParticleSystem>();
		var mainSettings = sparks.main;
		mainSettings.loop = true;
		mainSettings.playOnAwake = false;
		mainSettings.startLifetime = SparkLifetime;
		mainSettings.startSpeed = new ParticleSystem.MinMaxCurve(MinimumSparkSpeed, MaximumSparkSpeed);
		mainSettings.startSize = new ParticleSystem.MinMaxCurve(MinimumSparkSize, MaximumSparkSize);
		mainSettings.startColor = new ParticleSystem.MinMaxGradient(sparkColorA, sparkColorB);
		mainSettings.simulationSpace = ParticleSystemSimulationSpace.World;
		mainSettings.maxParticles = MaximumParticleCount;
		mainSettings.gravityModifier = SparkGravityMultiplier;
		var emission = sparks.emission;
		emission.rateOverTime = 0f;
		var emissionShape = sparks.shape;
		emissionShape.enabled = true;
		emissionShape.shapeType = ParticleSystemShapeType.Cone;
		emissionShape.angle = EmissionConeAngle;
		emissionShape.radius = EmissionRadius;
		var colorOverLifetime = sparks.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var sparkGradient = new Gradient();
		var colorKeys = new[] { new GradientColorKey(sparkColorB, 0f), new GradientColorKey(sparkColorA, WarmColorTime), new GradientColorKey(EmberColor, 1f) };
		var opacityKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(MidlifeOpacity, MidlifeTime), new GradientAlphaKey(0f, 1f) };
		sparkGradient.SetKeys(colorKeys, opacityKeys);
		colorOverLifetime.color = sparkGradient;
		var renderer = sparks.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		renderer.sortingOrder = SparkSortingOrder;
	}

	static void SetLayerRecursive(Transform rootTransform, int layer)
	{
		rootTransform.gameObject.layer = layer;
		for (int i = 0; i < rootTransform.childCount; i++)
			SetLayerRecursive(rootTransform.GetChild(i), layer);
	}
}
