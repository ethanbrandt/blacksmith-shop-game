# Liquid and gel materials

Create a material using **Custom/ToonLiquid**, then assign it to a mesh renderer.
The existing PixelRendererFeature generates its outlines automatically. Existing
Custom/ToonLit materials and their shader are unchanged.

## Controls

- **Body Color and Opacity:** alpha controls surface transparency. Alpha zero
  also removes the outline and selection highlight.
- **Outline Color / Outline Opacity:** independent of body opacity. A body at
  0.3 opacity can retain a fully opaque outline.
- **Light Bands / Light Wrap / Shadow Color / Highlight Color:** toon shading.
- **Rim Color / Strength / Power:** brighter grazing edges.
- **Specular Color / Size / Softness:** stylized wet highlights.
- **Normal Map / Normal Strength / Flow Speed XY:** scrolling surface detail.
  Import the texture as a normal map; the mesh needs UVs and tangents.
- **Wave Strength / Scale / Speed:** optional object-space waves.
  Strength defaults to zero. Subdivide the mesh for smoother deformation and
  allow extra renderer bounds for the maximum displacement (1.5 × strength).
- **Wave Motion:** selects the direction vertices actually move: **Up Down Y**
  (default), **Side To Side X**, or **Forward Back Z**. These are local object axes,
  so rotating the object also rotates the motion. For sideways movement, select
  Side To Side X and set Wave Strength above zero (try 0.05).
- **Wave Pattern Rotation (Degrees):** rotates the two-wave pattern in the object's local
  XZ plane. 0 preserves the original pattern, 90 turns it a quarter turn, and 180
  reverses it. With positive wave speed and scale, the main wave travels toward
  local -X at 0 degrees and local -Z at 90 degrees. This changes the pattern's
  travel direction; Wave Motion controls the displacement axis. Both controls
  are separate from the normal map's UV-based Flow Speed.

Suggested starting values:

| Control | Water | Gel |
| --- | --- | --- |
| Body opacity | 0.25–0.4 | 0.55–0.8 |
| Rim strength | 0.4 | 0.6 |
| Specular size | 0.08 | 0.25 |
| Specular softness | 0.02 | 0.1 |
| Normal strength | 0.3 | 0.1 |
| Flow speed XY | 0.03, 0.02 | 0.005, 0.003 |
| Wave strength | 0.02 | 0.04 |
| Wave speed | 1 | 0.4 |

Waves are a surface effect, not a liquid simulation. The shader does not include
refraction, thickness-based absorption, or transparent shadow casting.

## Rendering

The body is alpha blended without camera depth writes. A separate custom pass
uses identical deformation and normals to capture the nearest visible liquid
surface. It writes only its own depth buffer and rejects fragments behind opaque
scene depth. Three point-sampled textures hold normal/depth, IDs/opacity/highlight
state, and outline color. IDs use full float precision.

The existing outline material includes the liquid edge shader via UsePass, so
no extra renderer material needs assigning and that shader is a build dependency.
Opaque outlines are attenuated when liquid covers their source edge. Liquid
outlines are composited afterwards, before the existing sharp upscale.

Only the nearest transparent layer is outlined. Multiple overlapping transparent
bodies still have ordinary Unity transparent sorting limitations. Rendering all
their outlines through each other needs a multilayer solution. This path targets
the project's desktop URP Forward renderer, with three simultaneous color targets;
XR and mobile compatibility have not been validated.

## Validation

Run **Tools → Rendering → Validate Liquid Rendering** in Unity. It compiles the
shader passes and renders a disposable scene with background geometry, a foreground
occluder, and overlapping liquid meshes. Perspective and orthographic images at
opacity 0, 0.5, and 1, plus outline-disabled and selection-highlighted variants,
are saved with a report to `Temp/LiquidRenderingValidation`.
The user's scene and material assets are not modified. Inspect the images as well
as the report: shader compilation alone does not establish visual correctness.
