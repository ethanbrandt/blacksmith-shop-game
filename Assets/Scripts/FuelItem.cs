using UnityEngine;

[RequireComponent(typeof(Pickable))]
public class FuelItem : MonoBehaviour
{
	[Header("Fuel")]
	[SerializeField] string displayName = "Coal";
	[SerializeField] float burnValue = 25f;
	[Tooltip("If true, leftover burn value is discarded when the furnace cannot accept the full amount.")]
	[SerializeField] bool consumeEvenIfPartial = true;

	public string DisplayName => displayName;
	public float BurnValue => burnValue;
	public bool ConsumeEvenIfPartial => consumeEvenIfPartial;
}
