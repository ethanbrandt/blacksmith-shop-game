using UnityEngine;

public abstract class Station : MonoBehaviour
{
    public abstract Transform InteractionPoint { get; }
    public abstract Highlightable Highlight { get; }

    public abstract bool CanAccept(Pickable _pickable);
    public abstract bool TryUse(Pickable _pickable);
}
