import "./style.css";
import { start } from "./generated/App.js";

let dispose = start();
window.playground = { dispose: () => dispose() };
if (import.meta.hot) {
  import.meta.hot.accept("./generated/App.js", (updated) => {
    if (updated) {
      dispose();
      dispose = updated.start();
    }
  });
  import.meta.hot.dispose(() => dispose());
}
