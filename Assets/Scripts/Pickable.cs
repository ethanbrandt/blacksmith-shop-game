using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Pickable : MonoBehaviour
{
	public enum PickableType
	{
		HeatableMetal,
		QuenchedMetal,
		Fuel
	}
	
	[SerializeField] bool canBePickedUp = true;
	[SerializeField] Collider itemCollider;

	Rigidbody rb;
	Renderer visualRenderer;
	Transform defaultParent;
	Station containingStation;
	Transform followTarget;
	Vector3 followLocalOffset;
	Collider[] ignoredHolderColliders;
	bool isHeld;
	bool isOnFinalTable;
	PickableType pickableType;

	public bool CanBePickedUp => canBePickedUp && !isHeld && !(InStation && ForgeSessionController.IsBlockingPlayer && GrindSessionController.IsBlockingPlayer) && !isOnFinalTable;
	public bool IsHeld => isHeld;
	public bool InStation => containingStation != null;
	public PickableType Type => pickableType;

	void Awake()
	{
		if (TryGetComponent(out HeatableMetal heatableMetal))
			pickableType = PickableType.HeatableMetal;
		else if (TryGetComponent(out FuelItem fuelItem))
			pickableType = PickableType.Fuel;
		
		rb = GetComponent<Rigidbody>();
		if (itemCollider == null)
			itemCollider = GetComponent<Collider>();
		if (itemCollider == null)
			itemCollider = gameObject.AddComponent<SphereCollider>();

		defaultParent = transform.parent;
		rb.interpolation = RigidbodyInterpolation.Interpolate;
	}

	void LateUpdate()
	{
		if (followTarget == null)
			return;

		transform.SetPositionAndRotation(followTarget.TransformPoint(followLocalOffset), followTarget.rotation);
	}

	public void PickUp(Transform holder, Vector3 localHoldOffset)
	{
		if (!CanBePickedUp)
			return;

		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		ClearStationContainment();
		isHeld = true;
		SetPhysicsActive(false);
		IgnoreHolderCollisions(holder, true);

		transform.SetParent(defaultParent, true);
		followTarget = holder;
		followLocalOffset = localHoldOffset;
	}

	public bool TryPlaceInStation(Station _station, Transform _socket)
	{
		if (_station == null || _socket == null)
			return false;

		if (!_station.CanAccept(this))
			return false;
		
		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		containingStation = _station;
		isHeld = false;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(_socket, true);
		transform.localPosition = Vector3.zero;
		transform.rotation = Quaternion.identity;

		if (_station is QuenchVat)
			pickableType = PickableType.QuenchedMetal;
		
		return true;
	}
	
	public bool TryPlaceOnFinalTable(Transform socket)
	{
		if (socket == null || pickableType != PickableType.QuenchedMetal)
			return false;

		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		isHeld = false;
		isOnFinalTable = true;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(socket, true);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		return true;
	}
	
	public void Drop(Vector3 worldPosition, Vector3 velocity)
	{
		if (!isHeld)
			return;

		Release(worldPosition, velocity);
	}

	public void Throw(Vector3 worldPosition, Vector3 velocity)
	{
		if (!isHeld)
			return;

		Release(worldPosition, velocity);
	}

	void Release(Vector3 worldPosition, Vector3 velocity)
	{
		ClearStationContainment();
		isHeld = false;
		followTarget = null;
		transform.SetParent(defaultParent, true);
		transform.position = worldPosition;
		SetPhysicsActive(true);
		rb.linearVelocity = velocity;
		rb.angularVelocity = Vector3.zero;
		Invoke(nameof(ClearHolderCollisionIgnore), 0.4f);
	}

	void ClearHolderCollisionIgnore()
	{
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
	}

	void IgnoreHolderCollisions(Transform holder, bool ignore)
	{
		ignoredHolderColliders = holder != null ? holder.GetComponentsInChildren<Collider>() : null;
		SetHolderCollisionIgnored(ignore);
	}

	void SetHolderCollisionIgnored(bool ignore)
	{
		if (itemCollider == null || ignoredHolderColliders == null)
			return;

		for (int i = 0; i < ignoredHolderColliders.Length; i++)
		{
			Collider other = ignoredHolderColliders[i];
			if (other == null || other == itemCollider)
				continue;
			Physics.IgnoreCollision(itemCollider, other, ignore);
		}
	}

	void ClearStationContainment()
	{
		if (containingStation == null)
			return;

		containingStation.NotifyItemRemoved(this);
		containingStation = null;
	}

	void SetPhysicsActive(bool active)
	{
		rb.isKinematic = !active;
		rb.detectCollisions = active;
		rb.interpolation = active ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;

		if (itemCollider != null)
			itemCollider.enabled = active;

		if (active)
		{
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
		}
	}
}
