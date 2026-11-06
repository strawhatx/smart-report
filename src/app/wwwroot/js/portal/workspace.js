/**
 * The Smart Reports workspace.
 *
 * Reads the server-sent event stream from /api/reports/ask and renders it as it arrives: the
 * planner's narration, each authorization decision, the compiled SQL, then the report.
 *
 * EventSource only speaks GET and the question belongs in a body, so the stream is read from
 * fetch() and the SSE framing is parsed here.
 */

import { barChart, columnChart, donutChart, lineChart, formatValue, escapeHtml } from './charts.js';

const stream = document.getElementById('stream');
const form = document.getElementById('ask-form');
const input = document.getElementById('ask-input');
const submit = document.getElementById('ask-submit');
const canvas = document.getElementById('canvas-body');
const titleEl = document.getElementById('report-title');
const subtitleEl = document.getElementById('report-subtitle');
const exportButton = document.getElementById('export-csv');
const saveButton = document.getElementById('save-report');

/** Measures where a smaller number is the better one. */
const LOWER_IS_BETTER = new Set(['avg_days_to_ship', 'late_shipments', 'low_stock_products']);

/** Follow-ups the catalog can genuinely answer. Offering one it cannot would be a dead end. */
const SUGGESTIONS = [
  'How does our on-time rate compare to other Northpoint clients?',
  'Show returns by reason',
  'Which products are low on stock?',
  'What are we billed per shipment by carrier?',
  'Break our orders down by sales channel',
];

/** Offered once a report exists, because they only make sense against one. */
const FOLLOW_UPS = [
  'Show that as a table',
  'Make it a column chart',
  'Break that down by sales channel',
];

let inFlight = null;
let lastReport = null;
let lastQuestion = '';
let startedAt = 0;

// The last plan, kept so "convert this to a table" has something to refer to. It is sent back to
// the server as a hint for the planner — and re-validated and re-authorized there like anything
// else, so tampering with it buys nothing.
let lastPlanJson = null;
let previousQuestion = null;

if (form) {
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    ask(input.value.trim());
  });

  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      form.requestSubmit();
    }
  });

  // A workspace opened from a template or a saved report arrives with its question ready.
  const seeded = input.value.trim();
  if (seeded) {
    queueMicrotask(() => ask(seeded));
  }
}

exportButton?.addEventListener('click', exportCsv);
saveButton?.addEventListener('click', saveReport);

async function ask(question) {
  if (!question || inFlight) return;

  inFlight = new AbortController();
  previousQuestion = lastQuestion || null;
  lastQuestion = question;
  lastReport = null;
  startedAt = performance.now();

  submit.disabled = true;
  exportButton.disabled = true;
  saveButton.disabled = true;
  stream.replaceChildren();
  canvas.replaceChildren();
  sqlList = null;

  addBubble(question);

  try {
    const response = await fetch('/api/reports/ask', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        question,
        previousQuestion,
        previousPlan: lastPlanJson,
      }),
      signal: inFlight.signal,
    });

    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      addStep('denied', null, response.status === 429
        ? 'Too many questions in a minute. Give it a moment.'
        : body.error || 'That request was refused.');
      return;
    }

    await readStream(response.body);
  } catch (error) {
    if (error.name !== 'AbortError') {
      addStep('denied', null, 'The connection dropped before the report finished.');
    }
  } finally {
    inFlight = null;
    submit.disabled = false;
  }
}

/**
 * Parses the `event:` / `data:` framing out of the byte stream.
 *
 * A network chunk does not line up with an SSE frame — one read can carry half a frame, or three
 * of them — so bytes accumulate in a buffer and are consumed only at a blank-line boundary. The
 * decoder is kept in streaming mode for the same reason: a multi-byte character can straddle two
 * chunks, and decoding each chunk independently would corrupt it.
 */
