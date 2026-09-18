using System;
using System.Collections.Generic;
using UnityEngine;

public struct MetalFeel
{
	public float mobility;
	public float magnet;
	public float tension;
}

public struct MeltFeel
{
	public float outwardSpeed;
}

[Serializable]
public enum MetalHeatHeuristic
{
	LOW,
	MEDIUM,
	HIGH
}

[Serializable]
public class HeatGaugeRegion
{
	[Range(0f, 1f)] public float startHeat;
	public Color regionColor;
}

public abstract class MetalType : ScriptableObject
{
	[Header("Identity")]
	public string displayName = "Metal";
	public Color metalColor = new Color(1f, 0.35f, 0.08f, 1f);
	public MetalHeatHeuristic forgeHeatHeuristic;
	public MetalHeatHeuristic meltHeatHeuristic;

	[Header("World Heat Transfer")]
	public float furnaceHeatTransferRate = 55f;
	public float worldAmbientCoolRate = 18f;
	[Min(1f)] public float referenceMaxTemp = 1000f;

	[Header("Forging Heat Curves (X = heat 0-1, Y = multiplier)")]
	public AnimationCurve mobilityByHeat = AnimationCurve.Linear(0f, 0.15f, 1f, 1f);
	public AnimationCurve magnetByHeat = AnimationCurve.Linear(0f, 0.1f, 1f, 1f);
	public AnimationCurve tensionByHeat = AnimationCurve.Linear(0f, 1.2f, 1f, 0.85f);

	[Header("Melting")]
	public bool meltingEnabled = true;
	public float meltGracePeriod = 1.25f;
	[Min(0.001f)] public float meltOutwardSpeed = 0.05f;
	public AnimationCurve meltSpeedByIntensity = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	[Header("Heat Gauge Information")]
	public List<HeatGaugeRegion> heatGaugeRegions;

	public virtual void Initialize(HeatableMetal _metal) { }
	public virtual void Tick(float _deltaTime, float _heat01) { }
	
	public abstract bool IsWorkable(float _heat01);
	public abstract bool IsMelting(float _heat01);
	public abstract float GetMeltIntensity(float _heat01);
	public abstract float GetMeltingTempOverride(float _temperature, float _deltaTime);
	public abstract Color SampleColor(float _heat01, bool _avoidPulse = false);
	public abstract Color GetQuenchColor();
	public abstract MetalFeel Sample(float _heat01);
	public abstract List<HeatGaugeRegion> GetHeatGaugeRegions();

	public MeltFeel SampleMelt(float intensity)
	{
		intensity = Mathf.Clamp01(intensity);

		float mult = Mathf.Max(0f, meltSpeedByIntensity.Evaluate(intensity));

		return new MeltFeel
		{
			outwardSpeed = intensity > 0f ? meltOutwardSpeed * mult : 0f
		};
	}

	public float NormalizeHeat01(float _temperature)
	{
		float max = Mathf.Max(1f, referenceMaxTemp);
		return Mathf.Clamp01(_temperature / max);
	}

	public float GetHeatGaugeRegionEnd(int _i)
	{
		return _i + 1 < heatGaugeRegions.Count ? heatGaugeRegions[_i + 1].startHeat : 1f;
	}
}
