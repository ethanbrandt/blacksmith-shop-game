using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
	[CreateAssetMenu(fileName = "DialogueScene", menuName = "Dialogue/DialogueScene")]
	public class DialogueScene : ScriptableObject
	{
		public GameObject actorPrefab;
		public List<DialogueLine> lines;
	}

	[Serializable]
	public struct RawLine
	{
		public string speaker;
		public string pose;
		public ScrollSpeed scrollSpeed;
		[TextArea(3, 8)]
		public string text;
	}

	[Serializable]
	public struct DialogueLine
	{
		public RawLine rawLine;
		public List<Choice> choices;
	}

	[Serializable]
	public struct Choice
	{
		public string optionText;
		public RawLine responseLine;
	}

	[Serializable]
	public enum ScrollSpeed
	{
		NORMAL,
		SLOW,
		FAST,
		SUPER_FAST
	}
}
