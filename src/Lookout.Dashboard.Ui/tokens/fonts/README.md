# Bundled fonts

Dashboard bundle ships Inter (sans) and Geist Mono (code) as Latin-subset WOFF2 files.

Expected files:

- `inter-latin.woff2` — Inter variable, weights 400–700, Latin subset + common symbols.
- `geist-mono-latin.woff2` — Geist Mono variable, weights 400–600, Latin subset.

Referenced by `tokens/primitives.css` `@font-face` declarations. When absent at runtime, the system fallback stack (declared in `tokens/typography.css`) renders the dashboard without visual regression.

These binaries are staged into the repo (not fetched at runtime) so the dashboard works offline. Re-subset when the upstream fonts change — we do not rev fonts inside minor releases.
