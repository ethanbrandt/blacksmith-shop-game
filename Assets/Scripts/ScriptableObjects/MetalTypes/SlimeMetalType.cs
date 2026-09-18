using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SlimyMetalType", menuName = "Forging/MetalType/Slimy", order = 1)]
public class SlimeMetalType : MetalType
{
	[Header("Heat Tint")]
	[SerializeField] bool applyHeatTint = true;
	[Range(0f, 1f)]
	[SerializeField] float metalColorBlend = 0.75f;
	[SerializeField] Gradient heatTintGradient;
	[SerializeField] Color quenchedTint = new Color(0.2f, 0.1f, 0.35f);

	[Header("Melt Indicator")]
	[SerializeField] bool showMeltIndicator = true;
	[SerializeField] Color meltPulseColor = new Color(1f, 0.95f, 0.35f, 1f);
	[SerializeField] float meltPulseSpeed = 6f;
	[Range(0f, 1f)]
	[SerializeField] float meltPulseStrength = 0.75f;

	[Header("Slimy Melt")]
	[Range(0f, 1f)]
	[SerializeField] float meltHeat = 0.1f;
	[SerializeField] float meltCoolRate = 5f;
	[SerializeField] float slimyStrikeMobility;
	[SerializeField] float slimyStrikeMagnet;
	[SerializeField] float slimyStrikeTension;
	[SerializeField] GameObject slimeManagerPrefab;
	
	public override void Initialize(HeatableMetal _metal)
	{
		var obj = Instantiate(slimeManagerPrefab);
		var manager = obj.GetComponent<SlimeManager>();
		manager.Initialize(_metal);
	}

	public override bool IsWorkable(float _heat01)
	{
		return _heat01 > meltHeat;
	}

	public override bool IsMelting(float _heat01)
	{
		return meltingEnabled && _heat01 <= meltHeat;
	}

	public override float GetMeltIntensity(float _heat01)
	{
		if (!meltingEnabled)
			return 0f;

		return Mathf.InverseLerp(meltHeat, 0f, _heat01);
	}

	public override float GetMeltingTempOverride(float _temperature, float _deltaTime)
	{
		return Mathf.MoveTowards(_temperature, 0f, meltCoolRate * _deltaTime);
	}

	public override Color SampleColor(float _heat01, bool _avoidPulse = false)
	{
		if (!applyHeatTint)
			return metalColor;
		
		Color heatedColor = Color.Lerp(metalColor, Color.Lerp(metalColor, heatTintGradient.Evaluate(_heat01), metalColorBlend), _heat01);

		if (showMeltIndicator && IsMelting(_heat01) && !_avoidPulse)
		{
			float pulse = (Mathf.Sin(Time.time * meltPulseSpeed) + 1f) * 0.35f;
			heatedColor = Color.Lerp(heatedColor, meltPulseColor, pulse * meltPulseStrength);
		}

		return heatedColor;
	}

	public override Color GetQuenchColor()
	{
		return Color.Lerp(metalColor, quenchedTint, metalColorBlend);
	}

	public override MetalFeel Sample(float _heat01)
	{
		_heat01 = Mathf.Clamp01(_heat01);
		float curveHeat = RemapWorldHeatToForgeCurve(_heat01);
		float mobility = Mathf.Max(0f, mobilityByHeat.Evaluate(curveHeat));
		float magnet = Mathf.Max(0f, magnetByHeat.Evaluate(curveHeat));
		float tension = Mathf.Max(0.05f, tensionByHeat.Evaluate(curveHeat));

		if (!IsWorkable(_heat01))
		{
			mobility = slimyStrikeMobility;
			magnet = slimyStrikeMagnet;
			tension = slimyStrikeTension;
		}

		return new MetalFeel
		{
			mobility = mobility,
			magnet = magnet,
			tension = tension
		};
	}
	
	float RemapWorldHeatToForgeCurve(float _heat01)
	{
		return !IsWorkable(_heat01) ? 0f : Mathf.InverseLerp(meltHeat, 1f, _heat01);
	}

	public override List<HeatGaugeRegion> GetHeatGaugeRegions()
	{
		if (heatGaugeRegions.Count > 2)
			heatGaugeRegions.RemoveRange(2, heatGaugeRegions.Count - 2);

		heatGaugeRegions[0].startHeat = 0f;
		heatGaugeRegions[1].startHeat = meltHeat;
		
		return heatGaugeRegions;
	}
}
