# Bundled fonts

Dashboard bundle ships Inter (sans) and JetBrains Mono (code) as Latin-subset WOFF2 files.

Expected files:

- `inter-latin.woff2` — Inter variable, weights 400–700, Latin subset + common symbols. Source: [rsms/inter](https://github.com/rsms/inter) v4.1 (`InterVariable.woff2`). License: SIL OFL 1.1.
- `jetbrains-mono-latin.woff2` — JetBrains Mono variable, weights 400–700, Latin subset. Source: [JetBrains/JetBrainsMono](https://github.com/JetBrains/JetBrainsMono) v2.304 (`JetBrainsMono[wght].ttf`). License: Apache 2.0.

Referenced by `tokens/primitives.css` `@font-face` declarations. When absent at runtime, the system fallback stack (declared in `tokens/typography.css`) renders the dashboard without visual regression.

These binaries are staged into the repo (not fetched at runtime) so the dashboard works offline. Re-subset when the upstream fonts change — we do not rev fonts inside minor releases.

## Re-subsetting

```bash
python3 -m venv /tmp/ft && /tmp/ft/bin/pip install fonttools brotli

/tmp/ft/bin/pyftsubset InterVariable.woff2 \
  --output-file=inter-latin.woff2 --flavor=woff2 --layout-features='*' \
  --unicodes='U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+2000-206F,U+2074,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD'

/tmp/ft/bin/pyftsubset 'JetBrainsMono[wght].ttf' \
  --output-file=jetbrains-mono-latin.woff2 --flavor=woff2 --layout-features='*' \
  --unicodes='U+0000-00FF,U+2000-206F,U+2122,U+2212,U+FEFF,U+FFFD'
```
