// The JavaScript side of the conformance harness (JsdomHarness.HostSide): a deterministic clock with the DOM twin's
// semantics, and method interception. The clock replaces the window's and globalThis's queueMicrotask, setTimeout,
// clearTimeout, requestAnimationFrame and cancelAnimationFrame, so the view's queued work and Post share one queue
// that only runMicrotasks drains, and timers and frames (every 16 ms) run only in advanceBy, draining the queue after
// each callback.

const frameMs = 16;
const microtaskLimit = 100_000;
const timerNames = ["queueMicrotask", "setTimeout", "clearTimeout", "requestAnimationFrame", "cancelAnimationFrame"];

export function makeClock(targets, onAdvance = () => {}) {
  let now = 0, sequence = 0;
  const queue = [], timers = [], frames = [];

  const drain = () => {
    let count = 0;
    while (queue.length > 0) {
      if (++count > microtaskLimit) throw new Error("The harness's microtasks kept refilling");
      queue.shift()();
    }
  };

  const functions = {
    queueMicrotask: callback => { queue.push(callback); },
    setTimeout: (callback, milliseconds = 0, ...args) => {
      const id = ++sequence;
      timers.push({ id, due: now + Math.max(0, Number(milliseconds) || 0), sequence: id, run: () => callback(...args) });
      return id;
    },
    clearTimeout: id => { const index = timers.findIndex(t => t.id === id); if (index >= 0) timers.splice(index, 1); },
    requestAnimationFrame: callback => { const id = ++sequence; frames.push({ id, callback }); return id; },
    cancelAnimationFrame: id => { const index = frames.findIndex(f => f.id === id); if (index >= 0) frames.splice(index, 1); },
  };

  const saved = targets.map(target => [target, timerNames.map(name => [name, Object.getOwnPropertyDescriptor(target, name)])]);
  for (const target of targets) for (const name of timerNames) target[name] = functions[name];

  const runTimer = timer => {
    timers.splice(timers.indexOf(timer), 1);
    now = Math.max(now, timer.due);
    timer.run();
    drain();
  };

  const frame = () => {
    const due = frames.splice(0, frames.length);
    for (const { callback } of due) { callback(now); drain(); }
  };

  return {
    post: callback => { queue.push(callback); },
    runMicrotasks: drain,
    advanceBy: milliseconds => {
      if (milliseconds < 0) throw new RangeError("The harness clock does not move backwards");
      const target = now + milliseconds;
      for (;;) {
        const timer = timers.filter(t => t.due <= target).sort((a, b) => a.due - b.due || a.sequence - b.sequence)[0];
        const boundary = frames.length === 0 ? undefined : (Math.floor(now / frameMs) + 1) * frameMs;
        const nextFrame = boundary !== undefined && boundary <= target ? boundary : undefined;
        if (timer && nextFrame !== undefined && nextFrame < timer.due) { onAdvance(nextFrame - now); now = nextFrame; frame(); }
        else if (timer) { onAdvance(Math.max(now, timer.due) - now); runTimer(timer); }
        else if (nextFrame !== undefined) { onAdvance(nextFrame - now); now = nextFrame; frame(); }
        else break;
      }
      onAdvance(target - now);
      now = target;
    },
    restore: () => {
      for (const [target, names] of saved)
        for (const [name, descriptor] of names) { if (descriptor) Object.defineProperty(target, name, descriptor); else delete target[name]; }
    },
  };
}

// Routes calls of the method `name` on `target`, or on every node of the window's document when target is null,
// through `wrap(original, args)` until the returned function runs.
export function interceptIn(window) {
  return (target, name, wrap) => {
    const owners = target == null
      ? [window.EventTarget, window.Node, window.CharacterData, window.Element, window.HTMLElement, window.Document, window.DocumentFragment, window.Text, window.Comment]
          .map(type => type.prototype)
          .filter(prototype => Object.prototype.hasOwnProperty.call(prototype, name))
      : [target];
    const saved = owners.map(owner => [owner, Object.getOwnPropertyDescriptor(owner, name)]);
    for (const owner of owners) {
      const original = owner[name];
      owner[name] = function (...args) { return wrap(callArgs => original.apply(this, callArgs), args); };
    }
    return () => {
      for (const [owner, descriptor] of saved.reverse()) { if (descriptor) Object.defineProperty(owner, name, descriptor); else delete owner[name]; }
    };
  };
}

// Runs one conformance case on a browser or benchmark page: the clock replaces the page window's timer functions for
// the case only. The page records no artefacts; listener failures are the window's error events, marked handled.
export function runOnPage(window, create, run) {
  const clock = makeClock([window]);
  const errors = [];
  const onError = e => { errors.push(e.error ? e.error.message : String(e.message)); e.preventDefault(); };
  window.addEventListener("error", onError);
  try {
    const host = { post: clock.post, runMicrotasks: clock.runMicrotasks, advanceBy: clock.advanceBy, record: () => {}, errors: () => errors.splice(0), intercept: interceptIn(window) };
    return run(create(window.document, host));
  } finally {
    window.removeEventListener("error", onError);
    clock.restore();
  }
}

// Records the listeners a case adds to the window, its document and the document's html, head and body: those added
// between begin () and leftovers (). leftovers () describes and removes those still live and the view's $$ranvier
// expandos on the same five, as a case must leave none: the twin starts each case on a fresh document. Listeners added
// outside a case are jsdom's own, such as the document's load listener jsdom adds on a later tick and its selector
// engine's window listeners, and stay.
export function trackShared(window) {
  const document = window.document;
  document.querySelectorAll("*");
  document.body.matches(":hover");
  const shared = () => [["window", window], ["document", document], ["html", document.documentElement], ["head", document.head], ["body", document.body]];
  const live = [];
  let recording = false;
  const prototype = window.EventTarget.prototype;
  const add = prototype.addEventListener, remove = prototype.removeEventListener;
  const captureOf = options => typeof options === "boolean" ? options : Boolean(options?.capture);
  prototype.addEventListener = function (type, listener, options) {
    if (recording && shared().some(([, target]) => target === this)) live.push({ target: this, type, listener, capture: captureOf(options) });
    return add.call(this, type, listener, options);
  };
  prototype.removeEventListener = function (type, listener, options) {
    const index = live.findIndex(l => l.target === this && l.type === type && l.listener === listener && l.capture === captureOf(options));
    if (index >= 0) live.splice(index, 1);
    return remove.call(this, type, listener, options);
  };
  return {
    begin: () => { recording = true; },
    leftovers: () => {
      recording = false;
      const left = [];
      for (const l of live.splice(0)) {
        left.push(`${shared().find(([, target]) => target === l.target)[0]} '${l.type}' listener`);
        remove.call(l.target, l.type, l.listener, l.capture);
      }
      for (const [label, target] of shared())
        for (const key of Object.getOwnPropertyNames(target).filter(key => key.startsWith("$$ranvier"))) {
          left.push(`${label} expando ${key}`);
          delete target[key];
        }
      return left;
    },
  };
}
