# Looga Graphics Pro

Looga Graphics Pro unifies Looga Lighting, GTAO, bent normals, and virtual shadows for Unity 6 URP Deferred+.

## Renderer Setup

- Use Unity 6.3 with URP 17.3.
- Set the Universal Renderer to **Deferred+**.
- Add **Looga GTAO**, then **Looga Lighting**, to the renderer feature list.
- Add **Looga Shadows** when the renderer should use Looga virtual shadows.
- URP shadow masks, mixed lighting, light cookies, light layers, screen-space AO, reflection probes, and deferred decals are consumed by Looga's lighting pass.

Each system remains independently configurable. Looga GTAO can generate ambient occlusion only, or ambient occlusion plus bent normals. Bent normals are enabled by default to preserve the package's original GTBN behavior.

## Shader Variants

Open **Project Settings > LoogaSoft > Lighting > Shader Variants** and select **Detect From Project**. Looga Lighting records the states supported by active URP assets, generates only the model-specialized Master Deferred shaders referenced by those renderer assets, and skips unchanged shader imports across domain reloads. It validates the profile before builds and reports the retained count afterward. Commit `ProjectSettings/LoogaLightingVariants.asset` and the generated shaders under `Assets/Resources/Shaders/Generated/LoogaSoft`.

See [Shader Variant Optimization](Documentation~/shader-variant-optimization.md) for detection rules, manual overrides, and build behavior.

## Custom Shaders

Import the **Custom Shader Authoring** sample from Package Manager for working HLSL and Shader Graph starters. New graphs can also be created directly with **Assets > Create > Shader Graph > URP > Looga Lit Shader Graph**.

The **Looga Lit** Shader Graph target retains the normal URP Lit graph workflow while generating Looga-compatible GBuffer, forward, model-parameter, dual-lobe, and subsurface passes. Model-specific blocks can be added to the fragment context from the **Looga Lighting** block category. Unused or removed blocks use the same calibrated defaults as ordinary URP shaders.

See [Custom Shader Integration](Documentation~/custom-shader-integration.md) for the HLSL pass contract and the difference between baseline URP compatibility and full Looga material integration.

## Lighting Models

- **Disney/Burley** implements Burley diffuse and Disney's base GGX response. It does not add the full Disney Principled clearcoat, sheen, anisotropy, or subsurface parameter set.
- **Source 2 Inspired** uses Lambert/GGX PBR with bent-normal specular occlusion.
- **Minnaert** implements Minnaert diffuse with an explicit material `k` coefficient. Its indirect specular can use GGX, an approximate Beckmann probe mapping, or Phong.
- **Overwatch Inspired** uses softly wrapped diffuse and broad, controlled PBR highlights. No exact public Overwatch BRDF is available.
- **Oren-Nayar** implements the full rough-diffuse coefficient form with an explicit material `sigma`. Its indirect specular can use GGX, an approximate Beckmann probe mapping, or Phong.
- **Arkane Inspired** uses feathered band lighting and shaped highlights. It is an art-direction target rather than a published Arkane BRDF.

Looga opaque and cutout shaders expose model-specific inputs in the **Lighting Model Inputs** foldout. Other URP-compatible shaders receive calibrated defaults because their standard GBuffer data does not contain independent diffuse-model coefficients.

### Custom Model Profiles

Select a built-in model in the **Looga Lighting** renderer feature and click **Create Custom From Preset...** to create an editable model asset. Custom profiles independently select diffuse, direct-specular, indirect-specular, and specular-occlusion families, then expose response strength, roughness remapping, secondary-lobe, highlight-shaping, and grazing controls.

The built-in models remain optimized reference presets. The generated **Custom** Master Deferred shader contains the configurable evaluator without pulling all six built-in implementations into the same shader. See [Lighting Model Profiles](Documentation~/lighting-model-profiles.md) for the authoring workflow and control reference.

## Model-Aware Indirect Lighting

Add a **Looga Indirect Lighting Controller** to a loaded scene from **GameObject > LoogaSoft > Lighting**. Its data asset contains four optional extensions:

- **Model-aware reflections** recapture the scene's enabled Reflection Probes and prefilter independent GGX, Beckmann, and Phong cubemap arrays. Matching split-sum BRDF LUTs are generated at the same time. Probe importance, blend distance, intensity, box projection, transform, and capture offset are preserved. Up to 32 probes are uploaded.
- **Directional lightmap decoding** re-evaluates Unity's dominant-direction lightmap against the active Looga diffuse response instead of always using Unity's half-Lambert decoder. It works automatically with directional lightmaps produced by Unity or Bakery.
- **Auxiliary multi-lobe lightmaps** accept two additional baked radiance lobes per Unity lightmap index. RGB stores radiance, alpha stores lobe energy, and the direction texture stores octahedral lobe directions in RG and BA. This is an importer contract for Bakery or another external baker; use **Build Auxiliary Lightmap Arrays** after assigning the exported textures.
- **Radiance probe volume** captures a world-space grid and fits two directional radiance lobes at each probe. It supplies model-aware diffuse GI to dynamic objects and blends back to Unity light probes or APV at the volume boundary.

Use **Bake Model-Aware Reflections** after changing Reflection Probes or reflection-visible geometry. Use **Bake Radiance Probe Volume** after changing static lighting or geometry. These generated resources are scene data, not runtime captures.

## Baked Lighting

Unity Lightmapper and Bakery output remain supported through Unity lightmaps, directional lightmaps, light probes, APV, and mixed-light shadow masks. With no Indirect Lighting Controller, baked irradiance retains standard URP behavior. With directional decoding enabled, Looga shaders recover the baked dominant direction and apply the selected diffuse model. Non-Looga shaders continue using their own URP baked-lighting implementation.

A standard completed lightmap still contains only one dominant direction, so it cannot preserve several opposing lights or separate visibility terms. The optional two-lobe textures retain more of that information when supplied by a compatible external bake/export step. Mixed subtractive lighting is removed from baked GI before Looga adds the matching real-time main light.

## GTAO And Bent Normals

The **Looga GTAO** renderer feature owns ground-truth ambient occlusion. Enable **Generate Bent Normals** when lighting should use directional visibility; disable it for ordinary GTAO with lower compute work. GTAO and screen-space subsurface scattering reconstruct scene data from the camera depth/GBuffer. They cannot include off-screen or hidden geometry and should be treated as screen-space approximations.

## Virtual Shadows

The **Looga Shadows** renderer feature owns the package's virtual shadow-map path. Its runtime, editor tooling, validation content, and documentation live under the `Shadows` module in this package.
