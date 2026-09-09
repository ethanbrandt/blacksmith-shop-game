using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Pickable))]
public class HeatableMetal : MonoBehaviour
{
	const int LegacyProgressVersion = 0;
	const float MinimumReferenceTemperature = 0.0001f;
	const float MetalColorBlend = 0.65f;
	const float DamageForMaximumDarkening = 100f;

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

	[Header("Grind Progress")]
	[Tooltip("Baseline silhouette used by the grindstone (usually the forged outline).")]
	[SerializeField] List<Vector2> groundVertices = new List<Vector2>();
	[SerializeField] List<float> grindAmounts = new List<float>();
	[SerializeField] bool hasGrindProgress;
	[SerializeField] SharpnessQuality sharpnessQuality = SharpnessQuality.Blunt;

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
	[SerializeField] Color quenchedTint = new Color(0.2f, 0.1f, 0.35f);
	[SerializeField] float damageDarken = 0.35f;

	[Header("Overheat Indicator")]
	[SerializeField] bool showOverheatIndicator = true;
	[SerializeField] Color overheatPulseColor = new Color(1f, 0.95f, 0.35f, 1f);
	[SerializeField] float overheatPulseSpeed = 6f;
	[Range(0f, 1f)]
	[SerializeField] float overheatPulseStrength = 0.75f;
	MetalHeatGauge metalHeatGauge;

	static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	static readonly int ColorId = Shader.PropertyToID("_Color");

	Pickable pickable;
	Renderer visualRenderer;
	MaterialPropertyBlock tintBlock;
	float overheatTimer;
	float forgeMatchPercent;
	float grindMatchPercent;
	int progressVersion = WorkpieceProgress.CurrentVersion;
	public MetalType MetalType => metalType;
	public PartDefinition PartDefinition => partDefinition;
	public Pickable Pickable => pickable;
	public float Temperature => temperature;
	public float Damage => damage;
	public bool IsTooCold => metalType != null && metalType.IsTooCold(Heat01);
	public bool IsQuenchTemp => metalType != null && metalType.IsQuenchTemp(Heat01);
	public bool IsOverheating => metalType != null && metalType.IsOverheating(temperature);
	public float Heat01 => metalType != null ? metalType.NormalizeHeat01(temperature) : 0f;
	public bool HasForgeProgress => hasForgeProgress && forgedVertices != null && forgedVertices.Count >= PolygonGeometry.MinimumVertexCount;
	public IReadOnlyList<Vector2> ForgedVertices => forgedVertices;
	public ShapeQuality ForgeQuality => forgeQuality;
	public float ForgeMatchPercent => forgeMatchPercent;
	public bool HasGrindProgress => hasGrindProgress && groundVertices != null && groundVertices.Count >= PolygonGeometry.MinimumVertexCount;
	public IReadOnlyList<Vector2> GroundVertices => groundVertices;
	public IReadOnlyList<float> GrindAmounts => grindAmounts;
	public SharpnessQuality SharpnessQuality => sharpnessQuality;
	public float GrindMatchPercent => grindMatchPercent;

	void Awake()
	{
		if (progressVersion != LegacyProgressVersion && progressVersion != WorkpieceProgress.CurrentVersion)
			ClearProgress();
		progressVersion = WorkpieceProgress.CurrentVersion;
		if (hasForgeProgress && !PolygonGeometry.IsSimple(forgedVertices))
			ClearProgress();
		bool hasInvalidGroundShape = hasGrindProgress && !PolygonGeometry.IsSimple(groundVertices);
		bool shouldValidateGrindAmounts = hasGrindProgress && !hasInvalidGroundShape;
		bool hasInvalidGrindAmounts = shouldValidateGrindAmounts && !ValidAmounts(grindAmounts, groundVertices.Count);
		if (hasInvalidGroundShape || hasInvalidGrindAmounts)
			ClearGrindProgress();
		pickable = GetComponent<Pickable>();
		visualRenderer = GetComponent<Renderer>();
		metalHeatGauge = GetComponentInChildren<MetalHeatGauge>();
	}

	void Start()
	{
		ApplyQualityVisual();
		RefreshTint();
		if (metalHeatGauge != null)
			metalHeatGauge.SetFollowMetal(this);
	}

	void Update()
	{
		var furnace = pickable != null ? pickable.ContainingStation as Furnace : null;
		bool isInActiveFurnace = furnace != null && furnace.isActiveAndEnabled;
		float? furnaceTemperature = isInActiveFurnace ? furnace.InternalTemperature : (float? )null;
		TickTemperature(Time.deltaTime, furnaceTemperature);
		if (metalHeatGauge != null)
			metalHeatGauge.SetEnable(temperature > ambientTemperature);
	}

	public void SetMetalType(MetalType type)
	{
		metalType = type;
		RefreshTint();
	}

	public void SetPartDefinition(PartDefinition part)
	{
		if (partDefinition != part)
			ClearProgress();
		partDefinition = part;
		ApplyQualityVisual();
	}

