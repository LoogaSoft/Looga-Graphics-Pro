# Looga Post Processing

**Looga Post Processing** is one Volume override for the Looga post-processing effects. It currently contains bloom and tonemapping. Each effect is off by default, so adding the override does not change the image.

## Setup

1. Add the **Looga Post Processing** renderer feature to each Universal Renderer that renders the scene. The feature supplies the render passes. It runs only for Game and Scene view cameras that have post-processing enabled.
2. Add **LoogaSoft > Looga Post Processing** to a Volume profile.
3. Check the override checkbox of each parameter that you change.

The override inspector shows a warning when no renderer of the active URP asset has the feature, and when Unity Bloom or Unity Tonemapping is also active. Disable the Unity effects to prevent a double application.

## Bloom

Set **Intensity** above 0 to enable bloom. Bloom runs before URP post-processing, while the camera color is still linear HDR.

| Parameter | Default | Description |
| --- | --- | --- |
| Intensity | 0 | Part of the light that the lens scatters into the bloom. |
| Threshold | 0 | Brightness where the bloom starts, measured after pre-exposure. 0 lets all light bloom, as in a physical lens. |
| Knee | 0.5 | Width of the soft transition below the threshold, as a part of the threshold. 0 gives a hard cut. |
| Tint | White | Color multiplier for the bloom. |
| Mode | Pyramid | Pyramid, or FFT Convolution. See the modes below. |
| Scatter | 0.7 | Pyramid only. Blend toward the wider pyramid levels. Low values keep the bloom tight. High values make it wide and soft. |
| Levels | 7 | Pyramid only. Maximum number of pyramid levels. More levels give a wider bloom. |
| FFT Size | 512 | FFT only. Size of the FFT buffers: 256, 512 or 1024 texels. |
| Kernel Size | 1 | FFT only. Kernel diameter as a part of the largest screen side. |
| Kernel Texture | None | FFT only. Point spread function of the lens. None uses a physically based glare kernel. |
| Lens Dirt Texture | None | Texture that multiplies the bloom. It covers the screen and keeps its aspect ratio. |
| Lens Dirt Intensity | 0 | Lens dirt brightness. The dirt shows only where the bloom is bright. |

The bloom conserves energy. The composite removes the scattered part of each pixel and adds the bloom in its place, so a larger intensity spreads light but does not brighten the whole image. Lens dirt adds light.

### Pyramid mode

- Compute shaders build the pyramid in transient Render Graph textures in 16-bit float format. Level 0 is half the camera resolution.
- The prefilter and the downsample use the 13-tap filter from Jimenez, "Next Generation Post Processing in Call of Duty: Advanced Warfare". Each thread group loads its source tile into groupshared memory once.
- The prefilter uses a partial Karis average. Highlights that only part of the filter sees lose most of their weight, which prevents fireflies. Bright areas keep their energy.
- The upsample uses a 9-tap tent filter and blends each level over the same-size downsample level by **Scatter**.
- The composite writes a new camera color texture instead of copying the camera color.

### FFT Convolution mode

FFT Convolution convolves the thresholded image with a kernel through a fast Fourier transform. The kernel can have any shape, for example streaks, rings or a starburst from the aperture blades. Use it for high-end settings and cinematics. It costs more than the pyramid mode.

- The prefilter and the downsample make a thresholded image near the FFT image size. The image fills a region at the origin of an N x N buffer. The rest of the buffer stays black, so the kernel radius fits beside the image and light does not wrap around the screen borders. A larger Kernel Size therefore gives a smaller image region.
- A radix-2 FFT in groupshared memory transforms each row and each column in one thread group. R and G share one complex value, and B uses a second one, so one FFT carries all three channels.
- The kernel spectrum is built only when FFT Size, Kernel Size or the kernel texture changes. It persists between frames.
- The spectrum multiply separates the color channels, so colored kernels work. Each kernel channel is divided by its sum, so the bloom keeps the light energy of each channel. Use Tint to color the bloom.

Kernel texture guidelines:

- Center the point spread function in the texture. The texture covers the kernel diameter.
- Put only the scattered light in the kernel. The light that does not scatter stays in the image. A bright center dot in the kernel blurs the image instead of adding glare.
- Keep the texture edges black. Use a texture with mipmaps when it is larger than the kernel in buffer texels.
- The kernel must contain some light in each channel. A channel with no light gives no bloom for that channel.

The default kernel is the glare point spread function of Spencer et al., "Physically-Based Glare Effects for Digital Images" (1995), without its core. The kernel radius represents 20 degrees.

### Limits

- The camera color must be HDR. With an 8-bit color buffer, for example a camera that renders to an 8-bit render texture, values above 1 are clamped and a threshold of 1 or more removes all bloom.
- Multisampled camera color is not supported. The bloom is skipped when MSAA is active on the camera color texture.
- The platform must support compute shaders.
- In FFT Convolution mode, a camera with a different FFT Size, Kernel Size or kernel texture than the previous camera rebuilds the kernel spectrum.

## Tonemapping

Check the **Mode** override and select AgX, Khronos PBR Neutral, Sigmoid, or Reinhard Extended. **None** leaves the image unchanged. Override Mode to None in a higher-priority volume to suppress tonemapping from lower-priority volumes. Disable the component with its header checkbox to remove its contribution from blending.

Tonemapping runs after URP post-processing. Set the Unity Tonemapping Mode to None when you use Looga tonemapping.

Defaults are pre-exposure and post-exposure 0 stops, black point 0, white point 1, contrast 1, and saturation 1. Sigmoid Contrast is 1.5 and Reinhard White Point is 1.5. These values have no effect while Mode is None. Pre-exposure also applies to the bloom threshold.

## Upgrade from Looga Tonemapper

Version 1.4.0 replaces the **Looga Tonemapper** override with **Looga Post Processing**. The Looga Lighting renderer feature no longer applies tonemapping.

Select **Tools > LoogaSoft > Migrate Tonemapper To Post Processing**. The command does these steps:

- It replaces each Looga Tonemapper override in the volume profiles under Assets with a Looga Post Processing override. Values and override checkboxes stay the same.
- It adds the Looga Post Processing feature to each Universal Renderer that has the Looga Lighting feature.

Review and save the changed assets. Existing curve IDs stay the same: None -1, AgX 0, Khronos PBR Neutral 1, Sigmoid 2, Reinhard Extended 3. Profiles that are not migrated keep their data, but their tonemapping has no effect until you migrate them.
