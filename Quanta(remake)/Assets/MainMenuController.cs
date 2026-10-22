using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("UI Feedback")]
    public TextMeshProUGUI feedbackText;

    [Header("Buttons")]
    public Button photoelectricButton;
    public Button doubleSlitButton;
    public Button gravityButton;

    private void Start()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }

        // Disable keyboard/gamepad navigation on ALL buttons so EventSystem
        // never steals focus after a click
        SetNoNavigation(photoelectricButton);
        SetNoNavigation(doubleSlitButton);
        SetNoNavigation(gravityButton);

        if (photoelectricButton != null) photoelectricButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(DoLoadPhotoelectric)));
        if (doubleSlitButton    != null) doubleSlitButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(() => DoComingSoon("Double Slit Interference"))));
        if (gravityButton       != null) gravityButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(() => DoComingSoon("Gravity & Orbits"))));
    }

    // ── Coroutine wrapper: waits one full frame AFTER the click is released ──
    // This ensures the XR ray interactor and EventSystem both fully finish
    // processing the button press before we do anything.
    private IEnumerator DoAfterRelease(System.Action action)
    {
        // Release focus immediately
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        // Wait for end of frame so XRI pointer state fully resets
        yield return new WaitForEndOfFrame();

        // Release again after frame — belt-and-suspenders
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        action?.Invoke();
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void DoLoadPhotoelectric()
    {
        SceneManager.LoadScene("PhotoelectricWorld");
    }

    private void DoComingSoon(string lessonName)
    {
        ShowComingSoon(lessonName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetNoNavigation(Button btn)
    {
        if (btn == null) return;
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
    }

    private void ShowComingSoon(string lessonName)
    {
        if (feedbackText == null) { Debug.LogWarning($"{lessonName} is coming soon!"); return; }
        feedbackText.text = $"{lessonName} — Coming Soon!";
        feedbackText.gameObject.SetActive(true);
        CancelInvoke(nameof(ClearFeedback));
        Invoke(nameof(ClearFeedback), 3f);
    }

    private void ClearFeedback()
    {
        if (feedbackText == null) return;
        feedbackText.text = "";
        feedbackText.gameObject.SetActive(false);
    }
}
