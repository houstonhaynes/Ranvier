// Size entry: the plugin loader mounted through the client path.
import { matchLoader } from "./out-plugin/Fixtures.js";
import { mountWith } from "./out-plugin/Ranvier.View.Foundation/Mounting.js";
import { Component_render } from "./out-plugin/Ranvier.View.Portable/Template.js";
export const mount = (mode, graph, host, signal) => mountWith(mode, graph, undefined, () => {}, host, undefined, (context) => Component_render(matchLoader, signal)(context.Backend));
