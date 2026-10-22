using System;
using UnityEngine;

/// <summary>
/// Simulates and visualizes double-slit wave interference in VR.
/// Combines two-slit interference with single-slit diffraction envelope:
///     I(theta) = I0 * [cos(pi * d * sin(theta) / lambda)]^2
///                    * [sinc(pi * a * sin(theta) / lambda)]^2
///
/// Attach to an empty GameObject that also holds a LineRenderer (the
/// fringe-intensity plot) and, optionally, a "screen" quad the pattern
/// is projected onto. All physical parameters are exposed as public
/// fields/properties so VR UI sliders (e.g. XR Interaction Toolkit
/// sliders) can drive them via UnityEvents -> SetWavelengthNm(float) etc.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class DoubleSlitInterference : MonoBehaviour
{
    [Header("Light Source")]
    [Tooltip("Wavelength of light in nanometers (visible range ~380-750nm)")]
    [Range(380f, 750f)]
    public float wavelengthNm = 550f;

    [Header("Slit Geometry")]
    [Tooltip("Distance between the two slit centers, in millimeters")]
    [Range(0.01f, 2f)]
    public float slitSeparationMm = 0.25f;

    [Tooltip("Width of each individual slit, in millimeters")]
    [Range(0.001f, 0.5f)]
    public float slitWidthMm = 0.04f;

    [Header("Screen / Projection")]
    [Tooltip("Distance from slits to the projection screen, in meters")]
    [Range(0.2f, 10f)]
    public float screenDistanceM = 2f;

    [Tooltip("Physical width of the pattern rendered on the screen, in meters")]
    public float patternWidthM = 1.2f;

    [Tooltip("Number of sample points used to draw the fringe pattern")]
    [Range(50, 2000)]
    public int resolution = 400;

    [Header("Visualization")]
    public LineRenderer patternLine;
    public GameObject brightFringeMarkerPrefab; // optional, small glowing sphere prefab
    public Transform screenSurface;             // quad the pattern is mapped onto (local X = fringe axis)
    public Gradient intensityGradient;          // maps 0..1 intensity to color (dark -> bright)

    [Header("Runtime Info (read-only)")]
    public float fringeSpacingM;   // Δy = lambda * L / d
    public int brightFringeCount;

    private GameObject[] _markers;

    private const double EPSILON = 1e-9;

    private void Awake()
    {
        if (patternLine == null) patternLine = GetComponent<LineRenderer>();
        if (intensityGradient == null || intensityGradient.colorKeys.Length == 0)
        {
            intensityGradient = DefaultGradient();
        }
    }

    private void Start()
    {
        GeneratePattern();
    }

    /// <summary>
    /// Public entry point to recompute and redraw everything.
    /// Call after changing any parameter (also called automatically on Start).
    /// </summary>
    public void GeneratePattern()
    {
        double lambdaM = wavelengthNm * 1e-9;
        double dM = slitSeparationMm * 1e-3;
        double aM = slitWidthMm * 1e-3;

        fringeSpacingM = (float)(lambdaM * screenDistanceM / dM);
        brightFringeCount = 0;

        patternLine.positionCount = resolution;
        patternLine.useWorldSpace = false;

        Vector3[] points = new Vector3[resolution];
        float halfWidth = patternWidthM * 0.5f;

        // Track intensity maxima for marker placement
        float[] intensities = new float[resolution];

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);           // 0..1
            float x = Mathf.Lerp(-halfWidth, halfWidth, t);   // position along screen

            double theta = Math.Atan2(x, screenDistanceM);
            double intensity = ComputeIntensity(theta, lambdaM, dM, aM);
            intensities[i] = (float)intensity;

            // Height (y) encodes intensity for a readable "graph" line;
            // x stays fixed to the physical screen position.
            points[i] = new Vector3(x, (float)intensity * 0.3f, 0f);
        }

        patternLine.SetPositions(points);
        ColorLineByIntensity(intensities);
        PlaceBrightFringeMarkers(intensities, halfWidth);
    }

    /// <summary>
    /// Combined interference + diffraction intensity, normalized to [0,1].
    /// </summary>
    private double ComputeIntensity(double thetaRad, double lambdaM, double dM, double aM)
    {
        double sinTheta = Math.Sin(thetaRad);

        // Two-slit interference term
        double phaseInterference = Math.PI * dM * sinTheta / lambdaM;
        double interferenceTerm = Math.Cos(phaseInterference);
        interferenceTerm *= interferenceTerm;

        // Single-slit diffraction envelope (sinc^2)
        double phaseDiffraction = Math.PI * aM * sinTheta / lambdaM;
        double sinc = Math.Abs(phaseDiffraction) < EPSILON
            ? 1.0
            : Math.Sin(phaseDiffraction) / phaseDiffraction;
        double diffractionTerm = sinc * sinc;

        return interferenceTerm * diffractionTerm; // max value = 1
    }

    private void ColorLineByIntensity(float[] intensities)
    {
        if (patternLine.colorGradient == null) return;

        Gradient g = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[intensities.Length <= 8 ? intensities.Length : 8];
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2] {
            new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f)
        };

        int step = Mathf.Max(1, intensities.Length / colorKeys.Length);
        for (int k = 0; k < colorKeys.Length; k++)
        {
            int idx = Mathf.Min(k * step, intensities.Length - 1);
            float time = (float)k / (colorKeys.Length - 1);
            colorKeys[k] = new GradientColorKey(intensityGradient.Evaluate(intensities[idx]), time);
        }
        g.SetKeys(colorKeys, alphaKeys);
        patternLine.colorGradient = g;
    }

    private void PlaceBrightFringeMarkers(float[] intensities, float halfWidth)
    {
        if (brightFringeMarkerPrefab == null || screenSurface == null) return;

        // Clear old markers
        if (_markers != null)
        {
            foreach (var m in _markers) if (m != null) Destroy(m);
        }

        // Find local maxima above a threshold (bright fringes)
        var maxima = new System.Collections.Generic.List<int>();
        for (int i = 1; i < intensities.Length - 1; i++)
        {
            if (intensities[i] > 0.8f &&
                intensities[i] >= intensities[i - 1] &&
                intensities[i] >= intensities[i + 1])
            {
                maxima.Add(i);
            }
        }

        _markers = new GameObject[maxima.Count];
        brightFringeCount = maxima.Count;

        for (int k = 0; k < maxima.Count; k++)
        {
            int i = maxima[k];
            float t = (float)i / (intensities.Length - 1);
            float x = Mathf.Lerp(-halfWidth, halfWidth, t);

            Vector3 worldPos = screenSurface.TransformPoint(new Vector3(x, 0f, 0f));
            GameObject marker = Instantiate(brightFringeMarkerPrefab, worldPos, Quaternion.identity, screenSurface);
            marker.transform.localScale = Vector3.one * Mathf.Lerp(0.005f, 0.02f, intensities[i]);
            _markers[k] = marker;
        }
    }

    private Gradient DefaultGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(Color.black, 0f),
                new GradientColorKey(new Color(0.9f, 0.9f, 1f), 0.5f),
                new GradientColorKey(Color.white, 1f)
            },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return g;
    }

    // ---- VR UI hook-up helpers (bind to XR slider OnValueChanged events) ----

    public void SetWavelengthNm(float value)
    {
        wavelengthNm = Mathf.Clamp(value, 380f, 750f);
        GeneratePattern();
    }

    public void SetSlitSeparationMm(float value)
    {
        slitSeparationMm = Mathf.Max(0.001f, value);
        GeneratePattern();
    }

    public void SetSlitWidthMm(float value)
    {
        slitWidthMm = Mathf.Max(0.001f, value);
        GeneratePattern();
    }

    public void SetScreenDistanceM(float value)
    {
        screenDistanceM = Mathf.Max(0.05f, value);
        GeneratePattern();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Live update in the editor when tweaking values in the Inspector
        if (Application.isPlaying && patternLine != null)
        {
            GeneratePattern();
        }
    }
#endif
}
