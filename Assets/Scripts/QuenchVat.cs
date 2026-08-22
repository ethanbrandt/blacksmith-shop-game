using UnityEngine;

public class QuenchVat : MonoBehaviour
{
	[Header("References")]
	[SerializeField] Transform metalSocket;

	[Header("Slot")]
	[SerializeField] Vector3 metalSocketLocalOffset = new Vector3(0f, 0.35f, 0f);

	HeatableMetal containedMetal;

	void Awake()
	{
		EnsureSocket();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsHeld || pickable.IsInFurnace || pickable.IsOnAnvil || pickable.IsQuenched)
			return false;

		if (pickable.TryGetComponent(out HeatableMetal metal))
			return TryInsertPart(pickable, metal);

		return false;
	}

	bool TryInsertPart(Pickable pickable, HeatableMetal metal)
	{
		if (containedMetal != null)
			return false;

		EnsureSocket();
		if (!pickable.TryPlaceInQuenchVat(metalSocket))
			return false;
		
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

		TryAcceptPickable(pickable);
	}
}
