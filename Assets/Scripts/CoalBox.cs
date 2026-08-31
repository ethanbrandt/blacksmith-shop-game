using System;
using UnityEngine;

public class CoalBox : MonoBehaviour
{
    [SerializeField] GameObject coalPrefab;
    [SerializeField] Vector3 coalSpawnOffset;

    private Pickable currentCoal;

    void Start()
    {
		SpawnCoal();
    }

    void Update()
    {
        if (currentCoal.IsHeld)
            SpawnCoal();
    }

    private void SpawnCoal()
    {
         GameObject coalGO = Instantiate(coalPrefab, transform.position + coalSpawnOffset, Quaternion.identity);
         currentCoal = coalGO.GetComponent<Pickable>();
    }
}
