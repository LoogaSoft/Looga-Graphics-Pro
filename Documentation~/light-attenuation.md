# Custom light attenuation

Looga Lighting uses URP attenuation by default. Existing lights keep their
current appearance after this package update.

## Set the project default

Open the **Looga Lighting** renderer feature. Select a mode in the **Light
Attenuation** section. The selected mode applies to point and spot lights that do
not contain a `Looga Light Attenuation` component.

Set **Defaults** to **Shared** to use one profile for point and spot lights.
Set **Defaults** to **Per Light Type** to configure each light type separately.
Each type has independent mode, radius, fade, exponent, and curve values.

Use **URP Default** when the project must match standard URP lighting.

## Override one light

Add **LoogaSoft > Lighting > Looga Light Attenuation** to a GameObject that
contains a Unity `Light`. Select the required mode.

- **Physical** uses inverse-square falloff and clamps the near field to the
  source radius.
- **Soft Physical** adds a smooth fade near the Unity light range.
- **Linear** fades linearly from the source to the range.
- **Quadratic** squares the remaining normalized distance.
- **Power** uses the selected exponent.
- **Custom Curve** samples a normalized distance curve.

The custom curve uses zero for the light position and one for the Unity light
range. Looga stores eight curve samples for each visible light. This keeps the
shader data bounded and avoids a runtime texture dependency.

Looga preserves URP spot-cone attenuation, cookies, light layers, shadows, and
screen-space ambient occlusion.

## Baked and mixed lights

Custom real-time falloff does not automatically change Unity Lightmapper or
Bakery output. Configure the active baker to use a matching falloff. Use URP
Default when exact baked parity is required and the baker cannot match the
selected curve.

## Rendering limits

Looga applies custom attenuation in Deferred+ and in Looga forward shaders that
use the shared lighting include. Third-party forward shaders continue to use
their own attenuation code.
