import { hydrate } from "@solidjs/web";
import { flush } from "solid-js";
import { componentPage, keyedPage } from "./server-solid.jsx";

// Solid 2 hydrates once per document: a macrotask after a hydration ends, it sets `_$HY.done`, later hydrate() calls
// render fresh over the server nodes, and the first hydration's event replay sets `completed` and `events` to null.
// Each call here stands for a page's first hydration, so it restores the bootstrap's initial `done`, `completed` and
// `events`.
const firstHydration = (page, host, rows) => {
  globalThis._$HY.done = false;
  globalThis._$HY.completed = new WeakSet();
  globalThis._$HY.events = [];
  return hydrate(() => page(rows), host);
};

// Each hydrates the host's server HTML of the page at `rows` rows and returns the disposal.
export const hydrateComponentPage = (host, rows) => firstHydration(componentPage, host, rows);
export const hydrateKeyedPage = (host, rows) => firstHydration(keyedPage, host, rows);

// Commits the updates queued by a handled event.
export const flushSolid = () => flush();