async function readStream(body) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  for (;;) {
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

/** Counted here rather than taken from the report, so the rail reports what actually ran. */
const queriesRan = { count: 0 };

/**
 * One case per server event. The order they arrive in is the order the pipeline decided things,
 * which is why the rail reads as a sequence rather than a summary: narration, then the plan,
 * then each authorization decision, then the SQL, then the report.
 */
function handle(name, payload) {
  switch (name) {
    case 'thinking':
      addStep('thinking', null, payload.text);
      break;
    case 'spec':
      lastPlanJson = JSON.stringify(payload);
      addChips(payload);
      break;
    case 'guard':
      // "Allowed" is the default and says nothing a reader needs — the report itself is the
      // evidence. Refusals, safeguards and scope are what the rail is for.
      if (payload.outcome !== 'allowed') {
        addStep(payload.outcome, payload.subject, payload.reason, payload.remedy);
      }
      break;
    case 'sql':
      queriesRan.count += 1;
      collectSql(payload.model, payload.sql);
      break;
    case 'rows':
      break;
    case 'document':
      lastReport = payload.report;
      renderReport(payload.report);
      addProvenanceLine(payload.report);
      exportButton.disabled = false;
      saveButton.disabled = false;
      break;
    case 'findings':
      addFindings(payload.items, payload.disclosure);
      break;
    case 'done':
      addSuggestions();
      break;
    case 'refused':
      renderRefusal(payload.message, payload.remedy);
      addSuggestions();
      break;
    case 'failed':
      renderRefusal(payload.message);
      break;
  }
}

/* ─────────────────────────── conversation rail ─────────────────────────── */

/** The question, as asked. Everything below it in the rail is the answer to this one. */
function addBubble(question) {
  queriesRan.count = 0;
  const li = document.createElement('li');
  li.className = 'rounded-xl bg-ink px-3.5 py-3 text-[14px] leading-6 text-paper';
  li.textContent = question;
  stream.appendChild(li);
}

/**
 * Colour by outcome, not by severity. Green is a measure allowed, red refused, blue the client
 * scope, amber a disclosure safeguard — each a different kind of decision, so they are not
 * shades of one warning.
 */
const STEP_STYLES = {
  thinking: { dot: 'bg-ink-faint', text: 'text-ink-soft' },
  allowed: { dot: 'bg-good', text: 'text-ink-soft' },
  denied: { dot: 'bg-danger', text: 'text-danger' },
  scoped: { dot: 'bg-brand', text: 'text-ink-soft' },
  aggregated: { dot: 'bg-warn', text: 'text-ink-soft' },
};

/**
 * One line in the transcript. Returns the element so a caller can hang richer content — the
 * plan chips, the SQL disclosure — off the same row.
 */
function addStep(kind, subject, text, remedy) {
  const style = STEP_STYLES[kind] ?? STEP_STYLES.thinking;

  const li = document.createElement('li');
  li.className = 'grid grid-cols-[10px_1fr] gap-x-3 text-[14px] leading-6';

  const dot = document.createElement('span');
  dot.className = `mt-2 size-[7px] rounded-full ${style.dot}`;

  const body = document.createElement('div');
  body.className = style.text;

  if (subject) {
    const label = document.createElement('span');
    label.className = 'label-mono block mb-0.5';
    label.textContent = subject;
    body.appendChild(label);
  }

  body.appendChild(document.createTextNode(text));

  if (remedy) {
    const note = document.createElement('p');
    note.className = 'mt-1 text-ink-soft';
    note.textContent = remedy;
    body.appendChild(note);
  }

  li.append(dot, body);
  stream.appendChild(li);
  stream.scrollTop = stream.scrollHeight;
  return li;
}

/**
 * The plan, one line per query.
 *
 * A chip per metric was accurate and unreadable — a dozen boxes that say less than a sentence.
 * The model name is what matters at a glance; the measures read as text beside it.
 */
function addChips(spec) {
  const li = addStep('thinking', 'plan', '');
  const list = document.createElement('ul');
  list.className = 'mt-1.5 space-y-1';

  for (const query of spec.queries) {
    const item = document.createElement('li');
    item.className = 'flex flex-wrap items-baseline gap-x-2 gap-y-1 text-[13px] leading-5';

    const model = document.createElement('span');
    model.className = 'rounded border border-brand px-1.5 py-0.5 font-mono text-[11px] text-brand';
    model.textContent = query.model;

    const detail = document.createElement('span');
    detail.className = 'text-ink-soft';
    detail.textContent = query.metrics.join(', ')
      + (query.dimensions.length ? ` by ${query.dimensions.join(', ')}` : '');

    item.append(model, detail);
    list.appendChild(item);
  }

  li.lastElementChild.appendChild(list);
}

let sqlList = null;

/** Every compiled query under one disclosure, so the rail stays readable. */
function collectSql(model, sql) {
  if (!sqlList) {
    const li = document.createElement('li');
    const details = document.createElement('details');
    details.className = 'rounded-xl border border-rule bg-surface px-3 py-2.5';
    details.innerHTML =
      '<summary class="label-mono cursor-pointer hover:text-ink">View the queries</summary>';

    sqlList = document.createElement('div');
    sqlList.className = 'mt-2 space-y-2';
    details.appendChild(sqlList);
    li.appendChild(details);
    stream.appendChild(li);
  }

  const block = document.createElement('div');
  const caption = document.createElement('p');
  caption.className = 'label-mono';
  caption.textContent = model;

  const pre = document.createElement('pre');
  pre.className = 'mt-1 overflow-x-auto rounded-lg bg-sunk p-2.5 font-mono text-[11px] leading-5 text-ink-soft';
  pre.textContent = sql;

  block.append(caption, pre);
  sqlList.appendChild(block);
}

/**
 * What the report says, written by the model from the figures that came back.
 *
 * This sits at the top of the rail rather than the bottom: it is the part a reader actually
 * wants, and the transcript underneath it is the evidence for anyone who cares to check.
 */
function addFindings(items, disclosure) {
  if (!items?.length) return;

  const li = document.createElement('li');
  li.className = 'rounded-xl border border-rule bg-surface p-4';

  const heading = document.createElement('p');
  heading.className = 'label-mono mb-3';
  heading.textContent = 'What this shows';
  li.appendChild(heading);

  const list = document.createElement('ol');
  list.className = 'space-y-3';

  items.forEach((finding, index) => {
    const item = document.createElement('li');
    item.className = 'grid grid-cols-[1.25rem_1fr] gap-x-2 text-[14px] leading-6';
    item.innerHTML =
      `<span class="text-ink-faint tabular-nums">${index + 1}.</span>`
      + `<span><strong class="font-semibold">${escapeHtml(finding.title)}</strong> `
      + `<span class="text-ink-soft">${escapeHtml(finding.detail)}</span></span>`;
    list.appendChild(item);
  });

  li.appendChild(list);

  if (disclosure) {
    const note = document.createElement('p');
    note.className = 'mt-3 border-t border-rule pt-2.5 text-[12px] leading-5 text-ink-faint';
    note.textContent = disclosure;
    li.appendChild(note);
  }

  // Straight after the question, above the transcript of how it got there — and scroll back to
  // it. The rail follows each step down as the report builds, so the summary would otherwise
  // land off-screen at the moment it becomes the most useful thing there.
  const anchor = stream.firstElementChild?.nextElementSibling ?? null;
  stream.insertBefore(li, anchor);
  stream.scrollTo({ top: 0 });
}

/**
 * The line a technical reviewer checks: how many queries ran, against which tables, whose data
 * they touched, how long it took, and which engine planned it. Shown always, not on request.
 */
function addProvenanceLine(report) {
  const seconds = ((performance.now() - startedAt) / 1000).toFixed(1);
  const li = document.createElement('li');
  li.className = 'border-t border-rule pt-3 text-[13px] leading-5 text-ink-faint';
  li.textContent =
    `Ran ${queriesRan.count} read-only ${queriesRan.count === 1 ? 'query' : 'queries'} on `
    + `${report.provenance.tablesRead.join(', ')} — this account's data only · ${seconds}s · `
    + `planned by ${report.provenance.plannerProvider}`;
  stream.appendChild(li);
  stream.scrollTop = stream.scrollHeight;
}

function addSuggestions() {
  const pool = lastReport ? [...FOLLOW_UPS, ...SUGGESTIONS] : SUGGESTIONS;
  const options = pool.filter((s) => s !== lastQuestion).slice(0, 3);
  if (!options.length) return;

  const li = document.createElement('li');
  li.className = 'pt-1';
  li.innerHTML = '<p class="label-mono mb-2">Ask next</p>';

  for (const option of options) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className =
      'mb-2 block w-full rounded-xl border border-rule bg-surface px-3.5 py-2.5 text-left '
      + 'text-[14px] leading-6 hover:border-ink-faint';
    button.textContent = option;
    button.addEventListener('click', () => {
      input.value = option;
      ask(option);
    });
    li.appendChild(button);
  }

  // Deliberately does not scroll: the summary sits at the top and has just been put there.
  stream.appendChild(li);
}

