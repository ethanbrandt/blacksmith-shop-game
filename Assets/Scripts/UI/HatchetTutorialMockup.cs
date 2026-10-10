using UnityEngine.UIElements;

// Presentation samples only. No gameplay events advance these steps.
public enum TutorialPreviewStep
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

public static class HatchetTutorialMockup
{
    public static void ShowForge(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "04 / 08", "Shape the hot iron",
            "Left stick moves the hammer; right stick aims. Hold RB, then release to strike. Work toward the outline. Press B to finish.",
            "RB: charge / strike | B: finish", "Explain", "");
    }

    public static void ShowGrind(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "06 / 08", "Sharpen the axe head",
            "Left stick moves the axe head; right stick left/right rotates it. Work the highlighted cutting edge against the wheel. Press B to finish.",
            "B: finish grinding", "Inquisitive", "");
    }

    public static void ShowWelcome(VisualElement panel, bool visible)
    {
        SetPanel(panel, visible, "WELCOME", "Let's make your first hatchet.",
            "We need an axe head and a rod. I'll walk you through heating, forging, quenching and sharpening. Take your time!",
            "I'll be here every step of the way.", "Explain", "");
    }

    public static void ShowWorkshop(VisualElement panel, bool visible, TutorialPreviewStep step)
    {
        switch (step)
        {
            case TutorialPreviewStep.FuelFurnace:
                SetPanel(panel, visible, "02 / 08", "Give the furnace some fuel",
                    "Place your iron in the furnace with Y. With empty hands, stand near the furnace and press X to add fuel.",
                    "Y: place iron  |  X: add fuel", "Explain", "X");
                break;
            case TutorialPreviewStep.HeatMetal:
                SetPanel(panel, visible, "03 / 08", "Warm up your iron",
                    "Press Y at the bellows to heat the iron faster. Watch the metal's heat gauge. Pick it up with Y in the Forgeable range, before it melts!",
                    "Y: pump bellows / retrieve iron", "Inquisitive", "Y");
                break;
            case TutorialPreviewStep.ForgeShape:
                SetPanel(panel, visible, "04 / 08", "Let's hammer out the shape",
                    "Take the hot iron to the anvil and press Y. Aim for the outline. If it cools, close the forge, pick it up and reheat it.",
                    "Y: place iron on the anvil", "Explain", "Y");
                break;
            case TutorialPreviewStep.Quench:
                SetPanel(panel, visible, "05 / 08", "Cool it in the quench vat",
                    "Pick up the part from the anvil with Y, then place it in the quench vat with Y. It must still be Forgeable; reheat it first if needed.",
                    "Y: pick up / quench", "Explain", "Y");
                break;
            case TutorialPreviewStep.Sharpen:
                SetPanel(panel, visible, "06 / 08", "Give the axe a sharp edge",
                    "Pick up the quenched axe head with Y, then press Y at the grindstone. The rod is ready for the assembly table after quenching.",
                    "Y: pick up / use grindstone", "Inquisitive", "Y");
                break;
            case TutorialPreviewStep.Assemble:
                SetPanel(panel, visible, "07 / 08", "Bring the parts together",
                    "Pick up the part from the vat or grindstone with Y. Place it on the table to the left with Y. Make and add the other part to finish.",
                    "Y: pick up / place finished part", "ThumbsUp", "Y");
                break;
            case TutorialPreviewStep.Complete:
                SetPanel(panel, visible, "08 / 08", "Your first hatchet. Nice work!",
                    "You made both parts and put them together. Once the score reveal finishes, press A to deliver your hatchet to the customer.",
                    "A: deliver when prompted", "ThumbsUp", "");
                break;
            default:
                SetPanel(panel, visible, "01 / 08", "Pick up a piece of iron",
                    "Walk over to an iron blank and press Y to pick it up. We need an axe head and a rod; you can make them in either order.",
                    "Left stick: move  |  Y: pick up", "Explain", "Y");
                break;
        }
    }

    private static void SetPanel(VisualElement panel, bool visible, string progress, string title,
        string text, string hint, string pose, string control)
    {
        if (panel == null)
            return;

        panel.EnableInClassList("is-hidden", !visible);
        if (!visible)
            return;

        panel.Q<Label>("TutorialProgress").text = progress;
        panel.Q<Label>("TutorialTitle").text = title;
        panel.Q<Label>("TutorialText").text = text;
        panel.Q<Label>("TutorialHint").text = hint;

        foreach (var image in panel.Query<Image>(className: "tutorial-portrait").ToList())
            image.EnableInClassList("is-hidden", image.name != "TutorialPortrait" + pose);
        foreach (var image in panel.Query<Image>(className: "tutorial-control").ToList())
            image.EnableInClassList("is-hidden", image.name != "TutorialControl" + control);
    }
}
