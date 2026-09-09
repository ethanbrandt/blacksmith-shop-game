using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueSystem
{
	public class DialogueManager : MonoBehaviour
	{
		[Header("Text Scroll")]
		[SerializeField] float scrollWaitTime;
		
		[TextArea(10, 12)]
		[SerializeField] string testText;
		
		private UIDocument document;
		
		private Label dialogueBoxLabel;
		private DialogueParser dialogueParser;
		private ParsedLine currentLine;

		private int totalGlyphCount;
		private int visibleGlyphCount;

		private float nextScrollTimer = 0f; 

		void Start()
		{
			document = GetComponentInChildren<UIDocument>();
			dialogueParser = new DialogueParser();

			dialogueBoxLabel = document.rootVisualElement.Q<Label>("DialogueBoxLabel");

			dialogueBoxLabel.PostProcessTextVertices += ApplyTextEffects;
			
			SetNewLine(testText);
		}

		void Update()
		{
			dialogueBoxLabel?.MarkDirtyRepaint();

			if (visibleGlyphCount >= totalGlyphCount)
				return;

			nextScrollTimer -= Time.unscaledDeltaTime;
			if (nextScrollTimer <= 0f)
			{
				visibleGlyphCount++;
				nextScrollTimer = scrollWaitTime;
			}
		}

		public void SetNewLine(string _line)
		{
			currentLine = dialogueParser.ParseDialogueLine(_line);
			dialogueBoxLabel.text = currentLine.displayText;

			totalGlyphCount = -1;
			visibleGlyphCount = 0;
			nextScrollTimer = 0f;
		}

		private void ApplyTextEffects(TextElement.GlyphsEnumerable _glyps)
		{
			if (string.IsNullOrEmpty(currentLine.displayText))
				return;

			if (totalGlyphCount < 0)
				totalGlyphCount = _glyps.Count;
			
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
				
				foreach (var effect in currentLine.effects)
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

		void OnDisable()
		{
			if (dialogueBoxLabel == null)
				return;

			dialogueBoxLabel.PostProcessTextVertices -= ApplyTextEffects;
			dialogueBoxLabel.MarkDirtyRepaint();
			dialogueBoxLabel = null;
		}
	}
}