/* ─────────────────────────── the report ─────────────────────────── */

function renderRefusal(message, remedy) {
  subtitleEl.textContent = 'Not answered';
  canvas.replaceChildren();

  const box = document.createElement('div');
  box.className = 'max-w-2xl rounded-xl border border-danger bg-danger-wash p-5';
  box.innerHTML =
    '<h2 class="font-semibold text-danger">Not answered</h2>'
    + `<p class="mt-1.5 text-[15px] leading-7 text-ink-soft">${escapeHtml(message)}</p>`
    // A refusal without a way forward is a wall. Where there is one, it belongs here too.
    + (remedy ? `<p class="mt-3 text-[15px] font-medium leading-7">${escapeHtml(remedy)}</p>` : '');

  canvas.appendChild(box);
}

function renderReport(report) {
  titleEl.textContent = report.title;
  subtitleEl.textContent =
    `${report.sections.length} panel${report.sections.length === 1 ? '' : 's'} · built from `
    + `${report.provenance.tablesRead.join(', ')}`;

  canvas.replaceChildren();

  // A "comparison" panel is the previous period. It becomes the deltas on the headline figures
  // rather than a second copy of the same chart.
  const comparison = report.sections.find((s) => s.label?.startsWith('comparison'));
  const comparedTo = comparison?.label?.split(':')[1] ?? 'last month';
  const panels = report.sections.filter((s) => s !== comparison);

  const kpis = panels.flatMap((s) => s.kpis);
  if (kpis.length) {
    canvas.appendChild(kpiRow(kpis, comparison?.kpis ?? [], comparedTo));
  }

  // One card per chart, not per query. A query asking for three measures produces three charts,
  // and stacking them inside one tall panel buries the later ones. Tiled, it reads as a report.
  const grid = document.createElement('div');
  grid.className = 'mt-6 grid gap-4 xl:grid-cols-2';

  for (const section of panels) {
    for (const series of section.charts) {
      grid.appendChild(chartCard(series, section));
    }
  }

  if (grid.children.length) {
    canvas.appendChild(grid);
  }

  canvas.appendChild(provenanceStrip(report.provenance));
}


