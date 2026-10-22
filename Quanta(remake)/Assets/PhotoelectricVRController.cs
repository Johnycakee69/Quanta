using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Runtime VR controller for the Photoelectric Effect scene.
///
/// Bridges PhotoelectricEffectSimulator ↔ visual elements:
///   - Light beam colour changes with wavelength (UV violet → IR red)
///   - Ammeter needle animates with current (intensity × voltage × emission)
///   - Electron particles fly left→right inside the tube, speed ∝ KE
///   - Photon particles fly from lamp → cathode, colour ∝ wavelength
///   - Battery voltage slider controls retarding / accelerating field
///   - All UI readouts update in real time
/// </summary>
public class PhotoelectricVRController : MonoBehaviour
{
    // ─── Physics ──────────────────────────────────────────────────────────────
    [Header("Physics Simulator")]
    public PhotoelectricEffectSimulator simulator;

    // ─── Light & Beam ─────────────────────────────────────────────────────────
    [Header("Light & Beam")]
    public Light    lampSpotLight;
    public Renderer beamRenderer;          // cone mesh with additive material
    public Light    cathodeSurfaceLight;   // point light near cathode face

    // ─── Cathode plate renderer (changes colour with material) ────────────────
    [Header("Plate Renderers")]
    public Renderer cathodeRenderer;       // assigned in scene; tinted by material
    public Renderer anodeRenderer;

    // ─── Ammeter ──────────────────────────────────────────────────────────────
    [Header("Ammeter")]
    public Transform ammeterNeedle;
    public float needleZeroAngle  = -60f;
    public float needleMaxAngle   =  60f;
    public float maxExpectedCurrent = 10f;

    // ─── Voltage display ──────────────────────────────────────────────────────
    [Header("Battery Display")]
    public TextMeshPro batteryVoltageDisplay3D;   // world-space 3D label on battery

    // ─── UI Readouts (World-Space Canvas) ────────────────────────────────────
    [Header("UI Readouts")]
    public TextMeshProUGUI photonEnergyText;
    public TextMeshProUGUI kineticEnergyText;
    public TextMeshProUGUI stoppingVoltageText;
    public TextMeshProUGUI currentText;
    public TextMeshProUGUI wavelengthText;
    public TextMeshProUGUI intensityText;
    public TextMeshProUGUI voltageText;
    public TextMeshProUGUI thresholdText;
    public TextMeshProUGUI materialText;
    public TextMeshProUGUI noEmissionWarning;
    public TextMeshProUGUI stoppedWarning;         // shown when V retards all electrons

    // ─── VR Sliders ───────────────────────────────────────────────────────────
    [Header("VR Sliders")]
    public Slider wavelengthSlider;   // 100–1000 nm
    public Slider intensitySlider;    // 0–1
    public Slider voltageSlider;      // -10–+10 V

    // ─── Material Selector Buttons ────────────────────────────────────────────
    [Header("Material Buttons")]
    public Button[] materialButtons;  // one per preset (Cesium…Platinum)

    // ─── Internal ─────────────────────────────────────────────────────────────
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID     = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorID   = Shader.PropertyToID("_Color");
    private static readonly int TintColorID     = Shader.PropertyToID("_TintColor");
    [Header("Slider Smoothing")]
    [Tooltip("Smoothing time in seconds (0 = instant, 0.035 = silky smooth)")]
    public float sliderSmoothTime = 0.035f;

    private float _targetWavelength;
    private float _targetIntensity;
    private float _targetVoltage;

    private float _wavelengthVel;
    private float _intensityVel;
    private float _voltageVel;

    private bool _initialized;
    private MaterialPropertyBlock _beamMpb;

    private float _needleAngle;
    private float _needleVel;

