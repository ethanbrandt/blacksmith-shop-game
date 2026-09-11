using System;
using DialogueSystem;
using UnityEngine;

[CreateAssetMenu]
public class CustomerScenario : ScriptableObject
{
	[SerializeField] ForgePiece requestedPiece;
	[SerializeField] DialogueScene dialogueScene;

	[Header("Scenario Info")]
	[SerializeField] string pieceTitle;
	[SerializeField] Sprite piecePreviewSprite;
	[SerializeField] string customerName;
	[SerializeField] string estimatedReward;
	[SerializeField] string customerNote;

	public ForgePiece RequestedPiece => requestedPiece;
	public DialogueScene Scene => dialogueScene;
	public string PieceTitle => pieceTitle;
	public Sprite PiecePreviewSprite => piecePreviewSprite;
	public string CustomerName => customerName;
	public string EstimatedReward => estimatedReward;
	public string CustomerNote => customerNote;
	
}