function kpiRow(kpis, previous, comparedTo) {
  // Columns match the number of tiles, so there is never an empty outlined cell. Written as
  // literal class strings because Tailwind generates by scanning source text.
  const COLUMNS = {
    1: 'grid-cols-1',
    2: 'sm:grid-cols-2',
    3: 'sm:grid-cols-2 xl:grid-cols-3',
    4: 'sm:grid-cols-2 xl:grid-cols-4',
  };

  const row = document.createElement('div');
  row.className = 'grid overflow-hidden rounded-xl bg-surface outline outline-1 outline-rule '
    + (COLUMNS[Math.min(kpis.length, 4)] ?? COLUMNS[4]);

  for (const kpi of kpis) {
    const before = previous.find((p) => p.key === kpi.key)?.value ?? null;

    const tile = document.createElement('div');
    tile.className = 'bg-surface p-4 outline outline-1 outline-rule -outline-offset-[0.5px]';
    tile.innerHTML =
      `<p class="text-[13px] text-ink-soft">${escapeHtml(kpi.label)}</p>`
      + `<p class="mt-1.5 text-[clamp(1.5rem,4vw,2rem)] font-bold leading-none tabular-nums">`
      + `${escapeHtml(formatValue(kpi.value, kpi.unit))}</p>`;

    const note = deltaNote(kpi, before, comparedTo);
    if (note) tile.insertAdjacentHTML('beforeend', note);

    row.appendChild(tile);
  }

  return row;
}

/**
 * The change against the previous period.
 *
 * Direction and goodness are separate questions: late shipments falling is an improvement, and
 * orders falling is not, so the arrow follows the number and the colour follows
 * `LOWER_IS_BETTER`. Tying colour to direction would paint a drop in late shipments red.
 */
