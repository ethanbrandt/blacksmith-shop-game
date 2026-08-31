using UnityEngine;

public class Grindstone : Station
{
	[Header("References")]
	[SerializeField] Transform metalSocket;
	[SerializeField] Collider catchTrigger;

	[Header("Slot")]
	[SerializeField] Vector3 metalSocketLocalOffset = new Vector3(0f, 0.78f, 0f);
	[SerializeField] Vector3 catchTriggerSize = new Vector3(1.4f, 0.85f, 2.2f);
	[SerializeField] Vector3 catchTriggerCenter = new Vector3(0f, 0.7f, -0.05f);
	[SerializeField] float minVelToAccept = 0f;
	[SerializeField] float placeRange = 2.4f;

	HeatableMetal containedMetal;
	Highlightable highlight;
	
	public Transform MetalSocket => metalSocket;
	public bool HasMetal => containedMetal != null;
	public HeatableMetal ContainedMetal => containedMetal;
	public float PlaceRange => placeRange;
	
	public override Highlightable Highlight { get { return highlight; } }
    public override Transform InteractionPoint { get { return metalSocket; } }

    public override bool CanAccept(Pickable _pickable)
    {
    	return !containedMetal && _pickable.Type == Pickable.PickableType.QuenchedMetal;
    }

    public override bool TryUse(Pickable _pickable)
    {
        return TryAcceptPickable(_pickable);
    }
	
	void Awake()
	{
		highlight = GetComponent<Highlightable>();
		
		EnsureSocket();
		EnsureCatchTrigger();
	}

	void Start()
	{
		GrindSessionController.EnsureExists();
	}

	public bool IsInPlaceRange(Vector3 worldPoint)
	{
		Vector3 socketPos = metalSocket != null ? metalSocket.position : transform.position;
		return (worldPoint - socketPos).sqrMagnitude <= placeRange * placeRange;
	}

	public bool TryPlaceHeld(Pickable pickable)
	{
		if (pickable == null || containedMetal != null || !pickable.IsHeld)
			return false;

		if (!pickable.TryGetComponent(out HeatableMetal metal))
			return false;

		if (!CanAccept(metal, pickable, out string failReason))
		{
			if (!string.IsNullOrEmpty(failReason) && LogText.Instance != null)
				LogText.Instance.SetText(failReason);
			return false;
		}

		EnsureSocket();
		if (!pickable.TryPlaceOnGrindstone(this, metalSocket))
			return false;

		containedMetal = metal;
		OpenGrind(metal);
		return true;
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsInFurnace || pickable.IsOnAnvil || pickable.IsOnGrindstone)
			return false;

		if (containedMetal != null)
			return false;

		if (!pickable.TryGetComponent(out HeatableMetal metal))
			return false;

		if (!CanAccept(metal, pickable, out string failReason))
		{
			if (!string.IsNullOrEmpty(failReason) && LogText.Instance != null)
				LogText.Instance.SetText(failReason);
			return false;
		}

		EnsureSocket();
		if (!pickable.TryPlaceOnGrindstone(this, metalSocket))
			return false;

		containedMetal = metal;
		OpenGrind(metal);
		return true;
	}

	bool CanAccept(HeatableMetal metal, Pickable pickable, out string failReason)
	{
		failReason = null;
		if (pickable.Type != Pickable.PickableType.QuenchedMetal)
		{
			failReason = "MUST BE QUENCHED TO GRIND";
			return false;
		}

		if (metal.PartDefinition == null || !metal.PartDefinition.isBladed)
		{
			failReason = "ONLY BLADED PARTS CAN BE GROUND";
			return false;
		}

		return true;
	}

	public void NotifyItemRemoved(Pickable pickable)
	{
		if (containedMetal == null || containedMetal.GetComponent<Pickable>() != pickable)
			return;

		containedMetal = null;
		if (GrindSessionController.Instance != null)
			GrindSessionController.Instance.NotifyStationEmptied(this);
	}

	public static Grindstone FindClosestInRange(Vector3 origin, float range)
	{
		Grindstone closest = null;
		float best = range * range;
		var stones = FindObjectsByType<Grindstone>(FindObjectsSortMode.None);
		for (int i = 0; i < stones.Length; i++)
		{
			Grindstone stone = stones[i];
			if (stone == null || stone.containedMetal != null)
				continue;

			Vector3 socketPos = stone.metalSocket != null ? stone.metalSocket.position : stone.transform.position;
			float distSq = (socketPos - origin).sqrMagnitude;
			if (distSq > best)
				continue;

			best = distSq;
			closest = stone;
		}

		return closest;
	}

	void OpenGrind(HeatableMetal metal)
	{
		GrindSessionController.EnsureExists().BeginSession(this, metal);
	}

	void OnTriggerEnter(Collider other) => TryAcceptFromCollider(other);

	void OnCollisionEnter(Collision collision)
	{
		if (collision == null)
			return;

		TryAcceptFromCollider(collision.collider);
	}

	void TryAcceptFromCollider(Collider other)
	{
		if (other == null)
			return;

		if (!other.TryGetComponent(out Pickable pickable))
			pickable = other.GetComponentInParent<Pickable>();

		if (pickable == null)
			return;

		if (!other.TryGetComponent(out Rigidbody otherRb))
			otherRb = other.GetComponentInParent<Rigidbody>();

		if (otherRb != null && otherRb.linearVelocity.magnitude < minVelToAccept)
			return;

		TryAcceptPickable(pickable);
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

	void EnsureCatchTrigger()
	{
		if (catchTrigger != null)
			return;

		var box = gameObject.AddComponent<BoxCollider>();
		box.isTrigger = true;
		box.size = catchTriggerSize;
		box.center = catchTriggerCenter;
		catchTrigger = box;
	}
}
