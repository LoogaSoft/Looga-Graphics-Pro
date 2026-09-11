# Runtime Virtual Texturing

Looga Runtime Virtual Texturing creates a camera-centered surface cache. It is independent of Unity Streaming Virtual Texturing and the Looga Terrain per-tile virtual texture cache.

## Setup

1. Add **Looga Runtime Virtual Texture** to the active Universal Renderer.
2. Put cache writers on layers included by **Writer Layers** and the camera culling mask.
3. Set **Minimum World Height** and **Maximum World Height** to contain the scene geometry.
4. Include `Packages/com.loogasoft.loogagraphicspro/Includes/LoogaRuntimeVirtualTexture.hlsl` in receiver shaders.
5. Call `LoogaRvtSample(positionWS)` and check coverage before blending the sample.

Four levels cover 128, 256, 512 and 1024 world units by default. Each level uses one atlas quadrant. Centers snap to page boundaries. Each camera owns persistent atlases. At 512 pixels, the three RGBA8 atlases and depth atlas use approximately 4 MiB per camera, plus a 0.25 MiB scrolling scratch texture.

**On Page Movement** preserves unchanged atlas pixels. Page movement copies the overlap through a persistent scratch texture and redraws only exposed strips. Bounded refreshes clear and redraw affected rectangles in each clipmap. Large camera jumps and configuration changes rebuild the affected cache. Capture does not change the viewing camera's culling matrix.

Mesh writers use a retained XZ spatial index. Discovery runs on initial capture, scene changes, Editor hierarchy changes, or a full refresh. Queries reject writers outside dirty regions. Shared material properties are copied once per capture, and draw submissions are filtered for each update rectangle.

Add **LoogaRuntimeVirtualTextureWriter** to moving mesh writers. It tracks bounds and renderer visibility. Call its `Refresh()` method after material or mesh-content changes. Call `LoogaRuntimeVirtualTextureRendererFeature.RequestRefresh(bounds)` for an external bounded edit. New runtime objects without that component require a full `RequestRefresh()` to enter discovery. Use **Every Frame** for continuous surface changes; bounds changes still require the writer component or explicit notification.

Call `LoogaRuntimeVirtualTextureRendererFeature.RequestRefresh()` for changes with unknown bounds. Native terrain height and texture notifications request a refresh automatically. Changes made without Unity notifications also need an explicit refresh. `TryGetStatistics` reports capture attempts and the latest mesh collection and bounds-filter duration. The existing cullMilliseconds output name is retained. This value excludes terrain preparation and GPU execution; it is not a frame benchmark.

## Surface Contract

| Atlas | R | G | B | A |
| --- | --- | --- | --- | --- |
| Albedo | Linear red | Linear green | Linear blue | Coverage |
| Normal/material | Octahedral normal X | Octahedral normal Y | Smoothness | Metallic |
| Height/mask | Encoded height high | Encoded height low | Custom mask | Coverage |

The mesh writer reads common properties: `_BaseMap`, `_BaseColor`, `_BumpMap`, `_BumpScale`, `_Metallic` and `_Smoothness`. Materials without the base-color contract are skipped. The default mask is one, including URP Lit materials. A custom shader must expose both `_LoogaRvtUseMask = 1` and `_LoogaRvtMask` to write a custom mask. This explicit opt-in permits a zero mask while keeping materials without either property valid.

Capture uses forward depth with a less-equal comparison, including on platforms with reversed camera depth. The highest visible surface wins. Capture normals use world space. Terrain and mesh writers share the same depth attachment.

## Optional MicroSplat Terrain Writer

Graphics Pro does not depend on MicroSplat or Looga Terrain assemblies. Its **Terrain Writers** array accepts exported materials with a `LoogaRvtSourceShader` tag matching the native terrain shader.

When Looga Terrain's optional MicroSplat editor integration is available:

1. Use an existing MicroSplat page producer exported by Looga Terrain.
2. Open **Tools > Looga Terrain > Export Graphics Pro Clipmap Writer**.
3. Select that producer and its matching native MicroSplat material.
4. Export to a new asset path and assign the writer material to **Terrain Writers**.

The adapter reuses the page producer's MicroSplat surface code. It does not create another cache or alter native TerrainData. Capture binds each native tile's height, holes, normal and control textures and copies its material properties. This also works while the native Unity terrain renderer remains enabled. Axis-aligned native terrain tiles are required.

The exported writer captures the stable surface supported by the existing page producer. Camera-dependent detail, live noise and other effects omitted by that producer are not restored by this adapter. Its existing module validation still applies. Re-export both producer and writer after a native shader feature change. This is not a claim of support for every MicroSplat module.

## Surface Blend Material

Create a material with **Looga > Runtime Virtual Texture > Surface Blend**. Assign it to an opaque object that intersects captured geometry.

- **Surface Blend Distance** controls the vertical distance over which the captured surface replaces the object's material.
- **Surface Blend Strength** controls albedo, smoothness and metallic blending.
- **Surface Normal Strength** controls normal blending within that height range.
- **Surface Height Offset** moves the height used for the intersection comparison.