function deltaNote(kpi, before, comparedTo) {
  if (kpi.key === 'low_stock_products' && kpi.value > 0) {
    return '<p class="mt-2 text-[13px] font-medium text-warn">Reorder soon</p>';
  }

  if (before === null || before === 0 || kpi.value === null) return '';

  const lowerBetter = LOWER_IS_BETTER.has(kpi.key);
  const rose = kpi.value > before;
  const good = lowerBetter ? !rose : rose;

  // Percentages move in points, not percent-of-a-percent — "up 0.8%" of 97.2% would be wrong.
  const magnitude = kpi.unit === 'percent'
    ? `${Math.abs(kpi.value - before).toFixed(1)} pts`
    : kpi.unit === 'days'
      ? `${Math.abs(kpi.value - before).toFixed(1)} days`
      : `${Math.abs(((kpi.value - before) / before) * 100).toFixed(1)}%`;

  const arrow = rose ? '▲' : '▼';
  const tone = good ? 'text-good' : 'text-danger';

  return `<p class="mt-2 text-[13px] font-medium ${tone}">${arrow} ${magnitude} vs ${escapeHtml(comparedTo)}</p>`;
}

/**
 * Wide shapes take the full width; compact ones tile two across. A twelve-month line squeezed
 * into half a column is unreadable, and a four-slice ring does not need the whole page.
 */
function isWide(series, section) {
  return series.chart === 'line'
    || series.chart === 'area'
    || series.chart === 'table'
    || section.table.rows.length > 8;
}

function chartCard(series, section) {
  const card = document.createElement('section');
  card.className = 'rounded-xl border border-rule bg-surface p-4 sm:p-5'
    + (isWide(series, section) ? ' xl:col-span-2' : '');

  const grouping = section.table.columns.find((c) => !c.isMetric);

  const heading = document.createElement('div');
  heading.className = 'mb-4 flex flex-wrap items-baseline gap-x-2 gap-y-1';
  heading.innerHTML =
    `<h2 class="font-semibold">${escapeHtml(series.label)}</h2>`
    + (grouping ? `<span class="text-[14px] text-ink-faint">by ${escapeHtml(grouping.label.toLowerCase())}</span>` : '');
  card.appendChild(heading);

  const chart = buildChart(series, section);
  if (chart) card.appendChild(chart);

  // Said out loud when a requested shape was replaced. Quietly drawing something else would
  // leave the reader thinking the system ignored them.
  if (series.chartNote) {
    const note = document.createElement('p');
    note.className = 'mt-3 text-[13px] leading-6 text-ink-faint';
    note.textContent = series.chartNote;
    card.appendChild(note);
  }

  const details = document.createElement('details');
  details.className = 'mt-5 border-t border-rule pt-3';
  details.innerHTML = '<summary class="label-mono cursor-pointer hover:text-ink">Show the data</summary>';
  if (series.chart === 'table') details.open = true;
  details.appendChild(dataTable(seriesTable(series, section)));
  card.appendChild(details);

  return card;
}

/**
 * The figures behind one card.
 *
 * A query with three measures produces one table holding all three. Each card shows only its
 * own, so the numbers under a chart are the numbers in it.
 */
function seriesTable(series, section) {
  const keep = section.table.columns
    .map((column, index) => ({ column, index }))
    .filter(({ column }) => !column.isMetric || column.key === series.key);

  return {
    columns: keep.map((k) => k.column),
    rows: section.table.rows.map((row) => keep.map((k) => row[k.index])),
  };
}

