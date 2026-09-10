using System;
using UnityEngine;

public class RoundManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] FinishPartTable partTable;
    [SerializeField] Transform[] metalSpawnPoints;
    [SerializeField] GameObject metalPrefab;
    
    public float TimeElapsed => Time.time - startTime;
    
    private RoundUIHandler uiHandler;
    
    float startTime = -1f;

    void Start()
    {
        uiHandler = GetComponent<RoundUIHandler>();
    }

    public void BeginRound(ForgePiece _selectedForgePiece)
    {
        if (startTime > 0)
            return;
        
        startTime = Time.time;
        
        partTable.InitializePartLayout(_selectedForgePiece.PartLayout);

        ScenarioPart[] scenarioParts = _selectedForgePiece.ScenarioParts;
        if (metalSpawnPoints.Length < scenarioParts.Length)
        {
            Debug.LogError("Need more metal spawn points in the scene to load scenario");
            return;
        }
        
        for (int i = 0; i < scenarioParts.Length; i++)
        {
            var metalGO = Instantiate(metalPrefab, metalSpawnPoints[i].position, Quaternion.identity);
            var heatableMetal = metalGO.GetComponent<HeatableMetal>();
            heatableMetal.SetMetalType(scenarioParts[i].metalType);
            heatableMetal.SetPartDefinition(scenarioParts[i].partDefinition);
        }
    }

    public void EndRound(HeatableMetal[] finishedParts, PartDefinition[] partDefinitions, PartTableLayout partLayout)
    {
        if (uiHandler == null)
            uiHandler = GetComponent<RoundUIHandler>();

        float elapsedSeconds = startTime >= 0f ? Time.time - startTime : 0f;
        uiHandler.ShowFinalScores(finishedParts, partDefinitions, partLayout, elapsedSeconds);
    }
}
