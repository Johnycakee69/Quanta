# VR Physics Components: Interference & Photoelectric Effect

Two Unity `MonoBehaviour` components for a VR physics lab scene, assuming
Unity + XR Interaction Toolkit. Both expose public fields/methods sized for
binding to VR UI sliders and dropdowns (e.g. `Slider.onValueChanged`).

## 1. DoubleSlitInterference.cs

Models Young's double-slit experiment: two-slit interference combined with
the single-slit diffraction envelope.

**Setup**
1. Add to a GameObject with a `LineRenderer` (auto-required).
2. Optionally assign `screenSurface` (a quad representing the projection
   screen) and `brightFringeMarkerPrefab` (small glowing sphere) to get
   physical bright-fringe markers placed in world space.
3. Wire VR sliders to:
   - `SetWavelengthNm(float)` — 380–750 nm
   - `SetSlitSeparationMm(float)`
   - `SetSlitWidthMm(float)`
   - `SetScreenDistanceM(float)`

**Physics**
```
I(θ) = I0 · [cos(π d sinθ / λ)]² · [sinc(π a sinθ / λ)]²
```
`fringeSpacingM` (Δy = λL/d) and `brightFringeCount` are exposed read-only
for a UI readout or quiz check.

## 2. PhotoelectricEffect.cs

Models Einstein's photoelectric effect with a material work-function
library and correct decoupling of intensity (emission rate) from photon
energy (electron speed) — a common student misconception worth making
tactile in VR.

**Setup**
1. Add to the "metal plate" GameObject.
2. Assign a `ParticleSystem` (`electronParticles`) configured with a small
   spark/dot material for visible electron ejection.
3. Wire VR UI to:
   - `SetWavelengthNm(float)`
   - `SetIntensity(float)` — affects emission rate only
   - `SetMaterialByIndex(int)` — Cesium, Sodium, Calcium, Zinc, Copper, Platinum
   - `SetWorkFunctionEv(float)` — for a manual/custom material slider

**Physics**
```
E_photon = h f
KE_max   = h f − W        (emission only if E_photon > W)
V_stop   = KE_max / e
```
Read-only fields (`photonEnergyEv`, `thresholdWavelengthNm`,
`maxKineticEnergyEv`, `stoppingVoltageV`, `maxElectronSpeedMps`,
`emissionOccurs`) drive on-screen readouts. `OnEmissionStateChanged` fires
when crossing the threshold — handy for triggering a sound/haptic cue.
`SweepWavelength(...)` is a coroutine for an automated "watch it turn on"
demo.

## Notes
- Both scripts are self-contained; no external dependencies beyond UnityEngine.
- Electron speeds are scaled (`speedVisualizationScale`) since real
  photoelectron speeds (~10⁵–10⁶ m/s) are meaningless to render at true scale.
- Neither script assumes a specific VR rig — bind their public methods to
  whatever interaction system you're using (XR Interaction Toolkit sliders,
  hand-tracked dials, voice commands, etc.).
