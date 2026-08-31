using System;
using UnityEngine;
using UnityEngine.UI;

public class FinishPartTable : Station
{
	[Header("Part Image Params")]
	[SerializeField] Vector3 partImageRotationOffset;
	[SerializeField] Vector2 partImageDimensions;
	
	[Header("References")]
	[SerializeField] Canvas partTableCanvas;
	[SerializeField] RoundManager roundManager;
	
	PartTableLayout partLayout;
	PartSocket[] partSockets;
	Highlightable highlight;

	public override Highlightable Highlight { get { return highlight; } }
    public override Transform InteractionPoint { get { return transform; } }

    public override bool CanAccept(Pickable _pickable)
    {
    	return _pickable.Type == Pickable.PickableType.QuenchedMetal;
    }

    public override bool TryUse(Pickable _pickable)
    {
        return TryAcceptPickable(_pickable);
    }

    void Awake()
    {
	    highlight = GetComponent<Highlightable>();
    }

    public void InitializePartLayout(PartTableLayout _partLayout)
	{
		partLayout = _partLayout;
		EnsureSockets();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.IsHeld || pickable.IsInFurnace || pickable.IsOnAnvil || pickable.IsOnGrindstone)
			return false;
		
		if (pickable.Type != Pickable.PickableType.QuenchedMetal)
		{
			LogText.Instance.SetText("MUST BE QUENCHED TO BE PUT ON TABLE");
			return false;
		}

		if (pickable.TryGetComponent(out HeatableMetal metal))
			return TryInsertPart(pickable, metal);
		
		return false;
	}

	bool TryInsertPart(Pickable pickable, HeatableMetal metal)
	{
		int partSocketIndex = FindCorrectPartSocketIndex(metal.PartDefinition);

		if (partSocketIndex < 0)
			return false;

		if (partSockets[partSocketIndex].partSocket == null)
			return false;
		
		if (!pickable.TryPlaceOnFinalTable(partSockets[partSocketIndex].partSocket))
			return false;
		
		partSockets[partSocketIndex].containedPart = metal;
		return true;
	}

	int FindCorrectPartSocketIndex(PartDefinition _partDefinition)
	{
		for (var i = 0; i < partSockets.Length; i++)
		{
			var partSocket = partSockets[i];
			if (_partDefinition == partSocket.partDefinition && partSocket.containedPart == null)
				return i;
		}

		return -1;
	}

	void EnsureSockets()
	{
		if (partSockets != null && partSockets.Length == 0)
			return;

		PartTableSlot[] partTableSlots = partLayout.GetPartTableSlots();
		partSockets = new PartSocket[partTableSlots.Length];
		
		for (int i = 0; i < partTableSlots.Length; i++)
		{
			var socketGo = new GameObject($"{partTableSlots[i].part.displayName} Socket ({i})", typeof(Image));
			partSockets[i].partSocket = socketGo.transform;
			partSockets[i].partSocket.SetParent(partTableCanvas.transform, false);
			partSockets[i].partSocket.localPosition = partTableSlots[i].positionOffset;
			partSockets[i].partSocket.localRotation = Quaternion.Euler(partImageRotationOffset);
			
			partSockets[i].partDefinition = partTableSlots[i].part;
			partSockets[i].containedPart = null;
			
			Image socketImg = partSockets[i].partSocket.GetComponent<Image>();
			socketImg.sprite = partTableSlots[i].part.partSocketSprite;
			socketImg.rectTransform.sizeDelta = partImageDimensions;
		}
	}

	void OnTriggerEnter(Collider other)
	{
		TryAcceptFromCollider(other);
		
		if (IsFinished())
			Finish();
	}

	void OnCollisionEnter(Collision other)
	{
		TryAcceptFromCollider(other.collider);
		
		if (IsFinished())
			Finish();
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

	void Finish()
	{
		LogText.Instance.SetText("PIECE COMPLETE");

		HeatableMetal[] finishedParts = new HeatableMetal[partSockets.Length];
		PartDefinition[] partDefinitions = new PartDefinition[partSockets.Length];

		for (int i = 0; i < partSockets.Length; i++)
		{
			finishedParts[i] = partSockets[i].containedPart;
			partDefinitions[i] = partSockets[i].partDefinition;
		}
		
		roundManager.EndRound(finishedParts, partDefinitions, partLayout);
	}
	
	bool IsFinished()
	{
		foreach (PartSocket partSocket in partSockets)
			if (partSocket.containedPart == null)
				return false;
		
		return true;
	}
	
	struct PartSocket
	{
		public Transform partSocket;
		public PartDefinition partDefinition;
		public HeatableMetal containedPart;
	}
}
