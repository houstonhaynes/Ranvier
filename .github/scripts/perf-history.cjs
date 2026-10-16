const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');

function command(exe, args, options = {}) {
  const result = spawnSync(exe, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, ...options });
  if (result.error) throw result.error;
  return result;
}

function publishHistory(recordFile, tool, execute = command) {
  const record = JSON.parse(fs.readFileSync(recordFile, 'utf8'));
  assert.match(record.Commit, /^[0-9a-f]{40}$/);
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'ranvier-perf-history-'));
  const git = (args, extra = {}, allowFailure = false) => {
    const result = execute('git', args, extra);
    if (!allowFailure && result.status !== 0) throw new Error(`git ${args[0]} failed: ${result.stderr}`);
    return result;
  };
  try {
    for (let attempt = 0; attempt < 8; attempt++) {
      // Exit 2 alone means no matching branch; authentication/network failures must not create a replacement history.
      const remote = git(['ls-remote', '--exit-code', '--heads', 'origin', 'refs/heads/perf-data'], {}, true);
      if (![0, 2].includes(remote.status)) throw new Error(`Cannot inspect perf-data: ${remote.stderr}`);
      let parent;
      if (remote.status === 0) {
        git(['fetch', '--no-tags', 'origin', 'refs/heads/perf-data']);
        parent = git(['rev-parse', 'FETCH_HEAD']).stdout.trim();
        assert.match(parent, /^[0-9a-f]{40}$/);
      }
      const folder = path.join(scratch, String(attempt));
      const runs = path.join(folder, 'runs');
      fs.mkdirSync(runs, { recursive: true });
      const index = path.join(folder, 'index');
      const env = { ...process.env, GIT_INDEX_FILE: index,
        GIT_AUTHOR_NAME: 'github-actions[bot]', GIT_AUTHOR_EMAIL: '41898282+github-actions[bot]@users.noreply.github.com',
        GIT_COMMITTER_NAME: 'github-actions[bot]', GIT_COMMITTER_EMAIL: '41898282+github-actions[bot]@users.noreply.github.com' };
      git(parent ? ['read-tree', parent] : ['read-tree', '--empty'], { env });
      if (parent) {
        const names = git(['ls-tree', '-r', '--name-only', parent, '--', 'runs/']).stdout.trim().split('\n').filter(Boolean);
        for (const name of names) {
          assert.match(name, /^runs\/[0-9a-f]{40}\.json$/);
          fs.writeFileSync(path.join(folder, name), git(['show', `${parent}:${name}`]).stdout);
        }
      }
      fs.copyFileSync(recordFile, path.join(runs, `${record.Commit}.json`));
      const timeline = path.join(folder, 'timeline.md');
      const generated = execute('dotnet', [path.resolve(tool), 'timeline', '--runs', runs, '--out', timeline]);
      if (generated.status !== 0) throw new Error(`Timeline failed: ${generated.stderr}`);
      for (const [name, file] of [[`runs/${record.Commit}.json`, recordFile], ['timeline.md', timeline]]) {
        const blob = git(['hash-object', '-w', file]).stdout.trim();
        git(['update-index', '--add', '--cacheinfo', `100644,${blob},${name}`], { env });
      }
      const tree = git(['write-tree'], { env }).stdout.trim();
      const commit = git(['commit-tree', tree, ...(parent ? ['-p', parent] : []), '-m', `perf: record ${record.Commit}`], { env }).stdout.trim();
      const pushed = git(['push', 'origin', `${commit}:refs/heads/perf-data`], {}, true);
      if (pushed.status === 0) return;
      // A competing writer may have advanced the branch. Re-read all runs and regenerate the timeline.
      if (attempt === 7) throw new Error(`History push failed after retries: ${pushed.stderr}`);
    }
  } finally { fs.rmSync(scratch, { recursive: true, force: true }); }
}

if (require.main === module) publishHistory(process.argv[2], process.argv[3]);
module.exports = { publishHistory };
