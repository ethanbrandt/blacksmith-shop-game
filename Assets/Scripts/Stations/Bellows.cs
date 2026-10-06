using System;
using UnityEngine;

public class Bellows : Interactable
{
	[SerializeField] float coolDown = 0.25f;
	[SerializeField] float bufferTime = 0.25f;
	
	private Highlightable highlightable;
	private Animator animator;
	private Furnace furnace;

	private float canPumpTime;
	private float lastPumpInputTime = float.NegativeInfinity;

	void Awake()
	{
		highlightable = GetComponent<Highlightable>();
		animator = GetComponent<Animator>();
		furnace = FindFirstObjectByType<Furnace>();
	}

	void Update()
	{
		TryConsumePumpInput();
	}

	private void OnDisable()
	{
		lastPumpInputTime = float.NegativeInfinity;
	}

	public override Highlightable Highlight { get { return highlightable; } }

	public override bool CanInteract(Pickable _heldItem)
	{
		return _heldItem == null;
	}

	public override InteractionResult TryInteract(Pickable _heldItem)
	{
		if (!CanInteract(_heldItem))
			return InteractionResult.FAILED;

		lastPumpInputTime = Time.time;
		TryConsumePumpInput();
		
		return InteractionResult.USED;
	}

	private void TryConsumePumpInput()
	{
		if (Time.time - lastPumpInputTime > bufferTime)
			return;

		if (Time.time < canPumpTime)
			return;

		lastPumpInputTime = float.NegativeInfinity;
		canPumpTime = Time.time + coolDown;

		furnace.TryPumpBellows();
		animator.CrossFadeInFixedTime("Base Layer.Pump", 0.1f, 0, 0f);
	}

	public override float DistanceSquared(Vector3 _worldPoint)
	{
		return (_worldPoint - transform.position).sqrMagnitude;
	}
}
