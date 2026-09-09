using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DialogueSystem
{
	public class DialogueParser
	{
		public ParsedLine ParseDialogueLine(string _rawLine)
		{
			if (string.IsNullOrEmpty(_rawLine))
				return new ParsedLine { displayText = "" };
			
			var text = new StringBuilder();
			var effects = new List<EffectRange>();
			var pauses = new List<PauseCue>();

			var openEffects = new Stack<int>();

			int glyphIndex = 0;
			
			for (int i = 0; i < _rawLine.Length;)
			{
				if (_rawLine[i] != '<')
				{
					char c = _rawLine[i];
					i++;
					text.Append(c);

					if (!char.IsWhiteSpace(c))
						glyphIndex++;
					
					continue;
				}

				int tagStart = i;
				int tagEnd = _rawLine.IndexOf('>', tagStart + 1);

				if (tagEnd < 0)
					Debug.LogError($"Tag starting at {tagStart} missing \'>\'");

				string tag = _rawLine.Substring(tagStart + 1, (tagEnd - tagStart) - 1).Trim();

				i = tagEnd + 1;

				bool isClosing = tag.StartsWith('/');

				string body = isClosing ? tag.Substring(1).Trim() : tag;

				int nameEnd = 0;
				while (nameEnd < body.Length && !char.IsWhiteSpace(body[nameEnd]) && body[nameEnd] != '=')
					nameEnd++;

				string name = body.Substring(0, nameEnd);
				string args = body.Substring(nameEnd).Trim();

				if (name == "pause")
				{
					if (isClosing || !args.StartsWith('='))
						Debug.LogError($"Tag starting at {tagStart} is an improperly formatted pause ( use <pause=seconds> )");

					float duration = ReadNumber(args.Substring(1).Trim(), tagStart);
					
					if (duration > 0f)
						pauses.Add(new PauseCue { pauseIndex = text.Length, duration = duration });
					
					continue;
				}

				EffectType type = EffectType.NONE;
				switch (name)
				{
					case "wave":
						type = EffectType.WAVEY;
						break;
					case "wobble":
						type = EffectType.WOBBLY;
						break;
					default:
						text.Append(_rawLine, tagStart, i - tagStart);
						continue;
				}

				if (isClosing)
				{
					if (args.Length != 0 || openEffects.Count == 0)
						Debug.LogError($"Unexpected closing tag at {tagStart}");

					int effectIndex = openEffects.Peek();
					EffectRange effect = effects[effectIndex];

					if (effect.effectType != type)
						Debug.LogError($"Effects must close in nesting order {tagStart}");

					openEffects.Pop();
					effect.endIndex = glyphIndex;
					effects[effectIndex] = effect;
				}
				else
				{
					var parameters = new List<EffectParameter>();
					var paramNames = new HashSet<string>();

					string[] entries = args.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

					foreach (var entry in entries)
					{
						int equalsIndex = entry.IndexOf('=');
						if (equalsIndex <= 0 || equalsIndex == entries.Length - 1)
							Debug.LogError($"Invalid parameter at {tagStart} ( use parameter=value )");

						string label = entry.Substring(0, equalsIndex);
						string value = entry.Substring(equalsIndex + 1);
						
						if (!IsParameter(type, label))
							Debug.LogError($"Unknown parameter '{label}' at {tagStart}");
						
						if (!paramNames.Add(label))
							Debug.LogError($"Duplicate parameter '{label}' at {tagStart}");
						
						parameters.Add(new EffectParameter { label = label, value = ReadNumber(value, tagStart) });
					}
					
					openEffects.Push(effects.Count);
					effects.Add(new EffectRange { effectType = type, startIndex = glyphIndex, endIndex = -1, parameters = parameters });
				}
			}
			
			if (openEffects.Count > 0)
				Debug.LogError("Unclosed effect tag");

			return new ParsedLine { displayText = text.ToString(), effects = effects, pauses = pauses };
		}

		private bool IsParameter(EffectType _type, string _label)
		{
			switch (_label)
			{
				case "speed":
				case "amplitude":
				case "phase":
					return true;
				
				case "moveSpeed":
				case "moveAmplitude":
				case "movePhase":
					return _type == EffectType.WOBBLY;
					
				default:
					return false;
			}
		}
		
		private float ReadNumber(string _value, int _index)
		{
			if (!float.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out float num) || float.IsNaN(num) || float.IsInfinity(num))
			{
				Debug.LogError($"Invalid number '{_value}' at {_index}");
				return -1f;
			}
			
			return num;
		}
	}
}
