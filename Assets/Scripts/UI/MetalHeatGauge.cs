using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MetalHeatGauge : MonoBehaviour
{
    [SerializeField] RectTransform fillRect;
    [SerializeField] Image regionPrefab;

    private Slider heatGaugeSlider;
    private Canvas canvas;
    private HeatableMetal currentMetal;
    private bool gaugeEnabled;
    private Vector3 localOffset;
    private Transform attachedTransform;
    private List<Image> regionImages = new List<Image>();
    
    void Awake()
    {
        canvas = GetComponent<Canvas>();
        heatGaugeSlider = GetComponentInChildren<Slider>();
        localOffset = transform.localPosition;
        attachedTransform = transform.parent;

        canvas.enabled = false;
    }

    void Update()
    {
        if (!canvas.enabled)
            return;

        heatGaugeSlider.value = currentMetal.Heat01;
        transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        transform.position = attachedTransform.position + localOffset;
    }
    
    public void SetFollowMetal(HeatableMetal _metal)
    {
        currentMetal = _metal;

        foreach (var image in regionImages)
	        Destroy(image.gameObject);

        regionImages.Clear();
        
        MetalType metalType = _metal.MetalType;
		BuildRegions(metalType);
    }

    private void BuildRegions(MetalType _metalType)
    {
	    if (_metalType == null)
		    return;
	    
	    List<HeatGaugeRegion> heatGaugeRegions = _metalType.GetHeatGaugeRegions();
        
	    for (int i = 0; i < _metalType.heatGaugeRegions.Count; i++)
	    {
		    HeatGaugeRegion region = heatGaugeRegions[i];

		    float start = Mathf.Clamp01(region.startHeat);
		    float end = i + 1 < heatGaugeRegions.Count ? Mathf.Clamp01(heatGaugeRegions[i + 1].startHeat) : 1f;
	        
		    if (end <= start)
			    continue;

		    Image image = Instantiate(regionPrefab, fillRect);
		    image.color = region.regionColor;
		    image.raycastTarget = false;

		    RectTransform rect = image.rectTransform;
		    rect.anchorMin = new Vector2(start, 0f);
		    rect.anchorMax = new Vector2(end, 1f);
		    rect.offsetMin = Vector2.zero;
		    rect.offsetMax = Vector2.zero;
	        
		    regionImages.Add(image);
	    }    
    }

    public void SetEnable(bool _enabled)
    {
        canvas.enabled = _enabled;
    }
}
