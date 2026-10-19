// Runs each compiled build of the suite under both delivery modes and writes docs/.ai/fable-compat.md.
// Exits non-zero when a test fails under inline delivery and has no entry in `differences`, or fails only under
// promise delivery and has no entry in `microtask`.
//
//   node fable/Ranvier.Tests.Fable/Report.mjs
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const root = join(import.meta.dirname, "..", "..");
const suite = join(root, "tests", "Ranvier.Tests");
const output = process.argv[4] ?? join(root, "docs", ".ai", "fable-compat.md");

// The compiled builds: Release, and Release with the trace log compiled in.
const builds = [
    { name: "untraced", entry: process.argv[2] ?? "dist/tests/Main.fs.js" },
    { name: "traced", entry: process.argv[3] ?? "dist/tests-traced/Main.fs.js" },
];
const deliveries = ["inline", "promise"];

// Each inline-delivery failure, by test title, with the difference in the Fable guide that explains it.
const differences = {
    "Equality of value types differs": [
        "a struct tuple still compares by value",
        "an option is compared by reference, so Some 1 over Some 1 propagates",
        "the structural policy cuts off a nan write, unlike the identity one",
        "updateBy returns the input when f returns an equal value-typed element",
        "createOptionMemo under structural policy: an unchanged element costs one Equals, in the memo's cutoff",
        "a hand-written memo over Some wakes on every root write on .NET",
    ],
    "Every `await` suspends": [
        "a continuation run inside another computation's body leaves that computation's edges alone",
        "a cleanup registered in a continuation run inside an effect belongs to the root",
        "a pending read in a continuation run inside an effect does not suspend the effect",
        "an effect run inside a continuation keeps the edges it reads after settling another flight",
        "a memo run inside a continuation keeps the edges it reads after settling another flight",
        "a read after an await on a completed task is tracked",
        "an async memo whose task captures a pending read is pending",
    ],
    "A `Queue` flight applies on a later microtask": [
        "Queue applies every result in the order the flights started",
        "Queue shows an applied outcome while a later flight is in progress",
        "the Queue policy applies a failure in the middle without losing what follows",
        "the Queue policy applies a synchronous failure after the flight started before it",
        "the Queue policy keeps a run suspended on a source pending when an older flight lands",
        "Queue drops a failure that settles while the newest run waits on a source as Suspended",
    ],
    "Trace records carry less": [
        "a node reports the test's file:line",
        "a node created through a combinator reports the user's line",
        "why steps, history runs and snapshot nodes carry recorded values",
        "valueText renders a value as one line",
    ],
};

