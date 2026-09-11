# Changelog

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
