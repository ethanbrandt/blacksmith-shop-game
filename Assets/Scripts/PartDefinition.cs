using System.Collections.Generic;
using UnityEngine;

namespace ForgingPrototype
{
	/// <summary>
	/// Authorable part: world quality sprites + forging target outline.
	/// Create via Assets → Create → Forging Prototype → Part Definition.
	/// </summary>
	[CreateAssetMenu(fileName = "PartDefinition", menuName = "Forging Prototype/Part Definition", order = 2)]
	public class PartDefinition : ScriptableObject
	{
		[Header("Identity")]
		public string displayName = "Part";

		[Header("World Sprites")]
		[Tooltip("Shown when Incomplete / not forged to a quality tier.")]
		public Sprite unforgedSprite;
		[Tooltip("Index 0=Flawed, 1=Good, 2=Excellent, 3=Perfect")]
		public Sprite[] qualitySprites = new Sprite[4];

		[Header("Forge Outline")]
		[Tooltip("Outline vertices in local part space (relative to outline origin).")]
		public Vector2[] outlineLocal = System.Array.Empty<Vector2>();
		[Tooltip("Added to anvil center when placing this outline in the forge.")]
		public Vector2 outlineCenterOffset = new Vector2(0f, 0.15f);

		[Header("Optional Defaults")]
		public MetalType defaultMetalType;

		public string DisplayLabel => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

		public bool HasValidOutline => outlineLocal != null && outlineLocal.Length >= 3;

		public Sprite GetSpriteForQuality(ShapeQuality quality)
		{
			if (quality == ShapeQuality.Incomplete)
				return unforgedSprite;

			int index = (int)quality - 1;
			if (qualitySprites != null && index >= 0 && index < qualitySprites.Length && qualitySprites[index] != null)
				return qualitySprites[index];

			return unforgedSprite;
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
				return;
			}

			outlineLocal = new Vector2[points.Count];
			for (int i = 0; i < points.Count; i++)
				outlineLocal[i] = points[i];
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

			string[] labels = { "Flawed", "Good", "Excellent", "Perfect" };
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
				message = "Forge outline needs at least 3 vertices.";
				return false;
			}

			message = "OK";
			return true;
		}

		public static Vector2[] CreateDefaultSpearOutlineLocal()
		{
			return ShapeMatchEvaluator.CreateSpearheadTarget(Vector2.zero);
		}

		public static Vector2[] CreateDefaultAxeOutlineLocal()
		{
			return ShapeMatchEvaluator.CreateAxeHeadTarget(Vector2.zero);
		}
	}
}
