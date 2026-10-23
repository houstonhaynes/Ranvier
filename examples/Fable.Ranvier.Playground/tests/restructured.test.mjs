// jsdom (parse5) restructures every fixture the DOM twin's parser rejects as restructured markup
// (tests/Ranvier.View.Twin.Tests/restructured.json): it gives the fixture's recorded HTML, which differs from the input
// or, in a MathML text integration point, moves an element to the HTML namespace.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { JSDOM } from "jsdom";

const fixtures = JSON.parse(readFileSync(new URL("../../../tests/Ranvier.View.Twin.Tests/restructured.json", import.meta.url), "utf8"));
const document = new JSDOM("<!doctype html><body></body>").window.document;
const html = "http://www.w3.org/1999/xhtml";

for (const [markup, construct, parsed] of fixtures) {
  test(`jsdom restructures ${markup} (${construct})`, () => {
    const div = document.createElement("div");
    div.innerHTML = markup;
    assert.equal(div.innerHTML, parsed);

    if (construct.includes("integration point")) {
      const point = [...div.querySelectorAll("*")].find(e => ["mi", "mo", "mn", "ms", "mtext"].includes(e.localName));
      assert.equal(point.firstElementChild.namespaceURI, html, "its child is an HTML element");
    } else {
      assert.notEqual(parsed, markup);
    }
  });
}
