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

	HeatableMetal containedMetal;
	Highlightable highlight;

	public Transform MetalSocket => metalSocket;
	public bool HasMetal => containedMetal != null;
	public HeatableMetal ContainedMetal => containedMetal;

	public override Highlightable Highlight
	{
		get
		{
			return highlight;
		}
	}

	public override bool CanAccept(Pickable _pickable)
	{
		bool hasEmptySocket = containedMetal == null;
		bool hasAcceptedItemType = _pickable != null && _pickable.Type == Pickable.PickableType.QuenchedMetal;
		if (!hasEmptySocket || !hasAcceptedItemType)
			return false;
		var session = GrindSessionController.Instance;
		bool isSessionReady = session != null && session.CanBegin(_pickable.GetComponent<HeatableMetal>());
		return isSessionReady;
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

	public override float DistanceFromStationSquared(Vector3 _worldPoint)
	{
		Vector3 socketPos = metalSocket != null ? metalSocket.position : transform.position;
		return (_worldPoint - socketPos).sqrMagnitude;
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
		if (!pickable.TryPlaceInStation(this, metalSocket))
			return false;

		containedMetal = metal;
		OpenGrind(metal);
		return true;
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.InStation)
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
		if (!pickable.TryPlaceInStation(this, metalSocket))
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

	public override void NotifyItemRemoved(Pickable _pickable)
	{
		if (containedMetal == null || containedMetal.GetComponent<Pickable>() != _pickable)
			return;

		containedMetal = null;
		if (GrindSessionController.Instance != null)
			GrindSessionController.Instance.NotifyStationEmptied(this);
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
