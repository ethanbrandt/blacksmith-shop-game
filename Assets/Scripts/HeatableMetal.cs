using System.Collections.Generic;
using ForgingPrototype;
using UnityEngine;

[RequireComponent(typeof(Pickable))]
public class HeatableMetal : MonoBehaviour
{
	[Header("Metal")]
	[SerializeField] MetalType metalType;
	[SerializeField] PartDefinition partDefinition;
	[SerializeField] float temperature = 20f;
	[SerializeField] float ambientTemperature = 20f;
	[SerializeField] float damage;

	[Header("Forge Progress")]
	[Tooltip("Saved billet outline from anvil forging. Restored the next time this piece is forged.")]
	[SerializeField] List<Vector2> forgedVertices = new List<Vector2>();
	[SerializeField] bool hasForgeProgress;
	[SerializeField] ShapeQuality forgeQuality = ShapeQuality.Incomplete;

	[Header("Overheat Overrides")]
	[Tooltip("If >= 0, overrides MetalType.overheatGraceDuration.")]
	[SerializeField] float overheatGraceOverride = -1f;
	[Tooltip("If >= 0, overrides MetalType.overheatDamagePerSecond.")]
	[SerializeField] float overheatDamagePerSecondOverride = -1f;
	[Tooltip("If >= 0, cool rate toward melt temp when removed while overheated. Otherwise uses MetalType.worldAmbientCoolRate.")]
	[SerializeField] float coolToMeltRateOverride = -1f;

	[Header("Heat Tint")]
	[SerializeField] bool applyHeatTint = true;
	[SerializeField] Color coldTint = new Color(0.45f, 0.48f, 0.55f, 1f);
	[SerializeField] Color hotTint = new Color(1f, 0.45f, 0.12f, 1f);
	[SerializeField] float damageDarken = 0.35f;

	[Header("Overheat Indicator")]
	[SerializeField] bool showOverheatIndicator = true;
	[SerializeField] Color overheatPulseColor = new Color(1f, 0.95f, 0.35f, 1f);
	[SerializeField] float overheatPulseSpeed = 6f;
	[SerializeField, Range(0f, 1f)] float overheatPulseStrength = 0.75f;

	static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	static readonly int ColorId = Shader.PropertyToID("_Color");

	Pickable pickable;
	Renderer visualRenderer;
	MaterialPropertyBlock tintBlock;
	float overheatTimer;
	bool suspendWorldTicks;

	public MetalType MetalType => metalType;
	public PartDefinition PartDefinition => partDefinition;
	public float Temperature => temperature;
	public float Damage => damage;
	public bool IsTooCold => metalType != null && metalType.IsTooCold(temperature);
	public bool IsOverheating => metalType != null && metalType.IsOverheating(temperature);
	public float Heat01 => metalType != null ? metalType.NormalizeHeat01(temperature) : 0f;
	public bool HasForgeProgress => hasForgeProgress && forgedVertices != null && forgedVertices.Count >= 3;
	public IReadOnlyList<Vector2> ForgedVertices => forgedVertices;
	public ShapeQuality ForgeQuality => forgeQuality;
	public bool SuspendWorldTicks
	{
		get => suspendWorldTicks;
		set => suspendWorldTicks = value;
	}

	void Awake()
	{
		pickable = GetComponent<Pickable>();
	}

	void Start()
	{
		ResolveRenderer();
		ApplyQualityVisual();
		RefreshTint();
	}

	void Update()
	{
		if (suspendWorldTicks)
			return;

		if (pickable != null && pickable.IsInFurnace)
			return;

		TickOutsideFurnace(Time.deltaTime);
		RefreshTint();
	}

	public void SetMetalType(MetalType type)
	{
		metalType = type;
		RefreshTint();
	}

	public void SetPartDefinition(PartDefinition part)
	{
		partDefinition = part;
		ApplyQualityVisual();
	}

	public void SetTemperature(float value)
	{
		temperature = value;
		RefreshTint();
	}

	public void SetHeat01(float heat01)
	{
		heat01 = Mathf.Clamp01(heat01);
		if (metalType != null)
			temperature = heat01 * Mathf.Max(0.0001f, metalType.referenceMaxTemp);
		else
			temperature = heat01;
		RefreshTint();
	}

