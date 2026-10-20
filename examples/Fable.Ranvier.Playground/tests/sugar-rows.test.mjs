// The benchmark rows of the template comparison and the explicit twins in the combined bundle render the DOM of
// FrameworkPerf's explicit row after mount, a click, a props update and disposal.
import test, { after } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>");
const names = ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"];
for (const name of names) globalThis[name] = name === "window" ? dom.window : dom.window[name];
after(() => { dom.window.close(); for (const name of names) delete globalThis[name]; });

const { mountTemplate } = await import("../generated-tests/FrameworkPerf.js");
const { mountTemplateSugar } = await import("../generated-tests/FrameworkSugarPerf.js");
const { mountTemplateMembers } = await import("../generated-tests/FrameworkMembersPerf.js");
const { mountTemplateClass } = await import("../generated-tests/FrameworkClassPerf.js");
const { mountTemplateMembersTwin, mountTemplateClassTwin } = await import("../generated-tests/FrameworkTwinsPerf.js");

const trace = mount => {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const [update, dispose] = mount(host, 3);
  const shots = [host.innerHTML];
  host.querySelectorAll("button")[1].click();
  shots.push(host.innerHTML);
  update("blocked");
  shots.push(host.innerHTML);
  dispose();
  shots.push(host.innerHTML);
  host.remove();
  return shots;
};

const rows = {
  "free tags, Component.template": mountTemplateSugar,
  "Component.ofMembers": mountTemplateMembers,
  "Component.ofClass": mountTemplateClass,
  "explicit twin of the ofMembers row": mountTemplateMembersTwin,
  "explicit twin of the ofClass row": mountTemplateClassTwin
};

for (const [name, mount] of Object.entries(rows))
  test(`${name} renders like the explicit row after mount, a click, an update and disposal`, () => {
    const reference = trace(mountTemplate);
    assert.notEqual(reference[0], reference[1]);
    assert.notEqual(reference[1], reference[2]);
    assert.deepEqual(trace(mount), reference);
  });
