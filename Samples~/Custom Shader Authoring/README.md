# Custom Shader Authoring Sample

- **Looga Lit Starter.shadergraph** is a URP Lit-style graph using the Looga Lit target. Add model-specific fragment blocks from the **Looga Lighting** category as needed.
- **Looga Custom Lit Template.shader** is a complete ShaderLab/HLSL starting point. Keep its Looga GBuffer, forward, material-extras, and SSSS pass contracts while replacing the example material sampling.

The project renderer must use Deferred+ and include the Looga Lighting renderer feature for the full deferred lighting replacement.
