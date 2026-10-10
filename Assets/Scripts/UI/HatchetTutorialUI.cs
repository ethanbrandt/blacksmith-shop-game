using UnityEngine.UIElements;

// Display copy follows tutorial.md; gameplay progression lives in HatchetTutorialProgress.
public static class HatchetTutorialUI
{
    public static void ShowForge(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "04 / 07", "Shape the hot iron",
            "Left stick moves the hammer, right stick aims your strike. Hold Right Trigger, then release to strike. Now strike the outline into place.",
            "Explain");
    }

    public static void ShowGrind(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "06 / 07", "Sharpen the axe head",
            "Left stick moves the axe head, right stick rotates it. Work the highlighted cutting edge against the wheel.",
            "Inquisitive");
    }

    public static void ShowWelcome(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "", "Piece Overview",
            "Above is the piece overview. Pay attention to the heat gauge regions and material notes!",
            "Explain");
    }

    public static void ShowWorkshop(VisualElement panel, bool visible, HatchetTutorialStep step)
    {
        switch (step)
        {
            case HatchetTutorialStep.FuelFurnace:
                SetPanel(panel, visible, "02 / 07", "Give the furnace some fuel",
                    "Place your metal in the furnace with Y. Then with empty hands, stand near the furnace and press X to add fuel. Make sure to add a few to keep the heat up.",
                    "Explain");
                break;
            case HatchetTutorialStep.HeatMetal:
                SetPanel(panel, visible, "03 / 07", "Warm up your iron",
                    "Press Y at the bellows to heat metal faster. Watch the metal's heat gauge. Pick it up with Y in the Forgeable (white) range, before it melts!",
                    "Inquisitive");
                break;
            case HatchetTutorialStep.ForgeShape:
                SetPanel(panel, visible, "04 / 07", "Let's hammer out the shape",
                    "Take the hot iron to the anvil and press Y. Aim for the outline. If it cools, close the forge, pick it up and reheat it.",
                    "Explain");
                break;
            case HatchetTutorialStep.Quench:
                SetPanel(panel, visible, "05 / 07", "Cool it in the quench vat",
                    "Pick up the part from the anvil with Y, then place it in the quench vat with Y. It must still be Forgeable, reheat it first if needed.",
                    "Explain");
                break;
            case HatchetTutorialStep.Sharpen:
                SetPanel(panel, visible, "06 / 07", "Give the axe a sharp edge",
                    "Pick up the quenched axe head with Y, then press Y at the grindstone. The rod is ready for the assembly table after quenching.",
                    "Inquisitive");
                break;
            case HatchetTutorialStep.Assemble:
                SetPanel(panel, visible, "07 / 07", "Bring the parts together",
                    "Pick up the part from the vat or grindstone with Y. Place it on the table to the left with Y. Make and add the other part to finish.",
                    "ThumbsUp");
                break;
            case HatchetTutorialStep.Complete:
                panel?.EnableInClassList("is-hidden", true);
                break;
            default:
                SetPanel(panel, visible, "01 / 07", "Pick up a piece of iron",
                    "Walk over to an iron blank and press Y to pick it up.",
                    "Explain");
                break;
        }
    }

    private static void SetPanel(VisualElement panel, bool visible, string progress, string title,
        string text, string pose)
    {
        if (panel == null)
            return;

        panel.EnableInClassList("is-hidden", !visible);
        if (!visible)
            return;

        panel.Q<Label>("TutorialProgress").text = progress;
        panel.Q<Label>("TutorialProgress").EnableInClassList("is-hidden", string.IsNullOrEmpty(progress));
        panel.Q<Label>("TutorialTitle").text = title;
        panel.Q<Label>("TutorialText").text = text;

        foreach (var image in panel.Query<Image>(className: "tutorial-portrait").ToList())
            image.EnableInClassList("is-hidden", image.name != "TutorialPortrait" + pose);
    }
}
