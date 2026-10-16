(() => {
  'use strict';
  const roots = [...document.querySelectorAll('[data-perf-compare], [data-perf-history]')].filter(root => !root.dataset.perfReady);
  if (!roots.length) return;
  roots.forEach(root => { root.dataset.perfReady = 'true'; });
  const NS = 'http://www.w3.org/2000/svg';
  const names = { dotnet: '.NET', node: 'Node.js / Fable' };
  const number = value => value.toLocaleString('en-US', { maximumFractionDigits: 1 });
  const ratio = value => value > 0 && value < 0.1 ? value.toLocaleString('en-US', { maximumSignificantDigits: 2 }) : number(value);
  const short = commit => commit.Commit.slice(0, 7);
  const enginesIn = rows => [...new Set(rows.map(row => row.Engine))].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
  const rank = name => ['Ranvier', 'FSharp.Data.Adaptive', 'R3', 'Fable.Ripple'].findIndex(family => name.startsWith(family)) + 1 || 5;
  const family = engine => engine.split(' (')[0];
  const colour = engine => ['#3987e5', '#eb6834', '#1baf7a', '#bd8a31', '#9b7bc4'][rank(engine) - 1];
  const measurements = (commit, runtime) => commit.Medians.filter(row => row.Leg === 'arm64-counters'
    && row.Runtime === runtime && row.Metric === 'instructions' && (row.Side === undefined || row.Side === 'head'));

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
  function comparison(scenario, rows, relative, output, baselineValue) {
    const panel = element('section', { class: 'rv-perf-panel' });
    const header = element('header');
    const sorted = [...rows].sort((a, b) => rank(a.Engine) - rank(b.Engine) || a.Engine.localeCompare(b.Engine));
    const baseline = baselineValue ?? sorted.find(row => row.Engine === 'Ranvier')?.Value;
    const relativeAvailable = relative && baseline > 0;
    header.append(element('h4', {}, scenario), element('span', {}, relativeAvailable ? 'Ranvier = 1×' : 'instructions / op'));
    panel.append(header);
    if (relative && !relativeAvailable) panel.append(element('p', { class: 'rv-perf-note' }, 'Ranvier reference unavailable; showing absolute values.'));
    const max = Math.max(1, ...sorted.map(row => row.Value > 0 ? row.Value : 0));
    const svg = makeSvg(`${scenario}: instructions per operation by library`, 20 + sorted.length * 55);
    sorted.forEach((row, index) => {
      const y = 13 + index * 55;
      const group = svgElement('g', { class: 'rv-perf-bar', role: 'group' });
      const valid = row.Value >= 0;
      const value = !valid ? 'unavailable' : relativeAvailable ? `${ratio(row.Value / baseline)}×` : number(row.Value);
      group.append(svgElement('text', { x: 0, y, class: 'rv-perf-svg-label' }, row.Engine));
      group.append(svgElement('text', { x: 640, y, 'text-anchor': 'end', class: 'rv-perf-svg-value' }, value));
      group.append(svgElement('line', { x1: 0, x2: 640, y1: y + 22, y2: y + 22, class: 'rv-perf-track' }));
      if (valid) group.append(svgElement('rect', { x: 0, y: y + 15, width: Math.max(row.Value === 0 ? 0 : 1.5, row.Value / max * 640), height: 14, rx: 2, fill: colour(row.Engine) }));
      group.append(svgElement('rect', { x: 0, y: y - 12, width: 640, height: 48, fill: 'transparent' }));
      inspectable(group, `${scenario} · ${row.Engine}: ${valid ? `${number(row.Value)} instructions/op` : 'measurement unavailable'}${relativeAvailable && valid ? ` · ${ratio(row.Value / baseline)}× Ranvier` : ''}`, output);
      svg.append(group);
    });
    panel.append(svg);
    return panel;
  }

  function mount(root, commits) {
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
    const runtime = selectControl('Runtime', runtimes.map(key => [key, names[key] || key]), runtimes.includes('dotnet') ? 'dotnet' : runtimes[0]);
    const commit = selectControl('Commit', commits.map((item, index) => [String(index), `${short(item)} · ${item.Subject}`]), String(selectedCommit));
    const mode = selectControl('Values', [['absolute', 'Instructions / op'], ['relative', 'Relative to Ranvier']], 'absolute');
    controls.append(runtime.wrapper, commit.wrapper, mode.wrapper);
    const search = element('input', { type: 'search', placeholder: 'Find a scenario…', 'aria-label': 'Find a scenario', class: 'rv-perf-search' });
    const scenario = selectControl('Scenario', [], '');
    controls.append(isHistory ? scenario.wrapper : search);
    const legend = element('div', { class: 'rv-perf-legend', 'aria-label': 'Visible libraries' });
    const provenance = element('div', { class: 'rv-perf-provenance' });
    const body = element('div', { class: isHistory ? 'rv-perf-trend' : 'rv-perf-panels' });
    const detail = element('p', { class: 'rv-perf-inspect', role: 'status', 'aria-live': 'polite' }, 'Point to a bar or focus it to inspect the measurement.');
    root.append(heading, controls, legend, provenance, body, detail);
    let previousRuntime;

    function update() {
      const focusedKey = root.contains(document.activeElement) ? document.activeElement.getAttribute('data-focus-key') : null;
      selectedCommit = Number(commit.select.value);
      const selected = commits[selectedCommit];
      const rows = measurements(selected, runtime.select.value);
      const allRows = isHistory ? commits.flatMap(item => measurements(item, runtime.select.value)) : rows;
      const engines = enginesIn(allRows);
      if (previousRuntime !== runtime.select.value) hidden.clear();
      previousRuntime = runtime.select.value;
      const families = [...new Set(engines.map(family))];
      if (JSON.stringify(families) !== legend.dataset.families) {
        legend.replaceChildren(...families.map(engine => engineToggle(engine, hidden, update)));
        legend.dataset.families = JSON.stringify(families);
      }
      [...legend.children].forEach((button, index) => button.setAttribute('aria-pressed', String(!hidden.has(families[index]))));
      const date = new Date(selected.Date).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
      const link = element('a', { href: `https://github.com/shayanhabibi/Ranvier/commit/${selected.Commit}`, class: 'rv-perf-sha' }, short(selected));
      provenance.replaceChildren(link, document.createTextNode(` · ${date} · ${selected.Subject}`));
      const available = [...new Set(allRows.map(row => row.Scenario))].filter(name => !group.length || group.some(prefix => name.startsWith(prefix))).sort();
      const currentScenario = scenario.select.value;
      scenario.select.replaceChildren(...available.map(name => element('option', { value: name }, name)));
      scenario.select.value = available.includes(currentScenario) ? currentScenario : available.includes('create') ? 'create' : available[0] || '';
      body.replaceChildren();
      const visible = rows.filter(row => !hidden.has(family(row.Engine)));
      if (isHistory) {
        renderTrend(body, commits, runtime.select.value, scenario.select.value, engines.filter(engine => !hidden.has(family(engine))), mode.select.value === 'relative', detail, selectedCommit, index => {
          commit.select.value = String(index);
          update();
        });
        const current = visible.filter(row => row.Scenario === scenario.select.value);
        if (current.length) body.append(comparison(scenario.select.value, current, mode.select.value === 'relative', detail, rows.find(row => row.Scenario === scenario.select.value && row.Engine === 'Ranvier')?.Value));
      } else {
        for (const name of available.filter(name => name.toLowerCase().includes(search.value.trim().toLowerCase()))) {
          const matching = visible.filter(row => row.Scenario === name);
          if (matching.length) body.append(comparison(name, matching, mode.select.value === 'relative', detail, rows.find(row => row.Scenario === name && row.Engine === 'Ranvier')?.Value));
        }
      }
      if (!body.children.length) body.append(element('p', { class: 'rv-perf-empty' }, 'No matching measurements. Adjust the filters or show a library.'));
      detail.textContent = 'Hover or focus a mark for exact values. Each comparison panel scales from zero to its largest value.';
      if (focusedKey) [...root.querySelectorAll('[data-focus-key]')].find(node => node.getAttribute('data-focus-key') === focusedKey)?.focus({ preventScroll: true });
    }
    for (const control of [runtime.select, commit.select, mode.select, scenario.select]) control.addEventListener('change', update);
    search.addEventListener('input', update);
    update();
  }

  function renderTrend(body, commits, runtime, scenario, engines, relative, detail, selected, onSelect) {
    const svg = makeSvg(`${scenario}: instruction history. Select a commit to inspect its library comparison.`, 300);
    const left = 62, right = 608, top = 25, bottom = 240;
    const x = index => commits.length === 1 ? (left + right) / 2 : left + index / (commits.length - 1) * (right - left);
    const valueFor = (commit, engine) => {
      const rows = measurements(commit, runtime).filter(row => row.Scenario === scenario);
      const value = rows.find(row => row.Engine === engine)?.Value;
      const base = rows.find(row => row.Engine === 'Ranvier')?.Value;
      if (!(value >= 0)) return null;
      return relative ? base > 0 ? value / base : null : value;
    };
    const values = engines.map(engine => ({ engine, values: commits.map(commit => valueFor(commit, engine)) }));
    const max = Math.max(1, ...values.flatMap(series => series.values.filter(value => value !== null))) * 1.12;
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
        inspectable(dot, `${short(commits[index])} · ${commits[index].Subject} · ${engine}: ${relative ? ratio(value) : number(value)}${relative ? '× Ranvier' : ' instructions/op'}`, detail);
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
    svg.append(svgElement('text', { x: left, y: 290, class: 'rv-perf-tick' }, relative ? 'Relative to Ranvier at each commit · missing values leave gaps' : 'Instructions / op · lower is less work · missing values leave gaps'));
    body.append(svg);
    if (!values.some(series => series.values.some(value => value !== null))) body.append(element('p', { class: 'rv-perf-empty' }, 'No visible measurements for this scenario.'));
  }

  const source = new URL('../perf-data/index.json', document.currentScript.src);
  fetch(source).then(response => {
    if (!response.ok) throw new Error(`History request failed (${response.status})`);
    return response.json();
  }).then(history => {
    if (!Array.isArray(history.commits) || !history.commits.length) throw new Error('No recorded measurements yet.');
    const commits = [...history.commits].sort((a, b) => Date.parse(a.Date) - Date.parse(b.Date) || a.Commit.localeCompare(b.Commit));
    for (const root of roots) {
      try { mount(root, commits); }
      catch (error) { root.replaceChildren(element('p', { role: 'status', class: 'rv-perf-empty' }, error.message)); }
    }
  }).catch(error => {
    for (const root of roots) root.replaceChildren(element('p', { role: 'status', class: 'rv-perf-empty' }, `Performance history unavailable. ${error.message}`));
  });
})();
