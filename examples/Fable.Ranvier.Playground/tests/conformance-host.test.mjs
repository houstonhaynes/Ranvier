import test from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { trackShared, interceptIn, makeClock } from "./conformance-host.mjs";

test("a listener or expando left on the shared document is reported and removed", () => {
  const dom = new JSDOM("<!doctype html><body></body>");
  const { begin, leftovers } = trackShared(dom.window);
  begin();
  const document = dom.window.document;
  const removed = () => {};
  document.body.addEventListener("click", removed);
  document.body.removeEventListener("click", removed);
  let calls = 0;
  dom.window.addEventListener("keydown", () => calls++);
  document.body.$$ranvierHandler = {};
  assert.deepEqual(leftovers(), ["window 'keydown' listener", "body expando $$ranvierHandler"]);
  dom.window.dispatchEvent(new dom.window.Event("keydown"));
  assert.equal(calls, 0);
  assert.equal(document.body.$$ranvierHandler, undefined);
  assert.deepEqual(leftovers(), []);
  dom.window.close();
});

test("listeners on other elements are not shared state", () => {
  const dom = new JSDOM("<!doctype html><body></body>");
  const { begin, leftovers } = trackShared(dom.window);
  begin();
  const main = dom.window.document.createElement("main");
  dom.window.document.body.append(main);
  main.addEventListener("click", () => {});
  assert.deepEqual(leftovers(), []);
  dom.window.close();
});

test("an interception of every node wraps the prototypes until it is restored", () => {
  const dom = new JSDOM("<!doctype html><body></body>");
  const document = dom.window.document;
  let count = 0;
  const restore = interceptIn(dom.window)(null, "remove", (original, args) => { count++; return original(args); });
  document.createElement("i").remove();
  document.createTextNode("t").remove();
  restore();
  document.createElement("b").remove();
  assert.equal(count, 2);
  dom.window.close();
});

test("the clock drains microtasks after each timer and runs frames at 16 ms boundaries", () => {
  const target = {};
  const clock = makeClock([target]);
  const log = [];
  target.setTimeout(() => { log.push("t20"); target.queueMicrotask(() => log.push("m")); }, 20);
  target.requestAnimationFrame(now => log.push(`f${now}`));
  clock.advanceBy(40);
  assert.deepEqual(log, ["f16", "t20", "m"]);
  clock.restore();
  assert.equal(target.setTimeout, undefined);
});

test("a listener added to the shared document outside a case, as jsdom adds its load listener, is no case's leftover", () => {
  const dom = new JSDOM("<!doctype html><body></body>");
  const { begin, leftovers } = trackShared(dom.window);
  const document = dom.window.document;
  document.addEventListener("load", () => {});
  begin();
  assert.deepEqual(leftovers(), []);
  document.addEventListener("load", () => {});
  begin();
  document.body.addEventListener("click", () => {});
  assert.deepEqual(leftovers(), ["body 'click' listener"]);
  dom.window.close();
});
