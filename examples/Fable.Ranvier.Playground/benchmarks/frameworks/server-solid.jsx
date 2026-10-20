import { renderToString } from "@solidjs/web";
import { createSignal, For } from "solid-js";

function ComponentRow(props) {
  const [label] = createSignal(`row ${props.index}`);
  const [clicks, setClicks] = createSignal(0);
  return <div id={`row-${props.index}`} class="panel" title={label()}><input value={label()} /><button onClick={() => setClicks(clicks() + 1)}>{clicks()}</button></div>;
}

function KeyedRow(props) {
  return <tr><td class="col-md-1">{props.row.id}</td><td class="col-md-4"><a onClick={() => {}}>{props.row.label}</a></td><td class="col-md-1"><a onClick={() => {}}><span class="glyphicon glyphicon-remove" aria-hidden="true" /></a></td><td class="col-md-6" /></tr>;
}

// The page trees; hydration-solid.jsx hydrates the same trees, compiled for the DOM.
export const componentPage = rows => <section><For each={Array.from({ length: rows }, (_, i) => i + 1)}>{index => <ComponentRow index={index} />}</For></section>;
export const keyedPage = rows => <table class="table"><tbody><For each={Array.from({ length: rows }, (_, i) => ({ id: i + 1, label: `row ${i + 1}` }))}>{row => <KeyedRow row={row} />}</For></tbody></table>;

export const renderComponentPage = rows => renderToString(() => componentPage(rows));
export const renderKeyedPage = rows => renderToString(() => keyedPage(rows));
