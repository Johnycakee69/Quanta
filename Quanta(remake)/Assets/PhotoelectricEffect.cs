using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simulates Einstein's photoelectric effect in VR:
///     E_photon  = h * f
///     KE_max    = h * f - W      (only if E_photon > W)
///     V_stop    = KE_max / e
///
/// Battery voltage (batteryVoltageV):
///   Positive  → accelerating field  → more electrons reach anode (current ↑)
///   Negative  → retarding  field    → electrons stopped when |V| >= V_stop
///   At V = -V_stop exactly, current drops to zero.
///
/// Teaching points preserved:
///   - Intensity  → emission RATE only (never electron speed)
///   - Wavelength → photon energy → electron KE
///   - Material   → work function threshold
///   - Voltage    → controls whether emitted electrons reach anode
/// </summary>
public class PhotoelectricEffectSimulator : MonoBehaviour
{
    // ── Physical constants ────────────────────────────────────────────────────
    private const double PLANCK_EV_S         = 4.135667696e-15;  // eV·s
    private const double PLANCK_J_S          = 6.62607015e-34;   // J·s
    private const double ELECTRON_CHARGE_C   = 1.602176634e-19;  // C
    private const double SPEED_OF_LIGHT_M_S  = 2.99792458e8;     // m/s
    private const double ELECTRON_MASS_KG    = 9.1093837015e-31; // kg

    // ── Material presets ──────────────────────────────────────────────────────
    [Serializable]
    public struct MaterialPreset
    {
        public string name;
        public float  workFunctionEv;

        public MaterialPreset(string n, float w) { name = n; workFunctionEv = w; }
    }

    public static readonly MaterialPreset[] CommonMaterials = new[]
    {
        new MaterialPreset("Cesium",   2.14f),
        new MaterialPreset("Sodium",   2.28f),
        new MaterialPreset("Calcium",  2.87f),
        new MaterialPreset("Zinc",     4.33f),
        new MaterialPreset("Copper",   4.70f),
        new MaterialPreset("Platinum", 6.35f),
    };

    // ── Inspector fields ──────────────────────────────────────────────────────
    [Header("Material")]
    [Tooltip("Index into CommonMaterials")]
    public int   materialIndex    = 1;   // Sodium default (matches PhET)

    [Tooltip("Work function in eV (auto-set by material preset)")]
    public float workFunctionEv   = 2.28f;

    [Header("Incident Light")]
    [Range(100f, 1000f)]
    [Tooltip("Wavelength of incident light in nanometres")]
    public float wavelengthNm     = 400f;

    [Range(0f, 1f)]
    [Tooltip("Relative photon flux (0–1). Controls emission RATE, not electron speed.")]
    public float intensity        = 0.5f;

    [Header("Battery / Voltage")]
    [Range(-10f, 10f)]
    [Tooltip("Applied voltage in volts.\n" +
             "> 0 → accelerating (more electrons reach anode)\n" +
             "< 0 → retarding (electrons blocked when |V| ≥ V_stop)\n" +
             "= 0 → no external field")]
    public float batteryVoltageV  = 0f;

    [Header("Particle Visualisation")]
    [Tooltip("Electron ParticleSystem emitting from the cathode surface")]
    public ParticleSystem electronParticles;

    [Tooltip("Photon ParticleSystem shooting from the UV lamp toward the cathode")]
    public ParticleSystem photonParticles;

    [Tooltip("Transform of the cathode face (electrons spawn here)")]
    public Transform emissionSurface;

    [Tooltip("Transform of the anode face (used to compute travel distance/lifetime)")]
    public Transform anodeSurface;

    [Tooltip("Max electrons per second at full intensity when emitting")]
    public float maxEmissionRatePerSecond = 100f;

    [Tooltip("Max photons per second at full intensity (lamp particles)")]
    public float maxPhotonRatePerSecond   = 60f;

    [Tooltip("Scale factor: converts real electron speed to visible particle speed")]
    public float speedVisualizationScale  = 1.2f;

    [Tooltip("Distance from cathode to anode in world units (for lifetime calculation)")]
    public float tubeLengthUnits          = 2.3f;

    // ── Runtime results (read-only in Inspector) ──────────────────────────────
    [Header("Runtime Results (read-only)")]
    public double photonEnergyEv;
    public double thresholdFrequencyHz;
    public double thresholdWavelengthNm;
    public double maxKineticEnergyEv;
    public double stoppingVoltageV;
    public double maxElectronSpeedMps;
    public double electricCurrent;        // proportional display value (µA scale)
    public bool   emissionOccurs;         // photon energy > work function
    public bool   electronsReachAnode;    // emitting AND not stopped by retarding voltage

    public event Action<bool> OnEmissionStateChanged;
    public event Action<bool> OnElectronsReachAnodeChanged;

    private bool _lastEmissionState;
    private bool _lastReachAnodeState;

    // ─────────────────────────────────────────────────────────────────────────
    private void Start()
    {
        ApplyMaterialPreset(materialIndex);
        Recompute();
    }

