using UnityEngine;

namespace ForgingPrototype
{
	public class GrindSparks : MonoBehaviour
	{
		[SerializeField] ParticleSystem sparks;
		[SerializeField] Color sparkColorA = new Color(1f, 0.72f, 0.15f, 1f);
		[SerializeField] Color sparkColorB = new Color(1f, 0.92f, 0.45f, 1f);
		[SerializeField] float emissionRate = 55f;

		bool emitting;

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

			transform.position = new Vector3(worldPoint.x, worldPoint.y, -0.06f);
			float angle = Mathf.Atan2(awayNormal.y, awayNormal.x) * Mathf.Rad2Deg;
			transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);

			var emission = sparks.emission;
			emission.rateOverTime = emissionRate * Mathf.Clamp01(intensity01);

			if (!emitting)
			{
				sparks.Play();
				emitting = true;
			}
		}

		public void Stop()
		{
			if (sparks == null)
				return;

			var emission = sparks.emission;
			emission.rateOverTime = 0f;
			if (emitting)
			{
				sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
				emitting = false;
			}
		}

		void EnsureParticles()
		{
			if (sparks != null)
				return;

			sparks = gameObject.GetComponent<ParticleSystem>();
			if (sparks == null)
				sparks = gameObject.AddComponent<ParticleSystem>();

			var main = sparks.main;
			main.loop = true;
			main.playOnAwake = false;
			main.startLifetime = 0.35f;
			main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
			main.startColor = new ParticleSystem.MinMaxGradient(sparkColorA, sparkColorB);
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.maxParticles = 64;
			main.gravityModifier = 0.35f;

			var emission = sparks.emission;
			emission.rateOverTime = 0f;

			var shape = sparks.shape;
			shape.enabled = true;
			shape.shapeType = ParticleSystemShapeType.Cone;
			shape.angle = 28f;
			shape.radius = 0.02f;

			var colorOverLifetime = sparks.colorOverLifetime;
			colorOverLifetime.enabled = true;
			var grad = new Gradient();
			grad.SetKeys(
				new[]
				{
					new GradientColorKey(sparkColorB, 0f),
					new GradientColorKey(sparkColorA, 0.45f),
					new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1f)
				},
				new[]
				{
					new GradientAlphaKey(1f, 0f),
					new GradientAlphaKey(0.8f, 0.5f),
					new GradientAlphaKey(0f, 1f)
				});
			colorOverLifetime.color = grad;

			var renderer = sparks.GetComponent<ParticleSystemRenderer>();
			renderer.material = ForgingVisualUtility.CreateColorMaterial(sparkColorA);
			renderer.sortingOrder = 30;
		}

		static void SetLayerRecursive(Transform t, int layer)
		{
			t.gameObject.layer = layer;
			for (int i = 0; i < t.childCount; i++)
				SetLayerRecursive(t.GetChild(i), layer);
		}
	}
}
