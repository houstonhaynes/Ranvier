(() => {
  const root = document.querySelector('[data-perf-compare]');
  if (!root) return;

  const status = root.querySelector('[data-perf-status]');
  const runtimeSelect = root.querySelector('[data-perf-runtime]');
  const select = root.querySelector('[data-perf-scenario]');
  const canvas = root.querySelector('[data-perf-chart]');

  const setStatus = message => { status.textContent = message; };
  const runtimeNames = { dotnet: '.NET', node: 'Node.js' };

  function token(name, fallback) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
  }

  // Read the scenario group from the container's data attribute
  const scenarioGroup = root.getAttribute('data-perf-group') || '';

  function mediansFor(commit, runtime) {
    return commit.Medians.filter(median =>
      median.Leg === 'arm64-counters'
      && median.Runtime === runtime
      && median.Metric === 'instructions'
    );
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

      // Use the latest commit
      const commit = history.commits[history.commits.length - 1];
      const shortCommit = commit.Commit.slice(0, 7);

      const runtimes = [...new Set(commit.Medians
        .filter(median => median.Leg === 'arm64-counters' && median.Metric === 'instructions')
        .map(median => median.Runtime)
      )].sort();
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
        const medians = mediansFor(commit, runtimeSelect.value);
        let scenarios = [...new Set(medians.map(m => m.Scenario))].sort();

        // Filter to the group if specified
        if (scenarioGroup) {
          const groups = scenarioGroup.split(',').map(s => s.trim());
          scenarios = scenarios.filter(s => groups.some(g => s.startsWith(g)));
        }

        select.replaceChildren();
        for (const scenario of scenarios) {
          const option = document.createElement('option');
          option.value = scenario;
          option.textContent = scenario;
          select.append(option);
        }
        if (scenarios.length === 0) throw new Error(`No scenarios are available for ${runtimeSelect.value}`);
        select.value = scenarios.includes(previous) ? previous : scenarios[0];
      }
      populateScenarios();

      const chart = new Chart(canvas, {
        type: 'bar',
        data: { labels: [], datasets: [] },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { labels: { color: token('--rv-muted', '#9aa5b1') } },
            tooltip: {
              callbacks: {
                title(items) {
                  return items[0]?.label || '';
                },
                label(item) {
                  const val = item.raw;
                  if (val === null || val === undefined) return '';
                  return `${item.dataset.label}: ${val.toLocaleString(undefined, { maximumFractionDigits: 0 })} instr/op`;
                }
              }
            }
          },
          scales: {
            x: {
              grid: { display: false },
              ticks: { maxRotation: 45, color: token('--rv-muted', '#9aa5b1') },
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
        const medians = mediansFor(commit, runtime).filter(m => m.Scenario === scenario);

        const rank = engine => {
          const known = ['Ranvier', 'FSharp.Data.Adaptive', 'R3', 'Fable.Ripple'];
          const index = known.findIndex(name => engine === name || engine.startsWith(`${name} (`));
          return index < 0 ? known.length : index;
        };
        const engines = [...new Set(medians.map(m => m.Engine))]
          .sort((left, right) => rank(left) - rank(right) || left.localeCompare(right));

        const colors = [
          token('--rv-contour', '#69b9d0'),
          token('--rv-value', '#8bcba9'),
          token('--rv-violet', '#b2a0ff'),
          token('--rv-pending', '#f0bf73'),
          token('--rv-error', '#ef8585'),
        ];

        chart.data.labels = engines;
        chart.data.datasets = [{
          label: scenario,
          data: engines.map(engine => {
            const median = medians.find(m => m.Engine === engine);
            if (!(median?.Value > 0)) return null;
            return median.Value;
          }),
          backgroundColor: colors[0],
          hoverBackgroundColor: colors[0],
          borderRadius: 4,
        }];

        chart.options.scales.y.title.text = `Instructions per operation (${runtimeNames[runtime] ?? runtime})`;
        chart.update();

        setStatus(`${engines.length} engine${engines.length === 1 ? '' : 's'} for ${scenario} at ${shortCommit}`);
      };

      select.addEventListener('change', update);
      runtimeSelect.addEventListener('change', () => {
        populateScenarios();
        update();
      });
      update();
    })
    .catch(error => {
      console.error('Unable to render comparison chart', error);
      setStatus(`Unable to load comparison data: ${error.message}`);
    });
})();
