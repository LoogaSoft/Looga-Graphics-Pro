# Volume-controlled tonemapping

Add **LoogaSoft > Looga Tonemapper** to a Volume profile. A newly created override uses **Mode: None** and all parameter override checkboxes are unchecked. Adding it does not change rendering, and neither does omitting the component entirely.

To enable it, check the mode's override checkbox and select AgX, Khronos PBR Neutral, Sigmoid, or Reinhard Extended. Check other parameters only when changing those values. Disable the component using its header checkbox, or override Mode to None to suppress lower-priority tonemapping inside a higher-priority volume. Disabling a component means it no longer contributes to blending; another active volume can still supply tonemapping.

The Looga Lighting renderer feature supplies the render pass automatically when the camera's blended Volume settings request a curve. There is no separate renderer tonemapper toggle. The camera's post-processing setting is respected. Looga runs after URP post-processing; set Unity's own Tonemapping Mode to None when using Looga to avoid applying two curves.

Defaults are pre/post exposure 0 stops, black point 0, white point 1, contrast 1, and saturation 1. Algorithm-specific defaults remain Sigmoid Contrast 1.5 and Reinhard White Point 1.5; these do nothing while Mode is None. All parameter constructors leave overrideState false.

## Upgrade behavior

Existing serialized curve IDs are preserved: AgX 0, Khronos PBR Neutral 1, Sigmoid 2, Reinhard Extended 3. None uses -1. Existing profiles with an overridden curve continue selecting that curve. Profiles without a contributing mode now leave the image unchanged. The former renderer enableTonemapper setting is removed; projects that disabled that switch while retaining active curve overrides should disable those overrides or set Mode to None.
