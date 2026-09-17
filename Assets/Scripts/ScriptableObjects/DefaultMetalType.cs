using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DefaultMetalType", menuName = "Forging/MetalType/Default", order = 0)]
public class DefaultMetalType : MetalType
{
	[Header("Forging Workability")]
	[Range(0f, 1f)] public float minHeatToForge = 0.2f;
	[Range(0f, 1f)] public float coldStrikeScale = 0.15f;
	
	[Header("Overheat Melting")]
	[Range(0f, 1f)] public float meltStartHeat = 0.72f;
	[Range(0f, 1f)] public float meltFullHeat = 0.9f;
	public float overheatingCoolRate = 50f;

	[Header("Heat Tint")]
	[SerializeField] bool applyHeatTint = true;
	[Range(0f, 1f)] public float metalColorBlend;
	[SerializeField] Gradient heatTintGradient;
	[SerializeField] Color quenchedTint = new Color(0.2f, 0.1f, 0.35f);

	[Header("Overheat Indicator")]
	[SerializeField] bool showOverheatIndicator = true;
	[SerializeField] Color overheatPulseColor = new Color(1f, 0.95f, 0.35f, 1f);
	[SerializeField] float overheatPulseSpeed = 6f;
	[Range(0f, 1f)]
	[SerializeField] float overheatPulseStrength = 0.75f;
	
	public override bool IsWorkable(float _heat01)
	{
		return _heat01 >= minHeatToForge;
	}

	public override bool IsMelting(float _heat01)
	{
		return meltingEnabled && _heat01 > meltStartHeat;
	}

	public override float GetMeltIntensity(float _heat01)
	{
		if (!meltingEnabled || _heat01 < meltStartHeat)
			return 0f;

		float fullHeat = Mathf.Max(meltStartHeat + 0.001f, meltFullHeat);

		return Mathf.InverseLerp(meltStartHeat, fullHeat, _heat01);
	}

	public override float GetMeltingTempOverride(float _temperature, float _deltaTime)
	{
		return Mathf.MoveTowards(_temperature, meltStartHeat * referenceMaxTemp, overheatingCoolRate * _deltaTime);
	}

	public override Color SampleColor(float _heat01, bool _inAnvil = false)
	{
		if (!applyHeatTint)
			return metalColor;
		
		Color heatedColor = Color.Lerp(metalColor, Color.Lerp(metalColor, heatTintGradient.Evaluate(_heat01), metalColorBlend), _heat01);

		if (showOverheatIndicator && IsMelting(_heat01) && !_inAnvil)
		{
			float pulse = (Mathf.Sin(Time.time * overheatPulseSpeed) + 1f) * 0.5f;
			heatedColor = Color.Lerp(heatedColor, overheatPulseColor, pulse * overheatPulseStrength);
		}

		return heatedColor;
	}

	public override Color GetQuenchColor()
	{
		return Color.Lerp(quenchedTint, metalColor, metalColorBlend);
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
			mobility *= coldStrikeScale;
			magnet *= coldStrikeScale;
		}

		return new MetalFeel
		{
			mobility = mobility,
			magnet = magnet,
			tension = tension
		};
	}

	public override List<HeatGaugeRegion> GetHeatGaugeRegions()
	{
		if (heatGaugeRegions.Count > 3)
			heatGaugeRegions.RemoveRange(3, heatGaugeRegions.Count - 3);

		heatGaugeRegions[0].startHeat = 0f;
		heatGaugeRegions[1].startHeat = minHeatToForge;
		heatGaugeRegions[2].startHeat = meltStartHeat;
		
		return heatGaugeRegions;
	}

	float RemapWorldHeatToForgeCurve(float _heat01)
	{
		const float workingCurveHeat = 0.55f;
		if (_heat01 <= minHeatToForge)
			return minHeatToForge <= 0.0001f ? 0f : workingCurveHeat * (_heat01 / minHeatToForge);

		return Mathf.Lerp(workingCurveHeat, 1f, Mathf.InverseLerp(minHeatToForge , 1f, _heat01));
	}
}
