# Streaming virtual texturing

Looga SVT streams texture tiles from disk for explicitly configured opaque mesh materials.
It is separate from the Graphics Pro runtime surface cache.
Looga Terrain has no SVT integration or dependency.

## Setup

1. Create **Looga > Graphics > SVT Bake Settings** from the Assets creation menu.
2. Assign a square, power-of-two albedo from 128 to 8192 pixels.
3. Optionally assign a Unity normal map and a linear mask map.
4. Pack metallic in mask R, occlusion in G, and smoothness in A. B is reserved.
5. Select **Bake Streaming Virtual Texture** and choose a new output asset.
6. Create a material with **Looga/Streaming Virtual Texture/Lit**.
7. Assign that material to an opaque MeshRenderer.
8. Add **Looga > Graphics > Streaming Virtual Texture** to the object.
9. Assign the baked asset and select the material slot.
10. Add **Looga Streaming Virtual Texture** to the URP renderer used by the camera.

All renderers sharing a baked asset must use the same resident-page capacity.
The material exposes UV tiling, tint, normal strength, metallic offset, and smoothness multiplier.
Debug View displays albedo, tangent normals, or packed material channels.
The component inspector displays resident, pending, failed, uploaded, and evicted pages.
It also shows GPU cache allocation and the latest read error.

## Stored data

The baker writes a versioned .lsvt file under Assets/StreamingAssets/LoogaSVT.
Each 128-pixel tile has a four-pixel filtering border and independent Deflate compression.
Albedo uses sRGB. Tangent normals and masks use linear data.
Mip generation filters albedo in linear space and normalizes reduced normals.
The asset stores tile ranges, integrity checksums, and a small coarse fallback.
It does not reference the original full-resolution textures.
Editor bake settings retain source references for rebuilding.

Keep the generated file with its output asset. StreamingAssets files are included in player builds.
Rebaking preserves the output asset GUID and writes a new uniquely named tile file.
Previous tile files are retained. Remove obsolete files only after checking their asset references.
Disable the affected bindings before rebuilding assets during a content deployment.
The editor baker refreshes all active bindings that share its output.

## Runtime budgets

The cache allocates three RGBA8 texture arrays and one small page table.
GPU tiles are uncompressed in this implementation. Disk compression does not imply GPU block compression.
The default 64-page stack uses approximately 13.55 MiB for the three arrays.
The resident pool does not grow with source resolution.
A maximum of eight stacks can be active. Each binding supports 4 to 256 resident pages.
Budgets are per stack; account for their sum when choosing project settings.

One coarse page remains resident. Missing pages sample the nearest resident parent mip.
Visible-pixel feedback selects requested pages. A requested mip also requests its next coarser mip.
Requests are deduplicated and bounded. Each cache normally runs at most four worker reads and two uploads per update.
Inactive requests expire. Recently used pages receive eviction protection.
Movement can evict old pages while retaining the coarse fallback.
A missing or corrupt file produces a diagnostic and coarse rendering. Use Retry Failed Pages after repair.

Readback uses a reduced-resolution R32_UInt target and asynchronous GPU transfer.
The default feedback downsample is four. Derivatives account for this reduction.
Opaque scene depth rejects occluded fragments.
One feedback request can be pending per feature instance.
Feedback snapshots retain their cache identity, so delayed requests cannot reach a reused stack ID.

## Scope

The first target is Windows/D3D12, Unity 6.3, and URP 17.3 with RenderGraph.
The shader uses a forward-only opaque PBR pass within Forward or Deferred renderers.
Depth, normals, shadow, and feedback passes are included.
Base Game cameras and optional Scene views collect feedback.
Overlay cameras can sample resident data but do not collect additional requests.
XR, transparent materials, terrain, MicroSplat arrays, skinned renderers, and arbitrary vendor shaders are not integrated.
The current filtering uses bilinear tile sampling and trilinear mip blending, without anisotropic page footprints.
GPU feedback can miss very small geometry at reduced resolution. Lower Feedback Downsample when required.
Unsupported hardware does not receive a streaming cache and the component reports the failure.

Graphics Pro RVT continues to operate independently.
The supplied SVT shader is excluded from the generic RVT writer, which cannot capture its streamed material contract.
Do not assume that an RVT bake can consume these SVT materials.
A future writer adapter must request source tiles and invalidate captures after residency changes.

SVT can reduce memory use for large texture sets. It adds sampling, feedback, CPU, I/O, and upload costs.
It does not create texture detail, stream geometry, or guarantee an FPS increase.
Compare it with native mipmap streaming on representative content before a broad conversion.