    // Material colours for the cathode plate tinting
    private static readonly Color[] MatColors = new[]
    {
        new Color(0.70f, 0.75f, 0.90f),  // Cesium  – blue-silver
        new Color(0.85f, 0.85f, 0.80f),  // Sodium  – warm silver
        new Color(0.80f, 0.82f, 0.85f),  // Calcium – cool silver
        new Color(0.65f, 0.68f, 0.65f),  // Zinc    – grey
        new Color(0.72f, 0.45f, 0.20f),  // Copper  – copper tone
        new Color(0.90f, 0.90f, 0.88f),  // Platinum – bright silver
    };

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _beamMpb = new MaterialPropertyBlock();

        // Ensure EventSystem exists for VR UI interaction
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                Debug.Log("[PhotoelectricVRController] Auto-created EventSystem with InputSystemUIInputModule.");
            }
        }

        // 1. Ensure all World Space Canvases have event camera assigned and disable double raycasters
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            var camObj = GameObject.FindWithTag("MainCamera");
            if (camObj != null) mainCam = camObj.GetComponent<Camera>();
        }
        var canvases = FindObjectsOfType<Canvas>(true);
        foreach (var c in canvases)
        {
            if (c.renderMode == RenderMode.WorldSpace && c.worldCamera == null && mainCam != null)
            {
                c.worldCamera = mainCam;
            }

            var trackedRay = c.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            var standardRay = c.GetComponent<GraphicRaycaster>();
            if (trackedRay != null && standardRay != null)
            {
                standardRay.enabled = false;
            }
        }

        // 2. Disable navigation on sliders so controller thumbstick doesn't fight ray drag
        SetNoNavigation(wavelengthSlider);
        SetNoNavigation(intensitySlider);
        SetNoNavigation(voltageSlider);

        if (wavelengthSlider != null)
            wavelengthSlider.onValueChanged.AddListener(v =>
            {
                _targetWavelength = v;
                if (!_initialized || sliderSmoothTime <= 0f) simulator?.SetWavelengthNm(v);
            });

        if (intensitySlider != null)
            intensitySlider.onValueChanged.AddListener(v =>
            {
                _targetIntensity = v;
                if (!_initialized || sliderSmoothTime <= 0f) simulator?.SetIntensity(v);
            });

        if (voltageSlider != null)
            voltageSlider.onValueChanged.AddListener(v =>
            {
                _targetVoltage = v;
                if (!_initialized || sliderSmoothTime <= 0f) simulator?.SetBatteryVoltage(v);
            });

        for (int i = 0; i < materialButtons.Length; i++)
        {
            int idx = i;
            materialButtons[i]?.onClick.AddListener(() => OnMaterialSelected(idx));
        }

        if (simulator != null)
        {
            simulator.OnEmissionStateChanged        += _ => RefreshAll();
            simulator.OnElectronsReachAnodeChanged  += _ => RefreshAll();
        }
    }

    private void Start()
    {
        if (simulator != null)
        {
            _targetWavelength = simulator.wavelengthNm;
            _targetIntensity  = simulator.intensity;
            _targetVoltage    = simulator.batteryVoltageV;

            if (wavelengthSlider != null) wavelengthSlider.value = simulator.wavelengthNm;
            if (intensitySlider  != null) intensitySlider.value  = simulator.intensity;
            if (voltageSlider    != null) voltageSlider.value    = simulator.batteryVoltageV;
            OnMaterialSelected(simulator.materialIndex);
        }
        _needleAngle = needleZeroAngle;
        _initialized = true;
        RefreshAll();
    }

    private void Update()
    {
        if (simulator == null) return;

        // Smoothly interpolate simulator values towards slider targets to eliminate hand/ray jitter
        if (_initialized && sliderSmoothTime > 0f)
        {
            if (Mathf.Abs(simulator.wavelengthNm - _targetWavelength) > 0.01f)
            {
                float smoothW = Mathf.SmoothDamp(simulator.wavelengthNm, _targetWavelength, ref _wavelengthVel, sliderSmoothTime);
                simulator.SetWavelengthNm(smoothW);
            }
            if (Mathf.Abs(simulator.intensity - _targetIntensity) > 0.001f)
            {
                float smoothI = Mathf.SmoothDamp(simulator.intensity, _targetIntensity, ref _intensityVel, sliderSmoothTime);
                simulator.SetIntensity(smoothI);
            }
            if (Mathf.Abs(simulator.batteryVoltageV - _targetVoltage) > 0.005f)
            {
                float smoothV = Mathf.SmoothDamp(simulator.batteryVoltageV, _targetVoltage, ref _voltageVel, sliderSmoothTime);
                simulator.SetBatteryVoltage(smoothV);
            }
        }

        RefreshAll();
        UpdateBeamColor();
        UpdateNeedle();
        UpdateCathodeGlow();
    }

    public void SyncSliders()
    {
        if (simulator == null) return;
        _targetWavelength = simulator.wavelengthNm;
        _targetIntensity  = simulator.intensity;
        _targetVoltage    = simulator.batteryVoltageV;
        if (wavelengthSlider != null) wavelengthSlider.value = simulator.wavelengthNm;
        if (intensitySlider  != null) intensitySlider.value  = simulator.intensity;
        if (voltageSlider    != null) voltageSlider.value    = simulator.batteryVoltageV;
    }

    private static void SetNoNavigation(Slider s)
    {
        if (s == null) return;
        var nav = s.navigation;
        nav.mode = Navigation.Mode.None;
        s.navigation = nav;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // READOUTS
    // ─────────────────────────────────────────────────────────────────────────
    private void RefreshAll()
    {
        if (simulator == null) return;

        SetText(photonEnergyText,    $"Photon Energy:    {simulator.photonEnergyEv:F3} eV");
        SetText(kineticEnergyText,   $"Max KE (electrons): {simulator.maxKineticEnergyEv:F3} eV");
        SetText(stoppingVoltageText, $"Stopping Voltage: {simulator.stoppingVoltageV:F3} V");
        SetText(wavelengthText,      $"λ = {simulator.wavelengthNm:F0} nm");
        SetText(intensityText,       $"Intensity = {simulator.intensity * 100f:F0}%");
        SetText(voltageText,         $"V = {simulator.batteryVoltageV:+0.00;-0.00;0.00} V");
        SetText(thresholdText,       $"Threshold λ:  {simulator.thresholdWavelengthNm:F0} nm");
        SetText(materialText,        $"Metal: {simulator.CurrentMaterialName}  (W = {simulator.workFunctionEv:F2} eV)");
        SetText(currentText,         $"Current: {simulator.electricCurrent:F4} µA");

        // Warnings
        if (noEmissionWarning != null)
            noEmissionWarning.gameObject.SetActive(!simulator.emissionOccurs);

        if (stoppedWarning != null)
            stoppedWarning.gameObject.SetActive(
                simulator.emissionOccurs && !simulator.electronsReachAnode);

        // Battery voltage 3D label
        if (batteryVoltageDisplay3D != null)
            batteryVoltageDisplay3D.text = $"{simulator.batteryVoltageV:+0.00;-0.00} V";
    }

    private static void SetText(TextMeshProUGUI lbl, string txt)
    {
        if (lbl != null) lbl.text = txt;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BEAM COLOUR  (wavelength → colour)
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateBeamColor()
    {
        if (simulator == null) return;
        Color beamColor = WavelengthToColor(simulator.wavelengthNm);
        float bright    = simulator.intensity;

        if (lampSpotLight != null)
        {
            lampSpotLight.color     = beamColor;
            lampSpotLight.intensity = Mathf.Lerp(0.3f, 5f, bright);
        }

        if (beamRenderer != null)
        {
            beamRenderer.GetPropertyBlock(_beamMpb);
            Color baseCol = new Color(beamColor.r, beamColor.g, beamColor.b, bright * 0.65f);
            _beamMpb.SetColor(BaseColorID, baseCol);
            _beamMpb.SetColor(LegacyColorID, baseCol);
            _beamMpb.SetColor(TintColorID, baseCol);
            _beamMpb.SetColor(EmissionColorID, beamColor * (bright * 2.5f));
            beamRenderer.SetPropertyBlock(_beamMpb);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AMMETER NEEDLE
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateNeedle()
    {
        if (ammeterNeedle == null || simulator == null) return;
        float norm   = (float)Mathf.Clamp01((float)(simulator.electricCurrent / maxExpectedCurrent));
        float target = Mathf.Lerp(needleZeroAngle, needleMaxAngle, norm);
        _needleAngle = Mathf.SmoothDamp(_needleAngle, target, ref _needleVel, 0.15f);
        ammeterNeedle.localRotation = Quaternion.Euler(0f, 0f, _needleAngle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CATHODE GLOW  (point light at cathode face)
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateCathodeGlow()
    {
        if (cathodeSurfaceLight == null || simulator == null) return;
        if (simulator.emissionOccurs)
        {
            cathodeSurfaceLight.enabled   = true;
            cathodeSurfaceLight.intensity = Mathf.Clamp((float)simulator.maxKineticEnergyEv * 0.6f, 0.1f, 4f);
            cathodeSurfaceLight.color     = WavelengthToColor(simulator.wavelengthNm);
        }
        else
        {
            cathodeSurfaceLight.enabled = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MATERIAL SELECTION
    // ─────────────────────────────────────────────────────────────────────────
    public void OnMaterialSelected(int index)
    {
        simulator?.SetMaterialByIndex(index);

        // Highlight active button
        for (int i = 0; i < materialButtons.Length; i++)
        {
            if (materialButtons[i] == null) continue;
            var c = materialButtons[i].colors;
            c.normalColor = (i == index)
                ? new Color(0.15f, 0.55f, 1.00f)
                : new Color(0.15f, 0.20f, 0.30f);
            materialButtons[i].colors = c;
        }

        // Tint cathode plate to match material
        if (cathodeRenderer != null && index >= 0 && index < MatColors.Length)
        {
            var mpb = new MaterialPropertyBlock();
            cathodeRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorID, MatColors[index]);
            cathodeRenderer.SetPropertyBlock(mpb);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WAVELENGTH → RGB  (visible spectrum + UV/IR tails)
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Converts light wavelength in nanometres to a Unity Color.
    /// UV (&lt;380 nm) → violet/purple.  IR (&gt;700 nm) → deep red.
    /// </summary>
    public static Color WavelengthToColor(float nm)
    {
        float r, g, b;

        if      (nm < 380f) { r = 0.55f; g = 0f;   b = 1f; }
        else if (nm < 440f) { r = Remap(nm, 380,440, 0.55f,0f); g = 0f; b = 1f; }
        else if (nm < 490f) { r = 0f; g = Remap(nm,440,490,0f,1f); b = 1f; }
        else if (nm < 510f) { r = 0f; g = 1f; b = Remap(nm,490,510,1f,0f); }
        else if (nm < 580f) { r = Remap(nm,510,580,0f,1f); g = 1f; b = 0f; }
        else if (nm < 645f) { r = 1f; g = Remap(nm,580,645,1f,0f); b = 0f; }
        else if (nm <= 700f){ r = 1f; g = 0f; b = 0f; }
        else                { r = 0.45f; g = 0f; b = 0f; }   // IR

        float brightness = 1f;
        if      (nm < 420f) brightness = Remap(nm, 380f, 420f, 0.25f, 1f);
        else if (nm > 680f) brightness = Remap(nm, 680f, 780f, 1f, 0.25f);

        return new Color(r * brightness, g * brightness, b * brightness, 1f);
    }

    private static float Remap(float v, float a, float b, float c, float d)
        => Mathf.Lerp(c, d, Mathf.InverseLerp(a, b, v));

    private void OnDestroy()
    {
        if (simulator != null)
        {
            simulator.OnEmissionStateChanged       -= _ => RefreshAll();
            simulator.OnElectronsReachAnodeChanged -= _ => RefreshAll();
        }
    }
}
