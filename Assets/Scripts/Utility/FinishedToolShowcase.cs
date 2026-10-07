using UnityEngine;

public class FinishedToolShowcase : MonoBehaviour
{
	[SerializeField] Light spotLight;
	[SerializeField] Light directionalLight;
	
	private ToolSlot[] toolSlots;
	private bool shouldRotate = false;

	void Update()
	{
		if (!shouldRotate)
			return;

		float rotStep = 15f * Time.unscaledDeltaTime;
		transform.Rotate(Vector3.up, rotStep, Space.World);
	}
	
	public void BeginToolShowcase(HeatableMetal[] _finishedMetals, PartTableLayout _partLayout)
	{
		EnsureSockets(_finishedMetals, _partLayout);

		shouldRotate = true;

		spotLight.enabled = true;
		directionalLight.intensity = 0f;
		
		if (toolSlots == null || _finishedMetals == null || toolSlots.Length != _finishedMetals.Length)
			return;

		for (int i = 0; i < toolSlots.Length; i++)
		{
			toolSlots[i].containedPart = _finishedMetals[i];
			_finishedMetals[i].transform.SetParent(toolSlots[i].slotSocket, false);
			if (_finishedMetals[i].TryGetComponent(out Highlightable highlight))
				highlight.SetHighlighted(true);
		}
	}
	
	private void EnsureSockets(HeatableMetal[] _finishedMetals, PartTableLayout _partLayout)
	{
		if (toolSlots != null && toolSlots.Length == 0)
			return;

		PartToolSlot[] partToolSlots = _partLayout.GetPartToolSlots();
		
		if (_finishedMetals.Length != partToolSlots.Length)
			return;
		
		toolSlots = new ToolSlot[partToolSlots.Length];
		
		for (int i = 0; i < partToolSlots.Length; i++)
		{
			var socketGo = new GameObject($"Socket ({i})");
			toolSlots[i].slotSocket = socketGo.transform;
			toolSlots[i].slotSocket.SetParent(transform, false);
			toolSlots[i].slotSocket.localPosition = partToolSlots[i].positionOffset;
			toolSlots[i].slotSocket.localRotation = Quaternion.Euler(partToolSlots[i].rotationOffset);
			
			toolSlots[i].containedPart = null;
		}
	}

	private struct ToolSlot
	{
		public Transform slotSocket;
		public HeatableMetal containedPart;
	}
}
