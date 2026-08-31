using UnityEngine;

public class QuenchVat : Station
{
	[Range(0, 1)]
	[SerializeField] float heatPercentToQuench = 0.8f;
	
	[Header("References")]
	[SerializeField] Transform metalSocket;

	[Header("Slot")]
	[SerializeField] Vector3 metalSocketLocalOffset = new Vector3(0f, 0.35f, 0f);

	HeatableMetal containedMetal;
	Highlightable highlight;

    public override Highlightable Highlight { get { return highlight; } }
    public override Transform InteractionPoint { get { return metalSocket; } }

    public override bool CanAccept(Pickable _pickable)
    {
	    if (!_pickable.TryGetComponent(out HeatableMetal heatableMetal))
		    return false;
	    
        return !containedMetal && _pickable.Type == Pickable.PickableType.HeatableMetal && heatableMetal.Heat01 >= heatPercentToQuench;
    }

    public override bool TryUse(Pickable _pickable)
    {
        return TryAcceptPickable(_pickable);
    }

	void Awake()
	{
		highlight = GetComponent<Highlightable>();
		EnsureSocket();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsInFurnace || pickable.IsOnAnvil || pickable.IsOnGrindstone || pickable.Type != Pickable.PickableType.HeatableMetal)
			return false;

		if (pickable.TryGetComponent(out HeatableMetal metal))
			return TryInsertPart(pickable, metal);

		return false;
	}
	
	public void NotifyItemRemoved(Pickable pickable)
	{
		if (containedMetal != null && containedMetal.GetComponent<Pickable>() == pickable)
			containedMetal = null;
	}

	bool TryInsertPart(Pickable pickable, HeatableMetal metal)
	{
		if (containedMetal != null)
			return false;

		EnsureSocket();

		bool shouldQuench = metal.Heat01 >= heatPercentToQuench;
		if (!shouldQuench)
		{
			// TODO add clear feedback that the metal is too cold to quench
			LogText.Instance.SetText("TOO COLD TO QUENCH");
			return false;
		}
		
		if (!pickable.TryPlaceInQuenchVat(this, metalSocket))
			return false;
		
		metal.Quench();	
		containedMetal = metal;
		return true;
	}

	void EnsureSocket()
	{
		if (metalSocket != null)
			return;

		var socketGo = new GameObject("MetalSocket");
		metalSocket = socketGo.transform;
		metalSocket.SetParent(transform, false);
		metalSocket.localPosition = metalSocketLocalOffset;
	}

	void OnTriggerEnter(Collider other) => TryAcceptFromCollider(other);
	
	void OnCollisionEnter(Collision collision)
	{
		if (collision == null)
			return;

		TryAcceptFromCollider(collision.collider);
	}

	void TryAcceptFromCollider(Collider other)
	{
		if (other == null)
			return;

		if (!other.TryGetComponent(out Pickable pickable))
			pickable = other.GetComponentInParent<Pickable>();

		if (pickable == null)
			return;

		TryAcceptPickable(pickable);
	}
}
