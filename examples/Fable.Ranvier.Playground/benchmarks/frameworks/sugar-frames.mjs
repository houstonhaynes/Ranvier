// Names and modules of the template sugar's emitted code, for the allocation comparer and the bundle-size check.
// Requires `npm run compile:tests`: the names are read from the emitted sugar modules.
import { readFileSync } from "node:fs";

const portable = new URL("../../generated-tests/src/Ranvier.View/", import.meta.url);
const generatedSource = new URL("../../../../src/Ranvier.View/Generated/HtmlTemplateSugar.fs", import.meta.url);

/** Bundle module paths (as in `//#region` comments and frame `module` fields) of the sugar layer. */
export const sugarModules = ["src/Ranvier.View/TemplateSugar.js", "src/Ranvier.View/Generated/HtmlTemplateSugar.js"];
export const isSugarModule = path => sugarModules.some(module => path.endsWith(module));

/** A JavaScript source with comments removed and string and template literals emptied; line breaks are kept. */
export const stripLiterals = source =>
  source.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*|"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`/g, match => {
    const breaks = match.replace(/[^\n]/g, "");
    return match.startsWith("/") ? breaks : '""' + breaks;
  });

/** The functions and classes declared in a JavaScript source, with the 0-based line of each declaration keyword: `function` and `class` declarations, and `var`, `let` or `const` bound to a function, class or arrow. */
export function declarationsAt(source) {
  const stripped = stripLiterals(source), result = [];
  let line = 0, scanned = 0;
  for (const match of stripped.matchAll(/\b(?:function\*?|class)\s+(?!extends\b)([A-Za-z_$][\w$]*)|\b(?:var|let|const)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:class\b|function\b|async\b|\([^()]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>)/g)) {
    for (; scanned < match.index; scanned++) if (stripped.charCodeAt(scanned) === 10) line++;
    result.push({ name: match[1] ?? match[2], line });
  }
  return result;
}

/** The names of the functions and classes declared in a JavaScript source, as `declarationsAt` finds them. */
export const declarations = source => declarationsAt(source).map(declaration => declaration.name);

let derived;
/**
 * The emitted tag-class names without the `$1` suffix (`base'` is `base$0027`), checked against the classes declared in `HtmlTemplateSugar.fs`,
 * and every name declared by the emitted sugar modules: tag classes and their functions, `Element$2`, `VoidElement$1`, `ComponentTag$2` and the free builders.
 */
export function sugarDeclarations() {
  if (derived) return derived;
  const tagSource = [...readFileSync(generatedSource, "utf8").matchAll(/^\s*type ([a-z][a-z0-9']*)<'i> private \(ps:/gm)].map(match => match[1].replace("'", "$0027"));
  const tagsEmitted = readFileSync(new URL("Generated/HtmlTemplateSugar.js", portable), "utf8");
  const tagClasses = [...tagsEmitted.matchAll(/^export class ([a-z][a-z0-9$]*)\$1 extends (?:Element\$2|VoidElement\$1) /gm)].map(match => match[1]);
  if (tagSource.length !== tagClasses.length || tagSource.some((tag, index) => tag !== tagClasses[index]))
    throw Error(`Tag classes differ: ${tagSource.length} in HtmlTemplateSugar.fs, ${tagClasses.length} emitted in HtmlTemplateSugar.js`);
  const names = new Set([...declarations(readFileSync(new URL("TemplateSugar.js", portable), "utf8")), ...declarations(tagsEmitted)]);
  return (derived = { tagClasses, names });
}

/** Whether a function name is a sugar declaration, as emitted or with bundler suffixes `$<n>`. */
export function isSugarName(name) {
  const { names } = sugarDeclarations();
  for (let candidate = name; ; ) {
    if (names.has(candidate)) return true;
    const renamed = /^(.+)\$\d+$/.exec(candidate);
    if (!renamed) return false;
    candidate = renamed[1];
  }
}

/** The module of each line (0-based) of an unminified bundle, from its `//#region` comments; `undefined` outside regions. */
export function lineModules(bundle) {
  let current;
  return bundle.split("\n").map(line => {
    const region = /^\/\/#region (.*)$/.exec(line.trim());
    if (region) return (current = region[1]);
    if (line.trim() === "//#endregion") { const ended = current; current = undefined; return ended; }
    return current;
  });
}

/** The declarations of an unminified bundle inside the sugar modules, and the declarations elsewhere that carry a sugar name. */
export function bundleSugar(bundle) {
  const modules = lineModules(bundle), inSugar = [], collisions = [];
  for (const { name, line } of declarationsAt(bundle)) {
    if (modules[line] !== undefined && isSugarModule(modules[line])) inSugar.push(name);
    else if (isSugarName(name)) collisions.push(`${name} (${modules[line] ?? "no module"})`);
  }
  return { inSugar, collisions };
}

/** Whether a sampled frame belongs to the sugar layer: by its `module` when recorded, otherwise by name. */
export const isSugarFrame = frame => frame.module !== undefined ? isSugarModule(frame.module) : isSugarName(frame.name.split(" ")[0]);
