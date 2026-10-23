import test from "node:test";
import assert from "node:assert/strict";
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { execSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { readPages } from "../page-build.mjs";

const root = fileURLToPath(new URL("../", import.meta.url));
const document = body => `<!doctype html><html><head><title>Fixture</title><link rel="stylesheet" href="/style.css"></head><body>${body}<script type="module" src="/generated/client.js"></script></body></html>`;
const manifest = { pages: [{ name: "index", path: "index.html" }, { name: "story", path: "story/index.html" }] };

function fixture(t) {
  const directory = mkdtempSync(join(root, ".build-test-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  for (const name of ["story", "src", "generated-pages", "generated"]) mkdirSync(join(directory, name));
  for (const name of ["vite.config.mjs", "page-build.mjs"]) copyFileSync(join(root, name), join(directory, name));
  writeFileSync(join(directory, "style.css"), "body { color: navy; }");
  const pkg = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
  pkg.scripts["compile:client"] = "node compile.mjs";
  pkg.scripts.server = "node server.mjs";
  writeFileSync(join(directory, "package.json"), JSON.stringify(pkg));
  writeFileSync(join(directory, "compile.mjs"), 'import { readFileSync, writeFileSync } from "node:fs"; readFileSync("generated-pages/pages.json"); writeFileSync("generated/client.js", "document.body.dataset.compiled = \'yes\';");');
  writeFileSync(join(directory, "server.mjs"), `import { writeFileSync } from "node:fs";
    writeFileSync("generated-pages/pages.json", ${JSON.stringify(JSON.stringify(manifest))});
    writeFileSync("index.html", ${JSON.stringify(document("Fresh index"))});
    writeFileSync("story/index.html", ${JSON.stringify(document("Fresh server Story"))});`);
  return directory;
}

test("Vite refuses a missing manifest or document instead of silently dropping pages", t => {
  const directory = fixture(t);
  assert.throws(() => readPages(directory), /Missing generated-pages\/pages.json/);
  writeFileSync(join(directory, "generated-pages/pages.json"), JSON.stringify(manifest));
  writeFileSync(join(directory, "index.html"), document("Index"));
  assert.throws(() => readPages(directory), /Missing generated document story\/index.html/);
  assert.throws(() => execSync("npx --no-install vite build", { cwd: directory, stdio: "pipe" }));
});

test("manifest entries cannot escape the example directory or overwrite another input", t => {
  const directory = fixture(t);
  for (const pages of [
    [{ name: "escape", path: "../outside.html" }],
    [{ name: "same", path: "index.html" }, { name: "same", path: "story/index.html" }]
  ]) {
    writeFileSync(join(directory, "generated-pages/pages.json"), JSON.stringify({ pages }));
    writeFileSync(join(directory, "index.html"), document("Index"));
    assert.throws(() => readPages(directory), /Invalid or duplicate/);
  }
});

for (const initial of ["missing", "stale"]) {
  test(`the supported build replaces ${initial} documents and compiles entries before bundling`, t => {
    const directory = fixture(t);
    if (initial === "stale") {
      writeFileSync(join(directory, "generated-pages/pages.json"), JSON.stringify(manifest));
      writeFileSync(join(directory, "story/index.html"), document("Stale Story"));
    }
    execSync("npm run build", { cwd: directory, stdio: "pipe" });
    const html = readFileSync(join(directory, "dist/story/index.html"), "utf8");
    assert.match(html, /Fresh server Story/);
    assert.match(readFileSync(join(directory, "dist/index.html"), "utf8"), /Fresh index/);
    const assets = [...html.matchAll(/(?:src|href)="([^"]+)"/g)].map(match => match[1]);
    assert.equal(assets.length >= 2, true);
    for (const asset of assets) {
      assert.equal(asset.startsWith("../assets/"), true, asset);
      assert.equal(new URL(asset, "https://example.com/examples/motion/story/").pathname.startsWith("/examples/motion/assets/"), true);
      assert.equal(existsSync(join(directory, "dist/story", asset)), true);
    }
  });
}

test("a failed generator stops the supported build before publishing stale documents", t => {
  const directory = fixture(t);
  writeFileSync(join(directory, "generated-pages/pages.json"), JSON.stringify(manifest));
  writeFileSync(join(directory, "index.html"), document("Stale index"));
  writeFileSync(join(directory, "story/index.html"), document("Stale Story"));
  writeFileSync(join(directory, "server.mjs"), "process.exit(23);");
  assert.throws(() => execSync("npm run build", { cwd: directory, stdio: "pipe" }));
  assert.equal(existsSync(join(directory, "generated/client.js")), false);
  assert.equal(existsSync(join(directory, "dist/story/index.html")), false);
});
