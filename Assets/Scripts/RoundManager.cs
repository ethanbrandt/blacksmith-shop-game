using System;
using ForgingPrototype;
using UnityEngine;

public class RoundManager : MonoBehaviour
{
    public float TimeElapsed => Time.time - startTime;
    
    private RoundUIHandler uiHandler;
    
    float startTime = -1f;

    void Start()
    {
        uiHandler = GetComponent<RoundUIHandler>();
    }

    public void BeginRound()
    {
        if (startTime > 0)
            return;
        
        startTime = Time.time;
    }

    public void EndRound(
        HeatableMetal[] finishedParts,
        PartDefinition[] partDefinitions,
        PartTableLayout partLayout)
    {
        if (uiHandler == null)
            uiHandler = GetComponent<RoundUIHandler>();

        float elapsedSeconds = startTime >= 0f ? Time.time - startTime : 0f;
        uiHandler.ShowFinalScores(finishedParts, partDefinitions, partLayout, elapsedSeconds);
    }
}
