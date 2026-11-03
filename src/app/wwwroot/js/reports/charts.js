/**
 * Hand-rolled inline SVG charts.
 *
 * Every chart here plots a single series, which settles most of the design questions: one
 * colour, no legend (the title names what is plotted), and emphasis — amber — reserved for the
 * mark under the cursor.
 *
 * Two measures of different scale are never put on one plot. The workspace renders one chart per
 * measure instead, because a second y-axis invents a correlation the data doesn't contain.
 *
 * Specs held to: bars capped at 24px with a 4px rounded data-end square to the baseline, a 2px
 * surface gap between neighbours, 2px lines with round caps, end markers at r=4 inside a 2px
 * surface ring, hairline solid gridlines, and values labelled selectively rather than on every
 * mark. Text always wears a text token, never the data colour.
 */

const BAR_MAX = 24;
const BAR_GAP = 2;
const SVG = 'http://www.w3.org/2000/svg';

const el = (name, attrs = {}) => {
  const node = document.createElementNS(SVG, name);
  for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
  return node;
};

/** Compact above 10,000, in full below it — one style per axis. */
export function formatValue(value, unit) {
  if (value === null || value === undefined) return '—';

  const prefix = unit === 'currency' ? '$' : '';
  const abs = Math.abs(value);

  if (abs >= 1_000_000) return `${prefix}${(value / 1_000_000).toFixed(2)}M`;
  if (abs >= 10_000) return `${prefix}${(value / 1_000).toFixed(1)}K`;
  if (abs >= 1_000) return prefix + Math.round(value).toLocaleString('en-US');
  if (Number.isInteger(value)) return prefix + value.toLocaleString('en-US');
  return prefix + value.toFixed(2);
}

/* ─────────────────────────── tooltip ─────────────────────────── */

let tip;
function tooltip() {
  if (!tip) {
    tip = document.createElement('div');
    tip.className = 'chart__tip';
    tip.setAttribute('role', 'presentation');
    document.body.appendChild(tip);
  }
  return tip;
}

function bindTip(target, text) {
  target.addEventListener('pointerenter', (event) => {
    const node = tooltip();
    node.textContent = text;
    node.classList.add('is-visible');
    move(event);
  });
  target.addEventListener('pointermove', move);
  target.addEventListener('pointerleave', () => tooltip().classList.remove('is-visible'));

  function move(event) {
    const node = tooltip();
    node.style.left = `${event.clientX + 14}px`;
    node.style.top = `${event.clientY - 34}px`;
  }
}

/* ─────────────────────────── bar chart ─────────────────────────── */

/**
 * Ranked horizontal bars. Horizontal because category names here are words, not dates, and a
 * word reads better beside its bar than rotated under a column.
 */
export function barChart(series) {
  const points = series.points.filter((p) => p.value !== null);
  if (!points.length) return null;

  const labelWidth = 128;
  const valueWidth = 76;
  const rowHeight = Math.min(BAR_MAX + 16, 40);
  const height = points.length * rowHeight + 10;
  const width = 720;
  const plotWidth = width - labelWidth - valueWidth;

  const max = Math.max(...points.map((p) => Math.abs(p.value))) || 1;
  const svg = el('svg', {
    viewBox: `0 0 ${width} ${height}`,
    role: 'img',
    'aria-label': `${series.label} by category`,
  });

  points.forEach((point, i) => {
    const y = i * rowHeight;
    const barHeight = Math.min(BAR_MAX, rowHeight - BAR_GAP * 2 - 6);
    const barY = y + (rowHeight - barHeight) / 2;
    const barWidth = Math.max(2, (Math.abs(point.value) / max) * plotWidth);

    const label = el('text', {
      x: labelWidth - 12, y: barY + barHeight / 2 + 4,
      'text-anchor': 'end', class: 'axis-text',
    });
    label.textContent = point.label.length > 18 ? `${point.label.slice(0, 17)}…` : point.label;
    svg.appendChild(label);

    const bar = el('path', { d: roundedBar(labelWidth, barY, barWidth, barHeight), class: 'mark' });
    svg.appendChild(bar);

    // Value at the tip — the one label a bar always gets.
    const value = el('text', {
      x: labelWidth + barWidth + 10, y: barY + barHeight / 2 + 4, class: 'value-text',
    });
    value.textContent = formatValue(point.value, series.unit);
    svg.appendChild(value);

    // The hit target is the whole row, not the bar: a 2px bar is impossible to hover.
    const hit = el('rect', { x: 0, y, width, height: rowHeight, class: 'hit' });
    hit.addEventListener('pointerenter', () => bar.classList.add('is-active'));
    hit.addEventListener('pointerleave', () => bar.classList.remove('is-active'));
    bindTip(hit, `${point.label} · ${formatValue(point.value, series.unit)}`);
    svg.appendChild(hit);
  });

  return svg;
}

