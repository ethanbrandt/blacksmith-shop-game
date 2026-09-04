using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class QuenchSteamParticle : MonoBehaviour
{
    [SerializeField] float scale;
    [SerializeField] float scaleVariance;
    [SerializeField] float moveSpeed;
    [SerializeField] float moveSpeedVariance; 
    [SerializeField] float lifeTime;
    [SerializeField] float lifeTimeVariance;
    [SerializeField] Vector3 offScreenPos;

    private bool isAlive;
    private float endTime;
    private float actualMoveSpeed;
    
    public void Spawn(Vector3 _spawnPos, float _spawnRadius)
    {
        isAlive = true;
        
        Vector3 alivePos = _spawnPos + Random.insideUnitSphere * _spawnRadius;
        transform.position = new Vector3(alivePos.x, _spawnPos.y, alivePos.z);
        
        float lifeTimeChange = Random.Range(-lifeTimeVariance, lifeTimeVariance);
        endTime = Time.time + lifeTime + lifeTimeChange;

        float moveSpeedChange = Random.Range(-moveSpeedVariance, moveSpeedVariance);
        actualMoveSpeed = moveSpeed + moveSpeedChange;

        float scaleChange = Random.Range(0, scaleVariance);
        float uniformScale = scale + scaleChange;
        transform.localScale = Vector3.one * uniformScale;
    }

    void Start()
    {
        transform.position = offScreenPos;
    }

    void Update()
    {
        if (!isAlive)
            return;
        
        if (endTime <= Time.time)
        {
            isAlive = false;
            transform.position = offScreenPos;
            return;
        }
        
        transform.position += Vector3.up * (actualMoveSpeed * Time.deltaTime);
    }
}
