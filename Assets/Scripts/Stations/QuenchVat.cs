using UnityEngine;

public class QuenchVat : Station
{
	[SerializeField] int numberOfSteamParticles = 5;
	
	[Header("References")]
	[SerializeField] Transform metalSocket;
	[SerializeField] GameObject steamParticlePrefab;

	[Header("Slot")]
	[SerializeField] Vector3 metalSocketLocalOffset = new Vector3(0f, 0.35f, 0f);

	HeatableMetal containedMetal;
	Highlightable highlight;

	private QuenchSteamParticle[] steamParticlePool;

    public override Highlightable Highlight { get { return highlight; } }

    public override bool CanAccept(Pickable _pickable)
    {
	    if (!_pickable.TryGetComponent(out HeatableMetal heatableMetal))
		    return false;
	    
        return !containedMetal && _pickable.Type == Pickable.PickableType.HeatableMetal && heatableMetal.IsQuenchTemp;
    }

    public override bool TryUse(Pickable _pickable)
    {
        return TryAcceptPickable(_pickable);
    }

	void Awake()
	{
		highlight = GetComponent<Highlightable>();
		EnsureSocket();
		EnsureSteamParticles();
	}

	public bool TryAcceptPickable(Pickable pickable)
	{
		if (pickable == null || pickable.InStation || pickable.Type != Pickable.PickableType.HeatableMetal)
			return false;

		if (pickable.TryGetComponent(out HeatableMetal metal))
			return TryInsertPart(pickable, metal);

		return false;
	}
	
	public override void NotifyItemRemoved(Pickable _pickable)
	{
		if (containedMetal != null && containedMetal.GetComponent<Pickable>() == _pickable)
			containedMetal = null;
	}

	public override float DistanceFromStationSquared(Vector3 _worldPoint)
	{
		Vector3 socketPos = metalSocket != null ? metalSocket.position : transform.position;
		return (_worldPoint - socketPos).sqrMagnitude;
	}

	bool TryInsertPart(Pickable pickable, HeatableMetal metal)
	{
		if (containedMetal != null)
			return false;

		EnsureSocket();

		bool shouldQuench = metal.IsQuenchTemp;
		if (!shouldQuench)
		{
			// TODO add clear feedback that the metal is too cold to quench
			LogText.Instance.SetText("TOO COLD TO QUENCH");
			return false;
		}
		
		if (!pickable.TryPlaceInStation(this, metalSocket))
			return false;
		
		metal.Quench();
		containedMetal = metal;
		
		EnsureSteamParticles();
		SpawnSteamParticles();
		
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

	void EnsureSteamParticles()
	{
		if (steamParticlePool != null)
			return;
		
		steamParticlePool = new QuenchSteamParticle[numberOfSteamParticles];
		
		for (int i = 0; i < steamParticlePool.Length; i++)
		{
			var steamParticleGO = Instantiate(steamParticlePrefab);
			steamParticlePool[i] = steamParticleGO.GetComponent<QuenchSteamParticle>();
		}
	}

	void SpawnSteamParticles()
	{
		float radiusIncrement = 0.75f / (float)steamParticlePool.Length;
		for (int i = 0; i < steamParticlePool.Length; i++)
		{
			steamParticlePool[i].Spawn(transform.position + (Vector3.up * 0.5f), i * radiusIncrement);
		}
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
