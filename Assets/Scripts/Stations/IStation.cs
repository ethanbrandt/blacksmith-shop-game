using System;
using UnityEngine;

public abstract class Station : MonoBehaviour
{
    public abstract Highlightable Highlight { get; }

    public abstract bool CanAccept(Pickable _pickable);
    public abstract bool TryUse(Pickable _pickable);
    public abstract float DistanceFromStationSquared(Vector3 _worldPoint);
    public abstract void NotifyItemRemoved(Pickable _pickable);
}
