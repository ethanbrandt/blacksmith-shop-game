using UnityEngine;

public class Highlightable : MonoBehaviour
{
    private static readonly int HighlightedID = Shader.PropertyToID("_Highlighted");

    private Renderer[] renderers;
    private MaterialPropertyBlock propertyBlock;

    public bool highlight;
    private bool highlighted;
    
    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock = new MaterialPropertyBlock();
    }

    public void SetHighlighted(bool _highlighted)
    {
        highlighted = _highlighted;
        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(HighlightedID, _highlighted ? 1f : 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