	public void SetTemperature(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
			return;
		temperature = value;
		if (!IsOverheating)
			overheatTimer = 0f;
		RefreshTint();
	}

	public void SetHeat01(float heat01)
	{
		heat01 = Mathf.Clamp01(heat01);
		if (metalType != null)
			temperature = heat01 * Mathf.Max(MinimumReferenceTemperature, metalType.referenceMaxTemp);
		else
			temperature = heat01;
		RefreshTint();
	}

	public void Quench()
	{
		temperature = ambientTemperature;
		overheatTimer = 0f;
		if (visualRenderer == null)
			return;
		var quenchColor = Color.Lerp(quenchedTint, metalType != null ? metalType.metalColor : Color.white, MetalColorBlend);
		tintBlock ??= new MaterialPropertyBlock();
		visualRenderer.GetPropertyBlock(tintBlock);
		tintBlock.SetColor(BaseColorId, quenchColor);
		tintBlock.SetColor(ColorId, quenchColor);
		visualRenderer.SetPropertyBlock(tintBlock);
	}

	public void SaveForgeProgress(IReadOnlyList<Vector2> vertices, float heat01, ShapeQuality quality, float matchPercent, PartDefinition forgedPart)
	{
		if (!PolygonGeometry.IsSimple(vertices) || !IsFinite(matchPercent))
			return;
		// Public callers may pass this workpiece's own read-only view.
		if (ReferenceEquals(vertices, forgedVertices))
			vertices = new List<Vector2>(vertices);
		if (forgedPart != null && forgedPart != partDefinition)
			SetPartDefinition(forgedPart);
		bool hasShapeChanged = forgedVertices.Count != vertices.Count;
		for (int i = 0; !hasShapeChanged && i < vertices.Count; i++)
			hasShapeChanged = forgedVertices[i] != vertices[i];
		if (hasShapeChanged)
			ClearGrindProgress();
		forgedVertices.Clear();
		for (int i = 0; i < vertices.Count; i++)
			forgedVertices.Add(vertices[i]);

		hasForgeProgress = true;
		forgeQuality = quality;
		forgeMatchPercent = Mathf.Clamp01(matchPercent);
		if (forgedPart != null)
			partDefinition = forgedPart;
		ApplyQualityVisual();
	}

	public void SaveGrindProgress(IReadOnlyList<Vector2> baselineVertices, IReadOnlyList<float> amounts, SharpnessQuality sharpness, float matchPercent)
	{
		bool hasValidShape = PolygonGeometry.IsSimple(baselineVertices);
		bool hasValidAmounts = hasValidShape && ValidAmounts(amounts, baselineVertices.Count);
		bool hasValidProgress = hasValidAmounts && IsFinite(matchPercent);
		if (!hasValidProgress)
			return;
		if (ReferenceEquals(baselineVertices, groundVertices))
			baselineVertices = new List<Vector2>(baselineVertices);
		if (ReferenceEquals(amounts, grindAmounts))
			amounts = new List<float>(amounts);
		groundVertices.Clear();
		grindAmounts.Clear();
		for (int i = 0; i < baselineVertices.Count; i++)
			groundVertices.Add(baselineVertices[i]);

		for (int i = 0; i < baselineVertices.Count; i++)
		{
			float amount = amounts != null && i < amounts.Count ? Mathf.Max(0f, amounts[i]) : 0f;
			grindAmounts.Add(amount);
		}

		hasGrindProgress = true;
		sharpnessQuality = sharpness;
		grindMatchPercent = Mathf.Clamp01(matchPercent);
	}

	/// <summary>Called once per frame by this workpiece; stations only supply the environment temperature.</summary>
	public void TickTemperature(float deltaTime, float? furnaceTemperature = null)
	{
		if (metalType == null || deltaTime <= 0f)
			return;
		if (furnaceTemperature > temperature)
			temperature = Mathf.MoveTowards(temperature, furnaceTemperature.Value, metalType.furnaceHeatTransferRate * deltaTime);
		else
			TickOutsideFurnace(deltaTime);

		TickOverheatDamage(deltaTime);
		RefreshTint();
	}

	void ApplyQualityVisual()
	{
	// TODO Add quality visuals
	}

	void TickOutsideFurnace(float deltaTime)
	{
		if (metalType == null || deltaTime <= 0f)
			return;
		if (temperature > metalType.overheatTemp)
		{
			float coolRate = coolToMeltRateOverride >= 0f ? coolToMeltRateOverride : metalType.worldAmbientCoolRate;
			temperature = Mathf.MoveTowards(temperature, metalType.overheatTemp, coolRate * deltaTime);
			return;
		}

		temperature = Mathf.MoveTowards(temperature, ambientTemperature, metalType.worldAmbientCoolRate * deltaTime);
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

		float previousTimer = overheatTimer;
		overheatTimer += deltaTime;
		float graceDuration = overheatGraceOverride >= 0f ? overheatGraceOverride : metalType.overheatGraceDuration;
		if (overheatTimer < graceDuration)
			return;
		float damagePerSecond = overheatDamagePerSecondOverride >= 0f ? overheatDamagePerSecondOverride : metalType.overheatDamagePerSecond;
		float previousDamageDuration = Mathf.Max(0f, previousTimer - graceDuration);
		float currentDamageDuration = Mathf.Max(0f, overheatTimer - graceDuration);
		float damageDuration = currentDamageDuration - previousDamageDuration;
		damage += damagePerSecond * damageDuration;
	}

