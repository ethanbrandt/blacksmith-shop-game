using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(fileName = "LivingMetalType", menuName = "Forging/MetalType/LivingMetal", order = 2)]
public class LivingMetalType : MetalType
{
	[Header("Forging Workability")]
	[Range(0f, 1f)]
	[SerializeField] float minHeatToForge = 0.2f;
	[Range(0f, 1f)]
	[SerializeField] float coldStrikeScale = 0.15f;
	
	[Header("Overheat Melting")]
	[Range(0f, 1f)]
	[SerializeField] public float meltStartHeat = 0.72f;
	[Range(0f, 1f)]
	[SerializeField] float meltFullHeat = 0.9f;
	[SerializeField] float overheatingCoolRate = 50f;

	[Header("Heat Tint")]
	[SerializeField] bool applyHeatTint = true;
	[Range(0f, 1f)]
	[SerializeField] float metalColorBlend;
	[SerializeField] Gradient heatTintGradient;
	[SerializeField] Color quenchedTint = new Color(0.2f, 0.1f, 0.35f);

	[Header("Overheat Indicator")]
	[SerializeField] bool showOverheatIndicator = true;
	[SerializeField] Color overheatPulseColor = new Color(1f, 0.95f, 0.35f, 1f);
	[SerializeField] float overheatPulseSpeed = 6f;
	[Range(0f, 1f)]
	[SerializeField] float overheatPulseStrength = 0.75f;

	[Header("LivingMetalness")]
	[SerializeField] float moveSpeed;
	[SerializeField] LivingMetalSprites sprites;
	
	public override void Initialize(HeatableMetal _metal)
	{
		Destroy(_metal.GetComponent<NavMeshObstacle>());
		var navAgent = _metal.AddComponent<NavMeshAgent>();
		var livingMetal = _metal.AddComponent<LivingMetalAgent>();
		
		livingMetal.SetForgeSprites(sprites);

		navAgent.speed = moveSpeed;
		navAgent.angularSpeed = 120;
		navAgent.acceleration = 10;
	}

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

	public override Color SampleColor(float _heat01, bool _avoidPulse = false)
	{
		if (!applyHeatTint)
			return metalColor;
		
		Color heatedColor = Color.Lerp(metalColor, Color.Lerp(metalColor, heatTintGradient.Evaluate(_heat01), metalColorBlend), _heat01);

		if (showOverheatIndicator && IsMelting(_heat01) && !_avoidPulse)
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
		if (_heat01 <= minHeatToForge)
			return minHeatToForge <= 0.0001f ? 0f : minHeatToForge;

		Debug.Log(Mathf.InverseLerp(minHeatToForge , meltStartHeat, _heat01));
		return Mathf.InverseLerp(minHeatToForge , meltStartHeat, _heat01);
	}
}
