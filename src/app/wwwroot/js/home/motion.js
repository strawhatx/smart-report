/**
 * The motion system.
 *
 * Every animation on the landing page routes through this module, so timing and easing are
 * changeable in one place rather than scattered across page scripts. See docs/motion.md for the
 * full inventory.
 */

/** One custom ease used for every entrance, so the whole page moves like one system. */
export const EASE = 'sr';

export const DUR = {
  micro: 0.24,
  reveal: 0.6,
  hero: 1.2,
  boot: 1.8,
};

/**
 * What this device and this person should get.
 *
 * `reduced` disables tweening entirely but never removes content or state: the guard section
 * still switches roles, it just cuts instead of animating.
 *
 * `heavy` gates the WebGL scene. A phone or a four-core laptop renders the still drawing, which
 * says the same thing without spending a frame budget it doesn't have.
 */
export function capabilities() {
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const small = window.matchMedia('(max-width: 860px)').matches;
  const weak = (navigator.hardwareConcurrency || 8) <= 4;

  return { reduced, heavy: !reduced && !small && !weak };
}

let registered = false;

/** Registers plugins and the shared ease. Safe to call more than once. */
export function initMotion() {
  if (registered || !window.gsap) return window.gsap;

  const { gsap, ScrollTrigger, CustomEase } = window;

  if (CustomEase) {
    gsap.registerPlugin(CustomEase);
    CustomEase.create(EASE, 'M0,0 C0.16,1 0.3,1 1,1');
  }
  if (ScrollTrigger) gsap.registerPlugin(ScrollTrigger);
  if (window.SplitText) gsap.registerPlugin(window.SplitText);

  registered = true;
  return gsap;
}

/**
 * Drives Lenis from GSAP's ticker so the page has exactly one requestAnimationFrame loop —
 * two competing loops is the usual cause of scroll-linked jitter.
 */
export function initSmoothScroll({ reduced }) {
  if (reduced || !window.Lenis || !window.gsap) return null;

  const lenis = new window.Lenis({ duration: 1.05, smoothWheel: true });
  const { gsap, ScrollTrigger } = window;

  lenis.on('scroll', () => ScrollTrigger && ScrollTrigger.update());
  gsap.ticker.add((time) => lenis.raf(time * 1000));
  gsap.ticker.lagSmoothing(0);

  return lenis;
}

/** Fades and lifts an element as it enters. */
export function reveal(targets, { reduced }, options = {}) {
  const gsap = window.gsap;
  if (!gsap) return;

  const elements = gsap.utils.toArray(targets);
  if (!elements.length) return;

  if (reduced) {
    gsap.set(elements, { opacity: 1, y: 0 });
    return;
  }

  gsap.from(elements, {
    opacity: 0,
    y: 18,
    duration: DUR.reveal,
    ease: EASE,
    stagger: options.stagger ?? 0.07,
    scrollTrigger: { trigger: options.trigger || elements[0], start: 'top 82%', once: true },
  });
}

/**
 * Reveals a headline line by line.
 *
 * Anything already on screen at load animates straight away rather than waiting for a scroll
 * trigger — a headline that depends on a scroll event to become visible is a headline that can
 * fail to appear at all.
 *
 * Falls back to a plain fade if SplitText isn't available, and reverts the split afterwards so
 * screen readers and text selection get the original markup back rather than a pile of divs.
 */
export function splitIn(element, { reduced }) {
  const gsap = window.gsap;
  if (!gsap || !element) return;

  if (reduced || !window.SplitText) {
    gsap.set(element, { opacity: 1 });
    return;
  }

  const split = new window.SplitText(element, { type: 'lines', linesClass: 'split-line' });
  const inView = element.getBoundingClientRect().top < window.innerHeight * 0.9;

  const tween = {
    opacity: 0,
    yPercent: 108,
    duration: DUR.hero,
    ease: EASE,
    stagger: 0.075,
    onComplete: () => split.revert(),
  };

  if (inView) {
    gsap.from(split.lines, tween);
  } else {
    gsap.from(split.lines, {
      ...tween,
      scrollTrigger: { trigger: element, start: 'top 86%', once: true },
    });
  }
}

/**
 * Last line of defence.
 *
 * Every entrance above hides its target first and reveals it on a trigger. If a trigger never
 * fires — a stalled ScrollTrigger refresh, a prerender, a headless screenshot — the content
 * would stay invisible, which is far worse than losing the animation. After a short grace
 * period anything still hidden is simply shown.
 */
export function failsafeReveal(delayMs = 2600) {
  setTimeout(() => {
    document.querySelectorAll('[data-split], [data-reveal], .split-line').forEach((node) => {
      const { opacity } = getComputedStyle(node);
      if (Number(opacity) < 0.98) {
        node.style.opacity = '1';
        node.style.transform = 'none';
      }
    });
  }, delayMs);
}

/** A magnetic pull toward the cursor. Pointer-only; it is simply absent on touch. */
export function magnetic(element, { reduced }) {
  if (reduced || !window.gsap) return;
  if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return;

  const strength = 0.32;
  const quickX = window.gsap.quickTo(element, 'x', { duration: 0.4, ease: EASE });
  const quickY = window.gsap.quickTo(element, 'y', { duration: 0.4, ease: EASE });

  element.addEventListener('pointermove', (event) => {
    const box = element.getBoundingClientRect();
    quickX((event.clientX - (box.left + box.width / 2)) * strength);
    quickY((event.clientY - (box.top + box.height / 2)) * strength);
  });

  element.addEventListener('pointerleave', () => {
    quickX(0);
    quickY(0);
  });
}
