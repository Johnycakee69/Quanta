using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class WorldMenuController : MonoBehaviour
{
    // ── Default values that match the scene's initial state ──────────────────
    private const float DEFAULT_WAVELENGTH_NM = 400f;
    private const float DEFAULT_INTENSITY = 0.5f;
    private const float DEFAULT_BATTERY_VOLTAGE = 0f;
    private const int DEFAULT_MATERIAL_INDEX = 1;   // Sodium

    [Header("UI Feedback")]
    public TextMeshProUGUI feedbackText;

    [Header("Buttons")]
    public Button resetButton;
    public Button exitButton;
    public Button doubleSlitButton;
    public Button gravityButton;

    private void Start()
    {
        // Disable keyboard/gamepad navigation on ALL buttons so EventSystem
        // never steals focus after a click
        SetNoNavigation(resetButton);
        SetNoNavigation(exitButton);
        SetNoNavigation(doubleSlitButton);
        SetNoNavigation(gravityButton);

        if (resetButton != null) resetButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(DoReset)));
        if (exitButton != null) exitButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(DoExit)));
        if (doubleSlitButton != null) doubleSlitButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(() => DoComingSoon("Double Slit Interference"))));
        if (gravityButton != null) gravityButton.onClick.AddListener(() => StartCoroutine(DoAfterRelease(() => DoComingSoon("Gravity & Orbits"))));
    }

    // ── Coroutine wrapper: waits one full frame AFTER the click is released ──
    // This ensures the XR ray interactor and EventSystem both fully finish
    // processing the button press before we do anything.
    private IEnumerator DoAfterRelease(System.Action action)
    {
        // Release focus immediately
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        // Wait for end of frame so XRI pointer state resets
        yield return new WaitForEndOfFrame();

        // Release again after frame — belt-and-suspenders
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        action?.Invoke();
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void DoReset()
    {
        var simulator = FindObjectOfType<PhotoelectricEffectSimulator>();
        var controller = FindObjectOfType<PhotoelectricVRController>();

        if (simulator == null)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        simulator.SetWavelengthNm(DEFAULT_WAVELENGTH_NM);
        simulator.SetIntensity(DEFAULT_INTENSITY);
        simulator.SetBatteryVoltage(DEFAULT_BATTERY_VOLTAGE);
        simulator.SetMaterialByIndex(DEFAULT_MATERIAL_INDEX);

        if (controller != null)
        {
            if (controller.wavelengthSlider != null) controller.wavelengthSlider.value = DEFAULT_WAVELENGTH_NM;
            if (controller.intensitySlider != null) controller.intensitySlider.value = DEFAULT_INTENSITY;
            if (controller.voltageSlider != null) controller.voltageSlider.value = DEFAULT_BATTERY_VOLTAGE;
            controller.OnMaterialSelected(DEFAULT_MATERIAL_INDEX);
        }

        ShowFeedback("World reset to initial state!");
    }

    public void DoExit()
    {
        SceneManager.LoadScene("MenuWorld");
    }

    private void DoComingSoon(string lessonName)
    {
        ShowFeedback($"{lessonName} — Coming Soon!");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetNoNavigation(Button btn)
    {
        if (btn == null) return;
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText == null) return;
        feedbackText.text = message;
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
