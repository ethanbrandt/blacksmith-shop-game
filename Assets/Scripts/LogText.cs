using System;
using TMPro;
using UnityEngine;

public class LogText : MonoBehaviour
{
    public static LogText Instance;
    
    [SerializeField] float logTextLifetime = 3f;
    [SerializeField] TextMeshProUGUI text;

    float currentTextTime;
    
    private void Awake()
    {
        if (LogText.Instance != null)
        {
            Destroy(this.gameObject);
            return;
        }

        LogText.Instance = this;
    }

    public void SetText(string _inputText)
    {
        text.text = "LOG: " + _inputText;
        Debug.Log("LOGTEXT: " + _inputText);
        currentTextTime = Time.time + logTextLifetime;
    }

    public void LateUpdate()
    {
        if (currentTextTime < Time.time)
            text.text = "LOG: ";
    }
}
