using System;
using DialogueSystem;
using UnityEngine;

[CreateAssetMenu]
public class CustomerScenario : ScriptableObject
{
	[SerializeField] ForgePiece requestedPiece;
	[SerializeField] DialogueScene dialogueScene;

	[Header("Completion Dialogue by Final Rank")]
	[Tooltip("Customer reaction after delivering a D-rank piece.")]
	[SerializeField] DialogueScene dRankCompletionDialogue;
	[SerializeField] DialogueScene cRankCompletionDialogue;
	[SerializeField] DialogueScene bRankCompletionDialogue;
	[SerializeField] DialogueScene aRankCompletionDialogue;
	[SerializeField] DialogueScene sRankCompletionDialogue;
	[Tooltip("Used when the earned rank has no completion dialogue assigned. If both are empty, the reaction is skipped.")]
	[SerializeField] DialogueScene defaultCompletionDialogue;

	[Header("Scenario Info")]
	[SerializeField] string pieceTitle;
	[SerializeField] Sprite piecePreviewSprite;
	[SerializeField] string customerName;
	[SerializeField] string customerNote;

	public DialogueScene GetCompletionDialogue(FinalRank rank)
	{
		DialogueScene reaction = rank switch
		{
			FinalRank.D => dRankCompletionDialogue,
			FinalRank.C => cRankCompletionDialogue,
			FinalRank.B => bRankCompletionDialogue,
			FinalRank.A => aRankCompletionDialogue,
			FinalRank.S => sRankCompletionDialogue,
			_ => null
		};
		return reaction != null ? reaction : defaultCompletionDialogue;
	}

	public ForgePiece RequestedPiece => requestedPiece;
	public DialogueScene Scene => dialogueScene;
	public string PieceTitle => pieceTitle;
	public Sprite PiecePreviewSprite => piecePreviewSprite;
	public string CustomerName => customerName;
	public string CustomerNote => customerNote;
	
}