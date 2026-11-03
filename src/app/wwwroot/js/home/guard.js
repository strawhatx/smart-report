/**
 * [04] The guard — the page's signature interaction.
 *
 * The same two questions, answered for three different people. Switching the role rewrites both
 * result tables in place: rows disappear, a measure is struck through, and the reason appears.
 *
 * The numbers here are the seeded demo figures, held as data rather than fetched, so the section
 * works on a cold page with no session. The behaviour it illustrates is real — /reports does
 * exactly this against Postgres.
 */

const REVENUE = {
  analyst: [['North', '$4.21M'], ['South', '$3.68M'], ['East', '$2.94M']],
  regional_manager: [['North', '$4.21M']],
  hr_director: [['North', '$4.21M'], ['South', '$3.68M'], ['East', '$2.94M']],
};

const REVENUE_NOTE = {
  analyst: 'All three regions. Nothing withheld.',
  regional_manager:
    'Two regions are missing. Not filtered out in the application — Postgres returned one row, '
    + 'because the policy on orders is scoped to this manager’s region.',
  hr_director: 'All three regions. Nothing withheld.',
};

const SALARY = {
  hr_director: [
    ['Engineering', '$118,400'], ['Sales', '$96,250'], ['Customer Success', '$78,900'],
    ['People Ops', '$84,100'], ['Finance', '$102,700'], ['Marketing', '$88,300'],
  ],
  analyst: null,
  regional_manager: null,
};

const SALARY_NOTE = {
  analyst: 'Denied: Average salary is restricted to HR Director; you are signed in as Analyst.',
  regional_manager:
    'Denied: Average salary is restricted to HR Director; you are signed in as Regional Manager.',
  hr_director: 'Allowed. compensation.annual_salary read for 140 employees.',
};

export function initGuard({ reduced }) {
  const roles = Array.from(document.querySelectorAll('.role'));
  const rowsA = document.getElementById('guard-rows-a');
  const rowsB = document.getElementById('guard-rows-b');
  const noteA = document.getElementById('guard-note-a');
  const noteB = document.getElementById('guard-note-b');

  if (!roles.length || !rowsA || !rowsB) return;

  function render(role) {
    paint(rowsA, REVENUE[role], noteA, REVENUE_NOTE[role], false);
    paint(rowsB, SALARY[role], noteB, SALARY_NOTE[role], SALARY[role] === null);
  }

  function paint(tbody, rows, noteEl, noteText, denied) {
    tbody.replaceChildren();

    if (!rows) {
      const tr = document.createElement('tr');
      tr.className = 'is-denied';
      const td = document.createElement('td');
      td.colSpan = 2;
      td.textContent = 'No rows — the measure was refused before the query was built.';
      tr.appendChild(td);
      tbody.appendChild(tr);
    } else {
      for (const [label, value] of rows) {
        const tr = document.createElement('tr');
        const th = document.createElement('td');
        const td = document.createElement('td');
        th.textContent = label;
        td.textContent = value;
        tr.append(th, td);
        tbody.appendChild(tr);
      }
    }

    noteEl.textContent = noteText;
    noteEl.classList.toggle('is-denied', denied);

    // Reduced motion still gets the content change, just without the tween.
    if (!reduced && window.gsap) {
      window.gsap.from(tbody.children, {
        opacity: 0, y: 6, duration: 0.3, ease: 'sr', stagger: 0.035, overwrite: true,
      });
    }
  }

  function select(role) {
    for (const button of roles) {
      button.setAttribute('aria-checked', String(button.dataset.role === role));
    }
    render(role);
  }

  roles.forEach((button, index) => {
    button.addEventListener('click', () => select(button.dataset.role));

    // Arrow keys move between radios, which is what a radiogroup is expected to do.
    button.addEventListener('keydown', (event) => {
      const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1
                 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1
                 : 0;
      if (!step) return;
      event.preventDefault();
      const next = roles[(index + step + roles.length) % roles.length];
      next.focus();
      select(next.dataset.role);
    });
  });

  select('analyst');
}
