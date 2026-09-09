using System.Collections.Generic;

namespace DialogueSystem
{
	public struct EffectRange
	{
		public EffectType effectType;
		public int startIndex;
		public int endIndex;
		public List<EffectParameter> parameters;
	}

	public struct EffectParameter
	{
		public string label;
		public float value;
	}

	public enum EffectType
	{
		NONE,
		WAVEY,
		WOBBLY
	}

	public struct PauseCue
	{
		public int pauseIndex;
		public float duration;
	}

	public struct ParsedLine
	{
		public string displayText;
		public List<EffectRange> effects;
		public List<PauseCue> pauses;
	}
}