// Each test, by title, that fails only under promise delivery: it reads the graph before a settle reaches it.
const microtask = new Set([
    "createEffect repeats work on each re-flight of an async value",
    "createEffectOn acts once per settled value of an async value",
    "createEffect has done its work before a failing read",
    "createEffectOn keeps the previous action when compute fails",
    "createEffectOn does not act again when a re-flight settles on the acted value",
    "reading an unsettled flight suspends",
    "dependencies read before the await are tracked",
    "an effect flushed by a write in a continuation keeps its edges",
    "a re-run is pending again, and Peek keeps the last value",
    "a superseded flight does not publish",
    "FinishCurrent folds the changes during a flight into one trailing run",
    "FinishCurrent discards a failure that settles with a run owed",
    "FinishCurrent applies a flight with no change during it as KeepLatest does",
    "FinishCurrent does not hold a run back behind a body suspended on a pending source",
    "FinishCurrent starts the trailing run for an effect that reads the memo",
    "Drop discards the changes during a flight and keeps its value",
    "Drop discards a change no reader pulled during the flight",
    "Drop runs the first change after the flight settles",
    "Drop keeps a memo source current across a change discarded at the settle",
    "Drop keeps a memo source current across a change discarded at a read",
    "Drop starts an async memo source's flight at the settle and runs on its value",
    "DropWhilePending starts an async memo source's flight at the settle and runs on its value",
    "Drop settles a failed flight that discarded a change to its error",
    "Drop does not hold a run back behind a body suspended on a pending source",
    "Drop wakes an effect that read the memo while pending",
    "DropWhilePending keeps a successful flight's value and discards the changes during it",
    "DropWhilePending discards an unpulled change when the flight succeeds",
    "DropWhilePending keeps a memo source current across a change discarded at the settle",
    "DropWhilePending replaces a failed flight that discarded a change with one run",
    "DropWhilePending replaces a failed flight that discarded an unpulled change",
    "DropWhilePending settles a failed flight to its error when a memo source resolves unchanged",
    "DropWhilePending settles a failed flight with no change during it to its error",
    "DropWhilePending settles a failed replacing run to its error",
    "DropWhilePending starts the replacing run for an effect that reads the memo",
    "DropWhilePending does not hold a run back behind a body suspended on a pending source",
    "a failed flight settles as Failed",
    "a body suspended on a pending source waits on the node, not a task",
    "pending propagates into a memo and an effect",
    "a boundary whose fallback returns its last value shows stale data while refreshing",
    "a boundary tells a loaded empty list from a value not yet known",
    "a boundary over an AsyncMemo catches the flight",
    "an async value created and read in a boundary body restarts its flight on every settle",
    "an async value created and read in an effect body restarts its flight on every settle",
    "an async value created and read in an owning memo body restarts its flight on every settle",
    "an async memo's settle marks its readers with its Moved as cause",
    "why through an async memo walks past the settle to the flight's run",
    "FinishCurrent records a deferred run and a settle held for the trailing run",
    "FinishCurrent drops a failure that settles with a run owed as Trailing",
    "FinishCurrent's trailing run is notified with the dropped failure as its cause",
    "Drop records a run dropped at a read and a change dropped at the settle",
    "Drop names the next change as the cause of the run after a change dropped at a read",
    "Drop names the next change as the cause of the run after a change dropped at the settle",
    "DropWhilePending drops a failure that settles after a discarded change as Retried",
    "waitingOn reports superseded, settled and failed flights",
    "a flight that is already complete never makes the reader pending",
    "an effect over an already-complete flight runs once per write",
    "a memo over an already-complete flight recomputes once per write",
    "an already-faulted flight is Failed, not pending",
    "three supersessions deep, only the last one publishes",
    "a flight that settles from its own cancellation is discarded as superseded",
    "a flight that faults from its own cancellation does not mark the node failed",
    "a current flight that cancels itself settles as Failed",
    "a flight cancelled after its first await keeps the cancellation's exception",
    "a re-run after a failure clears the Error flag",
    "a re-run that suspends on a pending source clears the Error flag",
    "disposing a settled async memo keeps its value",
    "a boundary over a superseded flight shows the fallback throughout",
    "Runs counts body runs, including runs that start no flight",
    "Peek on a flight in progress is the previous value, not the default",
    "an async memo's cleanup runs before the next flight starts and at its disposal",
    "an effect created after an await under runWithOwner is disposed with the flight",
    "N2e an async memo cleanup that writes a source and reads the async memo starts one flight per change",
    "S1 a per-row fetch settles, and settling one row leaves the others' nodes alone",
    "a faulted flight originates at the async memo",
]);

const explained = new Map(Object.entries(differences).flatMap(([why, titles]) => titles.map(t => [t, why])));

function run(build, mode) {
    const file = join(mkdtempSync(join(tmpdir(), "ranvier-fable-")), `${build.name}-${mode}.json`);
    const mocha = spawnSync(
        process.execPath,
        [join(root, "node_modules", "mocha", "bin", "mocha.js"), build.entry, "--timeout", "10000",
         "--reporter", "fable/Ranvier.Tests.Fable/Reporter.cjs", "--reporter-option", `output=${file}`],
        { cwd: root, env: { ...process.env, RANVIER_FABLE_DELIVERY: mode }, stdio: "inherit" });
    if (mocha.error) throw mocha.error;
    return new Map(JSON.parse(readFileSync(file, "utf8")).map(r => [r.path.join(" / "), r]));
}

