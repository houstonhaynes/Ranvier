(() => {
  'use strict';
  const roots = [...document.querySelectorAll('[data-perf-tabs], [data-perf-compare], [data-perf-history], [data-perf-trends]')].filter(root => !root.dataset.perfReady);
  if (!roots.length) return;
  roots.forEach(root => { root.dataset.perfReady = 'true'; });
  const NS = 'http://www.w3.org/2000/svg';
  const names = { dotnet: '.NET', node: 'Node.js / Fable' };
  const number = value => Math.abs(value) < 10 && value !== 0
    ? value.toLocaleString('en-US', { maximumSignificantDigits: 3 })
    : value.toLocaleString('en-US', { maximumFractionDigits: 1 });
  const ratio = value => value > 0 && value < 0.1 ? value.toLocaleString('en-US', { maximumSignificantDigits: 2 }) : number(value);
  const short = commit => commit.Commit.slice(0, 7);
  const enginesIn = rows => [...new Set(rows.map(row => row.Engine))].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
  const rank = name => ['Ranvier', 'FSharp.Data.Adaptive', 'R3', 'Fable.Ripple'].findIndex(family => name.startsWith(family)) + 1 || 5;
  const family = engine => engine.split(' (')[0];
  const colour = engine => ['#3987e5', '#eb6834', '#1baf7a', '#bd8a31', '#9b7bc4'][rank(engine) - 1];
  // Each selectable metric, the leg that measures it, and its per-operation unit.
  const metrics = {
    instructions: { leg: 'arm64-counters', label: 'Instructions', unit: 'instr/op', title: 'Instructions', trends: 'Instruction trends' },
    cycles: { leg: 'arm64-counters', label: 'Cycles', unit: 'cycles/op', title: 'Cycles', trends: 'Cycle trends' },
    bytes: { leg: 'arm64-counters', label: 'Allocated bytes', unit: 'B/op', title: 'Allocations', trends: 'Allocation trends' },
    branch_misses: { leg: 'arm64-counters', label: 'Branch misses', unit: 'misses/op', title: 'Branch misses', trends: 'Branch-miss trends' },
    cpu_ns: { leg: 'x64-timing', label: 'CPU time (x64)', unit: 'ns/op', title: 'CPU time', trends: 'CPU-time trends' },
  };
  const measurements = (commit, runtime, metric = 'instructions') => commit.Medians.filter(row => row.Leg === metrics[metric].leg
    && row.Runtime === runtime && row.Metric === metric && (row.Side === undefined || row.Side === 'head'));
  // A median below zero lies within measurement noise of zero and reads as zero.
  const shown = value => Number.isFinite(value) ? Math.max(0, value) : null;
  const metricsFor = (commits, runtime) => Object.keys(metrics).filter(metric => commits.some(commit => measurements(commit, runtime, metric).length));
  const metricControl = (commits, runtime, current) => {
    const available = metricsFor(commits, runtime);
    return selectControl('Metric', available.map(key => [key, metrics[key].label]), available.includes(current) ? current : available[0]);
  };
  const processRange = row => Number.isFinite(row?.Min) && number(shown(row.Min)) !== number(shown(row.Max))
    ? `processes ${number(shown(row.Min))}–${number(shown(row.Max))}` : '';
  const countersFor = (commit, runtime, scenario, engine) => (commit.Counters || [])
    .filter(row => row.Runtime === runtime && row.Scenario === scenario && row.Engine === engine)
    .map(row => `${row.Name} ${number(row.Value)}`).join(', ');

  function element(tag, attributes = {}, text) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
    if (text !== undefined) node.textContent = text;
    return node;
  }
  function svgElement(tag, attributes = {}, text) {
    const node = document.createElementNS(NS, tag);
    for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
    if (text !== undefined) node.textContent = text;
    return node;
  }
  function selectControl(label, options, value) {
    const wrapper = element('label', { class: 'rv-perf-control' });
    const select = element('select', { 'aria-label': label });
    for (const [key, title] of options) select.append(element('option', { value: key }, title));
    select.value = value;
    wrapper.append(element('span', {}, label), select);
    return { wrapper, select };
  }
  function makeSvg(label, height) {
    const svg = svgElement('svg', { viewBox: `0 0 640 ${height}`, role: 'group', 'aria-label': label });
    svg.append(svgElement('title', {}, label));
    return svg;
  }
  function engineToggle(engine, hidden, update) {
    const button = element('button', { type: 'button', class: 'rv-perf-engine', 'data-focus-key': JSON.stringify(['engine', engine]), 'aria-pressed': String(!hidden.has(engine)) });
    button.style.setProperty('--engine', colour(engine));
    button.append(element('i', { 'aria-hidden': 'true' }), document.createTextNode(engine));
    button.addEventListener('click', () => {
      hidden.has(engine) ? hidden.delete(engine) : hidden.add(engine);
      update();
    });
    return button;
  }
  function inspectable(node, message, output) {
    node.setAttribute('tabindex', '0');
    node.setAttribute('aria-label', message);
    node.append(svgElement('title', {}, message));
    node.addEventListener('pointerenter', () => { output.textContent = message; });
    node.addEventListener('pointerdown', () => { output.textContent = message; });
    node.addEventListener('focus', () => { output.textContent = message; });
  }
  function comparison(scenario, rows, relative, output, baselineValue, metric = 'instructions', counters = () => '') {
    const panel = element('section', { class: 'rv-perf-panel' });
    const header = element('header');
    const sorted = [...rows].sort((a, b) => rank(a.Engine) - rank(b.Engine) || a.Engine.localeCompare(b.Engine));
    const baseline = shown(baselineValue ?? sorted.find(row => row.Engine === 'Ranvier')?.Value);
    const relativeAvailable = relative && baseline > 0;
    const unit = metrics[metric].unit;
    header.append(element('h4', {}, scenario), element('span', {}, relativeAvailable ? 'Ranvier = 1×' : unit));
    panel.append(header);
    if (relative && !relativeAvailable) panel.append(element('p', { class: 'rv-perf-note' }, 'Ranvier reference unavailable; showing absolute values.'));
    const max = Math.max(Number.MIN_VALUE, ...sorted.map(row => shown(row.Value) ?? 0));
    const svg = makeSvg(`${scenario}: ${metrics[metric].label.toLowerCase()} per operation by library`, 20 + sorted.length * 55);
    sorted.forEach((row, index) => {
      const y = 13 + index * 55;
      const group = svgElement('g', { class: 'rv-perf-bar', role: 'group' });
      const amount = shown(row.Value);
      const valid = amount !== null;
      const value = !valid ? 'unavailable' : relativeAvailable ? `${ratio(amount / baseline)}×` : number(amount);
      group.append(svgElement('text', { x: 0, y, class: 'rv-perf-svg-label' }, row.Engine));
      group.append(svgElement('text', { x: 640, y, 'text-anchor': 'end', class: 'rv-perf-svg-value' }, value));
      group.append(svgElement('line', { x1: 0, x2: 640, y1: y + 22, y2: y + 22, class: 'rv-perf-track' }));
      if (valid) group.append(svgElement('rect', { x: 0, y: y + 15, width: Math.max(amount === 0 ? 0 : 1.5, amount / max * 640), height: 14, rx: 2, fill: colour(row.Engine) }));
      group.append(svgElement('rect', { x: 0, y: y - 12, width: 640, height: 48, fill: 'transparent' }));
      const range = valid && processRange(row) ? ` (${processRange(row)})` : '';
      const library = counters(row.Engine);
      inspectable(group, `${scenario} · ${row.Engine}: ${valid ? `${number(amount)} ${unit}${range}` : 'measurement unavailable'}${relativeAvailable && valid ? ` · ${ratio(amount / baseline)}× Ranvier` : ''}${library ? ` · ${library}` : ''}`, output);
      svg.append(group);
    });
    panel.append(svg);
    return panel;
  }

  function mount(root, commits, shared) {
    const isHistory = root.hasAttribute('data-perf-history');
    const group = (root.dataset.perfGroup || '').split(',').filter(Boolean);
    const hidden = new Set();
    let selectedCommit = commits.length - 1;
    root.className = 'rv-perf';
    root.replaceChildren();
    const heading = element('div', { class: 'rv-perf-heading' });
    heading.append(element('p', { class: 'rv-perf-eyebrow' }, isHistory ? 'Across commits' : 'Library comparison'));
    const title = element('h3', {}, isHistory ? 'Instructions over time' : 'Instructions by library');
    heading.append(title);
    const controls = element('div', { class: 'rv-perf-controls' });
    const runtimes = [...new Set(commits.flatMap(commit => commit.Medians.filter(row => row.Leg === 'arm64-counters' && row.Metric === 'instructions').map(row => row.Runtime)))].sort();
    if (!runtimes.length) throw new Error('No instruction measurements are available yet.');
    const runtime = selectControl('Runtime', runtimes.map(key => [key, names[key] || key]), runtimes.includes(shared?.runtime) ? shared.runtime : runtimes.includes('dotnet') ? 'dotnet' : runtimes[0]);
    const commit = selectControl('Commit', commits.map((item, index) => [String(index), `${short(item)} · ${item.Subject}`]), String(selectedCommit));
    let metric = metricControl(commits, runtime.select.value, shared?.metric || 'instructions');
    const mode = selectControl('Values', [['absolute', 'Per operation'], ['relative', 'Relative to Ranvier']], 'absolute');
    controls.append(runtime.wrapper, metric.wrapper, commit.wrapper, mode.wrapper);
    const search = element('input', { type: 'search', placeholder: 'Find a scenario…', 'aria-label': 'Find a scenario', class: 'rv-perf-search' });
    const scenario = selectControl('Scenario', [], '');
    controls.append(isHistory ? scenario.wrapper : search);
    const legend = element('div', { class: 'rv-perf-legend', 'aria-label': 'Visible libraries' });
    const provenance = element('div', { class: 'rv-perf-provenance' });
    const body = element('div', { class: isHistory ? 'rv-perf-trend' : 'rv-perf-panels' });
    const detail = element('p', { class: 'rv-perf-inspect', role: 'status', 'aria-live': 'polite' }, 'Point to a bar or focus it to inspect the measurement.');
    root.append(heading, controls, legend, provenance, body, detail);
    let previousRuntime = runtime.select.value;

    function update() {
      const focusedKey = root.contains(document.activeElement) ? document.activeElement.getAttribute('data-focus-key') : null;
      selectedCommit = Number(commit.select.value);
      const selected = commits[selectedCommit];
      if (previousRuntime !== runtime.select.value) {
        hidden.clear();
        const next = metricControl(commits, runtime.select.value, metric.select.value);
        metric.wrapper.replaceWith(next.wrapper);
        metric = next;
        metric.select.addEventListener('change', update);
      }
      previousRuntime = runtime.select.value;
      const measure = metric.select.value;
      title.textContent = `${metrics[measure].title} ${isHistory ? 'over time' : 'by library'}`;
      const rows = measurements(selected, runtime.select.value, measure);
      const allRows = isHistory ? commits.flatMap(item => measurements(item, runtime.select.value, measure)) : rows;
      const engines = enginesIn(allRows);
      const families = [...new Set(engines.map(family))];
      if (JSON.stringify(families) !== legend.dataset.families) {
        legend.replaceChildren(...families.map(engine => engineToggle(engine, hidden, update)));
        legend.dataset.families = JSON.stringify(families);
      }
      [...legend.children].forEach((button, index) => button.setAttribute('aria-pressed', String(!hidden.has(families[index]))));
      const date = new Date(selected.Date).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
      const link = element('a', { href: `https://github.com/ranvier-org/Ranvier/commit/${selected.Commit}`, class: 'rv-perf-sha' }, short(selected));
      provenance.replaceChildren(link, document.createTextNode(` · ${date} · ${selected.Subject}`));
      const available = [...new Set(allRows.map(row => row.Scenario))].filter(name => !group.length || group.some(prefix => name.startsWith(prefix))).sort();
      const currentScenario = scenario.select.value;
      scenario.select.replaceChildren(...available.map(name => element('option', { value: name }, name)));
      scenario.select.value = available.includes(currentScenario) ? currentScenario : available.includes('create') ? 'create' : available[0] || '';
      body.replaceChildren();
      const visible = rows.filter(row => !hidden.has(family(row.Engine)));
      const counters = name => engine => countersFor(selected, runtime.select.value, name, engine);
      if (isHistory) {
        renderTrend(body, commits, runtime.select.value, measure, scenario.select.value, engines.filter(engine => !hidden.has(family(engine))), mode.select.value === 'relative', detail, selectedCommit, index => {
          commit.select.value = String(index);
          update();
        });
        const current = visible.filter(row => row.Scenario === scenario.select.value);
        if (current.length) body.append(comparison(scenario.select.value, current, mode.select.value === 'relative', detail, rows.find(row => row.Scenario === scenario.select.value && row.Engine === 'Ranvier')?.Value, measure, counters(scenario.select.value)));
      } else {
        for (const name of available.filter(name => name.toLowerCase().includes(search.value.trim().toLowerCase()))) {
          const matching = visible.filter(row => row.Scenario === name);
          if (matching.length) body.append(comparison(name, matching, mode.select.value === 'relative', detail, rows.find(row => row.Scenario === name && row.Engine === 'Ranvier')?.Value, measure, counters(name)));
        }
      }
      if (!body.children.length) body.append(element('p', { class: 'rv-perf-empty' }, 'No matching measurements. Adjust the filters or show a library.'));
      detail.textContent = 'Hover or focus a mark for exact values. Each comparison panel scales from zero to its largest value.';
      if (focusedKey) [...root.querySelectorAll('[data-focus-key]')].find(node => node.getAttribute('data-focus-key') === focusedKey)?.focus({ preventScroll: true });
      if (shared) Object.assign(shared, { runtime: runtime.select.value, metric: metric.select.value });
    }
    for (const control of [runtime.select, metric.select, commit.select, mode.select, scenario.select]) control.addEventListener('change', update);
    search.addEventListener('input', update);
    update();
    return {
      // Adopts the shared runtime and metric when either differs from the current selection.
      sync() {
        if (!shared || (shared.runtime === runtime.select.value && shared.metric === metric.select.value)) return;
        if (shared.runtime !== runtime.select.value) hidden.clear();
        runtime.select.value = shared.runtime;
        previousRuntime = runtime.select.value;
        const next = metricControl(commits, runtime.select.value, shared.metric);
        metric.wrapper.replaceWith(next.wrapper);
        metric = next;
        metric.select.addEventListener('change', update);
        update();
      },
    };
  }

  function renderTrend(body, commits, runtime, metric, scenario, engines, relative, detail, selected, onSelect) {
    const unit = metrics[metric].unit;
    const svg = makeSvg(`${scenario}: ${metrics[metric].label.toLowerCase()} history. Select a commit to inspect its library comparison.`, 300);
    const left = 62, right = 608, top = 25, bottom = 240;
    const x = index => commits.length === 1 ? (left + right) / 2 : left + index / (commits.length - 1) * (right - left);
    const valueFor = (commit, engine) => {
      const rows = measurements(commit, runtime, metric).filter(row => row.Scenario === scenario);
      const value = shown(rows.find(row => row.Engine === engine)?.Value);
      const base = shown(rows.find(row => row.Engine === 'Ranvier')?.Value);
      if (value === null) return null;
      return relative ? base > 0 ? value / base : null : value;
    };
    const values = engines.map(engine => ({ engine, values: commits.map(commit => valueFor(commit, engine)) }));
    const max = Math.max(Number.MIN_VALUE, ...values.flatMap(series => series.values.filter(value => value !== null))) * 1.12;
    const y = value => bottom - value / max * (bottom - top);
    for (let tick = 0; tick <= 4; tick++) {
      const value = max * tick / 4;
      svg.append(svgElement('line', { x1: left, x2: right, y1: y(value), y2: y(value), class: 'rv-perf-grid' }));
      svg.append(svgElement('text', { x: left - 10, y: y(value) + 4, 'text-anchor': 'end', class: 'rv-perf-tick' }, relative ? `${ratio(value)}×` : Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 }).format(value)));
    }
    svg.append(svgElement('line', { x1: x(selected), x2: x(selected), y1: top, y2: bottom, class: 'rv-perf-selected' }));
    if (relative && max >= 1) svg.append(svgElement('line', { x1: left, x2: right, y1: y(1), y2: y(1), class: 'rv-perf-reference' }));
    values.forEach(({ engine, values: points }) => {
      let path = '', connected = false;
      points.forEach((value, index) => {
        if (value === null) { connected = false; return; }
        path += `${connected ? 'L' : 'M'}${x(index)},${y(value)} `;
        connected = true;
      });
      svg.append(svgElement('path', { d: path, stroke: colour(engine), fill: 'none', 'stroke-width': 2.2, 'stroke-linejoin': 'round' }));
      points.forEach((value, index) => {
        if (value === null) return;
        const dot = svgElement('g', { class: 'rv-perf-point', role: 'button', 'data-focus-key': JSON.stringify(['point', commits[index].Commit, engine]) });
        dot.append(svgElement('circle', { cx: x(index), cy: y(value), r: 11, fill: 'transparent', stroke: 'none' }));
        dot.append(svgElement('circle', { cx: x(index), cy: y(value), r: index === selected ? 5 : 3.5, fill: colour(engine), class: 'rv-perf-dot' }));
        inspectable(dot, `${short(commits[index])} · ${commits[index].Subject} · ${engine}: ${relative ? ratio(value) : number(value)}${relative ? '× Ranvier' : ` ${unit}`}`, detail);
        dot.addEventListener('click', () => onSelect(index));
        dot.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(index); } });
        svg.append(dot);
      });
    });
    const stride = Math.max(1, Math.ceil(commits.length / 6));
    commits.forEach((commit, index) => {
      if (index % stride && index !== commits.length - 1) return;
      const label = svgElement('text', { x: x(index), y: bottom + 24, 'text-anchor': 'middle', class: 'rv-perf-tick rv-perf-commit', tabindex: 0, role: 'button', 'data-focus-key': JSON.stringify(['commit', commit.Commit]), 'aria-label': `Inspect ${short(commit)}: ${commit.Subject}` }, short(commit));
      label.addEventListener('click', () => onSelect(index));
      label.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(index); } });
      svg.append(label);
    });
    svg.append(svgElement('text', { x: left, y: 290, class: 'rv-perf-tick' }, relative ? 'Relative to Ranvier at each commit · missing values leave gaps' : `${metrics[metric].label} (${unit}) · lower is better · missing values leave gaps`));
    body.append(svg);
    if (!values.some(series => series.values.some(value => value !== null))) body.append(element('p', { class: 'rv-perf-empty' }, 'No visible measurements for this scenario.'));
  }

  function mountTrends(root, commits, shared) {
    const group = (root.dataset.perfGroup || '').split(',').filter(Boolean);
    const hidden = new Set();
    root.className = 'rv-perf';
    root.replaceChildren();
    const heading = element('div', { class: 'rv-perf-heading' });
    const title = element('h3', {}, 'Instruction trends');
    heading.append(element('p', { class: 'rv-perf-eyebrow' }, 'Across commits'), title);
    const controls = element('div', { class: 'rv-perf-controls' });
    const runtimes = [...new Set(commits.flatMap(commit => commit.Medians.filter(row => row.Leg === 'arm64-counters' && row.Metric === 'instructions').map(row => row.Runtime)))].sort();
    if (!runtimes.length) throw new Error('No instruction measurements are available yet.');
    const runtime = selectControl('Runtime', runtimes.map(key => [key, names[key] || key]), runtimes.includes(shared?.runtime) ? shared.runtime : runtimes.includes('dotnet') ? 'dotnet' : runtimes[0]);
    let metric = metricControl(commits, runtime.select.value, shared?.metric || 'instructions');
    const mode = selectControl('Values', [['change', 'Change since first record'], ['absolute', 'Per operation']], 'change');
    controls.append(runtime.wrapper, metric.wrapper, mode.wrapper);
    const legend = element('div', { class: 'rv-perf-legend', 'aria-label': 'Reference libraries' });
    const provenance = element('div', { class: 'rv-perf-provenance' });
    const body = element('div', { class: 'rv-perf-panels' });
    const detail = element('p', { class: 'rv-perf-inspect', role: 'status', 'aria-live': 'polite' });
    root.append(heading, controls, legend, provenance, body, detail);
    let previousRuntime = runtime.select.value;

    function update() {
      if (previousRuntime !== runtime.select.value) {
        hidden.clear();
        const next = metricControl(commits, runtime.select.value, metric.select.value);
        metric.wrapper.replaceWith(next.wrapper);
        metric = next;
        metric.select.addEventListener('change', update);
      }
      previousRuntime = runtime.select.value;
      const measure = metric.select.value;
      title.textContent = metrics[measure].trends;
      const perCommit = commits.map(commit => measurements(commit, runtime.select.value, measure));
      const inGroup = name => !group.length || group.some(prefix => name.startsWith(prefix));
      const rows = perCommit.flat().filter(row => inGroup(row.Scenario));
      const references = [...new Set(enginesIn(rows).map(family))].filter(name => name !== 'Ranvier');
      if (JSON.stringify(references) !== legend.dataset.families) {
        legend.replaceChildren(...references.map(engine => engineToggle(engine, hidden, update)));
        legend.dataset.families = JSON.stringify(references);
      }
      [...legend.children].forEach((button, index) => button.setAttribute('aria-pressed', String(!hidden.has(references[index]))));
      const date = commit => new Date(commit.Date).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
      provenance.replaceChildren(document.createTextNode(`${commits.length} recorded commits · ${short(commits[0])} (${date(commits[0])}) to `),
        element('a', { href: `https://github.com/ranvier-org/Ranvier/commit/${commits.at(-1).Commit}`, class: 'rv-perf-sha' }, short(commits.at(-1))),
        document.createTextNode(` (${date(commits.at(-1))})`));
      body.replaceChildren();
      for (const scenario of [...new Set(rows.map(row => row.Scenario))].sort()) {
        const engines = enginesIn(rows.filter(row => row.Scenario === scenario));
        const others = engines.filter(engine => family(engine) !== 'Ranvier' && !hidden.has(family(engine)));
        for (const subject of engines.filter(engine => family(engine) === 'Ranvier'))
          body.append(trendPanel(scenario, subject, others, commits, perCommit, mode.select.value === 'change', detail, runtime.select.value, measure));
      }
      if (!body.children.length) body.append(element('p', { class: 'rv-perf-empty' }, 'No recorded measurements for these scenarios on this runtime.'));
      detail.textContent = 'Hover a panel, or focus it and use the arrow keys, to read each commit. Each panel scales to its own range.';
      if (shared) Object.assign(shared, { runtime: runtime.select.value, metric: metric.select.value });
    }
    for (const control of [runtime.select, metric.select, mode.select]) control.addEventListener('change', update);
    update();
    return {
      // Adopts the shared runtime and metric when either differs from the current selection.
      sync() {
        if (!shared || (shared.runtime === runtime.select.value && shared.metric === metric.select.value)) return;
        if (shared.runtime !== runtime.select.value) hidden.clear();
        runtime.select.value = shared.runtime;
        previousRuntime = runtime.select.value;
        const next = metricControl(commits, runtime.select.value, shared.metric);
        metric.wrapper.replaceWith(next.wrapper);
        metric = next;
        metric.select.addEventListener('change', update);
        update();
      },
    };
  }

  function trendPanel(scenario, subject, others, commits, perCommit, changeRequested, output, runtime, metric) {
    const unit = metrics[metric].unit;
    const percent = value => {
      const rounded = Math.round(value * 10) / 10;
      return `${rounded > 0 ? '+' : rounded < 0 ? '−' : '±'}${Math.abs(rounded).toFixed(1)}%`;
    };
    const rowsFor = engine => perCommit.map(rows => rows.find(row => row.Scenario === scenario && row.Engine === engine));
    const firstOf = values => values.find(value => value !== null);
    // A panel whose Ranvier variant first recorded below one unit per operation plots absolute values.
    const subjectBase = firstOf(rowsFor(subject).map(row => shown(row?.Value)));
    const change = changeRequested && subjectBase >= 1;
    const series = [subject, ...others].map(engine => {
      const rows = rowsFor(engine);
      const raw = rows.map(row => shown(row?.Value));
      const base = firstOf(raw);
      const scale = value => value === null ? null : change ? base >= 1 ? (value / base - 1) * 100 : null : value;
      const band = rows.map(row => Number.isFinite(row?.Min) ? [scale(shown(row.Min)), scale(shown(row.Max))] : null);
      return { engine, raw, rows, band, plotted: raw.map(scale) };
    }).filter(item => item.plotted.some(value => value !== null));
    const subjectRows = rowsFor(subject);
    const counted = index => subjectRows[index] !== undefined && (commits[index].Counters || []).length > 0;
    const counters = commits.map(commit => countersFor(commit, runtime, scenario, subject));
    const countersChanged = counters.map((value, index) => index > 0 && counted(index) && counted(index - 1) && value !== counters[index - 1]);
    const variant = subject.includes(' (') ? subject.slice(subject.indexOf(' (') + 2, -1) : '';
    const name = variant ? `${scenario} · ${variant}` : scenario;
    const panel = element('section', { class: 'rv-perf-panel rv-perf-spark' });
    const header = element('header');
    header.append(element('h4', {}, name));
    const recorded = (series[0]?.engine === subject ? series[0].raw : []).map((value, index) => ({ value, index })).filter(point => point.value !== null);
    if (recorded.length) {
      const first = recorded[0], last = recorded.at(-1), previous = recorded.at(-2);
      const delta = (from, to) => from >= 1 ? percent((to / from - 1) * 100)
        : `${number(to) === number(from) ? '±' : to > from ? '+' : '−'}${number(Math.abs(to - from))}`;
      const sinceFirst = first.value >= 1 ? Math.abs(last.value / first.value - 1) * 100 : 0;
      const summary = element('span');
      summary.append(document.createTextNode(`${number(last.value)} ${unit} · `),
        element(sinceFirst >= 5 ? 'strong' : 'span', {}, `${delta(first.value, last.value)} since ${short(commits[first.index])}`));
      if (previous) summary.append(document.createTextNode(` · ${delta(previous.value, last.value)} latest`));
      if (changeRequested && !change) summary.append(document.createTextNode(` · absolute: first record below 1 ${unit}`));
      header.append(summary);
    }
    panel.append(header);
    const left = 96, right = 628, top = 16, bottom = 172;
    const plotted = series.flatMap(item => [...item.plotted, ...(item.engine === subject ? item.band.flat() : [])]).filter(value => value !== null && value !== undefined);
    let low = 0, high = (Math.max(0, ...plotted) || 1) * 1.1;
    if (change) {
      low = Math.min(-2, ...plotted);
      high = Math.max(2, ...plotted);
      const pad = (high - low) * 0.1;
      low -= pad;
      high += pad;
    }
    const x = index => commits.length === 1 ? (left + right) / 2 : left + index / (commits.length - 1) * (right - left);
    const y = value => bottom - (value - low) / (high - low) * (bottom - top);
    const quantity = `${metrics[metric].label.toLowerCase()} per operation`;
    const svg = makeSvg(`${name}: ${change ? `change in ${quantity} since its first record` : quantity} across ${commits.length} commits`, 212);
    svg.setAttribute('tabindex', '0');
    svg.setAttribute('role', 'img');
    const compact = value => high < 10 ? number(value) : Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 }).format(value);
    for (const tick of change ? [low, 0, high].filter(tick => tick === 0 || Math.abs(y(tick) - y(0)) >= 24) : [0, high / 2, high]) {
      svg.append(svgElement('line', { x1: left, x2: right, y1: y(tick), y2: y(tick), class: change && tick === 0 ? 'rv-perf-reference' : 'rv-perf-grid' }));
      svg.append(svgElement('text', { x: left - 10, y: y(tick) + 7, 'text-anchor': 'end', class: 'rv-perf-spark-tick' }, change ? percent(tick) : compact(tick)));
    }
    // The subject's lowest-to-highest process range, drawn as one filled area per run of consecutive recorded commits.
    const spread = series.find(item => item.engine === subject)?.band || [];
    let run = [];
    const closeRun = () => {
      if (run.length > 1) {
        const upper = run.map(index => `${x(index)},${y(spread[index][1])}`);
        const lower = run.map(index => `${x(index)},${y(spread[index][0])}`).reverse();
        svg.append(svgElement('path', { d: `M${upper.join(' L')} L${lower.join(' L')} Z`, fill: colour(subject), class: 'rv-perf-spread' }));
      }
      run = [];
    };
    spread.forEach((range, index) => range && range[0] !== null ? run.push(index) : closeRun());
    closeRun();
    countersChanged.forEach((changed, index) => {
      if (changed) svg.append(svgElement('path', { d: `M${x(index)},${bottom + 2} l-6,10 h12 Z`, class: 'rv-perf-counter-change' }));
    });
    [...series].reverse().forEach(({ engine, plotted: points }) => {
      const isSubject = engine === subject;
      let path = '', connected = false;
      points.forEach((value, index) => {
        if (value === null) { connected = false; return; }
        path += `${connected ? 'L' : 'M'}${x(index)},${y(value)} `;
        connected = true;
      });
      svg.append(svgElement('path', { d: path, stroke: colour(engine), fill: 'none', 'stroke-width': isSubject ? 2.5 : 1.75, 'stroke-linejoin': 'round', opacity: isSubject ? 1 : 0.7 }));
      const lastIndex = points.findLastIndex(value => value !== null);
      if (isSubject && lastIndex >= 0) svg.append(svgElement('circle', { cx: x(lastIndex), cy: y(points[lastIndex]), r: 4.5, fill: colour(engine), class: 'rv-perf-dot' }));
    });
    svg.append(svgElement('text', { x: left, y: bottom + 32, class: 'rv-perf-spark-tick' }, short(commits[0])));
    svg.append(svgElement('text', { x: right, y: bottom + 32, 'text-anchor': 'end', class: 'rv-perf-spark-tick' }, short(commits.at(-1))));
    const crosshair = svgElement('line', { y1: top, y2: bottom, class: 'rv-perf-crosshair', visibility: 'hidden' });
    const markers = svgElement('g', { visibility: 'hidden' });
    svg.append(crosshair, markers);
    let current = commits.length - 1;
    const show = index => {
      current = Math.max(0, Math.min(commits.length - 1, index));
      crosshair.setAttribute('x1', x(current));
      crosshair.setAttribute('x2', x(current));
      crosshair.setAttribute('visibility', 'visible');
      markers.replaceChildren(...series.filter(item => item.plotted[current] !== null)
        .map(item => svgElement('circle', { cx: x(current), cy: y(item.plotted[current]), r: 5, fill: colour(item.engine), class: 'rv-perf-dot' })));
      markers.setAttribute('visibility', 'visible');
      const values = series.map(item => {
        if (item.raw[current] === null) return `${item.engine}: not recorded`;
        const row = item.rows[current];
        const range = item.engine === subject ? processRange(row) : '';
        const relative = change && item.plotted[current] !== null ? percent(item.plotted[current]) : '';
        const detail = [relative, range].filter(Boolean).join(', ');
        return `${item.engine}: ${number(item.raw[current])} ${unit}${detail ? ` (${detail})` : ''}`;
      });
      const library = counters[current] ? ` · ${countersChanged[current] ? 'library counters changed: ' : 'library counters: '}${counters[current]}` : '';
      output.textContent = `${name} · ${short(commits[current])} · ${commits[current].Subject} — ${values.join(' · ')}${library}`;
    };
    const hide = () => {
      crosshair.setAttribute('visibility', 'hidden');
      markers.setAttribute('visibility', 'hidden');
    };
    const indexAt = event => {
      const bounds = svg.getBoundingClientRect();
      const viewX = (event.clientX - bounds.left) / bounds.width * 640;
      return commits.length === 1 ? 0 : Math.round((viewX - left) / (right - left) * (commits.length - 1));
    };
    svg.addEventListener('pointermove', event => show(indexAt(event)));
    svg.addEventListener('pointerdown', event => show(indexAt(event)));
    svg.addEventListener('pointerleave', () => { if (document.activeElement !== svg) hide(); });
    svg.addEventListener('focus', () => show(current));
    svg.addEventListener('blur', hide);
    svg.addEventListener('keydown', event => {
      const target = { ArrowLeft: current - 1, ArrowRight: current + 1, Home: 0, End: commits.length - 1 }[event.key];
      if (target === undefined) return;
      event.preventDefault();
      show(target);
    });
    panel.append(svg);
    return panel;
  }


  const views = [['compare', 'Library comparison', mount], ['trends', 'Trends', mountTrends]];
  const tabKey = 'ranvier.perf.tab';
  const tabGroups = [];
  const storedTab = () => { try { return localStorage.getItem(tabKey); } catch { return null; } };
  const storeTab = key => { try { localStorage.setItem(tabKey, key); } catch { } };
  const hashTab = () => views.find(([key]) => location.hash === `#perf-${key}`)?.[0];
  // Comparison and trend views of one scenario group under a tablist. A view mounts when first shown and
  // adopts the runtime and metric last chosen in the other. The URL hash `#perf-compare` or `#perf-trends`
  // selects the opening view, otherwise the view last chosen on any page, otherwise the comparison.
  function mountTabs(root, commits) {
    const shared = {};
    const anchored = !tabGroups.length;
    const prefix = `rv-perf-${tabGroups.length}`;
    root.className = 'rv-perf-tabs';
    root.replaceChildren();
    const list = element('div', { role: 'tablist', class: 'rv-perf-tablist', 'aria-label': 'Measurement view' });
    const items = views.map(([key, label, mountView]) => {
      const tab = element('button', { type: 'button', role: 'tab', class: 'rv-perf-tab', id: anchored ? `perf-${key}` : `${prefix}-${key}`, 'aria-controls': `${prefix}-${key}-panel` }, label);
      const pane = element('div', { role: 'tabpanel', id: `${prefix}-${key}-panel`, 'aria-labelledby': tab.id });
      if (root.dataset.perfGroup) pane.dataset.perfGroup = root.dataset.perfGroup;
      tab.addEventListener('click', () => choose(key, false));
      list.append(tab);
      return { key, tab, pane, mountView, view: null };
    });
    root.append(list, ...items.map(item => item.pane));
    function select(key, focus) {
      for (const item of items) {
        const active = item.key === key;
        item.tab.setAttribute('aria-selected', String(active));
        item.tab.tabIndex = active ? 0 : -1;
        item.pane.hidden = !active;
        if (!active) continue;
        if (item.view) item.view.sync();
        else {
          try { item.view = item.mountView(item.pane, commits, shared); }
          catch (error) {
            item.pane.replaceChildren(element('p', { role: 'status', class: 'rv-perf-empty' }, error.message));
            item.view = { sync() { } };
          }
        }
        if (focus) item.tab.focus();
      }
    }
    function choose(key, focus) {
      storeTab(key);
      for (const group of tabGroups) group === select ? select(key, focus) : group(key, false);
    }
    list.addEventListener('keydown', event => {
      const index = items.findIndex(item => item.tab === document.activeElement);
      const target = { ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: items.length - 1 }[event.key];
      if (index < 0 || target === undefined) return;
      event.preventDefault();
      choose(items[(target + items.length) % items.length].key, true);
    });
    tabGroups.push(select);
    const stored = storedTab();
    select(hashTab() || (views.some(([key]) => key === stored) ? stored : 'compare'), false);
    if (anchored && hashTab()) items.find(item => item.key === hashTab()).tab.scrollIntoView();
  }
  window.addEventListener('hashchange', () => { const key = hashTab(); if (key) for (const group of tabGroups) group(key, false); });

  // A root reads `perf-data/<data-perf-data>.json`, a scenario group written by perf-site-data.cjs, or else the
  // full `perf-data/index.json`. Each file is requested once per page.
  const script = document.currentScript.src;
  const requests = new Map();
  const history = name => {
    if (!requests.has(name)) requests.set(name, fetch(new URL(`../perf-data/${name}.json`, script)).then(response => {
      if (!response.ok) throw new Error(`History request failed (${response.status})`);
      return response.json();
    }).then(data => {
      if (!Array.isArray(data.commits) || !data.commits.length) throw new Error('No recorded measurements yet.');
      return [...data.commits].sort((a, b) => Date.parse(a.Date) - Date.parse(b.Date) || a.Commit.localeCompare(b.Commit));
    }));
    return requests.get(name);
  };
  for (const root of roots) {
    const name = /^[a-z0-9-]+$/.test(root.dataset.perfData || '') ? root.dataset.perfData : 'index';
    history(name).then(commits => {
      try { root.hasAttribute('data-perf-tabs') ? mountTabs(root, commits) : root.hasAttribute('data-perf-trends') ? mountTrends(root, commits) : mount(root, commits); }
      catch (error) { root.replaceChildren(element('p', { role: 'status', class: 'rv-perf-empty' }, error.message)); }
    }).catch(error => {
      root.replaceChildren(element('p', { role: 'status', class: 'rv-perf-empty' }, `Performance history unavailable. ${error.message}`));
    });
  }
})();
