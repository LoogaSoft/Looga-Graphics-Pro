# Changelog

## Unreleased

- Add optional mesh streaming virtual texturing with offline, independently compressed texture tiles.
- Add bounded GPU residency, asynchronous reads, visible-pixel feedback, mip fallback, and inspector diagnostics.
- Add an opaque SVT material with normal and mask channels, depth, normal, shadow, and feedback passes.
- Keep Looga Terrain and both existing runtime surface caches independent of the new streaming module.

- Release dead-camera RVT storage and empty moving-writer spatial cells.
- Capture one stable LOD per group and refresh tracked skinned writers each update.
- Add camera lifetime, spatial-index and LOD admission regression checks.

- Retain a spatial index for mesh capture, reject draws outside dirty regions, and reuse shared material copies.
- Scroll clipmaps through persistent scratch storage and redraw exposed strips or explicit dirty rectangles.
- Add a moving-writer component and bounded refresh APIs; keep conservative full-refresh fallbacks.
- Align mesh capture with terrain atlas coordinates and verify partial/full capture equivalence.
- Benchmark the current 37-terrain world in a separate Deferred+ player with both caches.

- Preserve generated lighting shader passes whose profile is encoded with fixed defines.
- Release indirect-lighting fallback buffers at player shutdown.
- Validate both surface caches with MicroSplat blending, MicroVerse roads and vegetation, and RAM water in an isolated Editor and Direct3D 12 player fixture.

- Add an opaque surface-blend receiver with URP forward lighting, depth, normals and shadows.
- Exclude receiver-only materials from generic cache writes.
- Restore the unconverted camera projection after capture to preserve normal scene drawing.
- Clamp quadrant sampling and preserve float precision while decoding surface height.

- Replace viewing-camera culling changes with bounded mesh collection and explicit draws per cache rebuild. Exclude native vegetation from capture to avoid a Unity player crash.
- Correct capture view handedness and depth ordering so the highest surface wins.
- Add optional native terrain writers and a MicroSplat page-producer adapter in Looga Terrain.
- Refresh clipmaps on native terrain height and texture notifications.
- Preserve common-material masks and add explicit custom-mask opt-in.
- Verify four-quadrant atlas data, page persistence, Scene/Game cameras and Forward camera stacks.
- Add capture-volume, depth-order and page-boundary regression tests.

## 1.3.2 - 2026-09-10

- Bind persistent runtime virtual texture atlases through their `RTHandle` objects so shader consumers retain valid cache textures across RenderGraph frames.
- Expand camera culling to the largest RVT clipmap during capture and restore the original culling matrix after rendering.
- Disable incompatible GPU instancing for the generic RVT override shader to prevent terrain instance-property warnings.

## 1.3.1 - 2026-09-10

- Give each runtime virtual texture clipmap its own RenderGraph renderer list.
- Preserve persistent atlas contents when a cache rebuild is not required.

## 1.3.0 - 2026-09-10

- Add a URP RenderGraph runtime virtual texture renderer feature.
- Add four packed and texel-snapped surface clipmaps.
- Keep a persistent cache for each camera and rebuild it at page boundaries.
- Add explicit every-frame and request-driven cache refresh paths.
- Store albedo, normal, height, smoothness, metallic, coverage, and a custom mask.
- Add the shared HLSL receiver contract and focused clipmap math tests.

## 1.2.0 - 2026-09-10

- Add separate renderer-default attenuation profiles for point and spot lights.
- Keep the shared renderer-default profile as the upgrade-safe configuration.

## 1.1.0 - 2026-09-10

- Add global and per-light attenuation modes for Looga real-time lighting.
- Preserve URP attenuation as the default for existing projects.
- Preserve URP spot, cookie, layer, shadow, and ambient-occlusion behavior.
- Add CPU evaluation and focused editor tests for attenuation modes.
