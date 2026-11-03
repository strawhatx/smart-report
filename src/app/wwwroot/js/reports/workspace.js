/**
 * The report workspace.
 *
 * Reads the server-sent event stream from /api/reports/ask and renders it as it arrives: the
 * planner's narration, the authorization decisions, the compiled SQL, then the report.
 *
 * EventSource only speaks GET, and the question belongs in a body rather than a URL, so the
 * stream is read from fetch() and the SSE framing is parsed here.
 */

import { chartFor, formatValue } from './charts.js';

const streamEl = document.getElementById('stream');
const form = document.getElementById('ask-form');
const input = document.getElementById('ask-input');
const submit = document.getElementById('ask-submit');
const canvasBody = document.getElementById('canvas-body');
const canvasLabel = document.getElementById('canvas-label');
const exportButton = document.getElementById('export-csv');

let inFlight = null;
let lastReport = null;

if (form) {
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    ask(input.value.trim());
  });

  // Enter sends, Shift+Enter breaks the line — what the box looks like it should do.
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      form.requestSubmit();
    }
  });

  // A workspace opened from a saved report arrives with its question already in the box.
  const seeded = document.querySelector('.workspace')?.dataset.question;
  if (seeded) ask(seeded);
}

exportButton?.addEventListener('click', exportCsv);

async function ask(question) {
  if (!question || inFlight) return;

  inFlight = new AbortController();
  form.setAttribute('aria-busy', 'true');
  submit.disabled = true;
  streamEl.replaceChildren();
  lastReport = null;
  exportButton.disabled = true;

  addStep('thinking', null, `Asked: “${question}”`);

  try {
    const response = await fetch('/api/reports/ask', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question }),
      signal: inFlight.signal,
    });

    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      addStep('failed', null, response.status === 429
        ? 'Too many questions in a minute. Give it a moment.'
        : body.error || 'That request was refused.');
      return;
    }

    await readStream(response.body);
  } catch (error) {
    if (error.name !== 'AbortError') {
      addStep('failed', null, 'The connection dropped before the report finished.');
    }
  } finally {
    inFlight = null;
    form.removeAttribute('aria-busy');
    submit.disabled = false;
  }
}

/** Parses the `event:` / `data:` framing out of the byte stream. */
async function readStream(body) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    let split;
    while ((split = buffer.indexOf('\n\n')) !== -1) {
      const frame = buffer.slice(0, split);
      buffer = buffer.slice(split + 2);

      let name = 'message';
      let data = '';
      for (const line of frame.split('\n')) {
        if (line.startsWith('event: ')) name = line.slice(7).trim();
        else if (line.startsWith('data: ')) data += line.slice(6);
      }

      if (data) handle(name, JSON.parse(data));
    }
  }
}

function handle(name, payload) {
  switch (name) {
    case 'thinking':
      addStep('thinking', null, payload.text);
      break;

    case 'spec':
      addChips(payload);
      break;

    case 'guard':
      addStep(payload.outcome, payload.subject, payload.reason);
      break;

    case 'sql':
      addSql(payload.model, payload.sql);
      break;

    case 'rows':
      addStep('thinking', null, `${payload.model}: ${payload.count} row${payload.count === 1 ? '' : 's'}.`);
      break;

    case 'document':
      lastReport = payload.report;
      renderReport(payload.report);
      exportButton.disabled = false;
      break;

    case 'refused':
      renderRefusal(payload.message);
      break;

    case 'failed':
      addStep('failed', null, payload.message);
      renderRefusal(payload.message);
      break;
  }
}

/* ─────────────────────────── conversation rail ─────────────────────────── */

function addStep(kind, subject, text) {
  const li = document.createElement('li');
  li.className = `step-item step-item--${kind}`;

  const dot = document.createElement('span');
  dot.className = 'step-item__dot';

  const body = document.createElement('div');
  if (subject) {
    const label = document.createElement('span');
    label.className = 'step-item__subject';
    label.textContent = subject;
    body.appendChild(label);
  }
  body.appendChild(document.createTextNode(text));

  li.append(dot, body);
  streamEl.appendChild(li);
  streamEl.scrollTop = streamEl.scrollHeight;

  if (window.gsap && !prefersReduced()) {
    window.gsap.from(li, { opacity: 0, y: 6, duration: 0.24, ease: 'power2.out' });
  }
  return li;
}

function addChips(spec) {
  const li = document.createElement('li');
  li.className = 'step-item step-item--thinking';
  li.appendChild(document.createElement('span')).className = 'step-item__dot';

  const body = document.createElement('div');
  const label = document.createElement('span');
  label.className = 'step-item__subject';
  label.textContent = 'plan';
  body.appendChild(label);

  const chips = document.createElement('div');
  chips.className = 'chips';

  for (const query of spec.queries) {
    chips.appendChild(chip(`model: ${query.model}`, 'chip--model'));
    query.metrics.forEach((m) => chips.appendChild(chip(m)));
    query.dimensions.forEach((d) => chips.appendChild(chip(`by: ${d}`)));
    query.filters.forEach((f) => chips.appendChild(chip(`${f.field} ${f.op} ${f.values.join('…')}`)));
  }

  body.appendChild(chips);
  li.appendChild(body);
  streamEl.appendChild(li);
}

function chip(text, extra = '') {
  const span = document.createElement('span');
  span.className = `chip ${extra}`.trim();
  span.textContent = text;
  return span;
}