The material uses its own base material outside capture coverage or while RVT is disabled. It supplies forward lighting, depth, depth-normal and shadow-caster passes. In Deferred+, URP draws it through UniversalForwardOnly. It uses URP PBR lighting; it does not reproduce Looga's custom deferred lighting models.

The receiver sets _LoogaRvtReceiverOnly = 1. The generic writer excludes these pixels, so the object cannot overwrite its source surface. Custom receiver shaders can expose this property too. Keep receivers off writer layers when possible to avoid unnecessary capture draw submissions.

The shared sampler clamps reads inside each atlas quadrant and decodes height with float precision. LoogaRvtSurfaceBlendWeight combines height distance, coverage and the custom mask.

This material supports opaque surfaces with a base texture, tint, normal texture, metallic value and smoothness value. Alpha clipping, displacement, baked RVT lightmapping, motion vectors and a complete URP Lit feature set are not implemented.

## Receiver Example

```hlsl
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaRuntimeVirtualTexture.hlsl"
LoogaRuntimeVirtualTextureSample rvt = LoogaRvtSample(positionWS);
albedo = lerp(albedo, rvt.albedo, rvt.coverage * blendStrength);
normalWS = normalize(lerp(normalWS, rvt.normalWS, rvt.coverage * normalBlend));
```

## Limits And Validation

- Unity 6000.3.21f1, URP 17.3 and Direct3D 12 were validated in Kubera World.
- Game and Scene camera paths and Forward base/overlay camera stacks were tested. URP's current Deferred renderer rejects overlay stacks.
- A Windows Direct3D 12 player with Deferred+ and Looga lighting passed the combined-cache fixture. Other graphics APIs, XR and long-duration performance were not qualified.
- Compatible opaque MeshRenderer surfaces were qualified as writers. The submission path also accepts alpha-tested and skinned meshes; their complete shader and animation variants are not qualified. Enabled LOD groups contribute only LOD 0, so camera movement cannot mix overlapping LOD surfaces in persistent storage. LOD admission has a regression test. Custom vertex deformation is not captured. Native terrain trees, details and indirect vegetation are excluded from capture and keep their normal rendering path. Transparent water does not write. A water shader needs explicit receiver integration to sample this cache; existing RAM shaders are not automatically converted.
- Custom shader deformation and layered materials need their own writer contract.
- Camera jumps larger than a clipmap and unknown edit bounds use a full refresh. Dirty bounds are conservative rectangles; a large union can refresh more area than the individual edits.

## Combined Cache Qualification

The isolated Kubera World fixture uses two synthetic terrain tiles, exported MicroSplat producers, a Graphics Pro surface receiver, MicroVerse roads and vegetation, a native MicroSplat blend object and a RAM river mesh. Editor checks cover a public MicroVerse road edit and invalidation of both caches. The baked player fixture checks four cache modes: neither cache, terrain cache only, Graphics Pro cache only and both caches.

GPU readbacks verify atlas bindings, material channels, stationary persistence, sub-page movement and page-boundary refresh. Visibility checks verify the receiver, native blending, roads, trees, details and water. These checks do not qualify every module, runtime authoring feature or asset shader. They do not establish a performance improvement.

Native terrain vegetation batches caused a Unity native crash when submitted through a generic override-shader RendererList in the player. Explicit bounded mesh capture avoids that path. This change retains the existing clipmaps, terrain writer and Looga Terrain cache.
A 37-terrain Kubera World player benchmark used Direct3D 12, Deferred+, an RTX 5080 and a 1920 by 1080 window. After region filtering and shared-material reuse, moving-camera capture preparation decreased from about 6.9 ms to 1.1 ms per update. This is the mesh query/preparation scope, not total frame time. Initial discovery and full refresh remain more expensive.

The 32 by 32 metre bounded-refresh case redrew 95,424 texels across 16 updates, versus 4,194,304 texels for full refreshes. GPU readbacks of moved/removed writers and clipmap scrolling matched full captures byte-for-byte. The existing world has no Graphics Pro surface receivers, so its benchmark measures capture overhead and does not establish a receiver performance benefit.


## Cache lifetime and installed-tool boundaries

Destroyed cameras release their atlases when the next camera requests a cache. Feature disposal releases all remaining camera storage.
Moving writers remove empty cells from the spatial index. Dead renderer candidates request rediscovery on the next capture.
Tracked skinned writers refresh each update, including poses whose renderer bounds do not change. This can make those writers expensive; use writer layers to limit capture work.
Changes to LOD membership need an explicit writer refresh or full rediscovery. The cache uses LOD 0 as a stable source and does not follow forced viewing-camera LOD selection.

The installed MicroVerse and RAM components continue to render and author native data. This is coexistence, not automatic conversion of their shaders into RVT writers or receivers.
MicroSplat clipmap writers are limited by the stable page-producer contract. Direct Looga MicroSplat module support does not expand this cache contract.
Snow, water flow, wind and other changing surface effects must stay live unless a dedicated writer/receiver implementation preserves them.
Keep the two existing caches separate: Graphics Pro supplies world-space receiver data; Looga Terrain reduces repeated terrain material sampling.
