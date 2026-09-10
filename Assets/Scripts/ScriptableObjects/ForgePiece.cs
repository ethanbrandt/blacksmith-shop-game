using System;
using UnityEngine;

[Serializable]
public struct ScenarioPart
{
    public PartDefinition partDefinition;
    public MetalType metalType;
}

[CreateAssetMenu(fileName = "Scenario", menuName = "Forging/ForgePiece", order = 3)]
public class ForgePiece : ScriptableObject
{
    [SerializeField] PartTableLayout partLayout;
    [SerializeField] ScenarioPart[] scenarioParts;

    public PartTableLayout PartLayout => partLayout;
    public ScenarioPart[] ScenarioParts { get { return scenarioParts; } }
    
    public bool IsValid()
    {
        if (partLayout == null || scenarioParts.Length < 1)
            return false;
        
        bool hasIssue = false;
        
        PartTableSlot[] partTableSlots = partLayout.GetPartTableSlots();
        if (partTableSlots.Length != scenarioParts.Length)
        {
            Debug.LogWarning("Scenario Part Length Mismatch");
            hasIssue = true;
        }

        for (int i = 0; i < scenarioParts.Length; i++)
        {
            if (scenarioParts[i].metalType == null)
            {
                Debug.LogWarning($"Scenario Part MetalType missing at index: {i}");
                hasIssue = true;
            }
            if (scenarioParts[i].partDefinition == null)
            {
                Debug.LogWarning($"Scenario Part Definition missing at index: {i}");
                hasIssue = true;
            }
            else if (partTableSlots[i].part != scenarioParts[i].partDefinition)
            {
                Debug.LogWarning($"Scenario Part Definition Mismatch at index: {i}");
                hasIssue = true;
            }
        }
        
        return !hasIssue;
    }
}
