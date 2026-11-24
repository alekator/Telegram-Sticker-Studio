# Bundled fonts

The preview package includes 220 families (442 font files), selected from the
[Google Fonts repository](https://github.com/google/fonts) at commit
`06bfe0d62a98375cb09c942fa18e68bd32cff057`. The checked-in
`eng/fonts-manifest.json` records each original file's SHA-256, size, family,
style and declared writing-system subsets. Every family includes its original
`OFL.txt`; no font file is modified. Metadata declares Cyrillic support for 60
families. Font files total 367,258,164 bytes before compression.

Run `eng/prepare-fonts.ps1` to fetch the pinned source and verify every font and
license. The packaging script runs it automatically and puts the catalog under
`fonts/`. Nothing is downloaded at application startup or during export.
Projects refer to catalog-relative identifiers; missing, unlisted or modified
selected font files produce an error instead of silently selecting a different
face. System fonts remain available.

The installed catalog contains individual static and variable font files. The
current controls select files and their default variable-font instance; arbitrary
variation axes are not yet exposed. Complex emoji sequences and all combinations
of fonts, writing systems and bidirectional text still need Windows review.
Inline editing shows the shared rendered text underneath a transparent input;
the selected font file is registered lazily with Avalonia. Caret placement may
still differ for custom letter spacing, line spacing and complex fallback runs.
