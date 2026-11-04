/**
 * Hand-rolled inline SVG charts.
 *
 * Specs held to: bars capped at 24px with a 4px rounded data end square to the baseline, 2px
 * lines with round caps, end markers at r=4 inside a 2px surface ring, hairline solid gridlines,
 * values labelled selectively, and every piece of text in a text token rather than the data
 * colour. Two measures of different scale are never put on one plot — the workspace renders one
 * chart per measure, because a second y-axis invents a correlation the data does not contain.
 *
 * Colours come from the four validated theme slots (see docs/palette.md). They are assigned by
 * slot and never by rank, so filtering a series out cannot repaint the survivors.
 */

const SVG = 'http://www.w3.org/2000/svg';
const BAR_MAX = 24;

/**
 * Written out in full, not built with a template string.
 *
 * Tailwind generates utilities by scanning source text, so `fill-series-${i}` produces a class
 * that does not exist and a shape with no colour. These literals are what put the four validated
 * slots in the stylesheet. Order is fixed: colour follows the entity, never its rank.
 */
const FILL_SLOTS = ['fill-series-1', 'fill-series-2', 'fill-series-3', 'fill-series-4'];
const DOT_SLOTS = ['bg-series-1', 'bg-series-2', 'bg-series-3', 'bg-series-4'];

const el = (name, attrs = {}) => {
  const node = document.createElementNS(SVG, name);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  return node;
};

export function formatValue(value, unit) {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';

  if (unit === 'percent') return `${value.toFixed(1)}%`;
  if (unit === 'days') return `${value.toFixed(1)} days`;

  const prefix = unit === 'currency' ? '$' : '';
  const abs = Math.abs(value);

  if (abs >= 1_000_000) return `${prefix}${(value / 1_000_000).toFixed(2)}M`;
  if (abs >= 10_000) return `${prefix}${(value / 1_000).toFixed(1)}k`;
  if (unit === 'currency') return prefix + value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  if (Number.isInteger(value)) return prefix + value.toLocaleString('en-US');
  return prefix + value.toFixed(1);
}

/* ─────────────────────────── tooltip ─────────────────────────── */

let tip;
function tooltip() {
  if (!tip) {
    tip = document.createElement('div');
    tip.className =
      'pointer-events-none fixed z-50 rounded-md bg-ink px-2.5 py-1.5 text-[12px] '
      + 'font-medium text-paper opacity-0 transition-opacity duration-100 whitespace-nowrap';
    document.body.appendChild(tip);
  }
  return tip;
}

function bindTip(target, text) {
  const move = (event) => {
    const node = tooltip();
    node.style.left = `${Math.min(event.clientX + 14, window.innerWidth - 180)}px`;
    node.style.top = `${event.clientY - 36}px`;
  };

  target.addEventListener('pointerenter', (event) => {
    const node = tooltip();
    node.textContent = text;
    node.classList.remove('opacity-0');
    move(event);
  });
  target.addEventListener('pointermove', move);
  target.addEventListener('pointerleave', () => tooltip().classList.add('opacity-0'));
}

/* ─────────────────────────── bar ─────────────────────────── */

/**
 * Ranked horizontal bars with emphasis: the largest wears the accent, the rest a single hue.
 *
 * Emphasis rather than a hue per category — the story is "one carrier accounts for most of it",
 * and giving every bar its own colour would bury that under a legend.
 */
export function barChart(series) {
  const points = series.points.filter((p) => p.value !== null);
  if (!points.length) return null;

  const labelWidth = 150;
  const valueWidth = 56;
  const row = 36;
  const width = 640;
  const height = points.length * row;
  const plot = width - labelWidth - valueWidth;
  const max = Math.max(...points.map((p) => Math.abs(p.value))) || 1;

  const svg = el('svg', {
    viewBox: `0 0 ${width} ${height}`,
    role: 'img',
    'aria-label': `${series.label} by category`,
    class: 'w-full',
  });

  points.forEach((point, i) => {
    const y = i * row;
    const h = Math.min(BAR_MAX, row - 12);
    const barY = y + (row - h) / 2;
    const w = Math.max(3, (Math.abs(point.value) / max) * plot);
    const isTop = Math.abs(point.value) === max;

    const label = el('text', {
      x: labelWidth - 14, y: barY + h / 2 + 4, 'text-anchor': 'end',
      class: 'fill-ink-soft text-[13px]',
    });
    label.textContent = point.label.length > 20 ? `${point.label.slice(0, 19)}…` : point.label;
    svg.appendChild(label);

    // A track behind every bar, so a short bar still reads as a proportion.
    svg.appendChild(el('rect', {
      x: labelWidth, y: barY, width: plot, height: h, rx: 4, class: 'fill-sunk',
    }));

    svg.appendChild(el('path', {
      d: roundedBar(labelWidth, barY, w, h),
      class: isTop ? 'fill-series-4' : 'fill-series-1',
    }));

    const value = el('text', {
      x: width - 6, y: barY + h / 2 + 4, 'text-anchor': 'end',
      class: 'fill-ink text-[13px] font-semibold tabular-nums',
    });
    value.textContent = formatValue(point.value, series.unit);
    svg.appendChild(value);

    // The hit target is the whole row: a 3px bar is impossible to hover.
    const hit = el('rect', { x: 0, y, width, height: row, class: 'fill-transparent' });
    bindTip(hit, `${point.label} · ${formatValue(point.value, series.unit)}`);
    svg.appendChild(hit);
  });

  return svg;
}

