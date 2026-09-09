using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueSystem
{
	public class DialogueManager : MonoBehaviour
	{
		[SerializeField] DialogueScene testScene;
		
		[Header("Text Scroll")]
		[SerializeField] float slowScrollWaitTime;
		[SerializeField] float normalScrollWaitTime;
		[SerializeField] float fastScrollWaitTime;
		[SerializeField] float superFastScrollWaitTime;
		
		private UIDocument document;
		private Label dialogueBoxLabel;
		private VisualElement choicesElement;
		private List<Button> choiceButtons;
		
		private DialogueParser dialogueParser;
		
		private DialogueScene currentScene;
		private ParsedLine currentParsedLine;
		private DialogueLine currentDialogueLine;
		private DialogueState state;

		private readonly List<Action> choiceCallbacks = new List<Action>();
		private int lineIndex;
		
		private int totalGlyphCount;
		private int visibleGlyphCount;

		private float currentScrollWaitTime;

		private float nextScrollTimer = 0f;

		private enum DialogueState
		{
			STARTING,
			SCROLLING,
			PICKING_CHOICE,
			STARTING_RESPONSE,
			RESPONSE_SCROLLING,
			FINISHED
		}

		void Awake()
		{
			document = GetComponentInChildren<UIDocument>();
			dialogueParser = new DialogueParser();
			state = DialogueState.STARTING;
		}

		private void OnEnable()
		{
			if (document == null)
				return;
			
			dialogueBoxLabel = document.rootVisualElement.Q<Label>("DialogueBoxLabel");
			dialogueBoxLabel.PostProcessTextVertices += ApplyTextEffects;

			choicesElement = document.rootVisualElement.Q<VisualElement>("ChoicesElement");
			choiceButtons = choicesElement.Query<Button>().ToList();
			
			for (int i = 0; i < choiceButtons.Count; i++)
			{
				int index = i;
				Action callback = () => OnChoiceSelected(index);
				choiceCallbacks.Add(callback);
				choiceButtons[i].clicked += callback;
			}
		}

		void OnDisable()
		{
			if (dialogueBoxLabel != null)
			{
				dialogueBoxLabel.PostProcessTextVertices -= ApplyTextEffects;
				dialogueBoxLabel.MarkDirtyRepaint();
				dialogueBoxLabel = null;
			}

			if (choiceButtons != null)
				for (int i = 0; i < choiceCallbacks.Count; i++)
					choiceButtons[i].clicked -= choiceCallbacks[i];
			
			choiceCallbacks.Clear();
			choiceButtons = null;
		}

		void Start()
		{
			choicesElement.EnableInClassList("is-hidden", true);
			SetDialogueScene(testScene);
		}

		void Update()
		{
			dialogueBoxLabel?.MarkDirtyRepaint();

			if (totalGlyphCount < 0)
				return;

			nextScrollTimer -= Time.unscaledDeltaTime;
			if (nextScrollTimer > 0f)
				return;
			
			if (visibleGlyphCount >= totalGlyphCount)
			{
				if (state == DialogueState.SCROLLING)
				{
					if (currentDialogueLine.choices.Count > 0)
					{
						state = DialogueState.PICKING_CHOICE;
						
						choicesElement.EnableInClassList("is-hidden", false);
						choiceButtons[0].EnableInClassList("is-hidden", false);
						choiceButtons[0].schedule.Execute(() => choiceButtons[0].Focus());
						
						for (int i = 0; i < choiceButtons.Count; i++)
						{
							Button button = choiceButtons[i];
							if (i >= currentDialogueLine.choices.Count)
							{
								button.EnableInClassList("is-hidden", true);
							}
							else
							{
								button.EnableInClassList("is-hidden", false);
								button.text = currentDialogueLine.choices[i].optionText;
							}
							
						}
					}
					else
						state = DialogueState.FINISHED;
				}
				else if (state == DialogueState.RESPONSE_SCROLLING)
					state = DialogueState.FINISHED;
				
				return;
			}

			while (nextScrollTimer <= 0f && visibleGlyphCount < totalGlyphCount)
			{
				visibleGlyphCount++;

				if (visibleGlyphCount < totalGlyphCount)
					nextScrollTimer += currentScrollWaitTime;

				foreach (var pause in currentParsedLine.pauses)
					if (pause.pauseIndex == visibleGlyphCount)
						nextScrollTimer += pause.duration;
			}
		}

		private void OnSubmit()
		{
			if (totalGlyphCount < 0)
				return;

			switch (state)
			{
				case DialogueState.SCROLLING:
				case DialogueState.RESPONSE_SCROLLING:
					visibleGlyphCount = totalGlyphCount;
					nextScrollTimer = 0f;
					break;
				case DialogueState.FINISHED:
					StartNextLine();
					break;
			}
		}

		private void OnChoiceSelected(int _choiceIndex)
		{
			if (state != DialogueState.PICKING_CHOICE || _choiceIndex < 0 || _choiceIndex >= currentDialogueLine.choices.Count)
				return;

			Choice choice = currentDialogueLine.choices[_choiceIndex];
			state = DialogueState.STARTING_RESPONSE;
			
			SetRawLine(choice.responseLine);
		}

		public void StartNextLine()
		{
			if (lineIndex == currentScene.lines.Count - 1)
			{
				EndDialogue();
				return;
			}
			
			lineIndex++;
			SetLine(currentScene.lines[lineIndex]);
		}

		private void EndDialogue()
		{
			print("END OF DIALOGUE");
		}

		public void SetDialogueScene(DialogueScene _dialogueScene)
		{
			if (_dialogueScene.lines.Count == 0)
				return;

			lineIndex = 0;
			currentScene = _dialogueScene;
			SetLine(currentScene.lines[lineIndex]);
		}

		private void SetLine(DialogueLine _line)
		{
			currentDialogueLine = _line;
			SetRawLine(_line.rawLine);
		}

		private void SetRawLine(RawLine _line)
		{
			currentParsedLine = dialogueParser.ParseDialogueLine(_line.text);
			
			if (string.IsNullOrEmpty(currentParsedLine.displayText))
				StartNextLine();
			
			dialogueBoxLabel.text = currentParsedLine.displayText;

			currentScrollWaitTime = ScrollSpeedToWaitTime(_line.scrollSpeed);
			
			choicesElement.EnableInClassList("is-hidden", true);
			document.rootVisualElement.schedule.Execute(() => dialogueBoxLabel.Focus());
			
			totalGlyphCount = -1;
			visibleGlyphCount = 0;
			nextScrollTimer = 0f;

			if (currentParsedLine.pauses != null)
				foreach (var pause in currentParsedLine.pauses)
					if (pause.pauseIndex == 0)
						nextScrollTimer += pause.duration;
		}

		private float ScrollSpeedToWaitTime(ScrollSpeed _scrollSpeed)
		{
			switch (_scrollSpeed)
			{
				case ScrollSpeed.SLOW:
					return slowScrollWaitTime;
				case ScrollSpeed.FAST:
					return fastScrollWaitTime;
				case ScrollSpeed.SUPER_FAST:
					return superFastScrollWaitTime;
				default:
					return normalScrollWaitTime;
			}
		}

		private void ApplyTextEffects(TextElement.GlyphsEnumerable _glyps)
		{
			if (string.IsNullOrEmpty(currentParsedLine.displayText))
			{
				StartNextLine();
				return;
			}

			if (totalGlyphCount < 0)
			{
				totalGlyphCount = _glyps.Count;
				state = (state == DialogueState.STARTING_RESPONSE) ? DialogueState.RESPONSE_SCROLLING : DialogueState.SCROLLING;
			}
			
			int glyphIndex = 0;
			foreach (var glyph in _glyps)
			{
				int i = glyphIndex++;

				if (i >= visibleGlyphCount)
				{
					var vertices = glyph.vertices;

					for (int j = 0; j < vertices.Length; j++)
					{
						var vertex = vertices[j];
						var tint = vertex.tint;
						tint.a = 0;
						vertex.tint = tint;
						vertices[j] = vertex;
					}
					
					continue;
				}
				
				foreach (var effect in currentParsedLine.effects)
				{
					if (i < effect.startIndex || i >= effect.endIndex)
						continue;
					
					switch (effect.effectType)
					{
						case EffectType.WAVEY:
							ApplyWave(glyph, i, effect.parameters);
							break;
						case EffectType.WOBBLY:
							ApplyWobble(glyph, i, effect.parameters);
							break;
					}
				}
			}
		}

		private void ApplyWave(TextElement.Glyph _glyph, int _glyphIndex, List<EffectParameter> _effectParameters)
		{
			const float AMPLITUDE = 3f;
			const float SPEED = 5f;
			const float PHASE_PER_GLYPH = 0.5f;

			float amplitude = AMPLITUDE;
			float speed = SPEED;
			float phase = PHASE_PER_GLYPH;
			
			
			foreach (var param in _effectParameters)
			{
				switch (param.label)
				{
					case "amplitude":
						amplitude = param.value;
						break;
					case "speed":
						speed = param.value;
						break;
					case "phase":
						phase = param.value;
						break;
				}
			}

			float offsetY = Mathf.Sin(Time.unscaledTime * speed + _glyphIndex * phase) * amplitude;
			
			var vertices = _glyph.vertices;
			for (int i = 0; i < vertices.Length; i++)
			{
				var vertex = vertices[i];
				vertex.position += new Vector3(0f, offsetY, 0f);
				vertices[i] = vertex;
			}
		}

		private void ApplyWobble(TextElement.Glyph _glyph, int _glyphIndex, List<EffectParameter> _effectParameters)
		{
			const float AMPLITUDE = 3f;
			const float SPEED = 12f;
			const float PHASE_PER_GLYPH = 0.73f;

			const float MOVE_AMPLITUDE = 0.15f;
			const float MOVE_SPEED = 12f;
			const float MOVE_PHASE_PER_GLYPH = 0.43f;
			
			const float Y_MOVE_SPEED_MULT = 0.63f;
			const float X_MOVE_PHASE_OFFSET = 0.183f;

			float amplitude = AMPLITUDE;
			float speed = SPEED;
			float phase = PHASE_PER_GLYPH;

			float moveAmplitude = MOVE_AMPLITUDE;
			float moveSpeed = MOVE_SPEED;
			float movePhase = MOVE_PHASE_PER_GLYPH;
			
			foreach (var param in _effectParameters)
			{
				switch (param.label)
				{
					case "amplitude":
						amplitude = param.value;
						break;
					case "speed":
						speed = param.value;
						break;
					case "phase":
						phase = param.value;
						break;
					
					case "moveAmplitude":
						moveAmplitude = param.value;
						break;
					case "moveSpeed":
						moveSpeed = param.value;
						break;
					case "movePhase":
						movePhase = param.value;
						break;
				}
			}
			
			var vertices = _glyph.vertices;
			
			Vector3 center = Vector3.zero;
			for (int i = 0; i < vertices.Length; i++)
				center += vertices[i].position;
			center /= vertices.Length;

			float angle = Mathf.Sin(Time.unscaledTime * speed + _glyphIndex * phase) * amplitude;
			float offsetY = Mathf.Cos(Time.unscaledTime * (moveSpeed * Y_MOVE_SPEED_MULT) + _glyphIndex * (movePhase)) * moveAmplitude;
			float offsetX = Mathf.Sin(Time.unscaledTime * moveSpeed + _glyphIndex * (movePhase + X_MOVE_PHASE_OFFSET)) * moveAmplitude;

			Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

			for (int i = 0; i < vertices.Length; i++)
			{
				var vertex = vertices[i];
				vertex.position = center + rotation * (vertex.position - center);
				vertex.position += new Vector3(offsetX, offsetY, 0f);
				vertices[i] = vertex;
			}
		}

	}
}
