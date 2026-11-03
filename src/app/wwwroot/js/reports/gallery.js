/**
 * Gallery enhancements. The grid is server-rendered and fully usable without this file —
 * all it adds is the entrance stagger and the magnetic pull on the primary button.
 */

import { capabilities, initMotion, reveal, magnetic } from '../home/motion.js';

const caps = capabilities();
document.documentElement.classList.add('js');
initMotion();

reveal('.report-card', caps, { stagger: 0.05 });
document.querySelectorAll('[data-magnetic]').forEach((el) => magnetic(el, caps));
