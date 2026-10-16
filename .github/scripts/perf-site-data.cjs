const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

function buildIndex(runsDirectory, outputFile) {
  const files = fs.readdirSync(runsDirectory).filter(file => file.endsWith('.json')).sort();
  if (files.length === 0) throw new Error(`No performance records found in ${runsDirectory}`);

  const seen = new Set();
  const commits = files.map(file => {
    const match = /^([0-9a-f]{40})\.json$/.exec(file);
    assert.ok(match, `Invalid performance record filename: ${file}`);

    const record = JSON.parse(fs.readFileSync(path.join(runsDirectory, file), 'utf8'));
    assert.equal(record.Commit, match[1], `Commit in ${file} does not match its filename`);
    assert.ok(!seen.has(record.Commit), `Duplicate performance record for ${record.Commit}`);
    seen.add(record.Commit);
    assert.ok(!Number.isNaN(Date.parse(record.Date)), `Invalid date in ${file}`);
    assert.equal(typeof record.Subject, 'string', `Missing subject in ${file}`);
    assert.ok(Array.isArray(record.Medians), `Missing median data in ${file}`);

    const medians = record.Medians.filter(median => median.Side === 'head');
    assert.ok(medians.length > 0, `No head measurements in ${file}`);
    for (const median of medians) {
      for (const field of ['Leg', 'Runtime', 'Scenario', 'Engine', 'Metric']) {
        assert.equal(typeof median[field], 'string', `Missing ${field} in ${file}`);
      }
      assert.ok(Number.isFinite(median.Value), `Invalid measurement in ${file}`);
    }

    return {
      Commit: record.Commit,
      Date: record.Date,
      Subject: record.Subject,
      Medians: medians,
    };
  });

  commits.sort((left, right) => Date.parse(left.Date) - Date.parse(right.Date));
  fs.mkdirSync(path.dirname(outputFile), { recursive: true });
  fs.writeFileSync(outputFile, `${JSON.stringify({ commits }, null, 2)}\n`);
}

if (require.main === module) {
  const [, , runsDirectory, outputFile] = process.argv;
  if (!runsDirectory || !outputFile) throw new Error('Usage: perf-site-data.cjs <runs-directory> <output-file>');
  buildIndex(runsDirectory, outputFile);
}

module.exports = { buildIndex };