/** Square at the baseline, 4px rounded at the data end. */
function roundedBar(x, y, width, height) {
  const r = Math.min(4, width / 2, height / 2);
  return `M${x},${y} H${x + width - r} A${r},${r} 0 0 1 ${x + width},${y + r} `
       + `V${y + height - r} A${r},${r} 0 0 1 ${x + width - r},${y + height} H${x} Z`;
}

/* ─────────────────────────── donut ─────────────────────────── */

/**
 * A whole split into at most four parts, read at a glance, with the total in the middle.
 *
 * This is the one shape a donut is defensible for: few segments, widely separated values, and a
 * figure in the centre that the ring is a breakdown of. Anything closer or more numerous becomes
 * ranked bars instead.
 */
export function donutChart(series, { total, totalLabel = 'orders' } = {}) {
  const points = series.points.filter((p) => p.value !== null && p.value > 0).slice(0, 4);
  if (!points.length) return null;

  const sum = total ?? points.reduce((acc, p) => acc + p.value, 0);
  const size = 220;
  const centre = size / 2;
  const radius = 92;
  const thickness = 34;

  const wrap = document.createElement('div');
  wrap.className = 'flex flex-wrap items-center gap-6';

  const svg = el('svg', {
    viewBox: `0 0 ${size} ${size}`,
    role: 'img',
    'aria-label': `${series.label} split into ${points.length} parts`,
    class: 'size-[200px] shrink-0',
  });

  // A 2px gap in the surface colour separates neighbours — white doing the separating, rather
  // than a stroke, which would add ink that isn't data.
  const gapDegrees = 2;
  let angle = -90;

  points.forEach((point, i) => {
    const sweep = (point.value / sum) * 360;
    const path = el('path', {
      d: arc(centre, radius, thickness, angle + gapDegrees / 2, angle + sweep - gapDegrees / 2),
      class: FILL_SLOTS[i],
    });
    bindTip(path, `${point.label} · ${formatValue(point.value, series.unit)} (${Math.round((point.value / sum) * 100)}%)`);
    svg.appendChild(path);
    angle += sweep;
  });

  const totalText = el('text', {
    x: centre, y: centre - 2, 'text-anchor': 'middle',
    class: 'fill-ink text-[26px] font-bold tabular-nums',
  });
  totalText.textContent = formatValue(sum, series.unit);
  svg.appendChild(totalText);

  const caption = el('text', {
    x: centre, y: centre + 18, 'text-anchor': 'middle', class: 'fill-ink-faint text-[12px]',
  });
  caption.textContent = totalLabel;
  svg.appendChild(caption);

  // The legend is the dependable identity channel — never make the reader match colours alone.
  const legend = document.createElement('ul');
  legend.className = 'min-w-0 flex-1 space-y-2.5';

  points.forEach((point, i) => {
    const item = document.createElement('li');
    item.className = 'flex items-center gap-2.5 text-[14px]';
    item.innerHTML =
      `<span class="size-2.5 shrink-0 rounded-full ${DOT_SLOTS[i]}"></span>`
      + `<span class="truncate text-ink-soft">${escapeHtml(point.label)}</span>`
      + `<span class="ml-auto font-semibold tabular-nums">${Math.round((point.value / sum) * 100)}%</span>`;
    legend.appendChild(item);
  });

  wrap.append(svg, legend);
  return wrap;
}

function arc(centre, radius, thickness, startDeg, endDeg) {
  const inner = radius - thickness;
  const toXY = (r, deg) => {
    const rad = (deg * Math.PI) / 180;
    return [centre + r * Math.cos(rad), centre + r * Math.sin(rad)];
  };

  const [x1, y1] = toXY(radius, startDeg);
  const [x2, y2] = toXY(radius, endDeg);
  const [x3, y3] = toXY(inner, endDeg);
  const [x4, y4] = toXY(inner, startDeg);
  const large = endDeg - startDeg > 180 ? 1 : 0;

  return `M${x1},${y1} A${radius},${radius} 0 ${large} 1 ${x2},${y2} `
       + `L${x3},${y3} A${inner},${inner} 0 ${large} 0 ${x4},${y4} Z`;
}

/* ─────────────────────────── line ─────────────────────────── */

/**
 * One or two series over time. A second series is distinguished by a dashed stroke as well as
 * colour, so the comparison survives for a reader who cannot separate the two hues.
 */