function addSql(model, sql) {
  const li = addStep('thinking', 'compiled sql', '');
  const details = document.createElement('details');
  details.className = 'sql-disclosure';

  const summary = document.createElement('summary');
  summary.textContent = `${model} — show query`;

  const pre = document.createElement('pre');
  pre.textContent = sql;

  details.append(summary, pre);
  li.lastElementChild.appendChild(details);
}

/* ─────────────────────────── the report ─────────────────────────── */

function renderRefusal(message) {
  canvasLabel.textContent = 'Refused';
  canvasBody.replaceChildren();

  const box = document.createElement('div');
  box.className = 'refusal';

  const heading = document.createElement('h2');
  heading.textContent = 'Not answered';

  const text = document.createElement('p');
  text.textContent = message;

  box.append(heading, text);
  canvasBody.appendChild(box);
}

function renderReport(report) {
  canvasLabel.textContent = `Report · ${report.provenance.plannerProvider}`;
  canvasBody.replaceChildren();

  const title = document.createElement('h2');
  title.className = 'report__title';
  title.textContent = report.title;
  canvasBody.appendChild(title);

  for (const section of report.sections) {
    const wrap = document.createElement('section');
    wrap.className = 'report__section';

    if (section.kpis.length) wrap.appendChild(kpiRow(section.kpis));

    // One chart per measure. Two measures of different scale never share an axis.
    for (const series of section.charts) {
      const chart = chartFor(series);
      if (!chart) continue;

      const figure = document.createElement('figure');
      figure.className = 'chart';

      // A single-series chart needs no legend: the caption names what is plotted.
      const caption = document.createElement('figcaption');

      const heading = document.createElement('div');
      heading.className = 'chart__title';
      heading.textContent = series.label;

      const sub = document.createElement('div');
      sub.className = 'chart__caption';
      sub.textContent = series.isTemporal
        ? `by month · ${series.points.length} points`
        : `by ${section.table.columns.find((c) => !c.isMetric)?.label.toLowerCase() ?? 'category'}`;

      caption.append(heading, sub);
      figure.append(caption, chart);
      wrap.appendChild(figure);
    }

    wrap.appendChild(dataTable(section.table));
    canvasBody.appendChild(wrap);
  }

  canvasBody.appendChild(provenance(report.provenance));

  if (window.gsap && !prefersReduced()) {
    window.gsap.from(canvasBody.children, {
      opacity: 0, y: 10, duration: 0.42, ease: 'power2.out', stagger: 0.06,
    });
  }
}

function kpiRow(kpis) {
  const row = document.createElement('div');
  row.className = 'kpis';

  for (const kpi of kpis) {
    const tile = document.createElement('div');
    tile.className = 'kpi';

    const label = document.createElement('div');
    label.className = 'kpi__label';
    label.textContent = kpi.label;

    const value = document.createElement('div');
    value.className = 'kpi__value';
    value.textContent = formatValue(kpi.value, kpi.unit);

    tile.append(label, value);
    row.appendChild(tile);
  }

  return row;
}

function dataTable(table) {
  const wrap = document.createElement('div');
  wrap.className = 'data-table-wrap';

  const element = document.createElement('table');
  element.className = 'data-table';

  const head = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const column of table.columns) {
    const th = document.createElement('th');
    th.scope = 'col';
    th.textContent = column.label;
    headRow.appendChild(th);
  }
  head.appendChild(headRow);

  const bodyEl = document.createElement('tbody');
  for (const row of table.rows) {
    const tr = document.createElement('tr');
    row.forEach((cell, i) => {
      const td = document.createElement('td');
      td.textContent = table.columns[i]?.isMetric
        ? formatValue(cell === null ? null : Number(cell), table.columns[i].unit)
        : (cell ?? '—');
      tr.appendChild(td);
    });
    bodyEl.appendChild(tr);
  }

  element.append(head, bodyEl);
  wrap.appendChild(element);
  return wrap;
}

function provenance(prov) {
  const box = document.createElement('div');
  box.className = 'prov';

  box.appendChild(provRow('Read', prov.tablesRead, false));

  if (prov.withheld.length) {
    box.appendChild(provRow('Withheld', prov.withheld, true));
  }

  const note = document.createElement('div');
  note.className = 'prov__row';
  note.textContent = `${prov.rowCount} row${prov.rowCount === 1 ? '' : 's'}`
    + (prov.rowLimitReached ? ' — row limit reached, this is not the whole set.' : '.')
    + ` Planned by ${prov.plannerProvider}.`;
  box.appendChild(note);

  return box;
}

function provRow(label, items, withheld) {
  const row = document.createElement('div');
  row.className = 'prov__row';

  const name = document.createElement('span');
  name.className = 'label';
  name.textContent = label;
  row.appendChild(name);

  for (const item of items) {
    const code = document.createElement('code');
    if (withheld) code.className = 'is-withheld';
    code.textContent = item;
    row.appendChild(code);
  }

  return row;
}

/* ─────────────────────────── export ─────────────────────────── */

function exportCsv() {
  if (!lastReport) return;

  const lines = [];
  for (const section of lastReport.sections) {
    lines.push(section.table.columns.map((c) => quote(c.label)).join(','));
    for (const row of section.table.rows) {
      lines.push(row.map((cell) => quote(cell ?? '')).join(','));
    }
    lines.push('');
  }

  const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${lastReport.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

// Function declarations, not const arrows: the seeded question fires during module evaluation,
// so anything it reaches must already be hoisted rather than sitting in the temporal dead zone.
function quote(value) {
  return `"${String(value).replace(/"/g, '""')}"`;
}

function prefersReduced() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}