/** What was read, and what was held back. Shown under every report rather than on request. */
function provenanceStrip(prov) {
  const strip = document.createElement('div');
  strip.className = 'mt-6 rounded-xl border border-rule bg-paper p-4 text-[13px] leading-6 text-ink-soft';

  const chip = (text, danger) =>
    `<code class="rounded border px-1.5 py-0.5 font-mono text-[12px] ${danger
      ? 'border-danger bg-danger-wash text-danger' : 'border-rule bg-surface'}">${escapeHtml(text)}</code>`;

  const read = document.createElement('p');
  read.innerHTML = `<span class="label-mono">Read</span> ${prov.tablesRead.map((t) => chip(t, false)).join(' ')}`;
  strip.appendChild(read);

  if (prov.withheld.length) {
    const withheld = document.createElement('p');
    withheld.className = 'mt-2';
    withheld.innerHTML = `<span class="label-mono">Withheld</span> ${prov.withheld.map((w) => chip(w, true)).join(' ')}`;
    strip.appendChild(withheld);
  }

  if (prov.rowLimitReached) {
    const capped = document.createElement('p');
    capped.className = 'mt-2 text-warn';
    capped.textContent = 'Row limit reached — this is not the whole set.';
    strip.appendChild(capped);
  }

  return strip;
}

function buildChart(series, section) {
  switch (series.chart) {
    case 'line':
      return lineChart(splitByYear(series), series.unit);
    case 'area':
      return lineChart(splitByYear(series), series.unit, { filled: true });
    case 'column':
      return columnChart(series);
    case 'donut':
      return donutChart(series, { totalLabel: section.model === 'shipping' ? 'orders' : 'total' });
    case 'table':
      return null;
    default:
      return barChart(series);
  }
}

/**
 * A long monthly run is really two years side by side. Splitting it lets the chart show this year
 * against last on one axis, which is the comparison people actually want.
 */
function splitByYear(series) {
  const years = new Map();
  for (const point of series.points) {
    const [year, month] = point.label.split('-');
    if (!month) return [{ label: series.label, points: series.points }];
    if (!years.has(year)) years.set(year, []);
    years.get(year).push({ label: monthName(month), value: point.value });
  }

  if (years.size < 2) return [{ label: series.label, points: series.points }];

  const sorted = [...years.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  return sorted.map(([year, points], index) => ({
    label: year,
    points,
    dashed: index < sorted.length - 1,
  })).reverse();
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const monthName = (mm) => MONTHS[Number(mm) - 1] ?? mm;

/**
 * The figures, always available under every chart.
 *
 * Built with createElement and textContent rather than innerHTML: these are database values, and
 * a product name containing a bracket should render as a bracket, not as markup.
 */
function dataTable(table) {
  const wrap = document.createElement('div');
  wrap.className = 'mt-3 overflow-x-auto';

  const element = document.createElement('table');
  element.className = 'w-full border-collapse text-[14px]';

  const head = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const column of table.columns) {
    const th = document.createElement('th');
    th.scope = 'col';
    th.className = 'label-mono border-b border-rule px-3 py-2 text-left last:text-right';
    th.textContent = column.label;
    headRow.appendChild(th);
  }
  head.appendChild(headRow);

  const body = document.createElement('tbody');
  for (const row of table.rows) {
    const tr = document.createElement('tr');
    row.forEach((cell, i) => {
      const td = document.createElement('td');
      const column = table.columns[i];
      td.className = 'border-b border-rule px-3 py-2 tabular-nums'
        + (column?.isMetric ? ' text-right' : '');
      td.textContent = column?.isMetric
        ? formatValue(cell === null ? null : Number(cell), column.unit)
        : (cell ?? '—');
      tr.appendChild(td);
    });
    body.appendChild(tr);
  }

  element.append(head, body);
  wrap.appendChild(element);
  return wrap;
}

/* ─────────────────────────── export and save ─────────────────────────── */

/**
 * Every panel's figures as CSV, built in the browser from the report already in hand — no second
 * request, so the file cannot disagree with what is on screen.
 */
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

/**
 * Saves the question, not the figures.
 *
 * Reopening re-asks it, so a saved report is never a stale snapshot presented as current. The
 * client and owner are not sent — the server takes them from the session (A4).
 */
async function saveReport() {
  if (!lastReport) return;

  saveButton.disabled = true;
  try {
    const response = await fetch('/api/reports/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: lastReport.title, question: lastQuestion }),
    });

    saveButton.textContent = response.ok ? 'Saved' : 'Could not save';
  } catch {
    saveButton.textContent = 'Could not save';
  }
}

/** CSV quoting: every field quoted, embedded quotes doubled — the one escaping rule RFC 4180 has. */
const quote = (value) => `"${String(value).replace(/"/g, '""')}"`;
