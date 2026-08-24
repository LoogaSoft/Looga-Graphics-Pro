# Custom Shader Integration

Looga Lighting can shade an ordinary URP-compatible deferred shader, but a shader must write Looga's additional material data to expose every model-specific control. The package provides two supported authoring paths for Unity 6.3 and URP 17.3.

## Shader Graph

Create a graph with **Assets > Create > Shader Graph > URP > Looga Lit Shader Graph**, or import the **Custom Shader Authoring** sample from Package Manager.

The graph behaves like URP Lit for surface type, alpha clipping, render face, workflow, normal space, shadows, depth, motion vectors, lightmapping, decals, and material overrides. Its generated Looga passes additionally provide:

- Looga-compatible GBuffer packing and stencil classification.
- Full Looga forward lighting for transparent materials and forward renderers.
- Model parameters for Minnaert, Overwatch, Oren-Nayar, and Arkane modes.
- Secondary specular lobe data for both metallic and specular workflows.
- Subsurface profile and transmission data for the Looga SSSS pass.

Add model inputs from the **Looga Lighting** block category in the fragment context. A missing block is valid and uses a calibrated default. The indirect-model values are `0` for GGX, `1` for the Beckmann approximation, and `2` for Phong.

The custom target is intentionally tied to the URP 17.3 Shader Graph API. Revalidate the package when upgrading URP because Unity does not expose these target descriptors as a stable public extension API.

## HLSL

Import the sample and duplicate **Looga Custom Lit Template.shader** into the project. It is a complete, compiling material shader rather than a fragment-only snippet. Keep its rendering passes and replace the property declarations, texture sampling, and material assembly with the custom shader's inputs.

For full deferred integration, preserve these contracts:

- The `UniversalGBuffer` pass writes standard URP material channels using Looga's packing helpers, calls `PackLoogaMaterialFlags`, and writes stencil reference/write mask `96`.
- The `LoogaMaterialExtras` pass writes secondary-lobe roughness/mix and encoded lighting-model parameters.
- The `SSSSProfile` pass writes subsurface color, scatter width, ambient scatter, and transmission softness when SSSS is enabled.
- The `UniversalForward` pass calls the Looga lighting helpers so forward and transparent materials use the selected model too.
- ShadowCaster, DepthOnly, DepthNormals, Meta, and motion-vector behavior must remain compatible with the material's opacity and vertex deformation.

The renderer feature requires **Deferred+** for its full deferred-light replacement. Forward rendering is supported by shaders that include their Looga forward pass, but an ordinary custom forward shader cannot be retroactively relit by the renderer feature.

## Baseline URP Compatibility

An opaque Shader Graph using the standard URP Lit target participates through its regular GBuffer data. Looga can apply the selected lighting model with calibrated defaults, but it cannot recover material data the shader never wrote, such as an independent Minnaert coefficient, a secondary specular lobe, or an SSSS profile.

Use the Looga Lit target or HLSL template when the material needs those controls or when its forward appearance must match Looga's deferred lighting.
