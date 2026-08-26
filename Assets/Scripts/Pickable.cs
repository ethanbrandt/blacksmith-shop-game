using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Pickable : MonoBehaviour
{
	[SerializeField] bool canBePickedUp = true;
	[SerializeField] Collider itemCollider;

	Rigidbody rb;
	Renderer visualRenderer;
	Transform defaultParent;
	Furnace containingFurnace;
	Anvil containingAnvil;
	QuenchVat containingVat;
	Transform followTarget;
	Vector3 followLocalOffset;
	Collider[] ignoredHolderColliders;
	bool isHeld;
	bool isQuenched;
	bool isOnFinalTable;

	public bool CanBePickedUp => canBePickedUp && !isHeld && !(IsOnAnvil && ForgeSessionController.IsBlockingPlayer) && !isOnFinalTable;
	public bool IsHeld => isHeld;
	public bool IsQuenched => isQuenched;
	public bool IsInFurnace => containingFurnace != null;
	public bool IsOnAnvil => containingAnvil != null;
	

	void Awake()
	{
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

		transform.SetPositionAndRotation(
			followTarget.TransformPoint(followLocalOffset),
			followTarget.rotation);
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

	public bool TryPlaceInFurnace(Furnace furnace, Transform socket)
	{
		if (furnace == null || socket == null || isHeld || isQuenched)
			return false;

		containingFurnace = furnace;
		isHeld = false;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(socket, true);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		return true;
	}

	public bool TryPlaceOnAnvil(Anvil anvil, Transform socket)
	{
		if (anvil == null || socket == null || isQuenched)
			return false;
		
		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		containingAnvil = anvil;
		isHeld = false;
		followTarget = null;
		SetPhysicsActive(false);

		transform.SetParent(socket, true);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		return true;
	}

	public bool TryPlaceInQuenchVat(QuenchVat vat, Transform socket)
	{
		if (socket == null || vat == null || isQuenched)
			return false;
		
		CancelInvoke(nameof(ClearHolderCollisionIgnore));
		SetHolderCollisionIgnored(false);
		ignoredHolderColliders = null;
		containingVat = vat;
		isHeld = false;
		isQuenched = true;
		followTarget = null;
		SetPhysicsActive(false);
		
		transform.SetParent(socket, true);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		return true;
	}

	public bool TryPlaceOnFinalTable(Transform socket)
	{
		if (socket == null || !isQuenched)
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
		ClearFurnaceContainment();
		ClearAnvilContainment();
		ClearQuenchVatContainment();
	}

	void ClearFurnaceContainment()
	{
		if (containingFurnace == null)
			return;

		containingFurnace.NotifyItemRemoved(this);
		containingFurnace = null;
	}

	void ClearAnvilContainment()
	{
		if (containingAnvil == null)
			return;

		containingAnvil.NotifyItemRemoved(this);
		containingAnvil = null;
	}

	void ClearQuenchVatContainment()
	{
		if (containingVat == null)
			return;
		
		containingVat.NotifyItemRemoved(this);
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
