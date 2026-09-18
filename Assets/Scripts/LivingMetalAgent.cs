using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

[Serializable]
public struct LivingMetalSprites
{
	public Sprite sSprite;
	public Sprite aSprite;
	public Sprite bSprite;
	public Sprite cSprite;
	public Sprite dSprite;

	public Sprite hitSprite;
	public Sprite meltSprite;
}

public class LivingMetalAgent : MonoBehaviour
{
	private const float WAIT_TIME = 2f;
	
	private NavMeshAgent navAgent;
	private HeatableMetal metal;
	private Rigidbody rb;
	private SpriteRenderer forgeImage;
	private LivingMetalSprites sprites;
	
	private float waitTimer = 0f;
	private Vector2 forgeImageOffset;
	private Coroutine knockbackRoutine;
	private bool isKnockedBack;
	private MetalDeformer2D subscribedDeformer;

	void Awake()
	{
		navAgent = GetComponent<NavMeshAgent>();
		navAgent.enabled = false;
		
		metal = GetComponent<HeatableMetal>();
		rb = GetComponent<Rigidbody>();
	}

	void OnEnable()
	{
		metal.Pickable.OnStateChanged += OnPickableStateChanged;
		RefreshStrikeSubscription();
	}

	private void OnDisable()
	{
		metal.Pickable.OnStateChanged -= OnPickableStateChanged;
		SetStrikeSubscription(null);
		CancelKnockback();
		if (forgeImage != null)
			forgeImage.enabled = false;
	}

	void LateUpdate()
	{
		RefreshStrikeSubscription();
		UpdateForgeImage();

		if (metal.Pickable.State != Pickable.PickableState.FREE)
			return;

		if (!navAgent.isActiveAndEnabled || !navAgent.isOnNavMesh || navAgent.remainingDistance > 0.25f)
			return;
		
		waitTimer -= Time.deltaTime;
		if (waitTimer <= 0f && TryGetRandomDestination(3f, out Vector3 destination))
		{
			waitTimer = WAIT_TIME;
			navAgent.SetDestination(destination);
		}
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (collision.gameObject.CompareTag("Floor") && metal.Pickable.State == Pickable.PickableState.FREE)
		{
			navAgent.enabled = true;
			rb.isKinematic = true;
		}
	}

	void OnPickableStateChanged(Pickable.PickableState _state)
	{
		navAgent.enabled = false;
		waitTimer = WAIT_TIME;

		UpdateForgeImage();
	}

