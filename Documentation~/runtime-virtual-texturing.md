# Runtime Virtual Texturing

Looga Runtime Virtual Texturing creates a camera-centered surface cache during each camera frame. The cache is independent of Unity Streaming Virtual Texturing.

## Setup

1. Add **Looga Runtime Virtual Texture** to the active Universal Renderer.
2. Put cache writers on layers included by **Writer Layers**.
3. Set **Minimum World Height** and **Maximum World Height** to contain the scene geometry.
4. Include `Packages/com.loogasoft.loogagraphicspro/Includes/LoogaRuntimeVirtualTexture.hlsl` in receiver shaders.
5. Call `LoogaRvtSample(positionWS)` and use the returned coverage before you blend the sample.

The default four levels cover 128, 256, 512, and 1024 world units. Each level uses one quadrant of the packed atlas. Clipmap centers snap to page boundaries to prevent texture shimmer. Each camera keeps a persistent cache. Looga rebuilds the cache only when that camera crosses a page boundary or the cache allocation changes.

Use **Every Frame** when writers change continuously. For occasional changes, keep **On Page Movement** and call `LoogaRuntimeVirtualTextureRendererFeature.RequestRefresh()` after a writer changes.

## Surface Contract

The cache stores these values:

- Linear albedo and coverage.
- Octahedral world normal, smoothness, and metallic.
- Sixteen-bit normalized world height, a custom mask, and coverage.

The writer override shader reads common URP and Looga material property names. Custom shaders can expose `_BaseMap`, `_BaseColor`, `_BumpMap`, `_BumpScale`, `_Metallic`, `_Smoothness`, and `_LoogaRvtMask` to participate without a separate material.

## Receiver Example

```hlsl
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaRuntimeVirtualTexture.hlsl"

LoogaRuntimeVirtualTextureSample rvt = LoogaRvtSample(positionWS);
albedo = lerp(albedo, rvt.albedo, rvt.coverage * blendStrength);
normalWS = normalize(lerp(normalWS, rvt.normalWS, rvt.coverage * normalBlend));
```

## Limits

- The first release uses the active camera culling results. It does not run another CPU cull for the top-down clipmaps.
- Opaque and alpha-tested geometry can write. Transparent geometry can only receive.
- Standard terrain shaders use a layered splat contract. Add a terrain-specific writer adapter before terrain color can be captured accurately.
- A moved clipmap currently rebuilds the complete packed cache. Incremental page scrolling is a later optimization.
