using UnityEngine;

public class SlimePuddle : MonoBehaviour
{
	[SerializeField] float minSize;
	[SerializeField] float maxSize;
	[SerializeField] float sizeShrinkPerSec;
	[SerializeField] float despawnSize;
	
	private Material mat;
	private SlimeManager manager;
	private float size = 0f;
	private Vector3 sizeVec = new Vector3();
	
	private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

	void Awake()
	{
		mat = GetComponent<Renderer>().material;
		manager = GetComponentInParent<SlimeManager>();
	}
	
	private void OnTriggerEnter(Collider other)
	{
		if (!gameObject.activeInHierarchy)
			return;
		
		if (other.TryGetComponent(out PlayerController player))
			player.ApplySlimeSlide();
	}

	void Update()
	{
		if (!gameObject.activeInHierarchy)
			return;
		
		size -= sizeShrinkPerSec * Time.deltaTime;
		if (size <= despawnSize)
		{
			manager.DespawnPuddle(this);
			return;
		}
		
		sizeVec.x = size;
		sizeVec.z = size;
		transform.localScale = sizeVec;
	}

	public void SetColor(Color _color)
	{
		mat.SetColor(BaseColor, _color);
	}

	public void NotifySpawned()
	{
		size = Random.Range(minSize, maxSize);
		sizeVec = new Vector3(size, 0.25f, size);
		transform.localScale = sizeVec;
	}
}
