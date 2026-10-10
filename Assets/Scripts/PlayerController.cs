using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

public class PlayerController : MonoBehaviour
{
	[SerializeField] float moveSpeed;
	[SerializeField] float pickupRange = 2.2f;
	[SerializeField] float placeIntoStationRange = 3f;
	[SerializeField] Vector3 holdLocalOffset = new Vector3(0.45f, 0.15f, 0.7f);
	[SerializeField] float throwSpeed = 8f;
	[SerializeField] float throwUpSpeed = 2.5f;
	[SerializeField] float dropForward = 0.9f;
	[SerializeField] float fuelTossCoolDown = 0.4f;
	[SerializeField] Transform actor;

	[Header("Animation")]
	[SerializeField] Animator animator;
	[SerializeField, Min(0.1f)] float idleStimDelay = 8f;
	[SerializeField, Min(0.01f)] float holdingBlendDuration = 0.15f;
	[SerializeField, Min(0f)] float animationMovementThreshold = 0.1f;

	static readonly int MovingParameter = Animator.StringToHash("Moving");
	static readonly int HoldingParameter = Animator.StringToHash("Holding");
	static readonly int IdleStimParameter = Animator.StringToHash("IdleStim");
	static readonly int ThrowStatePath = Animator.StringToHash("UpperBodyLayer.Throw");
	static readonly int ThrowState = Animator.StringToHash("Throw");
	static readonly int TossFuelStatePath = Animator.StringToHash("UpperBodyLayer.Fuel_Toss");
	static readonly int TossFuelState = Animator.StringToHash("Fuel_Toss");
	static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
	float idleTime;
	int upperBodyLayer = -1;
	float canFuelTime;

	bool IsThrowAnimationPlaying => animator && upperBodyLayer >= 0 && (animator.GetCurrentAnimatorStateInfo(upperBodyLayer).shortNameHash == ThrowState || (animator.IsInTransition(upperBodyLayer) && animator.GetNextAnimatorStateInfo(upperBodyLayer).shortNameHash == ThrowState));

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
	Component currentTarget;
	Highlightable currentHighlight;
	private bool endOfRound = false;

	static bool IsMinigameBlocking => StationSessionCoordinator.IsActive || GameManager.IsTransitioning;
	public Pickable Held => held;
	public Component InteractionTarget => currentTarget;
	public float SlimedPercentLeft => slimeSlideRemaining / slimeSlideDuration;
	public bool CanShowInteractionPrompts => isActiveAndEnabled && !IsMinigameBlocking && !endOfRound;

	public Furnace GetNearbyFuelFurnace()
	{
		return CanShowInteractionPrompts ? FindClosestStation() as Furnace : null;
	}

	public Action<Transform> OnPickUp;
	public Action<Transform> OnHighlight;

	void Start()
	{
		rb = GetComponent<Rigidbody>();
		if (!animator && actor)
			animator = actor.GetComponentInChildren<Animator>();
		if (animator)
		{
			animator.applyRootMotion = false;
			upperBodyLayer = animator.GetLayerIndex("UpperBody");
			if (upperBodyLayer >= 0)
				animator.SetLayerWeight(upperBodyLayer, held ? 1f : 0f);
		}
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
			targetVelocity = Vector3.Lerp(slideVelocity, targetVelocity, Mathf.Max(0f, recovery - 0.25f));
		}

		rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
		Vector3 facing = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
		if (facing.sqrMagnitude > 0.0001f)
			actor.rotation = Quaternion.RotateTowards(actor.rotation, Quaternion.LookRotation(facing, Vector3.up), 720f * Time.fixedDeltaTime);
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
		idleTime = 0f;
		if (animator)
			animator.ResetTrigger(IdleStimParameter);
		RestoreSlideFriction();
		RemoveCurrentHighlight();
	}

	public void NotifyEnding()
	{
		endOfRound = true;
		RemoveCurrentHighlight();

		if (!animator)
			return;

		animator.ResetTrigger(IdleStimParameter);
		
		if (upperBodyLayer >= 0)
			animator.SetLayerWeight(upperBodyLayer, 0f);
		
		animator.CrossFadeInFixedTime("Base Layer.Present", 0.25f, 0, 0f);
		actor.rotation = Quaternion.Euler(0, 145f, 0);
	}

	void Update()
	{
		UpdateAnimation();
		UpdateInteractionTarget();
	}

	void UpdateAnimation()
	{
		if (!animator || !rb || endOfRound)
			return;

		bool blocked = IsMinigameBlocking || endOfRound;
		Vector3 velocity = rb.linearVelocity;
		bool moving = !blocked && velocity.x * velocity.x + velocity.z * velocity.z > animationMovementThreshold * animationMovementThreshold;
		bool holding = held != null;
		animator.SetBool(MovingParameter, moving);
		animator.SetBool(HoldingParameter, holding);
		if (upperBodyLayer >= 0)
		{
			bool transitioning = animator.IsInTransition(upperBodyLayer);
			bool currentThrow = animator.GetCurrentAnimatorStateInfo(upperBodyLayer).shortNameHash == ThrowState;
			bool nextThrow = transitioning && animator.GetNextAnimatorStateInfo(upperBodyLayer).shortNameHash == ThrowState;
			bool showThrow = nextThrow || (currentThrow && !transitioning);
			bool nextFuelToss = transitioning && animator.GetNextAnimatorStateInfo(upperBodyLayer).shortNameHash == TossFuelState;
			bool showFuelToss = nextFuelToss || (!transitioning && animator.GetCurrentAnimatorStateInfo(upperBodyLayer).shortNameHash == TossFuelState);
			float weight = Mathf.MoveTowards(animator.GetLayerWeight(upperBodyLayer), holding || showThrow || showFuelToss ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, holdingBlendDuration));
			animator.SetLayerWeight(upperBodyLayer, weight);
		}

		if (moving || holding || IsThrowAnimationPlaying || blocked || animator.IsInTransition(0) || animator.GetCurrentAnimatorStateInfo(0).fullPathHash != IdleState)
		{
			idleTime = 0f;
			animator.ResetTrigger(IdleStimParameter);
			return;
		}

		idleTime += Time.deltaTime;
		if (idleTime >= idleStimDelay)
		{
			idleTime = 0f;
			animator.SetTrigger(IdleStimParameter);
		}
	}

	void UpdateInteractionTarget()
	{
		Component nextTarget = ResolveInteractionTarget(out Highlightable nextHighlight);
		if (ReferenceEquals(currentTarget, nextTarget) && ReferenceEquals(currentHighlight, nextHighlight))
			return;

		RemoveCurrentHighlight();
		currentTarget = nextTarget;
		if (nextHighlight)
			AddHighlight(nextHighlight);
	}

	void RemoveCurrentHighlight()
	{
		bool hadHighlight = !ReferenceEquals(currentHighlight, null);
		if (currentHighlight)
			currentHighlight.SetHighlighted(false);
		currentTarget = null;
		currentHighlight = null;

		if (hadHighlight)
			OnHighlight?.Invoke(null);
	}

	void AddHighlight(Highlightable _highlightable)
	{
		_highlightable.SetHighlighted(true);
		currentHighlight = _highlightable;
		OnHighlight?.Invoke(currentTarget.transform);
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

		if (!ReferenceEquals(currentTarget, null))
		{
			if (!TryGetTargetInfo(currentTarget, out _, out _))
			{
				RemoveCurrentHighlight();
				return;
			}

			if (currentTarget is Interactable interactable)
			{
				InteractionResult result = interactable.TryInteract(held);
				if (result == InteractionResult.ITEM_TRANSFERRED)
				{
					held = null;
					OnPickUp?.Invoke(null);
				}
			}
			else if (currentTarget is Pickable pickable)
			{
				pickable.PickUp(transform, holdLocalOffset);
				if (pickable.IsHeld)
				{
					held = pickable;
					OnPickUp?.Invoke(pickable.transform);
				}
			}

			UpdateInteractionTarget();
			return;
		}

		if (held == null)
			return;

		Vector3 facing = GetFacing();
		Vector3 dropPos = transform.position + facing * dropForward + Vector3.up * 0.35f;
		held.Drop(dropPos, rb.linearVelocity + facing * 1.5f);
		held = null;
		OnPickUp?.Invoke(null);
		UpdateInteractionTarget();
	}

	void OnAttack()
	{
		if (IsMinigameBlocking || endOfRound)
			return;

		if (FindClosestStation() is Furnace furnace)
		{
			furnace.TryInsertFuel(25);
			
			if (Time.time >= canFuelTime && animator && animator.isActiveAndEnabled && upperBodyLayer >= 0 && animator.HasState(upperBodyLayer, ThrowStatePath))
			{
				canFuelTime = Time.time + fuelTossCoolDown;
				idleTime = 0f;
				animator.ResetTrigger(IdleStimParameter);
				animator.SetLayerWeight(upperBodyLayer, 1f);
				animator.CrossFadeInFixedTime(TossFuelStatePath, 0f, upperBodyLayer, 0f);
			}
		}

		if (held == null)
			return;

		Vector3 facing = GetFacing();
		Vector3 throwPos = transform.position + facing * dropForward + Vector3.up * 0.55f;
		held.Throw(throwPos, rb.linearVelocity + facing * throwSpeed + Vector3.up * throwUpSpeed);

		held = null;
		OnPickUp?.Invoke(null);
		UpdateInteractionTarget();

		if (animator && animator.isActiveAndEnabled && upperBodyLayer >= 0 && animator.HasState(upperBodyLayer, ThrowStatePath))
		{
			idleTime = 0f;
			animator.ResetTrigger(IdleStimParameter);
			animator.SetLayerWeight(upperBodyLayer, 1f);
			animator.CrossFadeInFixedTime(ThrowStatePath, 0.05f, upperBodyLayer, 0f);
		}
	}

	public void ForceDrop(Vector3 _worldPos, Vector3 _velocity)
	{
		held.Drop(_worldPos, _velocity);
		
		held = null;
		OnPickUp?.Invoke(null);
		UpdateInteractionTarget();
	}

	Vector3 GetFacing()
	{
		if (moveDir.sqrMagnitude > 0.01f)
			return moveDir;

		Vector3 actorForward = actor.transform.forward;
		actorForward.y = 0f;
		if (actorForward.sqrMagnitude > 0.01f)
			return actorForward.normalized;

		return Vector3.forward;
	}

	Component ResolveInteractionTarget(out Highlightable highlight)
	{
		highlight = null;
		if (IsMinigameBlocking || endOfRound)
			return null;

		Component closest = null;
		float best = float.PositiveInfinity;
		var interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
		foreach (var interactable in interactables)
		{
			if (!TryGetTargetInfo(interactable, out Highlightable candidateHighlight, out float distSq) || distSq >= best)
				continue;

			best = distSq;
			closest = interactable;
			highlight = candidateHighlight;
		}

		if (held != null)
			return closest;

		var pickables = FindObjectsByType<Pickable>(FindObjectsSortMode.None);
		foreach (var pickable in pickables)
		{
			if (!TryGetTargetInfo(pickable, out Highlightable candidateHighlight, out float distSq) || distSq >= best)
				continue;

			best = distSq;
			closest = pickable;
			highlight = candidateHighlight;
		}

		return closest;
	}

	bool TryGetTargetInfo(Component target, out Highlightable highlight, out float distanceSq)
	{
		highlight = null;
		distanceSq = float.PositiveInfinity;
		if (!target || IsMinigameBlocking || endOfRound)
			return false;

		float rangeSq;
		if (target is Interactable interactable)
		{
			if (!interactable.isActiveAndEnabled || !interactable.CanInteract(held))
				return false;

			highlight = interactable.Highlight;
			distanceSq = interactable.DistanceSquared(transform.position);
			rangeSq = placeIntoStationRange * placeIntoStationRange;
		}
		else if (target is Pickable pickable)
		{
			if (held != null || !pickable.isActiveAndEnabled || !pickable.CanBePickedUp || !pickable.TryGetComponent(out highlight))
				return false;

			distanceSq = (pickable.transform.position - transform.position).sqrMagnitude;
			rangeSq = pickupRange * pickupRange;
		}
		else
			return false;

		return highlight && distanceSq <= rangeSq;
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
			if (station == null || !station.isActiveAndEnabled)
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
