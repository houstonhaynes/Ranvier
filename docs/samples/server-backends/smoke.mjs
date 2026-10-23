import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const dist = fileURLToPath(new URL('../../../examples/Ranvier.Hydration/dist', import.meta.url));

function state(html) {
  const script = html.match(/<script\b[^>]*\bid="hydration-props"[^>]*>([\s\S]*?)<\/script>/);
  assert.ok(script, 'Server HTML contains hydration state');
  return JSON.parse(script[1]);
}

for (const [backend, port] of [['Oxpecker', 5081], ['Suave', 5082]]) {
  const assembly = fileURLToPath(new URL(`./${backend}/bin/Release/net10.0/Ranvier.Docs.${backend}.dll`, import.meta.url));
  const server = spawn('dotnet', [assembly, dist], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  let stopped = false;
  const exited = new Promise(resolve => {
    server.once('error', error => { output += error.message; stopped = true; resolve(); });
    server.once('exit', () => { stopped = true; resolve(); });
  });
  server.stdout.on('data', chunk => { output += chunk; });
  server.stderr.on('data', chunk => { output += chunk; });
  const origin = `http://127.0.0.1:${port}`;

  try {
    let ready = false;
    for (let attempt = 0; attempt < 100; attempt++) {
      if (stopped) throw new Error(`${backend} exited during startup:\n${output}`);
      try {
        const response = await fetch(origin, { signal: AbortSignal.timeout(500) });
        if (response.ok) { ready = true; break; }
      } catch {}
      await delay(100);
    }
    assert.ok(ready, `${backend} did not become ready:\n${output}`);

    const rendered = await fetch(`${origin}/?greeting=Backend%20greeting&start=7`);
    assert.equal(rendered.status, 200);
    assert.match(rendered.headers.get('content-type'), /text\/html/);
    const html = await rendered.text();
    assert.ok(html.includes('Backend greeting'));
    assert.deepEqual(state(html), { greeting: 'Backend greeting', start: 7 });
    assert.ok(html.includes('./assets/client.js'));
    assert.ok(html.includes('./style.css'));

    const fallback = await (await fetch(`${origin}/?start=not-a-number`)).text();
    assert.ok(fallback.includes('Hello from F#'));
    assert.ok(!fallback.includes('Backend greeting'));
    assert.deepEqual(state(fallback), { greeting: 'Hello from F#', start: 3 });

    const hostile = '</script><script>alert(42)</script>';
    const escaped = await (await fetch(`${origin}/?greeting=${encodeURIComponent(hostile)}&start=9`)).text();
    assert.ok(!escaped.includes(hostile));
    assert.ok(escaped.includes('&lt;/script&gt;'));
    assert.deepEqual(state(escaped), { greeting: hostile, start: 9 });

    for (const [path, mime] of [['/assets/client.js', /javascript/], ['/style.css', /text\/css/]]) {
      const asset = await fetch(origin + path);
      assert.equal(asset.status, 200, `${backend} ${path}`);
      assert.match(asset.headers.get('content-type'), mime);
      assert.ok((await asset.text()).length > 0);
    }
    for (const path of ['/missing', '/index.html', '/assets/missing.js']) {
      assert.equal((await fetch(origin + path)).status, 404, `${backend} ${path}`);
    }
    console.log(`${backend}: request rendering, isolation, escaping, assets and missing routes passed`);
  } finally {
    if (!stopped) server.kill();
    await exited;
  }
}
