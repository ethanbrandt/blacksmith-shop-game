using UnityEngine;
using UnityEngine.UI;

public class SlimedUI : MonoBehaviour
{
	[SerializeField] Image slimedImage;

	private PlayerController player;
	private Canvas slimedCanvas;
	
    void Start()
    {
	    slimedCanvas = GetComponent<Canvas>();
	    player = FindFirstObjectByType<PlayerController>();
    }

    void Update()
    {
	    if (player.SlimedPercentLeft > 0.01f)
	    {
		    if (!slimedCanvas.enabled)
			    slimedCanvas.enabled = true;

		    slimedImage.fillAmount = player.SlimedPercentLeft;
		    transform.position = new Vector3(player.transform.position.x, transform.position.y, player.transform.position.z);
	    }
	    else if (slimedCanvas.enabled)
	    {
			slimedCanvas.enabled = false;
			slimedImage.fillAmount = 0f;
	    }
    }
}
