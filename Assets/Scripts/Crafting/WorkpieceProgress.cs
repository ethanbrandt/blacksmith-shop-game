using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Serializable station progress. Vertices are relative to the forge origin, including the authored outline offset.</summary>
[Serializable]
public class WorkpieceProgress
{
	public const int CurrentVersion = 1;

	public int version = CurrentVersion;
	public PartDefinition part;

	public List<Vector2> forgedVertices = new List<Vector2>();
	public ShapeQuality forgeQuality;
	public float forgeMatch;

	public List<Vector2> groundVertices = new List<Vector2>();
	public List<float> grindAmounts = new List<float>();
	public SharpnessQuality sharpness;
	public float grindMatch;
}
