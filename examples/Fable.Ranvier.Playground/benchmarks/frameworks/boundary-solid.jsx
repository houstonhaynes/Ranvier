import { createMemo, createSignal, flush, Loading, Show } from "solid-js";
import { render } from "@solidjs/web";

// Rows reading one async value under a Loading boundary; resolves the value and waits for the reveal.
export async function mountReveal(host, width) {
  let resolve;
  const promise = new Promise(settle => { resolve = settle; });
  const dispose = render(() => {
    const value = createMemo(() => promise);
    return <Loading fallback={<span>loading</span>}>
      {Array.from({ length: width }, (_, i) => <div class="row">{value() + i}</div>)}
    </Loading>;
  }, host);
  flush();
  resolve("ready");
  for (let turn = 0; turn < 20 && host.querySelectorAll(".row").length !== width; turn++) {
    await Promise.resolve();
    flush();
  }
  return dispose;
}

// Reads an already resolved value where the branch is created, as the Ranvier fixture creates its read. Solid 2 reads a
// promise through an async memo, so the fallback mounts and the rows reveal in a later microtask turn.
function Rows(props) {
  const value = createMemo(() => props.promise);
  return <Loading fallback={<span>loading</span>}>
    {Array.from({ length: props.width }, (_, i) => <div class="row">{value() + i}</div>)}
  </Loading>;
}

// The switch workload without a boundary: the rows read the resolved value directly.
function PlainRows(props) {
  const value = createMemo(() => props.promise);
  return Array.from({ length: props.width }, (_, i) => <div class="row">{value() + i}</div>);
}

export async function mountSwitchPlain(host, width) {
  const promise = Promise.resolve("ready");
  const [arm, setArm] = createSignal(false);
  const dispose = render(() => <Show when={arm()}><PlainRows promise={promise} width={width} /></Show>, host);
  flush();
  setArm(true);
  flush();
  for (let turn = 0; turn < 20 && host.querySelectorAll(".row").length !== width; turn++) {
    await Promise.resolve();
    flush();
  }
  return dispose;
}

// A Loading boundary mounted by a Show switch over an already resolved promise; the boundary mounts inside a flush.
export async function mountSwitchReveal(host, width) {
  const promise = Promise.resolve("ready");
  const [arm, setArm] = createSignal(false);
  const dispose = render(() => <Show when={arm()}><Rows promise={promise} width={width} /></Show>, host);
  flush();
  setArm(true);
  flush();
  for (let turn = 0; turn < 20 && host.querySelectorAll(".row").length !== width; turn++) {
    await Promise.resolve();
    flush();
  }
  return dispose;
}
