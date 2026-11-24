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
- Added 16 working presets and persistent custom styles with preview, rename
  and delete. Applying a style preserves text and geometry and deep-copies
  effect settings; one targeted test checks this. Style preview bytes are
  cached. The full requested preset list still needs implementation.
- Existing editor sections are collapsible with state retained across rebuilds;
  numeric inputs now have synchronized sliders. Individual and full effect
  resets were added. Shadow angle/distance and X/Y controls synchronize; a
  targeted headless check covers them. Layout still requires visual review.
- Added independent inner and outer glow controls including intensity. Inner
  glow is masked with the rasterized glyph and a targeted alpha test passes.
  Its current per-run 512×512 mask allocation needs optimization.

## Remaining work

- Visually verify picker theme/focus/dropdowns in both themes and RU/EN. Verify
  the color popup, its placement at small window sizes, alpha and eyedropper for
  stills and playing/paused GIF/MP4. Confirm transparent-pixel behavior.
- Implement curved text and ensure direct canvas selection, handles and caret
  agree with stretch, skew, case, curvature and effect bounds.
- Finish effects: blur, pixelation, RGB
  offset, metallic, gloss, emboss and inset. Use real serialized settings;
  no stubs. Verify background measurement and avoid overlap between per-line
  backgrounds and other lines; validate extrusion with outlines and fallback.
- Complete all remaining requested named presets using actual implemented
  parameters; ensure custom styles include any newly introduced effects.
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
