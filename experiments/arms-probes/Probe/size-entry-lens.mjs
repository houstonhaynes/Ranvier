// Size entry: the lens loader mounted through the client path.
import { lensLoader } from "./out-lens/Fixtures.js";
import { mountWith } from "./out-lens/Ranvier.View.Foundation/Mounting.js";
import { Component_render } from "./out-lens/Ranvier.View.Portable/Template.js";
export const mount = (mode, graph, host, signal) => mountWith(mode, graph, undefined, () => {}, host, undefined, (context) => Component_render(lensLoader, signal)(context.Backend));
