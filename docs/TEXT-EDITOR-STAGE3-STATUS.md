# Text editor stage 3 — work in progress

This is a continuation checklist, not a statement that stage 3 is complete.
The approved brief is the stage 3 attachment in the PR conversation. Keep work
on `feature/text-editor` and Draft PR #15. Do not merge or publish a release.

## Implemented so far

- Script names in the font picker are localized; theme resources are applied
  to the picker without changing its approved layout.
- The layer stores case, underline, strikethrough, horizontal/vertical stretch,
  skew, outlines, shadow, outer glow and a gradient. Old projects use defaults.
- The shared Skia renderer handles those options for preview and export.
  Gradient stops are editable and support linear, radial and angular gradients;
  outlines support several entries and outside, centered and inside placement.
- A compact color popup supplies saturation/value, hue, alpha, HEX and RGBA
  channel controls, presets, persisted recent/favorite colors, and sampling
  from the current transformed image or video frame. The existing quick colors
  remain available. Escape cancels a pending sample.
- Targeted checks passed for gradient/glow rendering, layer serialization, and
  three outline placements. Release desktop compiles. These checks do not
  establish GUI correctness, full export parity, or completion of the brief.
- Added extrusion (depth, direction, color, opacity, shading) and a background
  (whole text or per line, padding, rounding, border and gradient). Three
  targeted checks cover rendering and serialization. Whole-text background
  width currently uses basic font measurement and must be replaced by actual
  shaped bounds, especially for fallback glyphs and complex scripts.
- Added working presets and persistent custom styles with preview, rename
  and delete. Applying a style preserves text and geometry and deep-copies
  effect settings; one targeted test checks this. Style preview bytes are
  cached. The requested preset list has since been filled out as described below.
- Existing editor sections are collapsible with state retained across rebuilds;
  numeric inputs now have synchronized sliders. Individual and full effect
  resets were added. Shadow angle/distance and X/Y controls synchronize; a
  targeted headless check covers them. Layout still requires visual review.
- Added independent inner and outer glow controls including intensity. Inner
  glow is masked with the rasterized glyph. Inner glow, emboss and inset now
  share one mask per shaped run; further mask/geometry optimization remains.
- Added a real curved baseline using per-glyph rotation and curved decorations;
  curvature persists in projects. Selection bounds are approximate and inline
  caret/selection do not yet follow curved, skewed or stretched glyphs.
- Added blur, pixelation, RGB channel shift, metallic and gloss overlays,
  emboss and inset; all have serialized controls and are part of custom styles.
  Targeted rendering/alpha and project round-trip checks passed. The optimized
  premultiplied RGB path was checked for opaque and translucent text. Metallic
  and gloss now use the same glyph processing, and relief uses the processed
  glyph mask; one combined pixelation/metal/gloss/relief alpha check passed.
- Added the remaining named styles, bringing the preset list to 27. They use
  implemented parameters and have visual previews. Visual quality and the
  full range of editable settings still need Windows review.
- Whole-text and per-line backgrounds use bounds of the shaped/rotated text
  blobs and are composited behind the layer; a mixed-script curved text test
  passed. Text selection/effect bounds are still estimated rather than exact.
- Added a bounded cache for unchanged rendered layers. Cached bundled fonts are
  re-resolved and integrity-checked. Cache invalidation was tested; geometry
  and font-face caching, native resource profiling and effect ordering remain.
- Targeted export comparisons passed for PNG/JPG with curvature and effects,
  and for GIF/MP4 at two frames with curvature, gradient, glow, outline,
  background, metallic and emboss. They do not cover all combinations.

## Remaining work

- Visually verify picker theme/focus/dropdowns in both themes and RU/EN. Verify
  the color popup, its placement at small window sizes, alpha and eyedropper for
  stills and playing/paused GIF/MP4. Confirm transparent-pixel behavior.
- Make direct canvas selection, handles and caret agree with stretch, skew,
  case, curvature and the exact bounds of effects. Check curved text with
  ligatures, RTL scripts, emoji, fallback fonts and several lines.
- Verify and correct combinations of all effects, including processed glyphs
  with metallic/gloss/relief and output across multiple shaped runs. Validate
  extrusion with outlines and fallback, and backgrounds for complex scripts.
- Visually review all named presets and ensure custom styles include any
  newly introduced effects.
- Complete the specified section structure and order, including newly added
  effects. Verify both locales and themes and short/narrow window layouts.
- Optimize font, glyph geometry and unchanged-layer caching, with correct
  Skia lifetime management. Evaluate effect compositing over multiple runs:
  the current renderer draws effects run-by-run, so overlapping runs can have
  incorrect ordering. Gradient coordinates currently reset on each line.
- Verify effect bounds and alpha; complete mixed PNG/JPG/GIF/MP4 export checks
  with real FFmpeg and Telegram validation. Add necessary regression tests for
  each completed feature, compatibility and independent layer duplication.
- Run Debug and Release builds and the full test suite once after all features
  are complete, with bundled fonts and FFmpeg configured so integration tests
  are not skipped. Capture current light/dark UI, styles and effects screenshots.
  Produce the Windows x64 standalone package and update Draft PR #15.

The working tree also contains pre-existing modifications to four
`packages.lock.json` files; preserve them separately from this work.
