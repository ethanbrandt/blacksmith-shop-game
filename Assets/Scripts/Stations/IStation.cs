using System;
using UnityEngine;

public enum InteractionResult
{
	FAILED,
	USED,
	ITEM_TRANSFERRED
}

public abstract class Interactable : MonoBehaviour
{
	public abstract Highlightable Highlight { get; }

	public abstract bool CanInteract(Pickable _heldItem);
	public abstract InteractionResult TryInteract(Pickable _heldItem);
	public abstract float DistanceSquared(Vector3 _worldPoint);
}

public abstract class Station : Interactable
{

    public abstract bool CanAccept(Pickable _pickable);
    public abstract bool TryUse(Pickable _pickable);
    public abstract float DistanceFromStationSquared(Vector3 _worldPoint);
    public abstract void NotifyItemRemoved(Pickable _pickable);
}
