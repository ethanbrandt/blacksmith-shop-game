using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class SlimeParticle : MonoBehaviour
{
	[SerializeField] float minLaunchVelocity;
	[SerializeField] float maxLaunchVelocity;
	
	private Rigidbody rb;
	private Material mat;
	private SlimeManager manager;
	
	private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

	void Awake()
	{
		rb = GetComponent<Rigidbody>();
		mat = GetComponent<Renderer>().material;
		manager = GetComponentInParent<SlimeManager>();
	}
	
	private void OnTriggerEnter(Collider other)
	{
		if (!other.CompareTag("Floor"))
			return;

		rb.useGravity = false;
		rb.linearVelocity = Vector3.zero;
		transform.position = new Vector3(transform.position.x, other.transform.position.y, transform.position.z);
		manager.SpawnPuddle(this);
	}

	public void SetColor(Color _color)
	{
		mat.SetColor(BaseColor, _color);
	}

	public Color GetColor()
	{
		return mat.GetColor(BaseColor);
	}

	public void Launch()
	{
		Vector3 dir = Random.onUnitSphere;
		dir.y = Mathf.Abs(dir.y);

		rb.useGravity = true;
		rb.AddForce(dir * Random.Range(minLaunchVelocity, maxLaunchVelocity), ForceMode.Impulse);
	}
}
