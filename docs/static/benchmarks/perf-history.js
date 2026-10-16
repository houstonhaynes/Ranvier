(() => {
  const root = document.querySelector('[data-perf-history]');
  if (!root) return;

  const status = root.querySelector('[data-perf-status]');
  const runtimeSelect = root.querySelector('[data-perf-runtime]');
  const select = root.querySelector('[data-perf-scenario]');
  const comparative = root.querySelector('[data-perf-comparative]');
  const canvas = root.querySelector('[data-perf-chart]');
  const links = root.querySelector('[data-perf-commit-links]');

  const commitUrl = commit => `https://github.com/shayanhabibi/Ranvier/commit/${commit.Commit}`;
  const shortCommit = commit => commit.Commit.slice(0, 7);
  const setStatus = message => { status.textContent = message; };
  const runtimeNames = { dotnet: '.NET', node: 'Node.js' };

  function token(name, fallback) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
  }

  function mediansFor(commits, runtime) {
    return commits.flatMap(commit => commit.Medians.filter(median =>
      median.Leg === 'arm64-counters'
      && median.Runtime === runtime
      && median.Metric === 'instructions'
    ));
  }

  function axisLabelIndex(chart, event) {
    const { x, y } = event;
    if (!Number.isFinite(x) || !Number.isFinite(y)) return -1;
    if (y < chart.chartArea.bottom) return -1;

    const positions = chart.data.labels.map((_, index) => chart.scales.x.getPixelForTick(index));
    let nearest = -1;
    let distance = Infinity;
    positions.forEach((position, index) => {
      const candidate = Math.abs(position - x);
      if (candidate < distance) {
        nearest = index;
        distance = candidate;
      }
    });
    return distance <= chart.scales.x.width / Math.max(1, positions.length) / 2 ? nearest : -1;
  }

  fetch('/Ranvier/perf-data/index.json')
    .then(response => {
      if (!response.ok) throw new Error(`History request failed (${response.status})`);
      return response.json();
    })
    .then(history => {
      if (!Array.isArray(history.commits) || history.commits.length === 0) {
        throw new Error('The performance history contains no commits');
      }
      if (!window.Chart) throw new Error('Chart.js did not load');

      const runtimes = [...new Set(history.commits.flatMap(commit =>
        commit.Medians
          .filter(median => median.Leg === 'arm64-counters' && median.Metric === 'instructions')
          .map(median => median.Runtime)
      ))].sort();
      if (runtimes.length === 0) throw new Error('No instruction-count runtimes are available');

      for (const runtime of runtimes) {
        const option = document.createElement('option');
        option.value = runtime;
        option.textContent = runtimeNames[runtime] ?? runtime;
        runtimeSelect.append(option);
      }
      runtimeSelect.value = runtimes.includes('dotnet') ? 'dotnet' : runtimes[0];

      function populateScenarios() {
        const previous = select.value;
        const scenarios = [...new Set(mediansFor(history.commits, runtimeSelect.value).map(median => median.Scenario))].sort();
        select.replaceChildren();
        for (const scenario of scenarios) {
          const option = document.createElement('option');
          option.value = scenario;
          option.textContent = scenario;
          select.append(option);
        }
        if (scenarios.length === 0) throw new Error(`No scenarios are available for ${runtimeSelect.value}`);
        select.value = scenarios.includes(previous) ? previous : scenarios.includes('create') ? 'create' : scenarios[0];
      }
      populateScenarios();

      for (const commit of history.commits) {
        const item = document.createElement('li');
        const link = document.createElement('a');
        link.href = commitUrl(commit);
        link.textContent = shortCommit(commit);
        link.title = `${commit.Subject} (${new Date(commit.Date).toLocaleDateString()})`;
        item.append(link);
        links.append(item);
      }

      const chart = new Chart(canvas, {
        type: 'bar',
        data: { labels: [], datasets: [] },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          onClick(event, elements) {
            const index = elements[0]?.index ?? axisLabelIndex(chart, event);
            if (index >= 0 && history.commits[index]) window.location.assign(commitUrl(history.commits[index]));
          },
          onHover(event, elements) {
            canvas.style.cursor = elements.length || axisLabelIndex(chart, event) >= 0 ? 'pointer' : 'default';
          },
          plugins: {
            legend: { labels: { color: token('--rv-muted', '#9aa5b1') } },
            tooltip: {
              callbacks: {
                title(items) {
                  const commit = history.commits[items[0].dataIndex];
                  return `${shortCommit(commit)} · ${commit.Subject}`;
                },
              },
            },
          },
          scales: {
            x: {
              title: {
                display: true,
                text: 'Commit (click a bar or hash to open the commit)',
                color: token('--rv-muted', '#9aa5b1'),
              },
              grid: { display: false },
              ticks: { maxRotation: 0, color: token('--rv-muted', '#9aa5b1') },
            },
            y: {
              beginAtZero: true,
              title: { display: true, text: 'Instructions per operation', color: token('--rv-muted', '#9aa5b1') },
              grid: { color: token('--rv-border', '#56616d') },
              ticks: { color: token('--rv-muted', '#9aa5b1') },
            },
          },
        },
      });

      const update = () => {
        const runtime = runtimeSelect.value;
        const scenario = select.value;
        const medians = mediansFor(history.commits, runtime).filter(median => median.Scenario === scenario);
        const rank = engine => {
          const known = ['Ranvier', 'FSharp.Data.Adaptive', 'R3', 'Fable.Ripple'];
          const index = known.findIndex(name => engine === name || engine.startsWith(`${name} (`));
          return index < 0 ? known.length : index;
        };
        const engines = [...new Set(medians.map(median => median.Engine))]
          .sort((left, right) => rank(left) - rank(right) || left.localeCompare(right));
        const hasRanvierBaseline = engines.includes('Ranvier');
        if (!hasRanvierBaseline) comparative.checked = false;
        comparative.disabled = !hasRanvierBaseline;
        const colors = [
          token('--rv-contour', '#69b9d0'),
          token('--rv-value', '#8bcba9'),
          token('--rv-violet', '#b2a0ff'),
          token('--rv-pending', '#f0bf73'),
          token('--rv-error', '#ef8585'),
        ];
        chart.data.labels = history.commits.map(shortCommit);
        const baselineFor = commit => commit.Medians.find(item =>
          item.Leg === 'arm64-counters'
          && item.Runtime === runtime
          && item.Metric === 'instructions'
          && item.Scenario === scenario
          && item.Engine === 'Ranvier'
        )?.Value;
        chart.data.datasets = engines.map((engine, index) => ({
          label: engine,
          data: history.commits.map(commit => {
            const median = commit.Medians.find(item =>
              item.Leg === 'arm64-counters'
              && item.Runtime === runtime
              && item.Metric === 'instructions'
              && item.Scenario === scenario
              && item.Engine === engine
            );
            if (!(median?.Value > 0)) return null;
            if (!comparative.checked) return median.Value;
            const baseline = baselineFor(commit);
            return baseline > 0 ? median.Value / baseline * 100 : null;
          }),
          backgroundColor: colors[index % colors.length],
          hoverBackgroundColor: colors[index % colors.length],
          borderRadius: 4,
        }));
        if (comparative.checked) {
          chart.data.datasets.push({
            type: 'line',
            label: 'Ranvier baseline (100%)',
            data: history.commits.map(() => 100),
            borderColor: token('--rv-muted', '#9aa5b1'),
            borderDash: [5, 4],
            borderWidth: 1,
            pointRadius: 0,
            pointHitRadius: 0,
            fill: false,
          });
        }
        chart.options.scales.y.title.text = comparative.checked
          ? 'Instructions (% of Ranvier)'
          : `Instructions per operation (${runtimeNames[runtime] ?? runtime})`;
        chart.options.scales.y.ticks.callback = comparative.checked
          ? value => `${value}%`
          : undefined;
        chart.update();
        const comparison = comparative.checked ? ' Relative to Ranvier at 100%.' : '';
        setStatus(`${engines.length} engine${engines.length === 1 ? '' : 's'} available for ${scenario} across ${history.commits.length} commit${history.commits.length === 1 ? '' : 's'}.${comparison}`);
      };
      select.addEventListener('change', update);
      comparative.addEventListener('change', update);
      runtimeSelect.addEventListener('change', () => {
        populateScenarios();
        update();
      });
      update();
    })
    .catch(error => {
      console.error('Unable to render CI counter history', error);
      setStatus(`Unable to load CI counter history: ${error.message}`);
    });
})();
