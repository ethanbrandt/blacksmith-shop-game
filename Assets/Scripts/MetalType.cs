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
		float mobility = Mathf.Max(0f, mobilityByHeat.Evaluate(heat01));
		float magnet = Mathf.Max(0f, magnetByHeat.Evaluate(heat01));
		float tension = Mathf.Max(0.05f, tensionByHeat.Evaluate(heat01));

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

	public static MetalType CreateSoftGold()
	{
		var t = CreateInstance<MetalType>();
		t.name = "SoftGold";
		t.displayName = "Soft Gold";
		t.metalColor = new Color(1f, 0.78f, 0.22f, 1f);
		t.coolRate = 0.06f;
		t.holdHeatRate = 0.4f;
		t.minHeatToForge = 0.12f;
		t.coldStrikeScale = 0.35f;
		t.workingTempMin = 160f;
		t.overheatTemp = 620f;
		t.referenceMaxTemp = 900f;
		t.furnaceHeatTransferRate = 70f;
		t.worldAmbientCoolRate = 22f;
		t.overheatGraceDuration = 0.9f;
		t.overheatDamagePerSecond = 12f;
		t.mobilityByHeat = new AnimationCurve(
			new Keyframe(0f, 0.35f),
			new Keyframe(0.4f, 0.85f),
			new Keyframe(1f, 1.15f));
		t.magnetByHeat = new AnimationCurve(
			new Keyframe(0f, 0.25f),
			new Keyframe(0.35f, 0.8f),
			new Keyframe(1f, 1.2f));
		t.tensionByHeat = new AnimationCurve(
			new Keyframe(0f, 1.0f),
			new Keyframe(1f, 0.7f));
		return t;
	}

	public static MetalType CreateIron()
	{
		var t = CreateInstance<MetalType>();
		t.name = "Iron";
		t.displayName = "Iron";
		t.metalColor = new Color(1f, 0.35f, 0.08f, 1f);
		t.coolRate = 0.1f;
		t.holdHeatRate = 0.32f;
		t.minHeatToForge = 0.28f;
		t.coldStrikeScale = 0.12f;
		t.workingTempMin = 280f;
		t.overheatTemp = 820f;
		t.referenceMaxTemp = 1100f;
		t.furnaceHeatTransferRate = 45f;
		t.worldAmbientCoolRate = 14f;
		t.overheatGraceDuration = 1.5f;
		t.overheatDamagePerSecond = 7f;
		t.mobilityByHeat = new AnimationCurve(
			new Keyframe(0f, 0.1f),
			new Keyframe(0.35f, 0.35f),
			new Keyframe(0.7f, 0.85f),
			new Keyframe(1f, 1.05f));
		t.magnetByHeat = new AnimationCurve(
			new Keyframe(0f, 0.05f),
			new Keyframe(0.4f, 0.35f),
			new Keyframe(0.75f, 0.85f),
			new Keyframe(1f, 1f));
		t.tensionByHeat = new AnimationCurve(
			new Keyframe(0f, 1.35f),
			new Keyframe(1f, 0.9f));
		return t;
	}

	public static MetalType CreateScrap()
	{
		var t = CreateInstance<MetalType>();
		t.name = "Scrap";
		t.displayName = "Brittle Scrap";
		t.metalColor = new Color(0.75f, 0.55f, 0.45f, 1f);
		t.coolRate = 0.14f;
		t.holdHeatRate = 0.28f;
		t.minHeatToForge = 0.45f;
		t.coldStrikeScale = 0.05f;
		t.workingTempMin = 360f;
		t.overheatTemp = 700f;
		t.referenceMaxTemp = 950f;
		t.furnaceHeatTransferRate = 60f;
		t.worldAmbientCoolRate = 20f;
		t.overheatGraceDuration = 0.75f;
		t.overheatDamagePerSecond = 15f;
		t.mobilityByHeat = new AnimationCurve(
			new Keyframe(0f, 0.05f),
			new Keyframe(0.5f, 0.2f),
			new Keyframe(0.8f, 0.75f),
			new Keyframe(1f, 1.25f));
		t.magnetByHeat = new AnimationCurve(
			new Keyframe(0f, 0f),
			new Keyframe(0.55f, 0.15f),
			new Keyframe(0.85f, 0.7f),
			new Keyframe(1f, 1.1f));
		t.tensionByHeat = new AnimationCurve(
			new Keyframe(0f, 1.5f),
			new Keyframe(0.7f, 1.1f),
			new Keyframe(1f, 0.75f));
		return t;
	}
}
