using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Pickable))]
public class HeatableMetal : MonoBehaviour
{
	const float MinimumReferenceTemperature = 0.0001f;

	[Header("Metal")]
	[SerializeField] float temperature = 20f;
	[SerializeField] float ambientTemperature = 20f;
	[Min(0.1f)]
	[SerializeField] float maxRadius = 2f;
	
	[Header("Starting Shape")]
	[SerializeField] int startingVertexCount = 24;
	[SerializeField] Vector2 ovalRadii = new Vector2(1.4f, 0.7f);

	[Header("Overheat Overrides")]
	[Tooltip("If >= 0, overrides MetalType.overheatGraceDuration.")]
	[SerializeField] float overheatGraceOverride = -1f;
	[Tooltip("If >= 0, overrides MetalType.overheatDamagePerSecond.")]
	[SerializeField] float overheatDamagePerSecondOverride = -1f;
	[Tooltip("If >= 0, cool rate toward melt temp when removed while overheated. Otherwise uses MetalType.worldAmbientCoolRate.")]
	[SerializeField] float coolToMeltRateOverride = -1f;

	MetalHeatGauge metalHeatGauge;

	static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	static readonly int ColorId = Shader.PropertyToID("_Color");

	List<Vector2> shapeVertices = new List<Vector2>();

	List<Vector2> groundVertices = new List<Vector2>();
	List<float> grindAmounts = new List<float>();
	bool hasGrindProgress;
	SharpnessQuality sharpnessQuality = SharpnessQuality.Blunt;

	readonly List<Vector2> meltResult = new List<Vector2>();
	
	Pickable pickable;
	Renderer visualRenderer;
	MaterialPropertyBlock tintBlock;
	ShapeMatchEvaluator shapeMatchEvaluator;
	MetalType metalType = null;
	PartDefinition partDefinition = null;
	
	float overheatTimer;
	float grindMatchPercent;

	public MetalType MetalType => metalType;
	public PartDefinition PartDefinition => partDefinition;
	public Pickable Pickable => pickable;
	public float Temperature => temperature;
	public bool IsQuenchTemp => metalType != null && metalType.IsWorkable(Heat01);
	public bool IsMelting => metalType != null && metalType.IsMelting(Heat01);
	public float Heat01 => metalType != null ? metalType.NormalizeHeat01(temperature) : 0f;
	public IReadOnlyList<Vector2> ShapeVertices => shapeVertices;
	public bool HasGrindProgress => hasGrindProgress && groundVertices != null && groundVertices.Count >= PolygonGeometry.MinimumVertexCount;
	public IReadOnlyList<Vector2> GroundVertices => groundVertices;
	public IReadOnlyList<float> GrindAmounts => grindAmounts;
	public SharpnessQuality SharpnessQuality => sharpnessQuality;
	public float GrindMatchPercent => grindMatchPercent;

	public Action ShapeChanged;

	void Awake()
	{
		pickable = GetComponent<Pickable>();
		visualRenderer = GetComponent<Renderer>();
		metalHeatGauge = GetComponentInChildren<MetalHeatGauge>();
		shapeMatchEvaluator = FindFirstObjectByType<ShapeMatchEvaluator>();
		
		BuildStartingShape();
	}

