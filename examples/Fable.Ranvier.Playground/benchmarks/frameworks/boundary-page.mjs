import { mountReveal as ranvierReveal, mountSwitchReveal as ranvierSwitch, mountSwitchPlain as ranvierPlain } from "../../generated-tests/BoundaryPerf.js";
import { mountReveal as solidReveal, mountSwitchReveal as solidSwitch, mountSwitchPlain as solidPlain } from "./boundary-solid.jsx";

// "reveal" mounts the boundary at the root; "switch" mounts it from a branch switch, inside a flush; "plain" is the switch
// workload without a boundary.
const scenarios = { reveal: { ranvier: ranvierReveal, solid: solidReveal }, switch: { ranvier: ranvierSwitch, solid: solidSwitch }, plain: { ranvier: ranvierPlain, solid: solidPlain } };
let hostIndex = 0;
const freshHost = () => {
  const host = document.createElement("div");
  host.id = `boundary-${hostIndex++}`;
  document.body.append(host);
  return host;
};

window.boundaryReveal = {
  // Mounts `count` boundaries of `width` rows on fresh hosts, each revealed; returns milliseconds per boundary.
  async sample(mode, width, count, scenario = "reveal") {
    const mount = scenarios[scenario][mode];
    const hosts = Array.from({ length: count }, freshHost), disposals = [];
    const start = performance.now();
    for (const host of hosts) disposals.push(await mount(host, width));
    const ms = (performance.now() - start) / count;
    for (const dispose of disposals) dispose();
    for (const host of hosts) host.remove();
    return ms;
  },
  // Mounts one boundary and checks its rows, that the pending view was inserted before the reveal and is gone after it,
  // and that disposal empties the host.
  async verify(mode, width, scenario = "reveal") {
    const mount = scenarios[scenario][mode];
    const host = freshHost();
    const records = [];
    const observer = new MutationObserver(batch => records.push(...batch));
    observer.observe(host, { childList: true, subtree: true });
    const dispose = await mount(host, width);
    records.push(...observer.takeRecords());
    observer.disconnect();
    const pendingMounted = records.some(record => Array.from(record.addedNodes).some(node => node.textContent === "loading"));
    const rows = Array.from(host.querySelectorAll(".row"), row => row.textContent);
    const fallbackAbsent = !host.textContent.includes("loading");
    dispose();
    const emptied = host.childNodes.length === 0;
    host.remove();
    const expected = Array.from({ length: width }, (_, i) => `ready${i}`);
    if (JSON.stringify(rows) !== JSON.stringify(expected)) throw Error(`${scenario}/${mode}/${width}: ${JSON.stringify(rows)}`);
    if (!fallbackAbsent) throw Error(`${scenario}/${mode}/${width}: the pending view remains after the reveal`);
    if (!emptied) throw Error(`${scenario}/${mode}/${width}: the host is not empty after disposal`);
    if (mode === "ranvier" && scenario !== "plain" && !pendingMounted) throw Error(`${scenario}/${mode}/${width}: the pending view was never inserted`);
    return { scenario, mode, width, rows: rows.length, pendingMounted };
  },
  async retain(mode, width, count, scenario = "reveal") {
    const mount = scenarios[scenario][mode];
    const hosts = Array.from({ length: count }, freshHost), disposals = [];
    for (const host of hosts) disposals.push(await mount(host, width));
    return () => { for (const dispose of disposals) dispose(); for (const host of hosts) host.remove(); };
  }
};
