# Motion inventory

Every animation on the site, where it lives, what triggers it, and its timing — so you can retune
it without reading the JavaScript.

## The system

One ease and four durations, defined once in [`wwwroot/js/home/motion.js`](../src/app/wwwroot/js/home/motion.js).
Change them there and the whole site moves with you.

| Token | Value | Used for |
|---|---|---|
| `EASE` (`CustomEase "sr"`) | `M0,0 C0.16,1 0.3,1 1,1` | every entrance |
| `power2.inOut` | GSAP built-in | scrubbed, scroll-linked motion |
| `DUR.micro` | 0.24s | hovers, chips, stream items |
| `DUR.reveal` | 0.60s | section reveals |
| `DUR.hero` | 1.20s | headline lines |
| `DUR.boot` | 1.80s | the whole boot sequence |

CSS mirrors these as `--ease-out`, `--ease-in-out`, `--dur-micro`, `--dur-reveal` in
[`wwwroot/css/tokens.css`](../src/app/wwwroot/css/tokens.css).

**One RAF loop.** Lenis is driven from `gsap.ticker` rather than its own `requestAnimationFrame`.
Two competing loops is the usual cause of scroll-linked jitter.

---

## Landing page

| § | Animation | File | Trigger | Timing |
|---|---|---|---|---|
| `[00]` | Boot lines appear 001→010, meter fills, percentage counts | `boot.js` | page load, once per session | 1.8s total, 180ms per line |
| `[00]` | Boot overlay fades out | `boot.js` | sequence end, Esc, click, wheel or touch | 0.44s · `sr` |
| `[01]` | Headline reveals line by line (SplitText) | `motion.js` → `splitIn` | **on load** — it is above the fold | 1.2s · `sr` · 0.075s stagger |
| `[01]` | Pillars fade and lift | `motion.js` → `reveal` | scroll, 82% viewport, once | 0.6s · `sr` · 0.07s stagger |
| `[02]` | Problem cards fade and lift | `motion.js` → `reveal` | scroll, 82% viewport, once | 0.6s · `sr` · 0.07s stagger |
| `[03]` | Question types itself character by character | `index.js` → `typeTheQuestion` | scroll, 78% viewport, once | 1.5s · linear |
| `[03]` | Cursor blink | `home.css` `@keyframes blink` | always | 1.1s · steps(2) · infinite |
| `[04]` | Result tables rewrite on role change | `guard.js` → `paint` | click or arrow keys on a role chip | 0.3s · `sr` · 0.035s stagger |
| `[04]` | Role chip border and wash | `home.css` `.role` | `aria-checked` | 0.24s · `--ease-out` |
| `[01]`–`[05]` | Camera pushes through the schema; query path draws | `schema.js` | scrubbed from `#s01` top to `#s05` bottom | follows scroll position |
| `[04]` | Restricted tables shut with a red strike | `schema.js` → `setClearance` | entering `#s04`, and on role change | immediate |
| `[05]`–`[07]` | Provenance, flow and step reveals | `motion.js` → `reveal` | scroll, 82% viewport, once | 0.6s · `sr` |
| all | Magnetic pull on primary buttons | `motion.js` → `magnetic` | pointer move within the button | 0.4s follow · `sr` |

### The signature scene

[`wwwroot/js/home/schema.js`](../src/app/wwwroot/js/home/schema.js). Wireframe table slabs in
depth, with a tube traced along a curve through `orders → order_items → regions`.

- One scrubbed `ScrollTrigger` spans sections 01–05, so the whole sequence reads as one continuous
  move rather than five separate reactions.
- Camera `z` travels 7.4 → 4.0; the path reveals between 18% and 60% of that progress.
- Idle drift is driven by elapsed time, not scroll, so the scene is never completely still.

---

## Reports workspace

| Animation | File | Trigger | Timing |
|---|---|---|---|
| Conversation items slide in as events arrive | `reports/workspace.js` → `addStep` | each SSE event | 0.24s · `power2.out` |
| Report sections stagger in | `reports/workspace.js` → `renderReport` | `document` event | 0.42s · `power2.out` · 0.06s stagger |
| Chart bar highlight | `reports.css` `.mark` | row hover | 0.24s · `--ease-out` |
| Tooltip fade | `reports.css` `.chart__tip` | pointer enter/leave | 120ms linear |
| Gallery cards stagger in | `reports/gallery.js` | scroll, once | 0.6s · `sr` · 0.05s stagger |
| Card lift on hover | `reports.css` `.report-card__link` | hover | 0.24s · `--ease-out` |

---

## When motion is reduced or the device is small

`capabilities()` in `motion.js` decides, and everything downstream respects it.

| Condition | Effect |
|---|---|
| `prefers-reduced-motion: reduce` | no boot, no tweening, no Lenis, no WebGL. **All content and all state changes remain** — role switching still works, it cuts instead of animating. |
| viewport < 860px | WebGL replaced by the static drawing; drag has a tap equivalent everywhere. |
| `hardwareConcurrency <= 4` | same as above — a four-core laptop renders the still drawing. |
| WebGL unavailable or the module fails to load | caught, falls back to the static drawing. |

CSS also collapses every transition and animation to ~0s under `prefers-reduced-motion`, which
catches anything added later that forgets to ask.

### The failsafe

Every entrance hides its target and reveals it on a trigger, so a trigger that never fires would
leave content permanently invisible. `failsafeReveal()` runs 2.6s after start and shows anything
still hidden.

This is not theoretical: the hero headline was invisible on first build because its ScrollTrigger
hadn't fired. The headline now animates on load rather than on scroll, **and** the failsafe backs
it up. A missing animation is a blemish; missing content is a broken page.
