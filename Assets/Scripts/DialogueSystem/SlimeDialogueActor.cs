using UnityEngine;

namespace DialogueSystem
{
	public class SlimeDialogueActor : DialogueActor
	{
		[SerializeField] Sprite neutralSprite;
		[SerializeField] Sprite happySprite;
		[SerializeField] Sprite sadSprite;
		[SerializeField] Sprite strongSprite;
		
		private SpriteRenderer spriteRenderer;

		void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
		}

		public override void SetPose(string _poseName)
		{
			switch (_poseName)
			{
				case "neutral":
					spriteRenderer.sprite = neutralSprite;
					break;
				case "happy":
					spriteRenderer.sprite = happySprite;
					break;
				case "sad":
					spriteRenderer.sprite = sadSprite;
					break;
				case "strong":
					spriteRenderer.sprite = strongSprite;
					break;
				default:
					Debug.LogWarning($"{_poseName} is not a valid pose for {gameObject.name}");
					break;
			}
		}
	}
}
