# Lighting Model Profiles

Looga Lighting includes optimized built-in models and an editable **Custom Profile**
model. The built-ins remain the reference implementations; a profile lets you
assemble a new model from the same response families without editing HLSL.

## Create a profile

1. Select the **Looga Lighting** renderer feature.
2. Choose a built-in model that is close to the desired result.
3. Click **Create Custom From Preset...**.
4. Save the new `LoogaLightingModelProfile` asset.

The renderer switches to **Custom Profile** and assigns the new asset. The profile
can also be created from **Assets > Create > LoogaSoft > Lighting > Lighting Model
Profile**.

## Controls

- **Diffuse Model** selects Lambert, Disney/Burley, Minnaert, Oren-Nayar,
  wrapped, or banded diffuse response.
- **Direct Specular** selects GGX, Beckmann, or Phong highlights.
- **Indirect Specular** independently selects the reflection-probe lobe family.
- **Specular Occlusion** selects standard visibility or Source 2-style bent-normal
  occlusion.
- **Response** controls diffuse, direct-specular, and indirect-specular energy.
- **Roughness Scale/Bias** remap direct and indirect roughness independently.
- **Secondary Lobe** adds a broader highlight without requiring a material-side
  dual-lobe setup.
- **Highlight Shaping** compresses low-level highlights for a more graphic result.
- **Grazing/Edge Occlusion** controls reflection visibility near grazing angles.

Use **Initialize From Preset** in the profile inspector to reset the asset to a
built-in model's equivalent settings.

## Shader generation

The editor compile profile generates a fixed **Custom** Master Deferred shader only
when an active URP renderer asset references the Custom model. The Custom shader contains the configurable
evaluator but excludes all built-in model implementations, keeping its compile
surface isolated from the preset shaders.

Profile values are ordinary serialized project assets. They are portable across
machines and graphics hardware; generated shaders are Unity shader source assets,
not hardware-specific binaries.