/** Square at the baseline (left), 4px rounded at the data end (right). */
function roundedBar(x, y, width, height) {
  const r = Math.min(4, width / 2, height / 2);
  return `M${x},${y} H${x + width - r} A${r},${r} 0 0 1 ${x + width},${y + r} `
       + `V${y + height - r} A${r},${r} 0 0 1 ${x + width - r},${y + height} H${x} Z`;
}

/* ─────────────────────────── line chart ─────────────────────────── */

export function lineChart(series) {
  const points = series.points;
  if (points.length < 2) return null;

  const width = 720;
  const height = 240;
  const pad = { top: 18, right: 74, bottom: 28, left: 56 };
  const plotW = width - pad.left - pad.right;
  const plotH = height - pad.top - pad.bottom;

  const values = points.map((p) => p.value ?? 0);
  const max = Math.max(...values);
  const min = Math.min(0, ...values);
  const span = max - min || 1;

  const x = (i) => pad.left + (i / (points.length - 1)) * plotW;
  const y = (v) => pad.top + plotH - ((v - min) / span) * plotH;

  const svg = el('svg', {
    viewBox: `0 0 ${width} ${height}`,
    role: 'img',
    'aria-label': `${series.label} over time`,
  });

  // Recessive gridlines, hairline and solid, with the values the line isn't labelled with.
  for (let i = 0; i <= 3; i++) {
    const value = min + (span / 3) * i;
    const gy = y(value);
    svg.appendChild(el('line', { x1: pad.left, y1: gy, x2: pad.left + plotW, y2: gy, class: 'grid' }));

    const tick = el('text', { x: pad.left - 10, y: gy + 4, 'text-anchor': 'end', class: 'axis-text' });
    tick.textContent = formatValue(value, series.unit);
    svg.appendChild(tick);
  }

  const d = points.map((p, i) => `${i ? 'L' : 'M'}${x(i)},${y(p.value ?? 0)}`).join(' ');
  svg.appendChild(el('path', { d, class: 'line' }));

  // End marker with a surface ring, plus the single value label the line carries.
  const lastIndex = points.length - 1;
  svg.appendChild(el('circle', { cx: x(lastIndex), cy: y(points[lastIndex].value ?? 0), r: 4, class: 'line-end' }));

  const endLabel = el('text', {
    x: x(lastIndex) + 12, y: y(points[lastIndex].value ?? 0) + 4, class: 'value-text',
  });
  endLabel.textContent = formatValue(points[lastIndex].value, series.unit);
  svg.appendChild(endLabel);

  // Only the ends and the middle get an x label, so they never collide.
  [0, Math.floor(lastIndex / 2), lastIndex].forEach((i) => {
    const text = el('text', {
      x: x(i), y: height - 8,
      'text-anchor': i === 0 ? 'start' : i === lastIndex ? 'end' : 'middle',
      class: 'axis-text',
    });
    text.textContent = points[i].label;
    svg.appendChild(text);
  });

  // A crosshair column per point, wide enough to hover comfortably.
  const bandWidth = plotW / (points.length - 1);
  points.forEach((point, i) => {
    const hit = el('rect', {
      x: x(i) - bandWidth / 2, y: pad.top, width: bandWidth, height: plotH, class: 'hit',
    });
    bindTip(hit, `${point.label} · ${formatValue(point.value, series.unit)}`);
    svg.appendChild(hit);
  });

  return svg;
}

/** A time series reads chronologically; everything else is ranked. */
export function chartFor(series) {
  return series.isTemporal ? lineChart(series) : barChart(series);
}
