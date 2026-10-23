import test from "node:test";
import assert from "node:assert/strict";
import { createRebuilder, developmentPages } from "../page-build.mjs";
import { createServer } from "vite";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync, watch, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

test("development waits for compilation and coalesces rapid edits before reloading", async () => {
  const entered = deferred();
  const release = deferred();
  const events = [];
  const queue = createRebuilder(async () => {
    events.push("compile");
    if (events.length === 1) { entered.resolve(); await release.promise; }
  }, () => events.push("reload"));
  queue.request();
  await entered.promise;
  queue.request();
  queue.request();
  let served = false;
  const request = queue.ready().then(() => { served = true; });
  await Promise.resolve();
  assert.equal(served, false);
  release.resolve();
  await request;
  assert.deepEqual(events, ["compile", "compile", "reload"]);
});

test("development refuses stale pages after failure and recovers on the next edit", async () => {
  let fail = true;
  let reloads = 0;
  const queue = createRebuilder(async () => { if (fail) throw new Error("render failed"); }, () => reloads++);
  queue.request();
  await assert.rejects(queue.ready(), /render failed/);
  await assert.rejects(queue.ready(), /render failed/);
  assert.equal(reloads, 0);
  fail = false;
  queue.request();
  await queue.ready();
  assert.equal(reloads, 1);
});

test("closing cancels an active build and discards queued edits without reloading", async () => {
  const entered = deferred();
  const release = deferred();
  const events = [];
  const queue = createRebuilder(async () => { events.push("compile"); entered.resolve(); await release.promise; },
    () => events.push("reload"), async () => { events.push("cancel"); release.resolve(); });
  queue.request();
  await entered.promise;
  queue.request();
  await queue.close();
  queue.request();
  await assert.rejects(queue.ready(), /closed/);
  assert.deepEqual(events, ["compile", "cancel"]);
});

function fileCreated(path, signal) {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const watcher = watch(join(path, ".."), { signal }, () => {
      if (existsSync(path)) { watcher.close(); resolve(); }
    });
    signal.addEventListener("abort", () => reject(signal.reason), { once: true });
    if (existsSync(path)) { watcher.close(); resolve(); }
  });
}

function assertTerminated(pid) {
  try {
    process.kill(pid, 0);
  } catch (error) {
    if (error.code === "ESRCH") return;
    throw error;
  }
  if (process.platform === "linux") {
    let stat;
    try { stat = readFileSync(`/proc/${pid}/stat`, "utf8"); }
    catch (error) { if (error.code === "ENOENT") return; throw error; }
    assert.match(stat.slice(stat.lastIndexOf(")") + 2), /^[ZX] /, "the compiler must be exited or awaiting reaping");
  } else {
    assert.fail("the compiler process is still running");
  }
}

async function waitForTermination(pid) {
  const deadline = performance.now() + 2000;
  while (true) {
    try { assertTerminated(pid); return; }
    catch (error) {
      if (!(error instanceof assert.AssertionError) || performance.now() >= deadline) throw error;
      await delay(10);
    }
  }
}

test("the termination check rejects a live process", () => {
  assert.throws(() => assertTerminated(process.pid), assert.AssertionError);
});

test("the dev server gates HTML on rebuilds, reports failure and serves fresh output after recovery", { timeout: 30000 }, async t => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const directory = mkdtempSync(join(root, ".dev-test-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  for (const name of ["src", "generated-pages", "generated"]) mkdirSync(join(directory, name));
  writeFileSync(join(directory, "generated-pages/pages.json"), JSON.stringify({ pages: [{ name: "index", path: "index.html" }] }));
  writeFileSync(join(directory, "index.html"), "<html><body>Initial</body></html>");
  writeFileSync(join(directory, "src/Page.fs"), "initial");
  writeFileSync(join(directory, "generated/client.js"), 'export const value = "initial";');
  writeFileSync(join(directory, "package.json"), JSON.stringify({ type: "module", scripts: { compile: "node rebuild.mjs" } }));
  writeFileSync(join(directory, "rebuild.mjs"), `
    import { watch, readFileSync, writeFileSync } from "node:fs";
    const value = readFileSync("src/Page.fs", "utf8");
    if (value === "failure") { console.error("fixture generation failed"); process.exit(23); }
    const deadline = setTimeout(() => { writeFileSync("watchdog-expired", "compile was not terminated"); process.exit(124); }, 10000);
    const watcher = watch(".", (_, name) => {
      if (name !== "release") return;
      clearTimeout(deadline);
      watcher.close();
      writeFileSync("index.html", "<html><body>" + value + "</body></html>");
      writeFileSync("generated/client.js", 'export const value = "' + value + '";');
    });
    writeFileSync("child-pid", String(process.pid));
    writeFileSync("started", value);
  `);
  const server = await createServer({ configFile: false, root: directory, logLevel: "silent", plugins: [developmentPages(directory)], server: { host: "127.0.0.1", port: 0 } });
  t.after(() => server.close());
  await server.listen();
  const address = `http://127.0.0.1:${server.httpServer.address().port}`;
  assert.match(await (await fetch(address)).text(), /Initial/);
  assert.match(await (await fetch(address + "/generated/client.js")).text(), /initial/);
  const page = join(directory, "src/Page.fs");
  const rebuild = async value => {
    rmSync(join(directory, "started"), { force: true });
    rmSync(join(directory, "release"), { force: true });
    const changed = new Promise((resolve, reject) => {
      const listener = file => { if (file.replaceAll("\\", "/") === page.replaceAll("\\", "/")) { server.watcher.off("change", listener); resolve(); } };
      server.watcher.on("change", listener);
      t.signal.addEventListener("abort", () => { server.watcher.off("change", listener); reject(t.signal.reason); }, { once: true });
    });
    writeFileSync(page, value);
    await changed;
    if (value !== "failure") await fileCreated(join(directory, "started"), t.signal);
  };
  await rebuild("Fresh page");
  const pending = fetch(address);
  const pendingModule = fetch(address + "/generated/client.js");
  writeFileSync(join(directory, "release"), "go");
  assert.match(await (await pending).text(), /Fresh page/);
  assert.match(await (await pendingModule).text(), /Fresh page/);
  assert.match(await (await fetch(address + "/generated/client.js")).text(), /Fresh page/);
  await rebuild("failure");
  const failed = await fetch(address);
  assert.equal(failed.status, 500);
  assert.match(await failed.text(), /Motion page generation failed/);
  assert.equal((await fetch(address)).status, 500);
  assert.equal((await fetch(address + "/generated/client.js")).status, 500);
  await rebuild("Recovered page");
  writeFileSync(join(directory, "release"), "go");
  const recovered = await fetch(address);
  assert.equal(recovered.status, 200);
  assert.match(await recovered.text(), /Recovered page/);
  await rebuild("Abandoned page");
  const childPid = Number(readFileSync(join(directory, "child-pid"), "utf8"));
  server.watcher.emit("change", page);
  await server.close();
  assert.equal(existsSync(join(directory, "watchdog-expired")), false, "server.close must terminate its compiler before the fixture's safety deadline");
  await waitForTermination(childPid);
  assert.match(readFileSync(join(directory, "index.html"), "utf8"), /Recovered page/);
});
