using UnityEngine;
using UnityEngine.Serialization;

public class FinishTableSocket : MonoBehaviour
{
	[FormerlySerializedAs("metalSocket")]
	[Header("References")]
	[SerializeField] Transform partSocket;

	[Header("Slot")]
	[SerializeField] Vector3 partSocketLocalOffset = new Vector3(0f, 0.35f, 0f);

	HeatableMetal containedMetal;

	void Awake()
	{
		EnsureSocket();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsHeld || pickable.IsInFurnace || pickable.IsOnAnvil || !pickable.IsQuenched)
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
		if (!pickable.TryPlaceOnFinalTable(partSocket))
			return false;
		
		containedMetal = metal;
		return true;
	}

	void EnsureSocket()
	{
		if (partSocket != null)
			return;

		var socketGo = new GameObject("MetalSocket");
		partSocket = socketGo.transform;
		partSocket.SetParent(transform, false);
		partSocket.localPosition = partSocketLocalOffset;
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

		TryAcceptPickable(pickable);
	}
}
