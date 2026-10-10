using UnityEngine;

namespace DialogueSystem
{
	public class EthanDialogueActor : DialogueActor
	{
		[SerializeField] Sprite explainSprite;
		[SerializeField] Sprite fingerSprite;
		[SerializeField] Sprite sadSprite;
		[SerializeField] Sprite shrugSprite;
		[SerializeField] Sprite thumbsUpSprite;

		private SpriteRenderer spriteRenderer;

		void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
		}

		public override void SetPose(string _poseName)
		{
			switch (_poseName)
			{
				case "explain":
					spriteRenderer.sprite = explainSprite;
					break;
				case "finger":
					spriteRenderer.sprite = fingerSprite;
					break;
				case "sad":
					spriteRenderer.sprite = sadSprite;
					break;
				case "shrug":
					spriteRenderer.sprite = shrugSprite;
					break;
				case "thumbsUp":
					spriteRenderer.sprite = thumbsUpSprite;
					break;
				default:
					Debug.LogWarning($"{_poseName} is not a valid pose for {gameObject.name}");
					break;
			}
		}
	}
}
