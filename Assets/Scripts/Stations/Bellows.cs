using UnityEngine;

public class Bellows : Interactable
{
	private Highlightable highlightable;
	private Furnace furnace;

	void Awake()
	{
		highlightable = GetComponent<Highlightable>();
		furnace = FindFirstObjectByType<Furnace>();
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

		furnace.TryPumpBellows();
		return InteractionResult.USED;
	}

	public override float DistanceSquared(Vector3 _worldPoint)
	{
		return (_worldPoint - transform.position).sqrMagnitude;
	}
}
