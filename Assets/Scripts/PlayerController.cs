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

	[Header("Slime Sliding")]
	[SerializeField, Min(0.1f)] float slimeSlideDuration = 3f;
	[SerializeField, Min(0f)] float slimeAcceleration = 4f;
	[SerializeField, Min(0f)] float slimeDrag = 0.2f;
	[SerializeField, Min(0.01f)] float slimeRecoveryDuration = 1f;
	[SerializeField] PhysicsMaterial slimedPhysicsMat;

	float slimeSlideRemaining;
	Collider slideCollider;
	PhysicsMaterial originalPhysicsMaterial;

	Rigidbody rb;
	Vector3 moveDir;
	float moveInputStrength;
	Pickable held;
	Highlightable currentHighlight;
	private bool endOfRound = false;
	
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
		slimeSlideRemaining = Mathf.Max(0f, slimeSlideRemaining - Time.fixedDeltaTime);
		if (slimeSlideRemaining <= 0f)
			RestoreSlideFriction();

		if (IsMinigameBlocking || endOfRound)
		{
			moveDir = Vector3.zero;
			rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
			return;
		}

		Vector3 targetVelocity = moveDir * moveSpeed;
		
		if (slimeSlideRemaining > 0f)
		{
			Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
			Vector3 slideVelocity = horizontalVelocity * Mathf.Exp(-slimeDrag * Time.fixedDeltaTime);
			Vector3 acceleration = moveDir * (moveInputStrength * slimeAcceleration);
			
			if (slideVelocity.sqrMagnitude >= moveSpeed * moveSpeed && slideVelocity.sqrMagnitude > 0.0001f)
			{
				Vector3 heading = slideVelocity.normalized;
				acceleration -= heading * Mathf.Max(0f, Vector3.Dot(acceleration, heading));
			}
			
			slideVelocity += acceleration * Time.fixedDeltaTime;
			slideVelocity = Vector3.ClampMagnitude(slideVelocity, Mathf.Max(moveSpeed, horizontalVelocity.magnitude));
			float recovery = 1f - Mathf.Clamp01(slimeSlideRemaining / Mathf.Max(0.01f, slimeRecoveryDuration));
			targetVelocity = Vector3.Lerp(slideVelocity, targetVelocity, recovery);
		}

		rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
	}

	public void ApplySlimeSlide()
	{
		if (!isActiveAndEnabled || IsMinigameBlocking || endOfRound)
			return;

		if (slimeSlideRemaining <= 0f)
		{
			slideCollider = GetComponent<Collider>();
			if (slideCollider != null)
			{
				originalPhysicsMaterial = slideCollider.sharedMaterial;
				slideCollider.sharedMaterial = slimedPhysicsMat;
			}
		}
		slimeSlideRemaining = slimeSlideDuration;
	}

	void RestoreSlideFriction()
	{
		if (slideCollider != null)
		{
			slideCollider.sharedMaterial = originalPhysicsMaterial;
			slideCollider = null;
		}
	}

	void OnDisable()
	{
		slimeSlideRemaining = 0f;
		moveDir = Vector3.zero;
		moveInputStrength = 0f;
		RestoreSlideFriction();
	}

	public void NotifyEnding()
	{
		endOfRound = true;
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
		if (IsMinigameBlocking || endOfRound)
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
		moveInputStrength = Mathf.Clamp01(inputDir.magnitude);
		if (inputDir.sqrMagnitude <= 0.0001f)
		{
			moveDir = Vector3.zero;
			return;
		}
		moveDir = (camForward * inputDir.y + camRight * inputDir.x).normalized;
	}

	void OnInteract()
	{
		if (IsMinigameBlocking || endOfRound)
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
		if (IsMinigameBlocking || endOfRound)
			return;

		if (held == null)
			return;

		Vector3 facing = GetFacing();
		Vector3 throwPos = transform.position + facing * dropForward + Vector3.up * 0.55f;
		held.Throw(throwPos, rb.linearVelocity + facing * throwSpeed + Vector3.up * throwUpSpeed);

		held = null;
		OnPickUp?.Invoke(null);
	}

	public void ForceDrop(Vector3 _worldPos, Vector3 _velocity)
	{
		held.Drop(_worldPos, _velocity);
		
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
