import { flush, For, onSettled } from "solid-js";
import { render } from "@solidjs/web";

// A row that sets data-placed from onSettled, Solid 2's hook for code that needs the row's node in the document.
function HookedRow(props) {
  let node;
  onSettled(() => { node.setAttribute("data-placed", "true"); });
  return <div class="row" ref={node}>{props.index}</div>;
}

function PlainRow(props) {
  return <div class="row">{props.index}</div>;
}

const mountRows = Row => (host, width) => {
  const items = Array.from({ length: width }, (_, i) => i);
  const dispose = render(() => <div><For each={items}>{index => <Row index={index} />}</For></div>, host);
  flush();
  return dispose;
};

export const mountHooked = mountRows(HookedRow);
export const mountPlain = mountRows(PlainRow);
