# Shader Variant Optimization

Looga Lighting keeps the complete master deferred source as a non-imported `.shader.template`. Unity therefore never compiles the all-model source directly. The imported package shader is a compact, fixed Disney/Burley emergency fallback, while project-specific shaders contain only the lighting models and keyword states referenced by active Universal Render Pipeline renderer assets.

All lighting models remain available on demand. Each generated shader fixes exactly one model, so Unity's backend never receives an all-model BRDF matrix. Changing a renderer feature's model schedules generation of that model and removes generated models no longer referenced by active renderer assets.

## Setup

1. Open **Edit > Project Settings > LoogaSoft > Lighting > Shader Variants**, or use **LoogaSoft > Lighting > Shader Variants**.
2. Select **Detect From Project**.
3. Review the estimated retained count and feature modes.
4. Leave **Use Project Compile Profile**, **Auto-Detect On Script Reload**, **Detect Before Build**, and **Validate Before Build** enabled for normal use.

The settings are saved to `ProjectSettings/LoogaLightingVariants.asset`. Referenced model-specialized shaders are generated under `Assets/Resources/Shaders/Generated/LoogaSoft`, and the matching shader is assigned directly to each active Looga Lighting renderer feature. Commit both the settings and generated shaders to version control. Runtime model changes should be represented by renderer assets included in the project so their shaders are generated before the build.

## Editor Compilation

Each generated model shader declares only the keyword states retained by the profile. A fingerprint in `Library/LoogaLighting` covers the template, compile profile, Unity/URP version, and referenced models. Ordinary domain reloads perform no shader writes, imports, or validation when that fingerprint and the generated set are unchanged.

Select **Regenerate Editor Shader** to force regeneration and validation. Normal project changes are checked automatically, but only shader files whose generated source changed are imported. The compact package fallback remains available if generation fails.

Use **LoogaSoft > Lighting > Diagnostics > Profile Master Deferred Compile** to measure preprocessing and D3D backend time for every model. The CSV report is written to `Library/LoogaLighting/MasterDeferredCompileProfile.csv`.

## Detection Rules

Detection reads the default pipeline asset, the current pipeline asset, and pipeline overrides assigned to quality levels. It then inspects the renderer data used by those assets.

- Fixed pipeline choices, including accurate GBuffer normals, reflection-probe features, and light-layer support, collapse to their detected state.
- Additional-light shadows and light cookies compile one runtime-capable path when the active pipeline supports them. URP's runtime light data makes that path neutral for lights without a shadow or cookie.
- Structural mixed-lighting modes retain both enabled and disabled states when the active pipeline supports them.
- Main-light shadows retain the off state and the configured cascade mode. Screen-space shadows are retained when an active renderer feature can request them.
- Soft shadows compile URP's generic runtime-quality path when supported. That path selects hard, low, medium, or high filtering from each light's runtime shadow data without separate master-shader variants.
- Screen-space occlusion retains both states only when an active SSAO renderer feature can request it.
- Reflection-probe rotation is disabled because the package does not currently publish that keyword.

## Build Behavior

Before a player build, Looga Lighting can refresh the profile from the project and validates that every required state is retained. An invalid profile stops the build rather than producing missing lighting at runtime.

Before a player build, Looga Lighting gathers the models referenced by every active default and Quality URP renderer asset, generates any missing or changed shaders, and removes unreferenced generated shaders. Their pragma declarations are already reduced. The stencil-clear pass and all other shaders are left untouched. After a successful build, the Console report includes:

- compiler variants visited after Unity's own filtering;
- variants retained by Looga Lighting;
- actual build-time reduction;
- theoretical retained count relative to the former 573,440-combination seven-model matrix.

## Manual Profiles

Disable **Detect Before Build** only when the project intentionally supports a narrower set than automatic detection finds. Keep **Validate Before Build** enabled; it catches stale settings after URP, renderer, or quality-level changes.

**Reset Compatibility** restores every keyword state. Disabling **Use Project Compile Profile** still generates only referenced models, but each generated model retains the complete compatibility keyword matrix.

## Scope

This workflow reduces editor shader compilation, player-build shader variants, build time, and build size. It does not remove lighting models; it moves model selection from a compiled variant matrix to explicit generated shader assets.
