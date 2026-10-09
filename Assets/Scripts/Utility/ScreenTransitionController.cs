using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ScreenTransitionController : MonoBehaviour
{
    static ScreenTransitionController instance;
    VisualElement overlay;
    Label caption;
    Coroutine transition;
    bool busy;
    float previousTimeScale;

    public static bool IsTransitioning => instance != null && instance.busy;

    static ScreenTransitionController GetOrCreate()
    {
        if (instance == null)
        {
            var prefab = Resources.Load<GameObject>("UI/ScreenTransitionOverlay");
            if (prefab == null)
                throw new InvalidOperationException("The screen transition prefab is missing.");
            Instantiate(prefab);
        }
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void InitializeOverlay()
    {
        if (overlay != null)
            return;
        var document = GetComponent<UIDocument>();
        overlay = document.rootVisualElement.Q("TransitionOverlay");
        caption = document.rootVisualElement.Q<Label>("TransitionCaption");
        if (overlay == null || caption == null)
            throw new InvalidOperationException("Screen transition resources are missing their overlay or caption.");
        overlay.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation());
        overlay.RegisterCallback<NavigationMoveEvent>(evt => evt.StopPropagation());
    }

    public static bool LoadScene(string scenePath, float fadeSeconds = 0.45f, Action onRevealed = null)
    {
        return GetOrCreate().Begin(scenePath, null, null, onRevealed, fadeSeconds, 0f);
    }

    public static bool Interlude(string text, Action onCovered, Action onRevealed,
        float fadeSeconds = 0.45f, float holdSeconds = 2.5f)
    {
        return GetOrCreate().Begin(null, text, onCovered, onRevealed, fadeSeconds, holdSeconds);
    }

    bool Begin(string scenePath, string text, Action onCovered, Action onRevealed, float fadeSeconds, float holdSeconds)
    {
        if (busy)
            return false;
        InitializeOverlay();
        busy = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        transition = StartCoroutine(Run(scenePath, text, onCovered, onRevealed,
            Mathf.Max(0f, fadeSeconds), Mathf.Max(0f, holdSeconds)));
        return true;
    }

    IEnumerator Run(string scenePath, string text, Action onCovered, Action onRevealed, float fadeSeconds, float holdSeconds)
    {
        try
        {
            overlay.style.display = DisplayStyle.Flex;
            overlay.style.opacity = 0f;
            caption.style.display = DisplayStyle.None;
            caption.style.opacity = 0f;
            yield return Fade(overlay, 0f, 1f, fadeSeconds);

            if (!string.IsNullOrEmpty(text))
            {
                caption.text = text;
                caption.style.display = DisplayStyle.Flex;
                float captionFade = Mathf.Min(0.2f, fadeSeconds);
                yield return Fade(caption, 0f, 1f, captionFade);
                yield return new WaitForSecondsRealtime(holdSeconds);
                yield return Fade(caption, 1f, 0f, captionFade);
                caption.style.display = DisplayStyle.None;
            }

            if (!string.IsNullOrEmpty(scenePath))
                yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            onCovered?.Invoke();
            // Give the new scene and UIDocuments time to initialize while fully covered.
            yield return null;
            yield return null;
            yield return Fade(overlay, 1f, 0f, fadeSeconds);
        }
        finally
        {
            ResetOverlay();
        }
        onRevealed?.Invoke();
    }

    static IEnumerator Fade(VisualElement element, float from, float to, float duration)
    {
        float elapsed = 0f;
        element.style.opacity = from;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            element.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        element.style.opacity = to;
    }

    void ResetOverlay()
    {
        overlay.style.opacity = 0f;
        overlay.style.display = DisplayStyle.None;
        caption.style.display = DisplayStyle.None;
        if (busy)
            Time.timeScale = previousTimeScale;
        busy = false;
        transition = null;
    }

    void OnDestroy()
    {
        if (instance != this)
            return;
        if (transition != null)
            StopCoroutine(transition);
        if (overlay != null)
            ResetOverlay();
        instance = null;
    }
}
