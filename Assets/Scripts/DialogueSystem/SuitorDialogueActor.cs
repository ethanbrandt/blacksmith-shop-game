using UnityEngine;

namespace DialogueSystem
{
    public class SuitorDialogueActor : DialogueActor
    {
        [SerializeField] Sprite triumphSprite;
        [SerializeField] Sprite defeatedSprite;
        [SerializeField] Sprite determinedSprite;
        [SerializeField] Sprite beggingSprite;

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
                    spriteRenderer.sprite = triumphSprite;
                    break;
                case "defeated":
                    spriteRenderer.sprite = defeatedSprite;
                    break;
                case "determined":
                    spriteRenderer.sprite = determinedSprite;
                    break;
                case "begging":
                    spriteRenderer.sprite = beggingSprite;
                    break;
                default:
                    Debug.LogWarning($"{_poseName} is not a valid pose for {gameObject.name}");
                    break;
            }
        }
    }
}