// Per-page bundle sizes of the built app (npm run size builds it first): each page's entry chunk with every chunk it
// imports statically, and its stylesheets, each compressed with gzip level 9. Reports only; no budget. Writes
// dist/size.json.
import { readFileSync, writeFileSync } from "node:fs";
import { gzipSync } from "node:zlib";

const dist = new URL("./dist/", import.meta.url);
const manifest = JSON.parse(readFileSync(new URL(".vite/manifest.json", dist), "utf8"));
const gzip = file => gzipSync(readFileSync(new URL(file, dist)), { level: 9 }).length;
const raw = file => readFileSync(new URL(file, dist)).length;

const closure = key => {
  const seen = new Set();
  const visit = k => {
    if (seen.has(k)) return;
    seen.add(k);
    for (const imported of manifest[k].imports ?? []) visit(imported);
  };
  visit(key);
  return [...seen].map(k => manifest[k]);
};

const pages = Object.entries(manifest).filter(([key, chunk]) => chunk.isEntry && key.endsWith("index.html"));
const result = {};
for (const [key, entry] of pages) {
  const chunks = closure(key);
  const scripts = chunks.map(chunk => chunk.file);
  const styles = [...new Set(chunks.flatMap(chunk => chunk.css ?? []))];
  const page = key === "index.html" ? "index" : key.replace("/index.html", "");
  result[page] = {
    jsRaw: scripts.reduce((sum, file) => sum + raw(file), 0),
    jsGzip: scripts.reduce((sum, file) => sum + gzip(file), 0),
    cssGzip: styles.reduce((sum, file) => sum + gzip(file), 0),
    chunks: scripts.length
  };
}

writeFileSync(new URL("size.json", dist), JSON.stringify(result, null, 2) + "\n");
console.log("page        JS raw     JS gzip   CSS gzip  chunks");
for (const [page, size] of Object.entries(result)) {
  const kB = n => (n / 1024).toFixed(1).padStart(7) + " kB";
  console.log(`${page.padEnd(10)}${kB(size.jsRaw)}${kB(size.jsGzip)}${kB(size.cssGzip)}${String(size.chunks).padStart(6)}`);
}