export function lineChart(lines, unit) {
  const usable = lines.filter((l) => l.points.length > 1);
  if (!usable.length) return null;

  const width = 900;
  const height = 300;
  const pad = { top: 16, right: 24, bottom: 34, left: 56 };
  const plotW = width - pad.left - pad.right;
  const plotH = height - pad.top - pad.bottom;

  const all = usable.flatMap((l) => l.points.map((p) => p.value ?? 0));
  const scale = niceScale(Math.min(...all), Math.max(...all), 4);
  const min = scale.min;
  const span = (scale.max - scale.min) || 1;
  const length = Math.max(...usable.map((l) => l.points.length));

  const x = (i) => pad.left + (i / (length - 1)) * plotW;
  const y = (v) => pad.top + plotH - ((v - min) / span) * plotH;
  const compact = (!unit || unit === 'number') && scale.max >= 10_000;

  const svg = el('svg', {
    viewBox: `0 0 ${width} ${height}`,
    role: 'img',
    'aria-label': usable.map((l) => l.label).join(' and ') + ' over time',
    class: 'w-full',
  });

  for (let value = scale.min; value <= scale.max + 1e-9; value += scale.step) {
    const gy = y(value);
    svg.appendChild(el('line', {
      x1: pad.left, y1: gy, x2: pad.left + plotW, y2: gy, class: 'stroke-rule', 'stroke-width': 1,
    }));

    const tick = el('text', {
      x: pad.left - 12, y: gy + 4, 'text-anchor': 'end', class: 'fill-ink-faint text-[12px] tabular-nums',
    });
    // One style for the whole axis. Mixing "8,000" with "10.0k" makes the reader re-read the scale.
    tick.textContent = compact
      ? `${(value / 1000).toFixed(0)}k`
      : formatValue(value, unit);
    svg.appendChild(tick);
  }

  usable.forEach((line, index) => {
    const d = line.points.map((p, i) => `${i ? 'L' : 'M'}${x(i)},${y(p.value ?? 0)}`).join(' ');
    svg.appendChild(el('path', {
      d,
      fill: 'none',
      'stroke-width': 2,
      'stroke-linecap': 'round',
      'stroke-linejoin': 'round',
      ...(line.dashed ? { 'stroke-dasharray': '6 5', class: 'stroke-ink-faint' } : { class: 'stroke-series-1' }),
    }));

    if (!line.dashed) {
      const last = line.points.length - 1;
      svg.appendChild(el('circle', {
        cx: x(last), cy: y(line.points[last].value ?? 0), r: 4,
        class: 'fill-series-1 stroke-surface', 'stroke-width': 2,
      }));
    }

    void index;
  });

  const labels = usable[0].points;
  [0, Math.floor((length - 1) / 2), length - 1].forEach((i) => {
    if (!labels[i]) return;
    const text = el('text', {
      x: x(i), y: height - 10,
      'text-anchor': i === 0 ? 'start' : i === length - 1 ? 'end' : 'middle',
      class: 'fill-ink-faint text-[12px]',
    });
    text.textContent = labels[i].label;
    svg.appendChild(text);
  });

  const band = plotW / Math.max(1, length - 1);
  labels.forEach((point, i) => {
    const hit = el('rect', {
      x: x(i) - band / 2, y: pad.top, width: band, height: plotH, class: 'fill-transparent',
    });
    bindTip(hit, usable
      .map((l) => `${l.label} ${point.label}: ${formatValue(l.points[i]?.value, unit)}`)
      .join(' · '));
    svg.appendChild(hit);
  });

  // A legend is the dependable identity channel: with two series a reader must never have to
  // match colours alone. The dash on the earlier year carries it even without colour.
  if (usable.length < 2) {
    return svg;
  }

  const wrap = document.createElement('div');
  const legend = document.createElement('ul');
  legend.className = 'mb-3 flex flex-wrap gap-x-5 gap-y-1.5 text-[13px]';

  for (const line of usable) {
    const item = document.createElement('li');
    item.className = 'flex items-center gap-2 text-ink-soft';
    item.innerHTML = line.dashed
      ? '<span class="h-0 w-5 border-t-2 border-dashed border-ink-faint"></span>'
      : '<span class="h-0 w-5 border-t-2 border-series-1"></span>';
    item.append(document.createTextNode(line.label));
    legend.appendChild(item);
  }

  wrap.append(legend, svg);
  return wrap;
}

/**
 * Axis ticks on round numbers.
 *
 * An axis labelled 6924.7 makes the reader do arithmetic to place a point. Steps of 1, 2 or 5
 * times a power of ten are the ones people read without thinking.
 */
function niceScale(min, max, targetSteps) {
  const roughStep = (max - min) / targetSteps || 1;
  const magnitude = 10 ** Math.floor(Math.log10(roughStep));
  const normalized = roughStep / magnitude;
  const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;

  return {
    min: Math.floor(min / step) * step,
    max: Math.ceil(max / step) * step,
    step,
  };
}

export function escapeHtml(value) {
  const node = document.createElement('span');
  node.textContent = value;
  return node.innerHTML;
}
