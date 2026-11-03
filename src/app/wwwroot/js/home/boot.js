/**
 * [00] The boot sequence.
 *
 * Under two seconds, skippable with Esc / click / scroll, and shown once per session. It never
 * blocks the hero: the headline is already rendered underneath, so a visitor who skips (or whose
 * JavaScript never arrives) loses nothing but the flourish.
 */

import { DUR } from './motion.js';

const STEPS = [
  'INITIALIZE CORE',
  'MOUNT CATALOG',
  'RESOLVE MODELS',
  'LOAD METRICS',
  'LOAD DIMENSIONS',
  'BIND POLICY ENGINE',
  'ATTACH ROW SECURITY',
  'VERIFY PRINCIPAL',
  'WARM PLANNER',
  'READY',
];

const SESSION_KEY = 'sr.boot.seen';

export function runBoot({ reduced }, onDone) {
  const root = document.getElementById('boot');
  if (!root) return onDone();

  let seen = false;
  try {
    seen = sessionStorage.getItem(SESSION_KEY) === '1';
  } catch {
    // Private browsing or blocked storage: treat it as a first visit rather than failing.
  }

  if (reduced || seen) return onDone();

  root.hidden = false;
  document.documentElement.style.overflow = 'hidden';

  const lines = document.getElementById('boot-lines');
  const fill = document.getElementById('boot-fill');
  const pct = document.getElementById('boot-pct');
  const skip = document.getElementById('boot-skip');

  let finished = false;
  const perStep = (DUR.boot * 1000) / STEPS.length;

  const timers = [];

  STEPS.forEach((step, i) => {
    timers.push(setTimeout(() => {
      if (finished) return;
      const row = document.createElement('span');
      row.innerHTML = `<b>${String(i + 1).padStart(3, '0')}</b>${step}`;
      lines.appendChild(row);

      const progress = Math.round(((i + 1) / STEPS.length) * 100);
      fill.style.width = `${progress}%`;
      pct.textContent = String(progress).padStart(3, '0');
    }, i * perStep));
  });

  timers.push(setTimeout(finish, DUR.boot * 1000 + 180));

  function finish() {
    if (finished) return;
    finished = true;

    timers.forEach(clearTimeout);
    try {
      sessionStorage.setItem(SESSION_KEY, '1');
    } catch { /* see above */ }

    document.documentElement.style.overflow = '';
    window.removeEventListener('keydown', onKey);
    window.removeEventListener('wheel', finish);
    window.removeEventListener('touchmove', finish);

    const gsap = window.gsap;
    if (gsap) {
      gsap.to(root, {
        opacity: 0,
        duration: 0.44,
        ease: 'sr',
        onComplete: () => { root.hidden = true; onDone(); },
      });
    } else {
      root.hidden = true;
      onDone();
    }
  }

  function onKey(event) {
    if (event.key === 'Escape') finish();
  }

  skip?.addEventListener('click', finish);
  root.addEventListener('click', finish);
  window.addEventListener('keydown', onKey);
  window.addEventListener('wheel', finish, { passive: true });
  window.addEventListener('touchmove', finish, { passive: true });
}