	public void SaveForgeProgress(
		IReadOnlyList<Vector2> vertices,
		float heat01,
		ShapeQuality quality,
		PartDefinition forgedPart)
	{
		if (vertices == null || vertices.Count < 3)
			return;

		forgedVertices.Clear();
		for (int i = 0; i < vertices.Count; i++)
			forgedVertices.Add(vertices[i]);

		hasForgeProgress = true;
		forgeQuality = quality;
		if (forgedPart != null)
			partDefinition = forgedPart;

		SetHeat01(heat01);
		ApplyQualityVisual();
	}

	public void ClearForgeProgress()
	{
		forgedVertices.Clear();
		hasForgeProgress = false;
		forgeQuality = ShapeQuality.Incomplete;
		ApplyQualityVisual();
	}

	public void TickTowardFurnace(float furnaceTemperature, float deltaTime)
	{
		if (metalType == null || deltaTime <= 0f)
			return;

		temperature = Mathf.MoveTowards(
			temperature,
			furnaceTemperature,
			metalType.furnaceHeatTransferRate * deltaTime);

		TickOverheatDamage(deltaTime);
		RefreshTint();
	}

	void ApplyQualityVisual()
	{
		ResolveRenderer();
		if (visualRenderer is not SpriteRenderer spriteRenderer)
			return;

		Sprite next = null;
		if (partDefinition != null)
			next = partDefinition.GetSpriteForQuality(hasForgeProgress ? forgeQuality : ShapeQuality.Incomplete);

		if (next != null)
			spriteRenderer.sprite = next;
	}

	void TickOutsideFurnace(float deltaTime)
	{
		if (metalType == null || deltaTime <= 0f)
			return;

		overheatTimer = 0f;

		if (temperature > metalType.overheatTemp)
		{
			float coolRate = coolToMeltRateOverride >= 0f
				? coolToMeltRateOverride
				: metalType.worldAmbientCoolRate;
			temperature = Mathf.MoveTowards(temperature, metalType.overheatTemp, coolRate * deltaTime);
			return;
		}

		temperature = Mathf.MoveTowards(
			temperature,
			ambientTemperature,
			metalType.worldAmbientCoolRate * deltaTime);
	}

	void TickOverheatDamage(float deltaTime)
	{
		if (metalType == null || deltaTime <= 0f)
			return;

		if (!metalType.IsOverheating(temperature))
		{
			overheatTimer = 0f;
			return;
		}

		overheatTimer += deltaTime;
		float grace = overheatGraceOverride >= 0f ? overheatGraceOverride : metalType.overheatGraceDuration;
		if (overheatTimer < grace)
			return;

		float dps = overheatDamagePerSecondOverride >= 0f
			? overheatDamagePerSecondOverride
			: metalType.overheatDamagePerSecond;
		damage += dps * deltaTime;
	}

	void RefreshTint()
	{
		if (!applyHeatTint)
			return;

		ResolveRenderer();
		if (visualRenderer == null)
			return;

		Color baseColor = metalType != null ? metalType.metalColor : Color.white;
		float heat01 = Heat01;
		Color heated = Color.Lerp(Color.Lerp(coldTint, baseColor, 0.65f), hotTint, heat01);
		if (damage > 0f)
		{
			float dark = Mathf.Clamp01(damage / 100f) * damageDarken;
			heated = Color.Lerp(heated, Color.black, dark);
		}

		if (showOverheatIndicator && IsOverheating)
		{
			float pulse = (Mathf.Sin(Time.time * overheatPulseSpeed) + 1f) * 0.5f;
			heated = Color.Lerp(heated, overheatPulseColor, pulse * overheatPulseStrength);
		}

		if (visualRenderer is SpriteRenderer spriteRenderer)
		{
			spriteRenderer.color = heated;
			return;
		}

		tintBlock ??= new MaterialPropertyBlock();
		visualRenderer.GetPropertyBlock(tintBlock);
		tintBlock.SetColor(BaseColorId, heated);
		tintBlock.SetColor(ColorId, heated);
		visualRenderer.SetPropertyBlock(tintBlock);
	}

	void ResolveRenderer()
	{
		if (visualRenderer != null)
			return;

		if (pickable != null)
			visualRenderer = pickable.VisualRenderer;
		if (visualRenderer == null)
			visualRenderer = GetComponentInChildren<Renderer>();
	}
}
