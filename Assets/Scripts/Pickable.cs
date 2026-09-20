using System;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class Pickable : MonoBehaviour
{
	public enum PickableType
	{
		HEATABLE_METAL,
		QUENCHED_METAL,
		FUEL
	}

	public enum PickableState
	{
		FREE,
		HELD,
		IN_STATION,
		FINISHED
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
	PickableState state;
	PickableType pickableType;
	public bool CanBePickedUp
	{
		get
		{
			bool isLockedInStation = state == PickableState.IN_STATION && StationSessionCoordinator.IsActive;
			bool isUnavailable = state == PickableState.HELD || state == PickableState.FINISHED;
			bool isPickupBlocked = isLockedInStation || isUnavailable;
			return canBePickedUp && !isPickupBlocked;
		}
	}

	public Station ContainingStation => containingStation;
	public bool IsHeld => state == PickableState.HELD;
	public bool InStation => containingStation != null;
	public bool IsOnFinalTable => state == PickableState.FINISHED;
	public PickableType Type => pickableType;
	public PickableState State => state;

	public Action<PickableState> OnStateChanged;

	void Awake()
	{
		if (TryGetComponent(out HeatableMetal heatableMetal))
			pickableType = PickableType.HEATABLE_METAL;
		else if (TryGetComponent(out FuelItem fuelItem))
			pickableType = PickableType.FUEL;

		rb = GetComponent<Rigidbody>();
		if (itemCollider == null)
			itemCollider = GetComponent<Collider>();
		if (itemCollider == null)
			itemCollider = gameObject.AddComponent<BoxCollider>();

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
		
		state = PickableState.HELD;
		OnStateChanged?.Invoke(PickableState.HELD);

		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		ClearStationContainment();
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

		state = PickableState.IN_STATION;
		OnStateChanged?.Invoke(PickableState.IN_STATION);

		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		containingStation = _station;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(_socket, true);
		transform.localPosition = Vector3.zero;
		transform.rotation = Quaternion.identity;

		if (_station is QuenchVat)
			pickableType = PickableType.QUENCHED_METAL;

		return true;
	}

	public bool TryPlaceOnFinalTable(Transform socket)
	{
		if (socket == null || pickableType != PickableType.QUENCHED_METAL)
			return false;

		state = PickableState.FINISHED;
		OnStateChanged?.Invoke(PickableState.FINISHED);
		
		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(socket, true);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		return true;
	}

	public void Drop(Vector3 worldPosition, Vector3 velocity)
	{
		if (state != PickableState.HELD)
			return;

		Release(worldPosition, velocity);
	}

	public void Throw(Vector3 worldPosition, Vector3 velocity)
	{
		if (state != PickableState.HELD)
			return;

		Release(worldPosition, velocity);
	}

	public void ForceReleaseFromStation(Vector3 worldPosition, Vector3 velocity)
	{
		if (state != PickableState.IN_STATION)
			return;
		
		Release(worldPosition, velocity);
	}

	void Release(Vector3 worldPosition, Vector3 velocity)
	{
		state = PickableState.FREE;
		OnStateChanged?.Invoke(PickableState.FREE);
		
		ClearStationContainment();
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
