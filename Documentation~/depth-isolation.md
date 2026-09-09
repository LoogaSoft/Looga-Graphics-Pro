# Scene-depth compatibility

Looga's depth consumers use private bindings. There is no asset-specific compatibility option and no extra renderer feature to enable.

URP owns `_CameraDepthTexture`: it is the scene-depth input used by later materials and renderer passes. Looga must not replace it with the active depth attachment, which a subsequent transparent pass may be writing. Internal depth reads now use:

| Consumer | Binding |
| --- | --- |
| Deferred lighting and subsurface scattering | `_LoogaLightingDepthTexture` |
| GTAO and its two blur kernels | `_LoogaGtaoDepthTexture`, explicitly bound per compute kernel |
| Screen-space shadow resolve and filtering | `_LoogaShadowDepthTexture` |

Each raster consumer declares its depth resource dependency. Both scattering passes sample depth without also attaching that texture as a depth target. Private texel-size uniforms are set explicitly from the bound texture. These changes reuse the existing depth resources without adding a depth-copy pass.

Projects still need their usual URP depth-texture configuration for materials that sample scene depth; this fix preserves that resource rather than forcing a copy in every project. Once this package revision is installed, remove the earlier Kubera water-depth compatibility feature if present. The behavior applies to any downstream scene-depth consumer, including water and soft particles.

## Validation

Validated in Unity 6000.3.21f1, URP 17.3.0, Deferred+, on an RTX 5080. The legacy adapter was disabled. A GPU probe before transparents compared the global scene-depth input against RenderGraph's `cameraDepthTexture`:

- Lighting and scattering: all 163,840 pixels matched.
- Lighting/scattering plus GTAO: all pixels matched.
- Lighting/scattering plus GTAO and Looga shadows: all pixels matched.

Each case contained 99,734 geometry pixels, so the test did not merely compare empty depth buffers. Ocean, river and waterfall rendering were also inspected in the project without the adapter. C# compilation passed and no new runtime errors were reported during the rendering checks. Shader compilation also reports uninitialized-variable warnings in scattering/shadow filtering; these are not depth-binding compile errors. XR and other graphics backends were not tested.

`Tests~/DepthIsolation` contains the project regression harness used for these checks. The tilde folder keeps diagnostic renderer features out of normal package imports. To rerun it in Kubera, copy the probe C# and shader into an Editor folder, wait for compilation, then evaluate `ValidateLoogaDepthIsolation.cs` with the Unity evaluation tool. It attaches test features in memory and removes them in `finally`; it does not save test features into the renderer asset. Create `Temp/LoogaDepthValidation` before running. Remove the two imported probe assets afterward.
