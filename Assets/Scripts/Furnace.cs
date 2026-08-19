using UnityEngine;

public class Furnace : MonoBehaviour
{
	[Header("References")]
	[SerializeField] Transform metalSocket;
	[SerializeField] Light fuelAreaPointLight;

	[Header("Fuel")]
	[SerializeField] float maxFuel = 100f;
	[SerializeField] float fuel = 0f;
	[SerializeField] float fuelBurnRate = 4f;
	[SerializeField] float minFuelToStayLit = 0.5f;
	[SerializeField] float minVelToAcceptFuel = 1f;

	[Header("Temperature")]
	[SerializeField] float ambientTemperature = 20f;
	[SerializeField] float internalTemperature = 20f;
	[SerializeField] float maxTemperature = 1000f;
	[SerializeField] float heatUpRate = 80f;
	[SerializeField] float coolRate = 35f;
	[Tooltip("Target temp as a fraction of maxTemperature from fuel fill (0-1 X -> 0-1 Y).")]
	[SerializeField] AnimationCurve targetTempByFuel = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

	[Header("Slot")]
	[SerializeField] Vector3 metalSocketLocalOffset = new Vector3(0f, 0.35f, 0f);
	
	[Header("Lighting")]
	[SerializeField] float fuelLightMinIntensity;
	[SerializeField] float fuelLightMaxIntensity;
	[SerializeField] Gradient fuelLightColorGradient;

	HeatableMetal containedMetal;
	Vector3 fuelGaugeFillBaseScale;
	bool isLit;

	public float Fuel => fuel;
	public float MaxFuel => maxFuel;
	public float FuelNormalized => maxFuel > 0.0001f ? Mathf.Clamp01(fuel / maxFuel) : 0f;
	public float TemperatureNormalized
	{
		get
		{
			float span = maxTemperature - ambientTemperature;
			if (span <= 0.0001f)
				return 0f;
			return Mathf.Clamp01((internalTemperature - ambientTemperature) / span);
		}
	}
	
	public float InternalTemperature => internalTemperature;
	public bool IsLit => isLit;
	public bool HasMetal => containedMetal != null;
	public HeatableMetal ContainedMetal => containedMetal;
	public bool HasFuel => fuel > 0f;


	void Awake()
	{
		EnsureSocket();

		UpdateVisuals();
	}

	void Update()
	{
		TickFuelAndAir(Time.deltaTime);
		TickTemperature(Time.deltaTime);
		TickContainedMetal(Time.deltaTime);
		UpdateVisuals();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsHeld || pickable.IsInFurnace)
			return false;

		if (pickable.TryGetComponent(out FuelItem fuelItem))
			return TryConsumeFuel(pickable, fuelItem);

		if (pickable.TryGetComponent(out HeatableMetal metal))
			return TryInsertMetal(pickable, metal);

		return false;
	}

	public void NotifyItemRemoved(Pickable pickable)
	{
		if (containedMetal != null && containedMetal.GetComponent<Pickable>() == pickable)
			containedMetal = null;
	}

	void TickFuelAndAir(float dt)
	{
		if (!HasFuel)
		{
			fuel = 0f;
			return;
		}

		float burn = fuelBurnRate * dt;
		fuel = Mathf.Max(0f, fuel - burn);
	}

	void TickTemperature(float dt)
	{
		float fuelFactor = targetTempByFuel.Evaluate(FuelNormalized);
		float baseTarget = ambientTemperature + (maxTemperature - ambientTemperature) * fuelFactor;

		float target;
		float rate;

		if (!HasFuel)
		{
			target = ambientTemperature;
			rate = coolRate;
		}
		else
		{
			target = baseTarget;

			if (internalTemperature <= target)
				rate = heatUpRate;
			else
				rate = coolRate;

			if (fuel <= minFuelToStayLit)
				rate = coolRate;
		}

		internalTemperature = Mathf.MoveTowards(internalTemperature, target, rate * dt);
	}

	void TickContainedMetal(float dt)
	{
		if (containedMetal == null)
			return;

		containedMetal.TickTowardFurnace(internalTemperature, dt);
	}

	bool TryConsumeFuel(Pickable pickable, FuelItem fuelItem)
	{
		if (fuel >= maxFuel - 0.0001f)
			return false;

		float room = maxFuel - fuel;
		float add = fuelItem.BurnValue;
		if (add > room)
		{
			if (!fuelItem.ConsumeEvenIfPartial)
				return false;
			add = room;
		}

		fuel = Mathf.Min(maxFuel, fuel + add);
		Destroy(pickable.gameObject);
		return true;
	}

	bool TryInsertMetal(Pickable pickable, HeatableMetal metal)
	{
		if (containedMetal != null)
			return false;

		EnsureSocket();
		pickable.PlaceInFurnace(this, metalSocket);
		containedMetal = metal;
		return true;
	}

	void EnsureSocket()
	{
		if (metalSocket != null)
			return;

		var socketGo = new GameObject("MetalSocket");
		metalSocket = socketGo.transform;
		metalSocket.SetParent(transform, false);
		metalSocket.localPosition = metalSocketLocalOffset;
	}

	void UpdateVisuals()
	{
		bool shouldLit = fuel > minFuelToStayLit || internalTemperature > ambientTemperature + 40f;
		fuelAreaPointLight.intensity = Mathf.Lerp(fuelLightMinIntensity, fuelLightMaxIntensity, TemperatureNormalized) + Mathf.Sin(Time.time * 0.5f) * 0.2f;

		fuelAreaPointLight.color = fuelLightColorGradient.Evaluate(TemperatureNormalized);
	}

	void OnTriggerEnter(Collider other) => TryAcceptFromCollider(other);

	void TryAcceptFromCollider(Collider other)
	{
		if (other == null)
			return;

		if (!other.TryGetComponent(out Pickable pickable))
			pickable = other.GetComponentInParent<Pickable>();

		if (pickable == null)
			return;

		if (!other.TryGetComponent(out Rigidbody otherRB))
			otherRB = other.GetComponentInParent<Rigidbody>();

		if (otherRB.linearVelocity.magnitude < minVelToAcceptFuel)
			return;
		
		TryAcceptPickable(pickable);
	}
}
