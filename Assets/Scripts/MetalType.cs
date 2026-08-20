using UnityEngine;

public struct MetalFeel
{
	public float mobility;
	public float magnet;
	public float tension;
	public bool canForge;
}

/// <summary>
/// Static metal identity: heat response for forging and world furnace temperature bands.
/// </summary>
[CreateAssetMenu(fileName = "MetalType", menuName = "Forging Prototype/Metal Type", order = 1)]
public class MetalType : ScriptableObject
{
	[Header("Identity")]
	public string displayName = "Metal";
	public Color metalColor = new Color(1f, 0.35f, 0.08f, 1f);

	[Header("Forging Heat Rates (0-1 heat)")]
	[Tooltip("Heat lost per second while not heating on the forging screen.")]
	public float coolRate = 0.08f;
	[Tooltip("Heat gained per second while hold-to-heat is pressed (forging stand-in).")]
	public float holdHeatRate = 0.35f;

	[Header("Forging Heat Curves (X = heat 0-1, Y = multiplier)")]
	public AnimationCurve mobilityByHeat = AnimationCurve.Linear(0f, 0.15f, 1f, 1f);
	public AnimationCurve magnetByHeat = AnimationCurve.Linear(0f, 0.1f, 1f, 1f);
	public AnimationCurve tensionByHeat = AnimationCurve.Linear(0f, 1.2f, 1f, 0.85f);

	[Header("Forging Workability")]
	[Range(0f, 1f)] public float minHeatToForge = 0.2f;
	[Tooltip("Below min heat, strikes still land but at this mobility floor.")]
	[Range(0f, 1f)] public float coldStrikeScale = 0.15f;

	[Header("World Temperature Bands")]
	[Tooltip("Below this temperature the metal is too cold to work.")]
	public float workingTempMin = 220f;
	[Tooltip("At or above this temperature the metal begins taking overheat damage after the grace timer.")]
	public float overheatTemp = 780f;
	[Tooltip("Reference max used for tint / 0-1 normalization.")]
	public float referenceMaxTemp = 1000f;

	[Header("World Heat Transfer")]
	[Tooltip("How fast temperature moves toward the furnace internal temp (units/sec).")]
	public float furnaceHeatTransferRate = 55f;
	[Tooltip("How fast temperature falls toward ambient when outside a furnace (units/sec).")]
	public float worldAmbientCoolRate = 18f;

	[Header("Overheat Damage Defaults")]
	[Tooltip("Seconds above overheatTemp before damage starts accumulating.")]
	public float overheatGraceDuration = 1.25f;
	[Tooltip("Damage added per second while overheated after the grace timer.")]
	public float overheatDamagePerSecond = 8f;

	public bool IsTooCold(float temperature) => temperature < workingTempMin;
	public bool IsOverheating(float temperature) => temperature >= overheatTemp;

	public float NormalizeHeat01(float temperature)
	{
		float max = Mathf.Max(0.0001f, referenceMaxTemp);
		return Mathf.Clamp01(temperature / max);
	}

	public MetalFeel Sample(float heat01)
	{
		heat01 = Mathf.Clamp01(heat01);
		bool canForge = heat01 >= minHeatToForge;
		// World heat is temperature / referenceMaxTemp, so "just workable" is only ~0.27.
		// The forge curves were authored for the old 0-1 overlay slider that started at 0.55.
		float curveHeat = RemapWorldHeatToForgeCurve(heat01);
		float mobility = Mathf.Max(0f, mobilityByHeat.Evaluate(curveHeat));
		float magnet = Mathf.Max(0f, magnetByHeat.Evaluate(curveHeat));
		float tension = Mathf.Max(0.05f, tensionByHeat.Evaluate(curveHeat));

		if (!canForge)
		{
			mobility *= coldStrikeScale;
			magnet *= coldStrikeScale;
		}

		return new MetalFeel
		{
			mobility = mobility,
			magnet = magnet,
			tension = tension,
			canForge = canForge
		};
	}

	float RemapWorldHeatToForgeCurve(float heat01)
	{
		float working01 = Mathf.Clamp01(workingTempMin / Mathf.Max(0.0001f, referenceMaxTemp));
		const float workingCurveHeat = 0.55f;
		if (heat01 <= working01)
			return working01 <= 0.0001f ? 0f : workingCurveHeat * (heat01 / working01);

		return Mathf.Lerp(workingCurveHeat, 1f, Mathf.InverseLerp(working01, 1f, heat01));
	}
}