	void RefreshTint()
	{
		bool hasTintTarget = visualRenderer != null && pickable != null;
		if (!applyHeatTint || !hasTintTarget)
			return;
		bool isQuenched = pickable.Type == Pickable.PickableType.QuenchedMetal;
		if (isQuenched)
			return;

		Color baseColor = metalType != null ? metalType.metalColor : Color.white;
		float heat01 = Heat01;
		Color heatedColor = Color.Lerp(Color.Lerp(coldTint, baseColor, MetalColorBlend), hotTint, heat01);
		if (damage > 0f)
		{
			float damageDarkening = Mathf.Clamp01(damage / DamageForMaximumDarkening) * damageDarken;
			heatedColor = Color.Lerp(heatedColor, Color.black, damageDarkening);
		}

		if (showOverheatIndicator && IsOverheating)
		{
			float pulse = (Mathf.Sin(Time.time * overheatPulseSpeed) + 1f) * 0.5f;
			heatedColor = Color.Lerp(heatedColor, overheatPulseColor, pulse * overheatPulseStrength);
		}

		if (visualRenderer is SpriteRenderer spriteRenderer)
		{
			spriteRenderer.color = heatedColor;
			return;
		}

		tintBlock ??= new MaterialPropertyBlock();
		visualRenderer.GetPropertyBlock(tintBlock);
		tintBlock.SetColor(BaseColorId, heatedColor);
		tintBlock.SetColor(ColorId, heatedColor);
		visualRenderer.SetPropertyBlock(tintBlock);
	}

	void ClearGrindProgress()
	{
		groundVertices.Clear();
		grindAmounts.Clear();
		hasGrindProgress = false;
		sharpnessQuality = SharpnessQuality.Blunt;
		grindMatchPercent = 0f;
	}

	void ClearProgress()
	{
		forgedVertices.Clear();
		hasForgeProgress = false;
		forgeQuality = ShapeQuality.Incomplete;
		forgeMatchPercent = 0f;
		ClearGrindProgress();
	}

	static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	static bool ValidAmounts(IReadOnlyList<float> amounts, int count)
	{
		if (amounts == null || amounts.Count != count)
			return false;
		for (int i = 0; i < count; i++)
			if (!IsFinite(amounts[i]) || amounts[i] < 0f)
				return false;
		return true;
	}

	public WorkpieceProgress CaptureProgress() => new WorkpieceProgress
	{
		part = partDefinition,
		forgedVertices = new List<Vector2>(forgedVertices),
		forgeQuality = forgeQuality,
		forgeMatch = forgeMatchPercent,
		groundVertices = new List<Vector2>(groundVertices),
		grindAmounts = new List<float>(grindAmounts),
		sharpness = sharpnessQuality,
		grindMatch = grindMatchPercent
	};
	public bool RestoreProgress(WorkpieceProgress saved)
	{
		if (saved == null)
			return false;
		bool matchesVersion = saved.version == WorkpieceProgress.CurrentVersion;
		bool matchesPart = saved.part == partDefinition;
		if (!matchesVersion || !matchesPart)
			return false;
		bool hasVertexLists = saved.forgedVertices != null && saved.groundVertices != null;
		bool hasProgressLists = hasVertexLists && saved.grindAmounts != null;
		if (!hasProgressLists)
			return false;
		bool hasValidForgeShape = saved.forgedVertices.Count == 0 || PolygonGeometry.IsSimple(saved.forgedVertices);
		bool hasValidGrindShape = saved.groundVertices.Count == 0 || PolygonGeometry.IsSimple(saved.groundVertices);
		if (!hasValidForgeShape || !hasValidGrindShape)
			return false;
		bool hasValidAmounts = ValidAmounts(saved.grindAmounts, saved.groundVertices.Count);
		bool hasFiniteScores = IsFinite(saved.forgeMatch) && IsFinite(saved.grindMatch);
		if (!hasValidAmounts || !hasFiniteScores)
			return false;
		ClearProgress();
		if (saved.forgedVertices.Count > 0)
			SaveForgeProgress(saved.forgedVertices, Heat01, saved.forgeQuality, saved.forgeMatch, partDefinition);
		if (saved.groundVertices.Count > 0)
			SaveGrindProgress(saved.groundVertices, saved.grindAmounts, saved.sharpness, saved.grindMatch);
		progressVersion = saved.version;
		return true;
	}
}
