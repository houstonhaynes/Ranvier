import { createSignal, flush, For, Switch, Match } from "solid-js";
import { render } from "@solidjs/web";

let clicks = 0;

// The js-framework-benchmark keyed row: a label signal per row, rows keyed by identity.
export function mountKeyed(host) {
  let rows, setRows, nextId = 1;
  const build = count => Array.from({ length: count }, () => {
    const id = nextId++;
    const [label, setLabel] = createSignal(`row ${id}`);
    return { id, label, setLabel };
  });
  const dispose = render(() => {
    [rows, setRows] = createSignal([]);
    return <table class="table"><tbody><For each={rows()}>{row =>
      <tr>
        <td class="col-md-1">{row.id}</td>
        <td class="col-md-4"><a onClick={() => clicks++}>{row.label()}</a></td>
        <td class="col-md-1"><a onClick={() => clicks--}><span class="glyphicon glyphicon-remove" aria-hidden="true" /></a></td>
        <td class="col-md-6" />
      </tr>}</For></tbody></table>;
  }, host);
  flush();
  let live = true;
  return {
    create(count) { setRows(build(count)); flush(); },
    append(count) { setRows([...rows(), ...build(count)]); flush(); },
    updateEvery10th() { const current = rows(); for (let i = 0; i < current.length; i += 10) current[i].setLabel(current[i].label() + " !!!"); flush(); },
    swap() { const next = rows().slice(); if (next.length > 998) { const first = next[1]; next[1] = next[998]; next[998] = first; } setRows(next); flush(); },
    remove(index) { const next = rows().slice(); next.splice(index, 1); setRows(next); flush(); },
    clear() { setRows([]); flush(); },
    dispose() { if (live) { live = false; dispose(); } }
  };
}

// One arm per row: Plain renders a span, Counted a b with its count; a retained Counted arm reads its new count.
export function mountArms(host) {
  let rows, setRows;
  const dispose = render(() => {
    [rows, setRows] = createSignal([]);
    return <table><tbody><For each={rows()}>{row =>
      <tr>
        <td>{row.id}</td>
        <td><Switch>
          <Match when={row.mode().kind === "plain"}><span class="a">A</span></Match>
          <Match when={row.mode().kind === "counted" && row.mode()}>{mode => <b>{mode().count}</b>}</Match>
        </Switch></td>
      </tr>}</For></tbody></table>;
  }, host);
  flush();
  let live = true;
  const each = change => { for (const row of rows()) row.setMode(change(row.mode())); flush(); };
  return {
    create(count) {
      setRows(Array.from({ length: count }, (_, index) => {
        const [mode, setMode] = createSignal(index % 2 === 0 ? { kind: "plain" } : { kind: "counted", count: 0 });
        return { id: index + 1, mode, setMode };
      }));
      flush();
    },
    switch() { each(mode => mode.kind === "plain" ? { kind: "counted", count: 0 } : { kind: "plain" }); },
    payload() { each(mode => mode.kind === "plain" ? mode : { kind: "counted", count: mode.count + 1 }); },
    clear() { setRows([]); flush(); },
    dispose() { if (live) { live = false; dispose(); } }
  };
}
