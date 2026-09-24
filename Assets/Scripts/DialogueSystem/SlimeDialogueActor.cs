using UnityEngine;

namespace DialogueSystem
{
	public class SlimeDialogueActor : DialogueActor
	{
		private SpriteRenderer spriteRenderer;

		void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
		}

		public override void SetPose(string _poseName)
		{
			switch (_poseName)
			{
				case "triumph":
					break;
				default:
					Debug.LogWarning($"{_poseName} is not a valid pose for {gameObject.name}");
					break;
			}
		}
	}
}
