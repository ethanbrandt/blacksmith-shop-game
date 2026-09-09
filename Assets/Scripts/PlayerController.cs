using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
	[SerializeField] float moveSpeed;
	[SerializeField] float pickupRange = 2.2f;
	[SerializeField] float placeIntoStationRange = 3f;
	[SerializeField] Vector3 holdLocalOffset = new Vector3(0.45f, 0.15f, 0.7f);
	[SerializeField] float throwSpeed = 8f;
	[SerializeField] float throwUpSpeed = 2.5f;
	[SerializeField] float dropForward = 0.9f;

	Rigidbody rb;
	Vector3 moveDir;
	Pickable held;
	Highlightable currentHighlight;
	static bool IsMinigameBlocking => StationSessionCoordinator.IsActive;
	public Pickable Held => held;

	public Action<Transform> OnPickUp;
	public Action<Transform> OnHighlight;

	void Start()
	{
		rb = GetComponent<Rigidbody>();
	}

	void FixedUpdate()
	{
		if (IsMinigameBlocking)
		{
			moveDir = Vector3.zero;
			rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
			return;
		}

		rb.linearVelocity = new Vector3(moveDir.x * moveSpeed, rb.linearVelocity.y, moveDir.z * moveSpeed);
	}

	void Update()
	{
		if (held)
			StationHighlight(held);
		else
			PickableHighlight();
	}

	private void StationHighlight(Pickable _pickable)
	{
		Station closestStation = FindClosestStation();
		if (!closestStation)
		{
			RemoveCurrentHighlight();
			return;
		}

		if (!closestStation.CanAccept(_pickable))
		{
			RemoveCurrentHighlight();
			return;
		}

		Highlightable nextHighlight = closestStation.Highlight;
		if (!nextHighlight)
			return;

		if (currentHighlight != nextHighlight)
		{
			RemoveCurrentHighlight();
			AddHighlight(nextHighlight);
		}
	}

	private void PickableHighlight()
	{
		Pickable closestPickup = FindClosestPickable();
		if (!closestPickup)
		{
			RemoveCurrentHighlight();
			return;
		}

		if (!closestPickup.TryGetComponent(out Highlightable nextHighlight))
			return;

		if (currentHighlight != nextHighlight)
		{
			RemoveCurrentHighlight();
			AddHighlight(nextHighlight);
		}
	}

	void RemoveCurrentHighlight()
	{
		if (!currentHighlight)
			return;

		currentHighlight.SetHighlighted(false);
		currentHighlight = null;

		OnHighlight?.Invoke(null);
	}

	void AddHighlight(Highlightable _highlightable)
	{
		_highlightable.SetHighlighted(true);
		currentHighlight = _highlightable;
		OnHighlight?.Invoke(_highlightable.transform);
	}

	void OnMove(InputValue _value)
	{
		if (IsMinigameBlocking)
		{
			moveDir = Vector3.zero;
			return;
		}

		Vector3 camForward = Camera.main.transform.forward;
		camForward.y = 0f;
		camForward.Normalize();
		Vector3 camRight = Camera.main.transform.right;
		camRight.y = 0f;
		camRight.Normalize();
		Vector2 inputDir = _value.Get<Vector2>();
		moveDir = (camForward * inputDir.y + camRight * inputDir.x).normalized;
	}

	void OnInteract()
	{
		if (IsMinigameBlocking)
			return;

		if (held != null)
		{
			Station station = FindClosestStation();

			OnPickUp?.Invoke(null);

			if (station != null && station.TryUse(held))
			{
				held = null;
				return;
			}

			Vector3 facing = GetFacing();
			Vector3 dropPos = transform.position + facing * dropForward + Vector3.up * 0.35f;
			held.Drop(dropPos, rb.linearVelocity + facing * 1.5f);
			held = null;
			return;
		}

		Pickable nearest = FindClosestPickable();
		if (nearest != null)
		{
			nearest.PickUp(transform, holdLocalOffset);

			if (nearest.IsHeld)
			{
				held = nearest;
				OnPickUp?.Invoke(nearest.transform);
			}

			if (nearest.TryGetComponent(out Highlightable highlightable) && highlightable == currentHighlight)
				RemoveCurrentHighlight();
		}
	}

	void OnAttack()
	{
		if (IsMinigameBlocking)
			return;

		if (held == null)
			return;

		Vector3 facing = GetFacing();
		Vector3 throwPos = transform.position + facing * dropForward + Vector3.up * 0.55f;
		held.Throw(throwPos, rb.linearVelocity + facing * throwSpeed + Vector3.up * throwUpSpeed);

		held = null;
		OnPickUp?.Invoke(null);
	}

	Vector3 GetFacing()
	{
		if (moveDir.sqrMagnitude > 0.01f)
			return moveDir;

		Vector3 camForward = Camera.main.transform.forward;
		camForward.y = 0f;
		if (camForward.sqrMagnitude > 0.01f)
			return camForward.normalized;

		return Vector3.forward;
	}

	Pickable FindClosestPickable()
	{
		Pickable closest = null;
		float best = pickupRange * pickupRange;
		var pickables = FindObjectsByType<Pickable>(FindObjectsSortMode.None);
		Vector3 origin = transform.position;
		for (int i = 0; i < pickables.Length; i++)
		{
			Pickable pickable = pickables[i];
			if (pickable == null || !pickable.CanBePickedUp)
				continue;

			float distSq = (pickable.transform.position - origin).sqrMagnitude;
			if (distSq > best)
				continue;

			best = distSq;
			closest = pickable;
		}

		return closest;
	}

	Station FindClosestStation()
	{
		Station closest = null;
		float best = placeIntoStationRange * placeIntoStationRange;
		var stations = FindObjectsByType<Station>(FindObjectsSortMode.None);
		Vector3 origin = transform.position;
		for (int i = 0; i < stations.Length; i++)
		{
			Station station = stations[i];
			if (station == null)
				continue;

			float distSq = station.DistanceFromStationSquared(origin);
			if (distSq > best)
				continue;

			best = distSq;
			closest = station;
		}

		return closest;
	}
}
