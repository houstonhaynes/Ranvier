import { existsSync, readFileSync } from "node:fs";
import { isAbsolute, resolve, sep } from "node:path";
import { spawn, execFile } from "node:child_process";

export function readPages(root) {
  const manifest = resolve(root, "generated-pages/pages.json");
  if (!existsSync(manifest)) throw new Error("Missing generated-pages/pages.json: run npm run compile");
  const { pages } = JSON.parse(readFileSync(manifest, "utf8"));
  if (!Array.isArray(pages) || pages.length === 0) throw new Error("The generated page manifest must contain pages");
  const names = new Set();
  const paths = new Set();
  return pages.map(page => {
    if (typeof page.name !== "string" || !/^[A-Za-z][A-Za-z0-9_-]*$/.test(page.name) || names.has(page.name))
      throw new Error("Invalid or duplicate generated page name");
    names.add(page.name);
    const path = page.path;
    if (typeof path !== "string" || isAbsolute(path) || path.includes("\\") || path.split("/").includes("..") || !path.endsWith(".html") || paths.has(path))
      throw new Error("Invalid or duplicate generated page path");
    paths.add(path);
    const filename = resolve(root, path);
    if (!filename.startsWith(resolve(root) + sep) || !existsSync(filename))
      throw new Error(`Missing generated document ${path}: run npm run compile`);
    return [page.name, filename];
  });
}

export function createRebuilder(rebuild, reload, cancel = async () => {}) {
  let running;
  let dirty = false;
  let failure;
  let closed = false;
  return {
    request() {
      if (closed) return;
      dirty = true;
      if (!running) running = Promise.resolve().then(async () => {
        while (dirty && !closed) {
          dirty = false;
          try { await rebuild(); failure = undefined; }
          catch (error) { failure = error; }
        }
        if (!failure && !closed) reload();
      }).finally(() => { running = undefined; });
    },
    async ready() {
      while (running) await running;
      if (closed) throw new Error("The development page builder is closed");
      if (failure) throw failure;
    },
    async close() {
      closed = true;
      dirty = false;
      await cancel();
      await running;
    }
  };
}

function compilePages(root) {
  const child = spawn("npm run compile", {
    cwd: root,
    shell: true,
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
    detached: process.platform !== "win32"
  });
  let stdout = "";
  let stderr = "";
  child.stdout.on("data", data => { stdout = (stdout + data).slice(-131072); });
  child.stderr.on("data", data => { stderr = (stderr + data).slice(-131072); });
  const promise = new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", (code, signal) => {
      if (code !== 0) {
        const error = new Error(`npm run compile failed (${signal || code}):\n${stderr || stdout}`);
        error.stderr = stderr;
        reject(error);
      }
      else resolve(stdout);
    });
  });
  return {
    promise,
    async cancel() {
      if (child.exitCode !== null || !child.pid) return;
      if (process.platform === "win32") {
        await new Promise(resolve => {
          execFile("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true }, () => resolve());
        });
      } else {
        try { process.kill(-child.pid, "SIGKILL"); }
        catch (error) { if (error.code !== "ESRCH") throw error; }
      }
      await promise.catch(() => {});
    }
  };
}

export function developmentPages(root) {
  let shutdown = async () => {};
  const sourceRoots = [resolve(root, "src"), resolve(root, "Server"), resolve(root, "../../src")];
  const projects = [resolve(root, "Motion.fsproj"), resolve(root, "Client.fsproj")];
  const source = file => {
    const path = resolve(file);
    return /\.(fs|fsproj|props|targets)$/.test(path)
      && !/[\\/](bin|obj|generated|generated-pages|generated-tests|node_modules)[\\/]/.test(path)
      && (projects.includes(path) || sourceRoots.some(directory => path.startsWith(directory + sep)));
  };
  const generated = file => /[\\/](generated|generated-pages)[\\/]/.test(file) || file.endsWith(".html");
  return {
    name: "ranvier-generated-pages",
    apply: "serve",
    config() {
      return { server: { watch: { ignored: ["**/generated/**", "**/generated-pages/**", "**/*.html"] } } };
    },
    configureServer(server) {
      let compilation;
      const queue = createRebuilder(async () => {
        try {
          compilation = compilePages(root);
          await compilation.promise;
          readPages(root);
        } catch (error) {
          server.config.logger.error(error.stderr || error.message);
          throw error;
        } finally {
          compilation = undefined;
        }
      }, () => { server.moduleGraph.invalidateAll(); server.ws.send({ type: "full-reload" }); }, () => compilation?.cancel());
      server.watcher.add([...sourceRoots, ...projects]);
      const onChange = file => { if (source(file)) queue.request(); };
      server.watcher.on("add", onChange).on("change", onChange).on("unlink", onChange);
      shutdown = async () => {
        server.watcher.off("add", onChange).off("change", onChange).off("unlink", onChange);
        await queue.close();
      };
      server.middlewares.use(async (request, response, next) => {
        const path = new URL(request.url, "http://localhost").pathname;
        if (!path.includes("/generated/") && !path.endsWith("/") && !path.endsWith(".html") && !request.headers.accept?.includes("text/html")) return next();
        try { await queue.ready(); readPages(root); next(); }
        catch (error) { response.statusCode = 500; response.setHeader("Content-Type", "text/plain; charset=utf-8"); response.end(`Motion page generation failed:\n${error.message}`); }
      });
    },
    closeBundle() { return shutdown(); },
    handleHotUpdate(context) {
      if (source(context.file) || generated(context.file)) return [];
    }
  };
}
