// Size entry: the explicit loader mounted through the client path.
import { explicitLoader } from "./out-explicit/Fixtures.js";
import { mountWith } from "./out-explicit/Ranvier.View.Foundation/Mounting.js";
import { Component_render } from "./out-explicit/Ranvier.View.Portable/Template.js";
export const mount = (mode, graph, host, signal) => mountWith(mode, graph, undefined, () => {}, host, undefined, (context) => Component_render(explicitLoader, signal)(context.Backend));