	void BuildStartingShape()
	{
		shapeVertices.Clear();

		if (startingVertexCount <= 8)
		{
			Debug.LogError("Starting vertex count must be at least 8");
			return;
		}

		if (ovalRadii.x <= 0 || ovalRadii.y <= 0)
		{
			Debug.LogError("Oval radii must be greater than 0");
			return;
		}
		
		int count = startingVertexCount;

		float radiusX = ovalRadii.x;
		float radiusY = ovalRadii.y;

		for (int i = 0; i < count; i++)
		{
			float angle = i / (float)count * Mathf.PI * 2f;

			shapeVertices.Add(new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY));
		}
	}

	void Start()
	{
		ApplyQualityVisual();
		RefreshTint();
		if (metalHeatGauge != null)
			metalHeatGauge.SetFollowMetal(this);
	}

	void FixedUpdate()
	{
		var furnace = pickable != null ? pickable.ContainingStation as Furnace : null;
		bool isInActiveFurnace = furnace != null && furnace.isActiveAndEnabled;
		float? furnaceTemperature = isInActiveFurnace ? furnace.InternalTemperature : null;
		TickTemperature(Time.fixedDeltaTime, furnaceTemperature);
		TickMelting(Time.fixedDeltaTime);
	}

	void LateUpdate()
	{
		RefreshTint();
		
		if (metalHeatGauge != null)
			metalHeatGauge.SetEnable(temperature > ambientTemperature);
	}

	public void SetMetalType(MetalType type)
	{
		if (metalType != null)
		{
			Debug.LogError("Attempted to set metalType multiple times");
			return;
		}
		metalType = type;
	}

	public void SetPartDefinition(PartDefinition part)
	{
		if (partDefinition != null)
		{
			Debug.LogError("Attempted to set partDefinition multiple times");
			return;
		}
		
		partDefinition = part;
		ApplyQualityVisual();
	}

	public void SetTemperature(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
			return;
		temperature = value;
		if (!IsMelting)
			overheatTimer = 0f;
	}

	public void SetHeat01(float heat01)
	{
		heat01 = Mathf.Clamp01(heat01);
		if (metalType != null)
			temperature = heat01 * Mathf.Max(MinimumReferenceTemperature, metalType.referenceMaxTemp);
		else
			temperature = heat01;
	}

	public ShapeQuality GetForgedShapeQuality()
	{
		if (shapeMatchEvaluator == null || partDefinition == null || shapeVertices == null)
			return ShapeQuality.Incomplete;

		return shapeMatchEvaluator.EvaluateQuality(shapeVertices, partDefinition.outlineLocal);
	}

	public float GetMatchPercent()
	{
		if (shapeMatchEvaluator == null || partDefinition == null || shapeVertices == null)
			return 0f;

		return shapeMatchEvaluator.EvaluateMatchPercent(shapeVertices, partDefinition.outlineLocal);
	}

	public void Quench()
	{
		temperature = ambientTemperature;
		overheatTimer = 0f;
		if (visualRenderer == null)
			return;
		var quenchColor = metalType.GetQuenchColor();
		tintBlock ??= new MaterialPropertyBlock();
		visualRenderer.GetPropertyBlock(tintBlock);
		tintBlock.SetColor(BaseColorId, quenchColor);
		tintBlock.SetColor(ColorId, quenchColor);
		visualRenderer.SetPropertyBlock(tintBlock);
	}

	public bool TryCommitShapeVertices(IReadOnlyList<Vector2> _shapeVertices)
	{
		if (ReferenceEquals(_shapeVertices, shapeVertices))
			return true;
		
		if (!PolygonGeometry.IsSimple(_shapeVertices))
			return false;

		shapeVertices.Clear();
		
		for (int i = 0; i < _shapeVertices.Count; i++)
			shapeVertices.Add(_shapeVertices[i]);
		
		return true;
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

	void TickTemperature(float deltaTime, float? furnaceTemperature = null)
	{
		if (metalType == null || deltaTime <= 0f)
			return;

		if (furnaceTemperature != null)
		{
			float heatTransferRate = furnaceTemperature > temperature ? metalType.furnaceHeatTransferRate : metalType.worldAmbientCoolRate;
			temperature = Mathf.MoveTowards(temperature, furnaceTemperature.Value, heatTransferRate * deltaTime);
		}
		else
			TickOutsideFurnace(deltaTime);
	}

	void ApplyQualityVisual()
	{
		// TODO Add quality visuals
	}

	void TickOutsideFurnace(float deltaTime)
	{
		if (metalType == null || deltaTime <= 0f)
			return;
		
		if (IsMelting)
		{
			temperature = metalType.GetMeltingTempOverride(temperature, deltaTime);
			return;
		}

		temperature = Mathf.MoveTowards(temperature, ambientTemperature, metalType.worldAmbientCoolRate * deltaTime);
	}

	bool TickMelting(float _deltaTime)
	{
		if (metalType == null || !metalType.meltingEnabled || shapeVertices == null || shapeVertices.Count < PolygonGeometry.MinimumVertexCount)
			return false;

		float intensity = metalType.GetMeltIntensity(Heat01);

		if (!IsFinite(intensity) || intensity <= 0f)
			return false;

		MeltFeel feel = metalType.SampleMelt(intensity);
		float distance = feel.outwardSpeed * _deltaTime;

		if (!ClipperMeltGeometry.TryExpand(shapeVertices, distance, maxRadius, meltResult))
			return false;
		
		//? INVALIDATE OLD SCORES
		
		PolygonGeometry.CopyVertices(meltResult, shapeVertices);
		ShapeChanged?.Invoke();
		return true;
	}

	void RefreshTint()
	{
		bool hasTintTarget = visualRenderer != null && pickable != null;
		
		if (!hasTintTarget)
			return;
		
		bool isQuenched = pickable.Type == Pickable.PickableType.QuenchedMetal;
		if (isQuenched)
			return;

		Color tint = metalType.SampleColor(Heat01);

		tintBlock ??= new MaterialPropertyBlock();
		visualRenderer.GetPropertyBlock(tintBlock);
		tintBlock.SetColor(BaseColorId, tint);
		tintBlock.SetColor(ColorId, tint);
		visualRenderer.SetPropertyBlock(tintBlock);
	}

	static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	static bool ValidAmounts(IReadOnlyList<float> amounts, int count)
	{
		if (amounts == null || amounts.Count != count)
			return false;
		
		for (int i = 0; i < count; i++)
			if (!IsFinite(amounts[i]) || amounts[i] < 0f)
				return false;
		
		return true;
	}
}
