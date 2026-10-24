// Serves the production documentation output below /Ranvier/, as GitHub Pages does.
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("../../../output", import.meta.url)));
const port = Number(process.env.PORT ?? 5090);
const types = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml", ".png": "image/png", ".json": "application/json", ".wasm": "application/wasm",
  ".woff2": "font/woff2", ".txt": "text/plain; charset=utf-8", ".md": "text/markdown; charset=utf-8"
};

async function file(path) {
  const info = await stat(path).catch(() => undefined);
  if (info?.isDirectory()) return file(join(path, "index.html"));
  return info?.isFile() ? path : undefined;
}

createServer(async (request, response) => {
  const { pathname } = new URL(request.url, "http://localhost");
  if (!pathname.startsWith("/Ranvier/")) { response.writeHead(404).end("Not found"); return; }
  const target = normalize(join(root, decodeURIComponent(pathname.slice("/Ranvier/".length))));
  const found = target === root || target.startsWith(root + sep) ? await file(target) : undefined;
  if (!found) { response.writeHead(404).end("Not found"); return; }
  response.writeHead(200, { "Content-Type": types[extname(found)] ?? "application/octet-stream" });
  response.end(await readFile(found));
}).listen(port, "127.0.0.1", () => console.log(`docs output on http://127.0.0.1:${port}/Ranvier/`));
