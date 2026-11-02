import { chromium } from 'playwright';
import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve(process.argv[2]);
const server = http.createServer(async (request, response) => {
  try {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).replace(/^\//, '');
    const file = path.resolve(root, relative || 'index.html');
    if (!file.startsWith(root + path.sep)) throw new Error('Invalid path');
    response.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : 'text/html');
    response.end(relative ? await fs.readFile(file) : '<script type="module" src="/Browser.js"></script>');
  } catch {
    response.writeHead(404);
    response.end();
  }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true, executablePath: process.argv[3] || process.env.RANVIER_CHROMIUM_PATH || undefined });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(String(error)));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.waitForFunction(() => globalThis.__mutationProof !== undefined);
  const result = await page.evaluate(() => globalThis.__mutationProof);
  if (result.error || errors.length) throw new Error(JSON.stringify({ ...result, errors }));
  if (result.checks !== 50) throw new Error(`Incomplete fixture: ${result.checks}`);
  console.log(JSON.stringify({ ...result, browser: browser.version(), pass: true }));
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