// The files of the suite in compile order.
const sources = new Map(
    [...readFileSync(join(suite, "TestFiles.props"), "utf8")
        .matchAll(/<Compile Include="\$\(MSBuildThisFileDirectory\)([^"]+)" Link="([^"]+)"/g)]
        .map(m => [m[2], resolve(suite, m[1])]));
const files = [...sources.keys()];

// Tests compiled out under Fable: each `#if !FABLE_COMPILER` block that opens with a `// .NET only: <reason>` line.
// A block holding no test and sitting inside one excludes part of that test; a block of helpers is skipped.
const declaration = /^\s*(?:<\| )?(?:test|testCase|testCaseAsync)\s+\$?"([^"]+)"/;

function exclusions(file) {
    const lines = readFileSync(sources.get(file), "utf8").split(/\r?\n/);
    const found = [];
    let enclosing = null;
    for (let i = 0; i < lines.length; i++) {
        const test = lines[i].match(declaration);
        if (test) enclosing = test[1];
        if (!/^#if !FABLE_COMPILER/.test(lines[i])) continue;
        let depth = 1, end = i + 1;
        for (; end < lines.length && depth > 0; end++) {
            if (/^#if/.test(lines[end])) depth++;
            else if (/^#endif/.test(lines[end])) depth--;
        }
        const block = lines.slice(i + 1, end - 1);
        const marker = block[0]?.match(/\/\/ \.NET only: (.*)$/);
        if (!marker) continue;
        const tests = block.map(l => l.match(declaration)).filter(Boolean).map(m => m[1]);
        const code = block.slice(1).find(l => l.trim() !== "" && !l.trim().startsWith("//")) ?? "";
        if (tests.length > 0) tests.forEach(name => found.push({ name, reason: marker[1], kind: "test" }));
        else if (!/^(let|type|open|\[<)/.test(code)) found.push({ name: enclosing ?? "(file)", reason: marker[1], kind: "part" });
        i = end - 1;
    }
    return found;
}

// results[build][mode]: test key to its record.
const results = Object.fromEntries(builds.map(b => [b.name, Object.fromEntries(deliveries.map(m => [m, run(b, m)]))]));
const runs = builds.flatMap(b => deliveries.map(m => results[b.name][m]));
const byFile = new Map(files.map(f => [f, []]));
for (const key of new Set(runs.flatMap(r => [...r.keys()]))) {
    const file = key.split(" / ")[0];
    if (!byFile.has(file)) byFile.set(file, []);
    byFile.get(file).push(key);
}

const escape = s => s.replaceAll("|", "\\|");
const stateOf = (build, mode, key) => results[build][mode].get(key)?.state ?? "not run";
const ran = (build, key) => deliveries.some(m => results[build][m].has(key));
const nameOf = key => key.split(" / ").slice(2).join(" / ");
const untriaged = [];
const summary = [];
const sections = [];
const totals = Object.fromEntries(builds.map(b => [b.name, { run: 0, inline: 0, promise: 0 }]));
let excludedTotal = 0, partialTotal = 0;

for (const [file, keys] of byFile) {
    const excluded = files.includes(file) ? exclusions(file) : [];
    const whole = excluded.filter(e => e.kind === "test");
    const parts = excluded.filter(e => e.kind === "part");
    if (keys.length === 0 && excluded.length === 0) continue;
    excludedTotal += whole.length;
    partialTotal += parts.length;

    const cells = [];
    const failures = [];
    for (const { name: build } of builds) {
        const inBuild = keys.filter(k => ran(build, k));
        const passes = mode => inBuild.filter(k => stateOf(build, mode, k) === "passed").length;
        totals[build].run += inBuild.length;
        for (const m of deliveries) totals[build][m] += passes(m);
        cells.push(inBuild.length, passes("inline"), passes("promise"));
        for (const key of inBuild) {
            const inline = stateOf(build, "inline", key), promise = stateOf(build, "promise", key);
            if (inline === "passed" && promise === "passed") continue;
            const title = key.split(" / ").at(-1);
            let why = inline === "passed"
                ? (microtask.has(title) ? "Async results arrive on a later microtask" : undefined)
                : explained.get(title);
            if (!why) {
                why = "**untriaged**";
                const mode = inline === "passed" ? "promise" : "inline";
                untriaged.push(`${build} ${mode}: ${key}: ${results[build][mode].get(key)?.message ?? stateOf(build, mode, key)}`);
            }
            failures.push(`| ${build} | ${escape(nameOf(key))} | ${inline} | ${promise} | ${why} |`);
        }
    }
    summary.push(`| ${file} | ${cells.join(" | ")} | ${whole.length} | ${parts.length} |`);

    const out = [`## ${file}`, ""];
    if (failures.length > 0) out.push("| Build | Failed | Inline | Promise | Difference |", "| --- | --- | --- | --- | --- |", ...failures, "");
    if (excluded.length > 0) {
        out.push("| Excluded | Reason |", "| --- | --- |");
        for (const e of excluded)
            out.push(`| ${escape(e.kind === "part" ? `part of: ${e.name}` : e.name)} | ${escape(e.reason)} |`);
        out.push("");
    }
    const passed = keys.filter(k =>
        builds.every(({ name: b }) => !ran(b, k) || deliveries.every(m => stateOf(b, m, k) === "passed")));
    if (passed.length > 0) {
        out.push(`<details><summary>${passed.length} passed in every build and delivery that runs them</summary>`, "");
        for (const key of passed) {
            const only = builds.filter(b => ran(b.name, key));
            out.push(`- ${nameOf(key)}${only.length < builds.length ? ` (${only.map(b => b.name).join(", ")} only)` : ""}`);
        }
        out.push("", "</details>", "");
    }
    sections.push(out.join("\n"));
}

const header = builds.flatMap(b => [`${b.name}: run`, "inline", "promise"]);
const totalCells = builds.flatMap(b => [totals[b.name].run, totals[b.name].inline, totals[b.name].promise]);

writeFileSync(output, [
    "# Fable compatibility",
    "",
    "Generated by `dotnet fsi build.fsx test-fable` (`fable/Ranvier.Tests.Fable/Report.mjs`); do not edit by hand.",
    "",
    "The .NET suite in `tests/Ranvier.Tests`, compiled with Fable and run under Node.js with Mocha. Two builds:",
    "",
    "- **untraced**: the Release build.",
    "- **traced**: the Release build with the trace log (`RanvierTrace=true`) compiled in. Tests of the log run only here.",
    "",
    "Each build runs in two deliveries:",
    "",
    "- **inline**: a test's `TaskCompletionSource` settles its awaiters before `SetResult` returns, as on .NET.",
    "  A failure here is a difference in behaviour.",
    "- **promise**: a test's `TaskCompletionSource` is a promise, as in an application. A test that fails only here",
    "  observes microtask delivery.",
    "",
    "Each difference named below is described in [the Fable guide](../content/fable/index.md).",
    "Excluded tests exercise a .NET-only facility and are compiled out with `#if !FABLE_COMPILER`.",
    "",
    `| File | ${header.join(" | ")} | Excluded | Partly excluded |`,
    `| --- | ${header.map(() => "---").join(" | ")} | --- | --- |`,
    ...summary,
    `| **Total** | ${totalCells.join(" | ")} | ${excludedTotal} | ${partialTotal} |`,
    "",
    ...sections,
].join("\n"));

for (const { name } of builds) {
    const t = totals[name];
    console.log(`${name}: inline ${t.inline}/${t.run}, promise ${t.promise}/${t.run}`);
}
console.log(`excluded ${excludedTotal}; wrote ${output}`);
if (untriaged.length > 0) {
    console.error(`${untriaged.length} untriaged failure(s):\n  ${untriaged.join("\n  ")}`);
    process.exit(1);
}