	bool TryGetRandomDestination(float _radius, out Vector3 _destination)
	{
		_destination = default;

		if (!navAgent.isActiveAndEnabled || !navAgent.isOnNavMesh)
			return false;

		var filter = new NavMeshQueryFilter
		{
			agentTypeID = navAgent.agentTypeID,
			areaMask = navAgent.areaMask
		};

		var path = new NavMeshPath();

		for (int i = 0; i < 30; i++)
		{
			Vector2 offset = Random.insideUnitCircle * _radius;

			Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
			if (!NavMesh.SamplePosition(candidate, out var hit, 1f, filter))
				continue;
			
			if (!navAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
				continue;

			_destination = hit.position;
			return true;
		}
		
		return false;
	}

	private void OnStruck(Vector2 _impactPoint, float _impactRadius)
	{
		var session = ForgeSessionController.Instance;
		if (forgeImage == null || session == null || !session.IsOpen || session.ActiveMetal != metal || session.MetalDeformer == null || session.MetalDeformer.MetalVertices.Count < 3)
			return;

		Vector2 center = PolygonGeometry.ComputeBounds(session.MetalDeformer.MetalVertices).center;
		Vector2 direction = center - _impactPoint;
		if (direction.sqrMagnitude < 0.0001f)
			direction = Vector2.up;

		if (knockbackRoutine != null)
			StopCoroutine(knockbackRoutine);
		isKnockedBack = true;
		forgeImage.sprite = sprites.hitSprite;
		knockbackRoutine = StartCoroutine(KnockbackImage(direction.normalized * 0.1f));
	}

	private IEnumerator KnockbackImage(Vector2 peakOffset)
	{
		Vector2 startOffset = forgeImageOffset;
		const float pushDuration = 0.06f;
		const float returnDuration = 0.1f;
		for (float elapsed = 0f; elapsed < pushDuration; elapsed += Time.deltaTime)
		{
			float t = elapsed / pushDuration;
			forgeImageOffset = Vector2.Lerp(startOffset, peakOffset, 1f - (1f - t) * (1f - t));
			yield return null;
		}
		
		for (float elapsed = 0f; elapsed < returnDuration; elapsed += Time.deltaTime)
		{
			float t = Mathf.SmoothStep(0f, 1f, elapsed / returnDuration);
			forgeImageOffset = Vector2.Lerp(peakOffset, Vector2.zero, t);
			yield return null;
		}
		
		forgeImageOffset = Vector2.zero;
		isKnockedBack = false;
		knockbackRoutine = null;
		UpdateForgeImage();
	}

	private void CancelKnockback()
	{
		if (knockbackRoutine != null)
			StopCoroutine(knockbackRoutine);
		
		knockbackRoutine = null;
		isKnockedBack = false;
		forgeImageOffset = Vector2.zero;
	}

	private void RefreshStrikeSubscription()
	{
		var session = ForgeSessionController.Instance;
		SetStrikeSubscription(session != null ? session.MetalDeformer : null);
	}

	private void SetStrikeSubscription(MetalDeformer2D deformer)
	{
		if (subscribedDeformer == deformer)
			return;
		if (subscribedDeformer != null)
			subscribedDeformer.Struck -= OnStruck;
		subscribedDeformer = deformer;
		if (subscribedDeformer != null)
			subscribedDeformer.Struck += OnStruck;
	}

	private void OnDestroy()
	{
		if (forgeImage != null)
			Destroy(forgeImage.gameObject);
	}

	public void SetForgeSprites(LivingMetalSprites _sprites)
	{
		if (forgeImage == null)
		{
			var imageObject = new GameObject("LivingMetalForgeImage");
			forgeImage = imageObject.AddComponent<SpriteRenderer>();

			forgeImage.sortingOrder = 11;
			forgeImage.enabled = false;
		}

		sprites = _sprites;
		forgeImage.sprite = _sprites.dSprite;
	}

	private void UpdateForgeImage()
	{
		if (forgeImage == null)
			return;

		var session = ForgeSessionController.Instance;
		bool visible = session != null && session.IsOpen && session.ActiveMetal == metal
			&& session.MetalDeformer != null && session.MetalDeformer.MetalVertices.Count >= 3;

		forgeImage.enabled = visible;

		if (!visible)
		{
			CancelKnockback();
			return;
		}

		Sprite nextSprite = sprites.dSprite;
		switch (metal.GetForgedShapeQuality())
		{
			case ShapeQuality.Incomplete:
				nextSprite = sprites.dSprite;
				break;
			case ShapeQuality.Flawed:
				nextSprite = sprites.cSprite;
				break;
			case ShapeQuality.Good:
				nextSprite = sprites.bSprite;
				break;
			case ShapeQuality.Excellent:
				nextSprite = sprites.aSprite;
				break;
			case ShapeQuality.Perfect:
				nextSprite = sprites.sSprite;
				break;
		}

		if (metal.IsMelting)
			nextSprite = sprites.meltSprite;
		
		forgeImage.sprite = isKnockedBack ? sprites.hitSprite : nextSprite;
		
		var deformer = session.MetalDeformer;
		Bounds bound = PolygonGeometry.ComputeBounds(deformer.MetalVertices);

		forgeImage.gameObject.layer = deformer.gameObject.layer;
		forgeImage.transform.position = new Vector3(bound.center.x + forgeImageOffset.x,
			bound.center.y + forgeImageOffset.y, deformer.transform.position.z - 0.03f);
	}
}
