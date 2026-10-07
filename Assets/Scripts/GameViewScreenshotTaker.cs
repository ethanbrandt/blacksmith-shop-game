using UnityEngine;
using UnityEngine.InputSystem;

public class GameViewScreenshotTaker : MonoBehaviour
{
	private void Update()
	{
		if (Keyboard.current?.f12Key.wasPressedThisFrame != true)
			return;

		string fileName = $"GameView-{System.DateTime.Now:yyyyMMMMdd-HHmmss-fff}.png";
		ScreenCapture.CaptureScreenshot(fileName);
		Debug.Log("SCREENSHOT TAKEN");
	}
}
