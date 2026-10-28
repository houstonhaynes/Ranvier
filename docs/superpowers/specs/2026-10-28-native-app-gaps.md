# From the WebCore backend to a Ranvier native app: the remaining gaps

Written 2026-10-28, at `webcore-backend` `3556cdc5` and Ranvier.WebCore `v0.1.0-preview.4` (ABI 20). The
[backend design](2026-10-28-native-webcore-backend-design.md) covers what exists; this lists what stands between
it and an app a person runs: a window showing Ranvier components, taking input, shipped as one executable.

## Where things stand

| Piece | State |
|---|---|
| Ranvier.WebCore native host | ABI 20 on main. Builds on win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64 in CI; preview.4 publishing to nuget.org |
| `Ranvier.View.WebCore` | Mounts components into a WebCore page through the generated DOM members; paints to an RGBA buffer; forwards mouse input (`InputTests`) |
| Conformance | ConformanceCases' fifteen modules match the DOM twin, except four pending cases |
| AOT | `tests/Ranvier.View.WebCore.AotSmoke` mounts and paints; publishing it has not run end to end |
| Window | None. Every check so far reads the RGBA buffer back; nothing has been on screen |
| Accessibility | ABI complete; no platform bridge ([help wanted](https://github.com/ranvier-org/Ranvier.WebCore/pull/2)) |

Critical path for a first Windows app: gaps 1, 2 and 3. The rest can follow in any order.

## 1. A window: the SDL3 presenter

The backend paints into a buffer and takes input events; the design leaves putting the pixels on screen to
the app. SDL3, through `ppy.SDL3-CS` (plain P/Invoke, natives for all five RIDs), is the chosen layer.

Work:

1. **Project.** `src/Ranvier.View.WebCore.Sdl` (decision G1), referencing `Ranvier.View.WebCore` and `ppy.SDL3-CS`.
   Trimmable and AOT-compatible, like the backend.
2. **Window and texture.** `SDL_CreateWindow` with `SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WINDOW_RESIZABLE`, an
   `SDL_Renderer`, and a streaming `SDL_PIXELFORMAT_ABGR8888` texture (bytes R, G, B, A in memory, as
   `rv_host_paint_rgba` writes them) at the window's pixel size.
3. **Frame.** After `rv_host_update`, paint the dirty rectangles (or the whole page until dirty rectangles
   are used) into the texture with `SDL_UpdateTexture`, then `SDL_RenderTexture` and `SDL_RenderPresent`. Paint
   only when the page has asked for a frame.
4. **Loop.** One thread, the host thread: `SDL_WaitEventTimeout` with the timeout to the next WebCore timer,
   dispatch the events, `rv_run_loop_cycle`, then the frame. The wrapper table's finalizer releases also drain
   here, so the loop must keep cycling while the window is open.
5. **Input.** Mouse motion, buttons and wheel to `rv_host_mouse` and the wheel member; keys to the key member,
   with SDL keycodes mapped to DOM `key` and `code`; `SDL_EVENT_TEXT_INPUT` to text insertion; IME composition
   (`SDL_EVENT_TEXT_EDITING`) to the ABI's IME composition members (`rv_abi_has("ime")`, decision G4). Focus in and
   out of the window to the page's focus.
6. **Size and density.** `SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED` resizes the host and the texture;
   `SDL_GetWindowDisplayScale` sets the page's device scale factor, so CSS pixels stay CSS pixels on HiDPI.
7. **Cursor.** Map the page's cursor (the `cursor` CSS value at the pointer) to `SDL_CreateSystemCursor`.
8. **Clipboard.** Bridge the host's clipboard (docs/CLIPBOARD-DND.md in Ranvier.WebCore) to `SDL_SetClipboardText`
   and `SDL_GetClipboardText`.
9. **API.** One entry point an app calls, for example `Window.run (title, width, height) (fun host -> mount ...)`,
   returning when the window closes (decision G2).
10. **Sample.** `samples/native-counter` (or similar): a counter and a text input, published with
    `PublishAot`.

Checks: a test that opens a hidden window, mounts, presents and reads the texture back; the sample, run by
hand on Windows, then macOS. On macOS, check first that WebCore's run loop and SDL's event pump share the
main thread (decision G3): it is the riskiest unknown here.

## 2. The native package from nuget.org

`Ranvier.View.WebCore.fsproj` takes the natives from `RanvierWebCoreNativeDir` (default `C:\wk\native-v20`)
unless `RanvierWebCoreNativeVersion` is set, and then only from `Ranvier.WebCore.Native.win-x64`.

Work:

1. Once preview.4 is on nuget.org, default `RanvierWebCoreNativeVersion` to `0.1.0-preview.4` (Directory.Packages.props
   if the repo uses central package management) and drop the local-directory default; keep
   `RanvierWebCoreNativeDir` as an opt-in override for local WebCore builds.
2. Reference the package for each RID the app targets. Either a RID-conditional `PackageReference` per runtime
   in the fsproj, or a `Ranvier.WebCore.Native` meta-package in the WebCore repo that depends on the five
   (decision G5, which also un-pauses the meta-package).
3. Check: a clean clone, no `C:\wk`, builds and passes `Ranvier.View.WebCore.Tests`.

## 3. Landing on master

`webcore-backend` is ten commits ahead of master and has no PR. The design's three open decisions block it:

1. Where the managed interop lives: copied into Ranvier (today, `src/Ranvier.View.WebCore/Interop`), or a
   managed `Ranvier.WebCore` package from the WebCore repo versioned with the ABI (decision G6).
2. The package name: `Ranvier.View.WebCore` or `Ranvier.Native` (decision G7).
3. The change to `Fable.Ranvier`'s global seam, a production source file (decision G8).

Then: a PR against master; master's CI must restore the native package (gap 2) and run the backend tests on
Windows at least (decision G9: which runners).

## 4. AOT publish end to end

`tests/Ranvier.View.WebCore.AotSmoke` has not published cleanly: ILC needs `vswhere` (Visual Studio's C++
linker) on the machine, and it warns from `Interop/Native.fs`.

Work:

1. Publish the smoke app on a machine with the VS C++ build tools; record the exact prerequisites in the
   README.
2. Read the ILC warning; fix its cause in `Native.fs` (most likely a P/Invoke signature ILC cannot marshal
   without runtime code, such as `[<In; Out>] byte[]`, which `LibraryImport` or a `nativeptr` overload avoids).
   The generator in Ranvier.WebCore emits `Native.fs`, so the fix lands there and is copied back.
3. Run the published executable: it mounts, paints, and its pixels match.
4. Add the publish to CI on windows-latest (decision G10).

## 5. DOM members the ABI lacks

Four conformance cases are pending: three need a second document (`DOMImplementation.createHTMLDocument`),
one needs `createElementNS`. Members taking unions of nodes, sequences or variadic arguments
(`append(...nodes)`) throw `NotSupportedException`. The view's own paths use none of them, so apps are not
blocked, but user code calling them is.

Work, in the Ranvier.WebCore repo as the next ABI batch (ABI 21; one ABI version per release batch):

1. `createElementNS` and `createHTMLDocument` in `gen/dom.json`; the generator's handling of a second document's
   lifetime in the object table.
2. Mapping unions, sequences and variadics in the generator (decision G11: now, or when a user hits one).
3. Regenerate `Native.fs` and `NativeDom.fs`, copy into Ranvier, un-pend the four cases.

## 6. Other platforms

The backend has run only on win-x64. The packages for the other four RIDs exist and their CI smoke paints a
page, but no Ranvier code has loaded them.

Work: run `Ranvier.View.WebCore.Tests` on osx-arm64 and linux-x64 (CI runners), then the SDL3 sample on each.
Known blocker: macOS 15 lacks `libswiftCompatibilitySpan.dylib`, so the osx package needs macOS 26 for now
(decision G12: ship that, or the paused macOS 15 work first). win-arm64 needs a real ARM machine.
Linux loads the WPE port, whose input and clipboard paths differ from Windows'.

## 7. Accessibility bridges

The ABI is complete; the bridges (UI Automation, NSAccessibility, AT-SPI) are documented as help wanted in
Ranvier.WebCore's `docs/ACCESSIBILITY.md`. The SDL3 presenter must expose its native window handle so a bridge
can attach. Not on the critical path (decision G13).

## 8. Optional: GPU compositing

PR #1 in Ranvier.WebCore (`gpu-compositing`) adds accelerated compositing on Windows. The CPU paint works
without it; with it, the presenter would take a GPU texture instead of copying pixels. Linux and macOS
compositing are paused. After the first app works.

## Decisions

Numbered as above; answers recorded in the next section.

| # | Decision |
|---|---|
| G1 | The presenter's project: a package in Ranvier, or a sample only |
| G2 | The presenter's API shape |
| G3 | macOS: the main thread's run loop shared by WebCore and SDL |
| G4 | IME composition in the first presenter |
| G5 | Native packages: RID-conditional references or a meta-package |
| G6 | Where the managed interop lives |
| G7 | The package name |
| G8 | The `Fable.Ranvier` seam change |
| G9 | Master's CI runners for the backend |
| G10 | AOT publish in CI |
| G11 | Unions, sequences, variadics: now or on demand |
| G12 | macOS 15 support |
| G13 | Accessibility timing |

## Answers

Round 1, 2026-10-28:

- **First app's platforms:** Windows, then macOS; Linux once CI runs the backend tests there.
- **G1:** a package, `Ranvier.View.WebCore.Sdl`, so the backend stays free of a window toolkit.
- **G5:** a `Ranvier.WebCore.Native` meta-package in the WebCore repo depending on the five RID packages.
- **G6:** a managed `Ranvier.WebCore` package from the WebCore repo, versioned with the ABI; Ranvier stops copying
  `Native.fs` and `NativeDom.fs`.
- **G7:** `Ranvier.View.WebCore`.
- **G8:** reshaped. Before any stable release the native path drops its dependency on Fable: today the generated
  wrappers implement `Fable.Core.TS.Dom` interfaces from `Xantham.Fable.Core.TS`, and `Ranvier.View.WebCore`
  references `Fable.Ranvier`. The user proposes a conditionally compiled project publishing `Fable.Ranvier` and a
  native equivalent; the shape is decided in round 2.
- **G10:** add the Windows AOT publish of the smoke app to CI once it works locally.
- **G11:** an idiomatic F# surface now, not on demand: consumers go where convenience lies. Unions, sequences and
  variadics map in ABI 21 with `createElementNS` and `createHTMLDocument`; the mapping is decided in round 2.
- **G12:** ship macOS 26 only; revisit when macOS 15 is needed.
- **G13:** bridges left to contributors, the presenter exposing the native window handle; a stable release
  waits for at least one bridge.

Round 2, 2026-10-28:

- **G8, shape:** one set of DOM interface sources, two thin projects (one for Fable, one for .NET) that include
  them, with `#if FABLE_COMPILER` only where the two differ (attributes, `Emit`). Not one project switched by a
  property: one package per project file keeps restore, `obj/` and the IDE sane. Fable compiles an interface
  member call to a member access by name, so one definition serves browser objects and native wrappers.
- **Who owns the interfaces:** Ranvier. Xantham stays Fable-only (the user owns it; separating Fable from it is
  not its concern). The source of truth is WebKit's IDL as `gen/domgen.py` reads it for `gen/dom.json`, so the
  ABI, the wrappers and the interfaces come from one model and cannot drift; `tools/Ranvier.View.WebCore.Generate`
  moves off reading `Xantham.Fable.Core.TS`. Xantham's generator is not needed: it reads TypeScript's
  `lib.dom.d.ts`, which describes the browser, not what this WebCore build implements (IDL `Conditional`,
  `EnabledBySetting`, the members `dom.json` scopes).
- **Timing:** land `webcore-backend` on master as a preview on the current Fable interfaces; decouple before the
  first stable release.
- **G11, mapping:** overloads per union arm, `[<ParamArray>]` for variadics, discriminated unions for union
  results. No erased `U2` types.
- **G2:** both: `Window.create`/`Step` for apps that own their loop, and `Window.run` built on them.
- **G4:** IME composition wired from the first presenter.
- **G9:** master's CI runs the backend on Windows first; macOS and Linux join with gap 6.
- **G3:** a throwaway macOS spike on the CI runner right after the Windows presenter works.

Round 3, 2026-10-28:

- **Where the interfaces are built:** `gen/domgen.py` in Ranvier.WebCore writes the F# interface sources; they are
  committed into Ranvier, which publishes the interface packages (a .NET `Ranvier.Dom` and its Fable variant) in
  its own release cycle. Ranvier's CI runs the generator's check against a pinned WebCore commit to catch drift.
- **Scope for Fable users:** the view and Ranvier's public API move to the new interfaces on both targets, so a
  component compiles unchanged for the browser and native. Fable code needing a member outside the scope casts
  to Xantham's types, which costs nothing on JavaScript.
- **`dom.json` scope:** curated to what native UI needs (forms, events, layout reads, ranges, selection), widened
  generously on request; `gen/dom-report.txt` lists each member left out and why, and each batch measures the
  size cost.

The frontier is empty: every decision above is settled.

## Order of work

1. Publish preview.4 (in progress); switch `Ranvier.View.WebCore` to the package (gap 2, win-x64 first).
2. Land `webcore-backend` on master as a preview, with Windows CI (gap 3).
3. The SDL3 presenter on Windows, `Window.create`/`Step`/`run`, IME included, and the sample (gap 1).
4. The macOS run loop spike (G3), then the presenter on macOS (gap 6).
5. AOT publish locally, then in CI (gap 4).
6. ABI 21 in Ranvier.WebCore: `createElementNS`, `createHTMLDocument`, the union, sequence and variadic mapping,
   the managed `Ranvier.WebCore` package, the `Ranvier.WebCore.Native` meta-package, and `domgen.py` emitting the
   F# interfaces (gaps 5, 2; G5, G6).
7. The Fable decoupling onto the generated interfaces, before the first stable release (G8).
8. Linux and win-arm64 (gap 6); accessibility bridges from contributors, one required for stable (gap 7); GPU
   compositing (gap 8).
