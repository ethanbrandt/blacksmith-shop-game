using System;
using ForgingPrototype;
using UnityEngine;

[Serializable]
public struct PartTableSlot
{
	public PartDefinition part;
	public Vector2 positionOffset;
}

[CreateAssetMenu(fileName = "MetalType", menuName = "Forging Prototype/Part Table Layout", order = 3)]
public class PartTableLayout : ScriptableObject
{
	[SerializeField] PartTableSlot[] partTableSlots;

	public PartTableSlot[] GetPartTableSlots()
	{
		return partTableSlots;
	}
}
