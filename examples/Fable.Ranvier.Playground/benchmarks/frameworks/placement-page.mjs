import { mountHooked as ranvierHooked, mountPlain as ranvierPlain } from "../../generated-tests/PlacementPerf.js";
import { mountHooked as solidHooked, mountPlain as solidPlain } from "./placement-solid.jsx";

// "hooked" rows each run one hook once in the document; "plain" rows run none.
const scenarios = { hooked: { ranvier: ranvierHooked, solid: solidHooked }, plain: { ranvier: ranvierPlain, solid: solidPlain } };
let hostIndex = 0;
const freshHost = () => {
  const host = document.createElement("div");
  host.id = `placement-${hostIndex++}`;
  document.body.append(host);
  return host;
};
// Waits until every hooked row is marked; a framework that runs hooks after a microtask is measured to that point.
const settle = async (host, width, scenario) => {
  for (let turn = 0; turn < 20 && scenario === "hooked" && host.querySelectorAll("[data-placed]").length !== width; turn++)
    await Promise.resolve();
};

window.placementHooks = {
  // Mounts `count` lists of `width` rows on fresh hosts; returns milliseconds per list.
  async sample(mode, width, count, scenario) {
    const mount = scenarios[scenario][mode];
    const hosts = Array.from({ length: count }, freshHost), disposals = [];
    const start = performance.now();
    for (const host of hosts) { disposals.push(mount(host, width)); await settle(host, width, scenario); }
    const ms = (performance.now() - start) / count;
    for (const dispose of disposals) dispose();
    for (const host of hosts) host.remove();
    return ms;
  },
  // Mounts one list and checks its rows, the marked rows and that disposal empties the host.
  async verify(mode, width, scenario) {
    const mount = scenarios[scenario][mode];
    const host = freshHost();
    const dispose = mount(host, width);
    await settle(host, width, scenario);
    const rows = host.querySelectorAll(".row").length, placed = host.querySelectorAll("[data-placed]").length;
    dispose();
    const emptied = host.childNodes.length === 0;
    host.remove();
    if (rows !== width) throw Error(`${scenario}/${mode}/${width}: ${rows} rows`);
    if (placed !== (scenario === "hooked" ? width : 0)) throw Error(`${scenario}/${mode}/${width}: ${placed} placed rows`);
    if (!emptied) throw Error(`${scenario}/${mode}/${width}: the host is not empty after disposal`);
    return { scenario, mode, width, rows, placed };
  }
};
