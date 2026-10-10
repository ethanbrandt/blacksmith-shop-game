using System.Collections.Generic;

public enum HatchetTutorialStep
{
    PickUpMetal,
    FuelFurnace,
    HeatMetal,
    ForgeShape,
    Quench,
    Sharpen,
    Assemble,
    Complete
}

// Follow the part the player is working on, remembering station visits per part.
// Sampling after gameplay updates also handles thrown parts and repeated heating.
public sealed class HatchetTutorialProgress
{
    readonly HashSet<HeatableMetal> forgedParts = new HashSet<HeatableMetal>();
    readonly HashSet<HeatableMetal> groundParts = new HashSet<HeatableMetal>();
    HeatableMetal currentPart;
    HeatableMetal previousForgePart;
    HeatableMetal previousGrindPart;

    public HatchetTutorialStep Update(IReadOnlyList<HeatableMetal> parts, HeatableMetal heldPart,
        HeatableMetal forgePart, HeatableMetal grindPart, bool roundEnded)
    {
        RememberClosedSession(previousForgePart, forgePart, forgedParts);
        RememberClosedSession(previousGrindPart, grindPart, groundParts);
        previousForgePart = forgePart;
        previousGrindPart = grindPart;

        if (roundEnded || AllPartsPlaced(parts))
            return HatchetTutorialStep.Complete;

        if (IsRoundPart(parts, forgePart))
        {
            currentPart = forgePart;
            return HatchetTutorialStep.ForgeShape;
        }
        if (IsRoundPart(parts, grindPart))
        {
            currentPart = grindPart;
            return HatchetTutorialStep.Sharpen;
        }

        if (IsRoundPart(parts, heldPart) && !heldPart.Pickable.IsOnFinalTable)
            currentPart = heldPart;

        if (currentPart == null || currentPart.Pickable.IsOnFinalTable)
            currentPart = FindPartInStation(parts);

        if (currentPart == null)
            return HatchetTutorialStep.PickUpMetal;

        Pickable pickable = currentPart.Pickable;
        if (pickable.Type == Pickable.PickableType.QUENCHED_METAL)
        {
            bool needsGrinding = currentPart.PartDefinition != null && currentPart.PartDefinition.isBladed
                && !groundParts.Contains(currentPart) && !currentPart.HasGrindProgress;
            return needsGrinding ? HatchetTutorialStep.Sharpen : HatchetTutorialStep.Assemble;
        }

        if (pickable.ContainingStation is Furnace furnace)
            return furnace.HasFuel ? HatchetTutorialStep.HeatMetal : HatchetTutorialStep.FuelFurnace;

        // A closed forge leaves the metal on the anvil. Ask the player to retrieve it
        // before returning to heating if it has cooled below workability.
        if (pickable.ContainingStation is Anvil)
        {
            forgedParts.Add(currentPart);
            return HatchetTutorialStep.Quench;
        }

        if (pickable.State == Pickable.PickableState.FREE)
            return HatchetTutorialStep.PickUpMetal;

        if (!currentPart.IsWorkable)
            return HatchetTutorialStep.FuelFurnace;

        return forgedParts.Contains(currentPart) ? HatchetTutorialStep.Quench : HatchetTutorialStep.ForgeShape;
    }

    static void RememberClosedSession(HeatableMetal previous, HeatableMetal active, HashSet<HeatableMetal> completed)
    {
        if (previous != null && previous != active)
            completed.Add(previous);
    }

    static bool IsRoundPart(IReadOnlyList<HeatableMetal> parts, HeatableMetal part)
    {
        if (part == null || parts == null)
            return false;
        for (int i = 0; i < parts.Count; i++)
            if (parts[i] == part)
                return true;
        return false;
    }

    static HeatableMetal FindPartInStation(IReadOnlyList<HeatableMetal> parts)
    {
        if (parts == null)
            return null;
        for (int i = 0; i < parts.Count; i++)
        {
            HeatableMetal part = parts[i];
            if (part != null && part.Pickable != null && !part.Pickable.IsOnFinalTable && part.Pickable.InStation)
                return part;
        }
        return null;
    }

    static bool AllPartsPlaced(IReadOnlyList<HeatableMetal> parts)
    {
        if (parts == null || parts.Count == 0)
            return false;
        for (int i = 0; i < parts.Count; i++)
            if (parts[i] == null || parts[i].Pickable == null || !parts[i].Pickable.IsOnFinalTable)
                return false;
        return true;
    }
}
