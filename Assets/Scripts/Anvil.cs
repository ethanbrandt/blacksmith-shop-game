using UnityEngine;

public class Anvil : MonoBehaviour
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

	public Transform MetalSocket => metalSocket;
	public bool HasMetal => containedMetal != null;
	public HeatableMetal ContainedMetal => containedMetal;
	public float PlaceRange => placeRange;

	void Awake()
	{
		EnsureSocket();
		EnsureCatchTrigger();
	}

	void Start()
	{
		ForgeSessionController.EnsureExists();
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

		EnsureSocket();
		pickable.PlaceOnAnvil(this, metalSocket);
		containedMetal = metal;
		OpenForge(metal);
		return true;
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsHeld || pickable.IsInFurnace || pickable.IsOnAnvil)
			return false;

		if (containedMetal != null)
			return false;

		if (!pickable.TryGetComponent(out HeatableMetal metal))
			return false;

		EnsureSocket();
		pickable.PlaceOnAnvil(this, metalSocket);
		containedMetal = metal;
		OpenForge(metal);
		return true;
	}

	public void NotifyItemRemoved(Pickable pickable)
	{
		if (containedMetal == null || containedMetal.GetComponent<Pickable>() != pickable)
			return;

		containedMetal = null;
		if (ForgeSessionController.Instance != null)
			ForgeSessionController.Instance.NotifyAnvilEmptied(this);
	}

	public static Anvil FindClosestInRange(Vector3 origin, float range)
	{
		Anvil closest = null;
		float best = range * range;
		var anvils = FindObjectsByType<Anvil>(FindObjectsSortMode.None);
		for (int i = 0; i < anvils.Length; i++)
		{
			Anvil anvil = anvils[i];
			if (anvil == null || anvil.containedMetal != null)
				continue;

			Vector3 socketPos = anvil.metalSocket != null ? anvil.metalSocket.position : anvil.transform.position;
			float distSq = (socketPos - origin).sqrMagnitude;
			if (distSq > best)
				continue;

			best = distSq;
			closest = anvil;
		}

		return closest;
	}

	void OpenForge(HeatableMetal metal)
	{
		ForgeSessionController.EnsureExists().BeginSession(this, metal);
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
