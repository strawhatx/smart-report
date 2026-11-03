# Vendored front-end libraries

These are served from `wwwroot/lib/` rather than a CDN, because the Content Security Policy
(rule C8) allows `script-src 'self'` only. A policy that permits arbitrary third-party script
origins isn't worth setting, so the trade is deliberate: a slightly larger repository in exchange
for a policy that actually constrains something.

Re-download with the exact URLs below — do not bump a version without checking the changelog and
re-running the motion pass (`docs/motion.md`).

| File | Version | Source |
|---|---|---|
| `gsap/gsap.min.js` | 3.13.0 | `https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/gsap.min.js` |
| `gsap/ScrollTrigger.min.js` | 3.13.0 | `https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/ScrollTrigger.min.js` |
| `gsap/SplitText.min.js` | 3.13.0 | `https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/SplitText.min.js` |
| `gsap/CustomEase.min.js` | 3.13.0 | `https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/CustomEase.min.js` |
| `lenis/lenis.min.js` | 1.1.20 | `https://cdn.jsdelivr.net/npm/lenis@1.1.20/dist/lenis.min.js` |
| `three/three.module.min.js` | r169 | `https://cdn.jsdelivr.net/npm/three@0.169.0/build/three.module.min.js` |

## Notes

- **GSAP 3.13 ships SplitText and CustomEase free.** They were club-only before April 2025, so
  older guidance saying otherwise is out of date.
- **GSAP and Lenis load as classic scripts** (they set `window.gsap`, `window.ScrollTrigger`,
  `window.SplitText`, `window.CustomEase`, `window.Lenis`). No import map is needed.
- **Three.js loads as a single self-contained ES module**, imported by relative path from
  `wwwroot/js/home/schema.js` and only when the hero is actually in view — it is by far the
  largest file here and most visitors never download it.
- No charting library: charts are hand-rolled inline SVG (C9, and it matches the drawing style).