    /// <summary>
    /// Full recalculation of all photoelectric quantities.
    /// Call after any slider or dropdown change.
    /// </summary>
    public void Recompute()
    {
        // ── Photon energy ─────────────────────────────────────────────────────
        double freqHz   = SPEED_OF_LIGHT_M_S / (wavelengthNm * 1e-9);
        photonEnergyEv  = PLANCK_EV_S * freqHz;

        // ── Threshold ─────────────────────────────────────────────────────────
        thresholdFrequencyHz  = workFunctionEv / PLANCK_EV_S;
        thresholdWavelengthNm = (SPEED_OF_LIGHT_M_S / thresholdFrequencyHz) * 1e9;

        // ── Electron kinetic energy ───────────────────────────────────────────
        double ke      = photonEnergyEv - workFunctionEv;
        emissionOccurs = ke > 0.0;
        maxKineticEnergyEv = emissionOccurs ? ke : 0.0;
        stoppingVoltageV   = maxKineticEnergyEv;   // V_stop [V] == KE [eV] numerically

        // ── Max electron speed (relativistic ignored at these energies) ────────
        if (emissionOccurs)
        {
            double keJ = maxKineticEnergyEv * ELECTRON_CHARGE_C;
            maxElectronSpeedMps = Math.Sqrt(2.0 * keJ / ELECTRON_MASS_KG);
        }
        else
        {
            maxElectronSpeedMps = 0.0;
        }

        // ── Voltage effect on current ─────────────────────────────────────────
        // Electrons reach the anode only if:
        //   a) emission is occurring, AND
        //   b) the retarding voltage hasn't stopped them
        //      (batteryVoltageV > -stoppingVoltageV)
        electronsReachAnode = emissionOccurs &&
                              (batteryVoltageV >= -(float)stoppingVoltageV);

        if (electronsReachAnode)
        {
            double keEff = maxKineticEnergyEv + batteryVoltageV;
            double fractionArriving = batteryVoltageV >= 0.0
                ? 1.0
                : Math.Max(0.0, keEff / maxKineticEnergyEv);
            
            // Base the electric current correctly using the theoretical formula:
            // I = (P / E_photon) * e * quantum_efficiency
            double incidentPowerW = intensity * 1e-3; // max 1 mW
            double quantumEfficiency = 0.01; // 1% efficiency
            double ePhotonJ = photonEnergyEv * ELECTRON_CHARGE_C;
            double photonFlux = incidentPowerW / ePhotonJ;
            double trueCurrentA = photonFlux * quantumEfficiency * ELECTRON_CHARGE_C * fractionArriving;
            
            // Store current in microAmperes for proportional display
            electricCurrent = trueCurrentA * 1e6;
        }
        else
        {
            electricCurrent = 0.0;
        }

        UpdateElectronParticles();
        UpdatePhotonParticles();

        // ── Fire state-change events ──────────────────────────────────────────
        if (emissionOccurs != _lastEmissionState)
        {
            _lastEmissionState = emissionOccurs;
            OnEmissionStateChanged?.Invoke(emissionOccurs);
        }
        if (electronsReachAnode != _lastReachAnodeState)
        {
            _lastReachAnodeState = electronsReachAnode;
            OnElectronsReachAnodeChanged?.Invoke(electronsReachAnode);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ELECTRON PARTICLE SYSTEM (particles inside glass tube cathode -> anode)
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateElectronParticles()
    {
        if (electronParticles == null) return;

        var emission = electronParticles.emission;
        var main     = electronParticles.main;
        var force    = electronParticles.forceOverLifetime;
        var vel      = electronParticles.velocityOverLifetime;

        if (emissionOccurs)
        {
            // ── Emission rate ∝ intensity (core teaching point) ───────────────
            float rate = maxEmissionRatePerSecond * Mathf.Clamp01(intensity);
            emission.rateOverTime = rate;

            // ── Base speed ∝ √KE_max (wavelength & target metal work function)
            float baseSpeed = Mathf.Sqrt((float)maxKineticEnergyEv);
            float visSpeed  = Mathf.Clamp(baseSpeed * speedVisualizationScale, 0.4f, 6.0f);

            // ── Electric field acceleration from battery voltage (voltage option)
            // a_field = (v0^2 * V) / (2 * d * KE_max)
            float a_field = 0f;
            if (maxKineticEnergyEv > 0.0001)
            {
                a_field = (visSpeed * visSpeed * batteryVoltageV) / (2f * tubeLengthUnits * (float)maxKineticEnergyEv);
            }

            float d = tubeLengthUnits;
            float lifetime;

            if (batteryVoltageV >= -(float)stoppingVoltageV)
            {
                // Particles reach anode (V >= -V_stop)
                if (Mathf.Abs(a_field) < 0.001f)
                {
                    lifetime = d / visSpeed;
                }
                else
                {
                    float disc = visSpeed * visSpeed + 2f * a_field * d;
                    if (disc >= 0f)
                        lifetime = (-visSpeed + Mathf.Sqrt(disc)) / a_field;
                    else
                        lifetime = d / visSpeed;
                }
            }
            else
            {
                // Retarding voltage stops particles before anode and repels them back
                float absA = Mathf.Abs(a_field);
                lifetime = absA > 0.001f ? (2f * visSpeed / absA) : (2f * d / visSpeed);
            }

            lifetime = Mathf.Clamp(lifetime, 0.2f, 10f);

            main.startSpeed    = new ParticleSystem.MinMaxCurve(visSpeed * 0.9f, visSpeed * 1.1f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.95f, lifetime * 1.05f);

            // Enable forceOverLifetime for visual electric field acceleration/deceleration
            force.enabled = true;
            force.space   = ParticleSystemSimulationSpace.World;
            force.x       = new ParticleSystem.MinMaxCurve(a_field);
            force.y       = new ParticleSystem.MinMaxCurve(0f);
            force.z       = new ParticleSystem.MinMaxCurve(0f);

            // Disable hardcoded velocityOverLifetime override
            vel.enabled = false;

            // ── Particle colour: energetic cyan-white vs low-energy blue ───────
            Color loEnergy = new Color(0.20f, 0.55f, 1.00f);   // blue (low KE)
            Color hiEnergy = new Color(0.75f, 0.95f, 1.00f);   // bright cyan-white (high KE)
            float keNorm   = Mathf.Clamp01((float)maxKineticEnergyEv / 5f);
            Color eColor   = Color.Lerp(loEnergy, hiEnergy, keNorm);
            main.startColor = new ParticleSystem.MinMaxGradient(eColor * 0.85f, eColor);

            if (!electronParticles.isPlaying) electronParticles.Play();
        }
        else
        {
            emission.rateOverTime = 0f;
            electronParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHOTON PARTICLE SYSTEM (incoming light photons from lamp -> target metal)
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdatePhotonParticles()
    {
        if (photonParticles == null) return;

        var emission = photonParticles.emission;
        var main     = photonParticles.main;
        var vel      = photonParticles.velocityOverLifetime;

        // Photons travel from the left glass surface rightwards to the target metal plate (Cathode)
        float photonRate = maxPhotonRatePerSecond * Mathf.Clamp01(intensity);
        emission.rateOverTime = photonRate;

        // Photon colour matches light wavelength
        Color photonColor = PhotoelectricVRController.WavelengthToColor(wavelengthNm);
        main.startColor = new ParticleSystem.MinMaxGradient(
                              photonColor * 0.85f, photonColor * 1.25f);

        // Speed: 3.2 units/s towards Cathode plate (dist = 0.21 units)
        float speed    = 3.2f;
        float dist     = 0.21f; // From x=-1.35 to x=-1.14
        float lifetime = dist / speed; // ~0.065s

        main.startSpeed    = new ParticleSystem.MinMaxCurve(speed * 0.95f, speed * 1.05f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.95f, lifetime * 1.05f);

        // Size & glow: photon energy visualization (E = hf)
        float energyNorm = Mathf.Clamp01((float)photonEnergyEv / 6.0f);
        float pSize = Mathf.Lerp(0.018f, 0.034f, energyNorm);
        main.startSize = new ParticleSystem.MinMaxCurve(pSize * 0.85f, pSize * 1.15f);

        // Ensure velocityOverLifetime doesn't override the forward motion
        vel.enabled = false;

        if (photonRate > 0f)
        {
            if (!photonParticles.isPlaying) photonParticles.Play();
        }
        else
        {
            photonParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC SETTERS (bind these to VR UI sliders / dropdowns)
    // ─────────────────────────────────────────────────────────────────────────
    public void SetWavelengthNm(float value)
    {
        wavelengthNm = Mathf.Clamp(value, 100f, 1000f);
        Recompute();
    }

    public void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
        Recompute();
    }

    public void SetBatteryVoltage(float value)
    {
        batteryVoltageV = Mathf.Clamp(value, -10f, 10f);
        Recompute();
    }

    public void SetWorkFunctionEv(float value)
    {
        materialIndex   = -1;
        workFunctionEv  = Mathf.Max(0.01f, value);
        Recompute();
    }

    public void SetMaterialByIndex(int index) => ApplyMaterialPreset(index);

    public void ApplyMaterialPreset(int index)
    {
        if (index < 0 || index >= CommonMaterials.Length) return;
        materialIndex  = index;
        workFunctionEv = CommonMaterials[index].workFunctionEv;
        Recompute();
    }

    public string CurrentMaterialName =>
        (materialIndex >= 0 && materialIndex < CommonMaterials.Length)
            ? CommonMaterials[materialIndex].name
            : "Custom";

    /// <summary>
    /// Coroutine: sweeps wavelength from startNm to endNm over durationSeconds.
    /// Great for a guided demo showing the threshold crossing live.
    /// </summary>
    public IEnumerator SweepWavelength(float startNm, float endNm, float durationSeconds)
    {
        float elapsed = 0f;
        while (elapsed < durationSeconds)
        {
            elapsed += Time.deltaTime;
            SetWavelengthNm(Mathf.Lerp(startNm, endNm, Mathf.Clamp01(elapsed / durationSeconds)));
            yield return null;
        }
        SetWavelengthNm(endNm);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) Recompute();
    }
#endif
}
