using ForgingPrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GrindstoneSetup
{
	[MenuItem("Forging/Create Grindstone Station")]
	public static void CreateGrindstoneStation()
	{
		var existing = Object.FindFirstObjectByType<Grindstone>();
		if (existing != null)
		{
			Selection.activeGameObject = existing.gameObject;
			EditorGUIUtility.PingObject(existing.gameObject);
			Debug.Log("Grindstone station already exists in the scene.");
			return;
		}

		var go = new GameObject("Grindstone");
		Undo.RegisterCreatedObjectUndo(go, "Create Grindstone");
		go.transform.position = new Vector3(6.2f, 0f, 3.9f);
		go.AddComponent<Grindstone>();

		var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Undo.RegisterCreatedObjectUndo(visual, "Create Grindstone Visual");
		visual.name = "Visual";
		visual.transform.SetParent(go.transform, false);
		visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
		visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
		Object.DestroyImmediate(visual.GetComponent<Collider>());
		var renderer = visual.GetComponent<MeshRenderer>();
		if (renderer != null)
			renderer.sharedMaterial = ForgingVisualUtility.CreateColorMaterial(new Color(0.55f, 0.65f, 0.75f, 1f));

		GrindSessionController.EnsureExists();
		var grindSession = Object.FindFirstObjectByType<GrindSessionController>();
		if (grindSession != null)
			GrindRigBuilder.Build(grindSession);
		Selection.activeGameObject = go;
		EditorSceneManager.MarkSceneDirty(go.scene);
		Debug.Log("Created Grindstone station. Place quenched bladed parts on it to open the grind minigame.");
	}

	[MenuItem("Forging/Seed Bladed Defaults On Selected PartDefinition")]
	public static void SeedBladedDefaults()
	{
		var part = Selection.activeObject as PartDefinition;
		if (part == null)
		{
			Debug.LogWarning("Select a PartDefinition asset first.");
			return;
		}

		Undo.RecordObject(part, "Seed Bladed Grind Defaults");
		part.isBladed = true;
		if (part.outlineLocal == null || part.outlineLocal.Length < 3)
		{
			Debug.LogError("[GRIND SETUP] Invalid part outline");
			return;
		}
		
		part.EnsureSharpeningFlagsMatchOutline();
		if (!part.HasSharpeningTargets)
		{
			// Default: mark the rightmost third of outline edges as the cutting edge.
			int n = part.OutlineEdgeCount;
			var scored = new (int index, float x)[n];
			for (int i = 0; i < n; i++)
			{
				int next = (i + 1) % part.outlineLocal.Length;
				Vector2 mid = (part.outlineLocal[i] + part.outlineLocal[next]) * 0.5f;
				scored[i] = (i, mid.x);
			}

			System.Array.Sort(scored, (a, b) => b.x.CompareTo(a.x));
			int markCount = Mathf.Max(2, n / 3);
			for (int i = 0; i < markCount; i++)
				part.outlineEdgeNeedsSharpening[scored[i].index] = true;
		}

		EditorUtility.SetDirty(part);
		AssetDatabase.SaveAssets();
		Debug.Log($"Seeded bladed grind defaults on '{part.DisplayLabel}' ({CountFlags(part)} sharpening edges).");
	}

	static int CountFlags(PartDefinition part)
	{
		int count = 0;
		if (part.outlineEdgeNeedsSharpening == null)
			return 0;
		for (int i = 0; i < part.outlineEdgeNeedsSharpening.Length; i++)
		{
			if (part.outlineEdgeNeedsSharpening[i])
				count++;
		}

		return count;
	}
}
