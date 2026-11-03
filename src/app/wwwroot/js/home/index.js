/**
 * Landing page entry point.
 *
 * Order matters: content is already rendered by the server, so everything here is enhancement.
 * If this file never loads, the page still reads top to bottom — only the motion is missing.
 */

import { capabilities, initMotion, initSmoothScroll, reveal, splitIn, magnetic, failsafeReveal } from './motion.js';
import { runBoot } from './boot.js';
import { initGuard } from './guard.js';

const caps = capabilities();
document.documentElement.classList.add('js');

const gsap = initMotion();
initSmoothScroll(caps);

runBoot(caps, start);

function start() {
  document.querySelectorAll('[data-split]').forEach((el) => splitIn(el, caps));
  reveal('[data-reveal]', caps);
  document.querySelectorAll('[data-magnetic]').forEach((el) => magnetic(el, caps));

  initGuard(caps);
  typeTheQuestion();
  mountScene();
  failsafeReveal();
}

/**
 * [03] The question types itself when the section arrives. Under reduced motion the full text is
 * simply present, which is what it says in the markup anyway.
 */
function typeTheQuestion() {
  const target = document.getElementById('ask-typed');
  if (!target || !gsap || caps.reduced) return;

  const full = target.textContent;
  target.textContent = '';

  window.ScrollTrigger?.create({
    trigger: target,
    start: 'top 78%',
    once: true,
    onEnter: () => {
      const state = { n: 0 };
      gsap.to(state, {
        n: full.length,
        duration: 1.5,
        ease: 'none',
        onUpdate: () => { target.textContent = full.slice(0, Math.round(state.n)); },
      });
    },
  });
}

/**
 * Loads the WebGL scene only when the device qualifies and the hero is actually on screen, so
 * the 687KB of Three.js is never fetched on a phone or under reduced motion.
 */
async function mountScene() {
  const canvas = document.getElementById('schema-canvas');
  const scene = document.querySelector('.scene');
  if (!canvas || !scene) return;

  if (!caps.heavy) {
    scene.classList.add('scene--static');
    return;
  }

  const styles = getComputedStyle(document.documentElement);
  const colour = (name, fallback) => (styles.getPropertyValue(name).trim() || fallback);

  let schema;
  try {
    const { createSchemaScene } = await import('./schema.js');
    schema = createSchemaScene(canvas, {
      ink: colour('--ink', '#0B0D0E'),
      signal: colour('--signal', '#B06E12'),
      strike: colour('--strike', '#A62F24'),
    });
  } catch {
    // No WebGL, or the module failed to load. The still drawing says the same thing.
    scene.classList.add('scene--static');
    return;
  }

  canvas.classList.add('is-live');

  // One scrubbed timeline across sections 01–05, so the scene reads as a single continuous
  // move rather than five separate reactions.
  if (window.ScrollTrigger) {
    window.ScrollTrigger.create({
      trigger: '#s01',
      endTrigger: '#s05',
      start: 'top top',
      end: 'bottom center',
      scrub: true,
      onUpdate: (self) => schema.setProgress(self.progress),
    });

    // The scene dims the restricted tables while the guard section is on screen, and only then.
    window.ScrollTrigger.create({
      trigger: '#s04',
      start: 'top 70%',
      end: 'bottom 30%',
      onEnter: () => applyClearance(),
      onEnterBack: () => applyClearance(),
      onLeave: () => schema.setClearance(1),
      onLeaveBack: () => schema.setClearance(1),
    });
  }

  function applyClearance() {
    const selected = document.querySelector('.role[aria-checked="true"]');
    schema.setClearance(selected?.dataset.role === 'hr_director' ? 1 : 0);
  }

  document.querySelectorAll('.role').forEach((button) => {
    button.addEventListener('click', applyClearance);
  });
}
