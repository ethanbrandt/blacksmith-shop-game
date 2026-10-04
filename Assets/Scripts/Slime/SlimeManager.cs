using UnityEngine;
using UnityEngine.Pool;

public class SlimeManager : MonoBehaviour
{
	[SerializeField] float minSlimeParticleSpawnTime;
	[SerializeField] float maxSlimeParticleSpawnTime;
	[SerializeField] GameObject slimeParticlePrefab;
	[SerializeField] GameObject slimePuddlePrefab;
	
	private HeatableMetal slimyMetalInstance;
	private ObjectPool<SlimeParticle> particlePool;
	private ObjectPool<SlimePuddle> puddlePool;
	
	private float particleSpawnTimer;

	void Awake()
	{
		particlePool = new ObjectPool<SlimeParticle>(
			createFunc: () =>
			{
				var obj = Instantiate(slimeParticlePrefab, transform);
				SlimeParticle particle = obj.GetComponent<SlimeParticle>();
				obj.SetActive(false);
				return particle;
			},
			actionOnGet: null,
			actionOnRelease: particle => particle.gameObject.SetActive(false),
			actionOnDestroy: particle => Destroy(particle.gameObject),
			collectionCheck: true,
			defaultCapacity: 10,
			maxSize: 20
		);

		puddlePool = new ObjectPool<SlimePuddle>(
			createFunc: () =>
			{
				var obj = Instantiate(slimePuddlePrefab, transform);
				SlimePuddle puddle = obj.GetComponent<SlimePuddle>();
				obj.SetActive(false);
				return puddle;
			},
			actionOnGet: null,
			actionOnRelease: particle => particle.gameObject.SetActive(false),
			actionOnDestroy: particle => Destroy(particle.gameObject),
			collectionCheck: true,
			defaultCapacity: 10,
			maxSize: 20
		);
	}
	
	public void Initialize(HeatableMetal _metal)
	{
		slimyMetalInstance = _metal;
		particleSpawnTimer = minSlimeParticleSpawnTime;
	}

	void Update()
	{
		particleSpawnTimer -= Time.deltaTime;
		
		if (particleSpawnTimer <= 0f && slimyMetalInstance.IsMelting)
		{
			particleSpawnTimer = Random.Range(minSlimeParticleSpawnTime, maxSlimeParticleSpawnTime);
			SlimeParticle particle = particlePool.Get();
			particle.transform.position = slimyMetalInstance.transform.position;
			particle.gameObject.SetActive(true);
			particle.SetColor(slimyMetalInstance.MetalType.SampleColor(slimyMetalInstance.Heat01, true));
			particle.Launch();
		}
	}

	public void SpawnPuddle(SlimeParticle _particle)
	{
		SlimePuddle puddle = puddlePool.Get();
		puddle.gameObject.SetActive(true);
		puddle.transform.position = _particle.transform.position;
		puddle.SetColor(_particle.GetColor());
		puddle.NotifySpawned();
		
		particlePool.Release(_particle);
	}

	public void DespawnPuddle(SlimePuddle _puddle)
	{
		puddlePool.Release(_puddle);
	}
}
