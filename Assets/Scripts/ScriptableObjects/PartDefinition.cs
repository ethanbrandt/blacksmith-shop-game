using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PartDefinition", menuName = "Forging/PartDefinition", order = 2)]
public class PartDefinition : ScriptableObject
{
	[Header("Identity")]
	public string displayName = "Part";
	[Tooltip("Bladed parts can use the grindstone after quench.")]
	public bool isBladed;

	[Header("World Sprites")]
	[Tooltip("Shown when Incomplete / not forged to a quality tier.")]
	public Sprite unforgedSprite;
	[Tooltip("Index 0=Flawed, 1=Good, 2=Excellent, 3=Perfect")]
	public Sprite[] qualitySprites = new Sprite[4];
	public Sprite partSocketSprite;

	[Header("Forge Outline")]
	[Tooltip("Outline vertices in local part space (relative to outline origin).")]
	public Vector2[] outlineLocal = System.Array.Empty<Vector2>();
	[Tooltip("Added to anvil center when placing this outline in the forge.")]
	public Vector2 outlineCenterOffset = new Vector2(0f, 0.15f);
	[Tooltip("One flag per outline edge. Edge i runs from outlineLocal[i] to outlineLocal[(i+1) % count].")]
	public bool[] outlineEdgeNeedsSharpening = System.Array.Empty<bool>();

	[Header("Optional Defaults")]
	public MetalType defaultMetalType;

	public string DisplayLabel => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
	public bool HasValidOutline => PolygonGeometry.IsSimple(outlineLocal);
	public int OutlineEdgeCount => HasValidOutline ? outlineLocal.Length : 0;

	public bool HasSharpeningTargets
	{
		get
		{
			if (!HasValidOutline || outlineEdgeNeedsSharpening == null)
				return false;

			for (int i = 0; i < OutlineEdgeCount; i++)
			{
				if (OutlineEdgeNeedsSharpening(i))
					return true;
			}

			return false;
		}
	}

	public bool OutlineEdgeNeedsSharpening(int edgeIndex)
	{
		return outlineEdgeNeedsSharpening != null && edgeIndex >= 0 && edgeIndex < outlineEdgeNeedsSharpening.Length && outlineEdgeNeedsSharpening[edgeIndex];
	}

	public Sprite GetSpriteForQuality(ShapeQuality quality)
	{
		if (quality == ShapeQuality.Incomplete)
			return unforgedSprite;

		int index = (int)quality - 1;
		if (qualitySprites != null && index >= 0 && index < qualitySprites.Length && qualitySprites[index] != null)
			return qualitySprites[index];

		return unforgedSprite;
	}

	public Vector2[] BuildForgeOutline(Vector2 forgeOrigin)
	{
		return BuildWorldOutline(forgeOrigin);
	}

	public Vector2[] BuildWorldOutline(Vector2 anvilCenter)
	{
		if (!HasValidOutline)
			return System.Array.Empty<Vector2>();

		Vector2 origin = anvilCenter + outlineCenterOffset;
		var world = new Vector2[outlineLocal.Length];
		for (int i = 0; i < outlineLocal.Length; i++)
			world[i] = origin + outlineLocal[i];
		return world;
	}

	public void SetOutlineLocal(IReadOnlyList<Vector2> points)
	{
		if (points == null || points.Count < 3)
		{
			outlineLocal = System.Array.Empty<Vector2>();
			outlineEdgeNeedsSharpening = System.Array.Empty<bool>();
			return;
		}

		var oldEdgeFlags = outlineEdgeNeedsSharpening;
		outlineLocal = new Vector2[points.Count];
		outlineEdgeNeedsSharpening = new bool[points.Count];
		for (int i = 0; i < points.Count; i++)
		{
			outlineLocal[i] = points[i];
			if (oldEdgeFlags != null && i < oldEdgeFlags.Length)
				outlineEdgeNeedsSharpening[i] = oldEdgeFlags[i];
		}
	}

	public void EnsureSharpeningFlagsMatchOutline()
	{
		int edgeCount = OutlineEdgeCount;
		if (edgeCount <= 0)
		{
			outlineEdgeNeedsSharpening = System.Array.Empty<bool>();
			return;
		}

		if (outlineEdgeNeedsSharpening != null && outlineEdgeNeedsSharpening.Length == edgeCount)
			return;

		var resized = new bool[edgeCount];
		if (outlineEdgeNeedsSharpening != null)
		{
			int copy = Mathf.Min(edgeCount, outlineEdgeNeedsSharpening.Length);
			for (int i = 0; i < copy; i++)
				resized[i] = outlineEdgeNeedsSharpening[i];
		}

		outlineEdgeNeedsSharpening = resized;
	}

	public bool Validate(out string message)
	{
		if (string.IsNullOrWhiteSpace(displayName))
		{
			message = "Display name is empty.";
			return false;
		}

		if (unforgedSprite == null)
		{
			message = "Unforged sprite is missing.";
			return false;
		}

		if (qualitySprites == null || qualitySprites.Length < 4)
		{
			message = "Quality sprites need 4 slots (Flawed, Good, Excellent, Perfect).";
			return false;
		}

		string[] labels =
		{
			"Flawed",
			"Good",
			"Excellent",
			"Perfect"
		};
		for (int i = 0; i < 4; i++)
		{
			if (qualitySprites[i] == null)
			{
				message = $"Quality sprite missing: {labels[i]}.";
				return false;
			}
		}

		if (!HasValidOutline)
		{
			message = "Forge outline must be a non-degenerate simple polygon with at least 3 vertices.";
			return false;
		}

		EnsureSharpeningFlagsMatchOutline();
		if (isBladed && !HasSharpeningTargets)
		{
			message = "Bladed parts need at least one outline edge marked for sharpening.";
			return false;
		}

		message = "OK";
		return true;
	}
}
