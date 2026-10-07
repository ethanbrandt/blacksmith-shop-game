using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

public class PartAutoCompleter : MonoBehaviour
{
	const float GrindTransitionFraction = 0.01f;
	static readonly FieldInfo PickableTypeField = typeof(Pickable).GetField("pickableType", BindingFlags.Instance | BindingFlags.NonPublic);

	[SerializeField] PlayerController player;
	[SerializeField] Key completionKey = Key.F8;
	[SerializeField, Min(0.05f)] float idealGrindAmount = 1f;

	readonly List<Vector2> groundVertices = new List<Vector2>();
	readonly List<float> grindAmounts = new List<float>();

	void Awake()
	{
		if (player == null)
			player = GetComponent<PlayerController>();
		if (player == null)
			player = FindFirstObjectByType<PlayerController>();
	}

	void Update()
	{
		if (Keyboard.current == null || completionKey == Key.None)
			return;
		if (Keyboard.current[completionKey].wasPressedThisFrame)
			CompleteHeldPart();
	}

	[ContextMenu("Complete Held Part")]
	public void CompleteHeldPart()
	{
		if (!Application.isPlaying || StationSessionCoordinator.IsActive)
		{
			Debug.LogWarning("Complete parts in Play mode after exiting the station view.", this);
			return;
		}

		if (player == null || player.Held == null || !player.Held.TryGetComponent(out HeatableMetal metal))
		{
			Debug.LogWarning("Hold a metal part before completing it.", this);
			return;
		}

		PartDefinition part = metal.PartDefinition;
		if (part == null || metal.MetalType == null || !part.HasValidOutline)
		{
			Debug.LogError("The held metal needs a metal type and a valid part outline.", metal);
			return;
		}

		if (PickableTypeField == null)
		{
			Debug.LogError("The test helper could not find Pickable's quench state field.", this);
			return;
		}

		if (part.isBladed && !part.HasSharpeningTargets)
		{
			Debug.LogError("The bladed part needs at least one edge marked for sharpening.", part);
			return;
		}

		if (part.isBladed)
		{
			BuildPerfectGrind(part);
			if (!PolygonGeometry.IsSimple(groundVertices))
			{
				Debug.LogError("The part outline is too small to add grind transitions.", part);
				return;
			}
		}

		metal.Quench();
		if (!metal.TryCommitShapeVertices(part.outlineLocal))
			return;
		PickableTypeField.SetValue(metal.Pickable, Pickable.PickableType.QUENCHED_METAL);

		if (part.isBladed)
		{
			float sharpAmount = Mathf.Max(0.05f, idealGrindAmount);
			metal.SaveGrindProgress(groundVertices, grindAmounts, SharpnessQuality.Keen, 1f, sharpAmount);
		}

		Debug.Log($"Completed {part.DisplayLabel}: perfect forging, quenched, and any required grinding complete.", metal);
	}

	void BuildPerfectGrind(PartDefinition part)
	{
		groundVertices.Clear();
		grindAmounts.Clear();
		float sharpAmount = Mathf.Max(0.05f, idealGrindAmount);
		int vertexCount = part.outlineLocal.Length;

		for (int i = 0; i < vertexCount; i++)
		{
			int previousIndex = (i + vertexCount - 1) % vertexCount;
			int nextIndex = (i + 1) % vertexCount;
			Vector2 edgeStart = part.outlineLocal[i];
			Vector2 edgeEnd = part.outlineLocal[nextIndex];
			bool previousEdgeIsSharp = part.OutlineEdgeNeedsSharpening(previousIndex);
			bool currentEdgeIsSharp = part.OutlineEdgeNeedsSharpening(i);
			bool nextEdgeIsSharp = part.OutlineEdgeNeedsSharpening(nextIndex);
			float cornerAmount = previousEdgeIsSharp || currentEdgeIsSharp ? sharpAmount : 0f;
			AddGrindVertex(edgeStart, cornerAmount);

			if (currentEdgeIsSharp)
				continue;
			if (previousEdgeIsSharp)
				AddGrindVertex(Vector2.Lerp(edgeStart, edgeEnd, GrindTransitionFraction), 0f);
			if (nextEdgeIsSharp)
				AddGrindVertex(Vector2.Lerp(edgeStart, edgeEnd, 1f - GrindTransitionFraction), 0f);
		}
	}

	void AddGrindVertex(Vector2 vertex, float amount)
	{
		groundVertices.Add(vertex);
		grindAmounts.Add(amount);
	}
}
