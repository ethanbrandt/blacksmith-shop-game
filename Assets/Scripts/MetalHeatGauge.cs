using UnityEngine;
using UnityEngine.UI;

public class MetalHeatGauge : MonoBehaviour
{
    [SerializeField] RectTransform quenchTempFill;
    [SerializeField] RectTransform tooColdFill;

    private Slider heatGaugeSlider;
    private Canvas canvas;
    private HeatableMetal currentMetal;
    private bool gaugeEnabled;
    private Vector3 localOffset;
    private Transform attachedTransform;
    
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

        MetalType metalType = _metal.MetalType;

        var sliderRectTransform = heatGaugeSlider.transform as RectTransform;
        
        float tooColdSize = Mathf.Lerp(0, sliderRectTransform.rect.width, metalType.minHeatToForge);
        tooColdFill.sizeDelta = new Vector2(tooColdSize, tooColdFill.sizeDelta.y);
        tooColdFill.anchoredPosition = new Vector2(tooColdSize / 2f, 0f);
        
        float quenchTempSize = Mathf.Lerp(0, sliderRectTransform.rect.width, 1f - metalType.minHeatToQuench);
        quenchTempFill.sizeDelta = new Vector2(quenchTempSize, quenchTempFill.sizeDelta.y);
        quenchTempFill.anchoredPosition = new Vector2(sliderRectTransform.rect.width - quenchTempSize / 2f, 0f);
    }

    public void SetEnable(bool _enabled)
    {
        canvas.enabled = _enabled;
    }
}
