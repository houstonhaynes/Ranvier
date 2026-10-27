import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import postcss from 'postcss';
import { parse as parseValue, ident } from 'css-tree';

const directory = path.dirname(fileURLToPath(import.meta.url));
const reserved = new Set('abstract and as asr assert atomic base begin break checked class component const constraint constructor continue default delegate do done downcast downto eager elif else end event exception extern external false finally fixed for fun function global if in include inherit inline interface internal land lazy let lor lsl lsr lxor match member method mixin mod module mutable namespace new not null object of open or override parallel private process protected public pure rec return sealed sig static struct tailcall then to trait true try type upcast use val virtual void volatile when while with yield'.split(' '));

function identifier(value) {
  if (typeof value !== 'string' || !/^[A-Za-z_][A-Za-z0-9_]*$/.test(value)) throw new Error(`Invalid F# identifier: ${value}`);
  return reserved.has(value) ? '``' + value + '``' : value;
}

function qualified(value) {
  if (typeof value !== 'string' || !value) throw new Error('Expected an F# module or namespace name.');
  return value.split('.').map(identifier).join('.');
}

function literal(value) {
  return '"' + [...value].map(char => {
    if (char === '\\') return '\\\\';
    if (char === '"') return '\\"';
    if (char.charCodeAt(0) < 32 || char === '\u2028' || char === '\u2029') return '\\u' + char.charCodeAt(0).toString(16).padStart(4, '0');
    return char;
  }).join('') + '"';
}

function decimalIdentity(text) {
  const match = /^([+-]?)(\d*\.?\d+)(?:[eE]([+-]?\d+))?$/.exec(text);
  if (!match) return null;
  const [whole, fraction = ''] = match[2].split('.');
  let digits = (whole + fraction).replace(/^0+/, '');
  if (!digits) return '0';
  let power = BigInt(match[3] ?? '0') - BigInt(fraction.length);
  while (digits.endsWith('0')) { digits = digits.slice(0, -1); power++; }
  return (match[1] === '-' ? '-' : '') + digits + 'e' + power;
}

function numberLiteral(text, integer) {
  const value = Number(text);
  if (!Number.isFinite(value) || decimalIdentity(String(value)) !== decimalIdentity(text)) return null;
  if (integer) return Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? String(value) : null;
  const rendered = String(value);
  return /[.eE]/.test(rendered) ? rendered : rendered + '.0';
}

function prepare(manifest) {
  const sugar = manifest?.sugar;
  if (!sugar || sugar.schemaVersion !== 2 || !Array.isArray(sugar.properties) || !Array.isArray(sugar.primitives) || !Array.isArray(sugar.keywords)) {
    throw new Error('The binding manifest has no sugar map. Regenerate it with the current specification tool and --runtime.');
  }
  qualified(sugar.namespaceName);
  for (const item of [...sugar.properties, ...sugar.primitives, ...sugar.keywords]) identifier(item.memberName);
  const properties = new Map();
  for (const item of sugar.properties) {
    if (item.scopes.length === 0 && !item.obsolete) properties.set(item.cssName.toLowerCase(), item);
  }
  return { ...sugar, properties, keywords: new Map(sugar.keywords.map(item => [item.token, item])) };
}

function rangesAllow(value, unit, ranges) {
  const factors = { deg: [1, 'angle'], rad: [180 / Math.PI, 'angle'], turn: [360, 'angle'], grad: [.9, 'angle'], s: [1, 'time'], ms: [.001, 'time'], px: [1, 'length'], in: [96, 'length'], cm: [96 / 2.54, 'length'], mm: [96 / 25.4, 'length'], pt: [96 / 72, 'length'], pc: [16, 'length'] };
  for (const range of ranges) {
    for (const [bound, lower] of [[range.minimum, true], [range.maximum, false]]) {
      if (bound.kind !== 'finite') continue;
      let comparable = value;
      if (bound.unitName && bound.unitName !== unit) {
        const from = factors[unit], to = factors[bound.unitName];
        if (from && to && from[1] === to[1]) comparable = value * from[0] / to[0];
        else if (value !== 0) return false;
      }
      if (lower ? comparable < Number(bound.value) : comparable > Number(bound.value)) return false;
    }
  }
  return true;
}

function typedValue(value, property, map) {
  let tree;
  try { tree = parseValue(value, { context: 'value', onParseError(error) { throw error; } }); }
  catch { return null; }
  const nodes = tree.children.toArray();
  if (nodes.length !== 1) return null;
  const token = nodes[0];
  if (token.type === 'Identifier') {
    const keyword = ident.decode(token.name).toLowerCase();
    const helper = map.keywords.get(keyword);
    if (helper && property.acceptedKeywords.includes(keyword)) return 'Css.' + identifier(helper.memberName);
  }
  for (const primitive of map.primitives) {
    if (!property.acceptedMarkers.includes(primitive.marker)) continue;
    const conversion = property.acceptedValues?.find(item => item.marker === primitive.marker);
    if (!conversion || !Array.isArray(conversion.ranges)) continue;
    let argument = null;
    if (token.type === 'Dimension' && ['length', 'angle', 'time', 'frequency', 'resolution', 'flex'].includes(primitive.production)
        && primitive.methodName.toLowerCase() === ident.decode(token.unit).toLowerCase()) {
      argument = numberLiteral(token.value, false);
    } else if (token.type === 'Percentage' && ['percentage', 'percentage-token'].includes(primitive.production)) {
      argument = numberLiteral(token.value, false);
    } else if (token.type === 'Number' && ['number', 'number-token', 'integer'].includes(primitive.production)) {
      argument = numberLiteral(token.value, primitive.argumentType === 'int');
    } else if (token.type === 'String' && ['string', 'string-token'].includes(primitive.production)) {
      argument = literal(token.value);
    }
    if (argument !== null) {
      const unit = token.type === 'Dimension' ? primitive.methodName : token.type === 'Percentage' ? '%' : '';
      if (token.type !== 'String' && !rangesAllow(Number(token.value), unit, conversion.ranges)) continue;
      return '(Css.' + identifier(primitive.memberName) + ' ' + (argument.startsWith('-') ? '(' + argument + ')' : argument) + ')';
    }
  }
  return null;
}

function validProperty(name) {
  return /^--[A-Za-z0-9_-]+$|^-?[A-Za-z_][A-Za-z0-9_-]*$/.test(name);
}

/** Parses CSS and emits a portable F# rule module plus source-located fallback coverage. */
export function generate(css, manifest, options = {}) {
  const moduleName = qualified(options.moduleName ?? 'ImportedStyles');
  const map = prepare(manifest);
  const root = postcss.parse(css, { from: options.from });
  const report = { schemaVersion: 1, inputSha256: createHash('sha256').update(css).digest('hex'), typedDeclarations: 0, rawDeclarations: 0, fallbacks: [] };
  const diagnostic = (node, kind, reason) => report.fallbacks.push({ kind, reason, line: node.source?.start?.line ?? 1, column: node.source?.start?.column ?? 1 });
  const raw = (node, reason) => {
    if (node.type !== 'comment') {
      diagnostic(node, 'rule', reason);
      node.walkDecls?.(() => report.rawDeclarations++);
    }
    const start = node.source?.start?.offset, end = node.source?.end?.offset;
    const tokens = Number.isInteger(start) && Number.isInteger(end) ? node.source.input.css.slice(start, end) : node.toString();
    return 'Rule.unsafeFromText ' + literal(tokens);
  };
  const declaration = (node, typed) => {
    const tokens = node.raws.value?.raw ?? node.value;
    const property = typed ? map.properties.get(node.prop.toLowerCase()) : null;
    const value = property && !tokens.includes('/*') ? typedValue(tokens, property, map) : null;
    let result;
    if (value !== null) {
      result = 'Css.' + identifier(property.memberName) + ' ' + value;
      report.typedDeclarations++;
    } else {
      result = 'Declaration.create ' + literal(node.prop) + ' ' + literal(tokens);
      report.rawDeclarations++;
      diagnostic(node, 'declaration', typed ? 'No lossless typed sugar conversion for this property/value.' : 'Descriptor declarations retain their original tokens.');
    }
    return node.important ? '(' + result + ' |> Declaration.important)' : result;
  };
  const list = (items, indent) => items.length === 0 ? '[]' : '[\n' + items.map(item => ' '.repeat(indent + 4) + item).join('\n') + '\n' + ' '.repeat(indent) + ']';
  const declarations = (nodes, indent, typed) => list(nodes.map(node => declaration(node, typed)), indent);
  const selector = text => {
    if (/^\.[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.className ' + literal(text.slice(1));
    if (/^#[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.id ' + literal(text.slice(1));
    if (/^[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.element ' + literal(text);
    return 'Selector.unsafeFromText ' + literal(text);
  };
  function rule(node, indent = 4, styleContext = false) {
    if (node.type === 'comment') return raw(node);
    if (node.type !== 'rule' && node.type !== 'atrule') return raw(node, 'Unsupported parsed node.');
    if (node.type === 'atrule' && node.raws.afterName === '' && node.params.startsWith('\\')) return raw(node, 'Preserves escaped at-rule heads.');
    if (node.type === 'rule' && /[{};]/.test(node.selector)) return raw(node, 'Selector exceeds the portable selector factory subset.');
    if (node.type === 'atrule' && !/^[A-Za-z_][A-Za-z0-9_-]*$/.test(node.name)) return raw(node, 'Escaped at-rule name exceeds the portable factory subset.');
    const opaqueHead = node.type === 'atrule' && /[{};]/.test(node.params);
    if (!node.nodes) return opaqueHead ? 'Rule.StatementRule (' + literal(node.name) + ', ' + literal(node.params) + ')' : 'Rule.statement ' + literal(node.name) + ' ' + literal(node.params);
    if (node.nodes.some(child => child.type === 'comment') && node.nodes.some(child => child.type === 'decl')) return raw(node, 'Preserves comments within declaration blocks.');
    if (node.nodes.some(child => child.type === 'decl' && !validProperty(child.prop))) return raw(node, 'Escaped or unsupported declaration name.');
    if (node.nodes.some(child => child.type === 'decl' && (/\S/.test(child.raws.before ?? '') || (child.raws.between ?? ':').replace(/[\t\r\n\f ]/g, '') !== ':'))) return raw(node, 'Preserves declaration hacks or nonstandard punctuation.');
    if (node.nodes.some(child => child.type === 'decl' && [child.raws.between, child.raws.important].some(text => text?.includes('/*')))) return raw(node, 'Preserves comments within declaration punctuation.');
    if (node.type === 'rule') {
      const firstChild = node.nodes.findIndex(child => child.type !== 'decl');
      if (firstChild >= 0 && node.nodes.slice(firstChild).some(child => child.type === 'decl')) return raw(node, 'Preserves declarations interleaved with nested rules.');
      const head = firstChild < 0 ? node.nodes : node.nodes.slice(0, firstChild);
      const children = firstChild < 0 ? [] : node.nodes.slice(firstChild);
      const prefix = children.length ? 'Rule.nested' : 'Rule.style';
      return prefix + ' (' + selector(node.selector) + ') ' + declarations(head, indent, true)
        + (children.length ? ' ' + list(children.map(child => rule(child, indent + 4, true)), indent) : '');
    }
    if (node.nodes.every(child => child.type === 'decl')) {
      const typed = styleContext && ['media', 'supports', 'container', 'layer', 'starting-style'].includes(node.name.toLowerCase());
      const body = declarations(node.nodes, indent, typed);
      return opaqueHead ? 'Rule.DescriptorRule (' + literal(node.name) + ', ' + literal(node.params) + ', ' + body + ')' : 'Rule.descriptors ' + literal(node.name) + ' ' + literal(node.params) + ' ' + body;
    }
    if (node.nodes.some(child => child.type === 'decl')) return raw(node, 'Preserves mixed at-rule contents.');
    const children = list(node.nodes.map(child => rule(child, indent + 4, styleContext)), indent);
    if (opaqueHead) return 'Rule.GroupRule (' + literal(node.name) + ', ' + literal(node.params) + ', ' + children + ')';
    if (['media', 'supports', 'container'].includes(node.name)) return 'Rule.' + node.name + ' ' + literal(node.params) + ' ' + children;
    if (node.name === 'layer') return 'Rule.layer ' + (node.params ? '(Some ' + literal(node.params) + ')' : 'None') + ' ' + children;
    return 'Rule.group ' + literal(node.name) + ' ' + literal(node.params) + ' ' + children;
  }
  const rules = root.nodes.map(node => rule(node));
  if (options.strictTyped && report.fallbacks.length) throw new Error(`Strict typed import rejected ${report.fallbacks.length} fallback(s); first at ${report.fallbacks[0].line}:${report.fallbacks[0].column}: ${report.fallbacks[0].reason}`);
  const imports = 'open Ranvier.Css\n' + (map.namespaceName === 'Ranvier.Css' ? '' : 'open ' + qualified(map.namespaceName) + '\n');
  const source = '// <auto-generated />\nmodule ' + moduleName + '\n\n' + imports + '\n/// <summary>Imported stylesheet rules in source order; explicit escapes retain unsupported syntax.</summary>\nlet rules: Rule list = ' + list(rules, 0) + '\n';
  return { source, report };
}

async function atomicWrite(target, contents) {
  await fs.mkdir(path.dirname(target), { recursive: true });
  const temporary = target + '.' + randomUUID() + '.tmp';
  try { await fs.writeFile(temporary, contents, 'utf8'); await fs.rename(temporary, target); }
  finally { await fs.rm(temporary, { force: true }); }
}

/** Runs file import; validation and strict-mode failures preserve existing output files. */
export async function run(args) {
  const settings = { manifest: path.resolve(directory, '../../src/Ranvier.Css.Bindings/Generated/Css.manifest.json') };
  for (let index = 0; index < args.length; index++) {
    const option = args[index];
    if (option === '--help') { process.stdout.write('CSS to Ranvier.Css: --input file.css --output file.fs [--module ImportedStyles] [--manifest bindings.manifest.json] [--report report.json] [--strict-typed]\n'); return 0; }
    if (option === '--strict-typed') { settings.strictTyped = true; continue; }
    const key = { '--input': 'input', '--output': 'output', '--module': 'moduleName', '--manifest': 'manifest', '--report': 'report' }[option];
    if (!key || index + 1 >= args.length || args[index + 1].startsWith('--')) throw new Error('Unknown option or missing value: ' + option);
    settings[key] = args[++index];
  }
  if (!settings.input || !settings.output) throw new Error('--input and --output are required.');
  settings.report ??= settings.output.replace(/\.fs$/i, '') + '.report.json';
  const paths = [settings.input, settings.manifest, settings.output, settings.report].map(value => path.resolve(value).toLowerCase());
  if (new Set(paths).size !== paths.length) throw new Error('Inputs, F# output, and report must have distinct paths.');
  const result = generate(await fs.readFile(settings.input, 'utf8'), JSON.parse(await fs.readFile(settings.manifest, 'utf8')), { ...settings, from: settings.input });
  await atomicWrite(path.resolve(settings.output), result.source);
  await atomicWrite(path.resolve(settings.report), JSON.stringify(result.report, null, 2) + '\n');
  process.stdout.write(`${result.report.typedDeclarations} typed declarations, ${result.report.fallbacks.length} explicit fallbacks.\n`);
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { process.exitCode = await run(process.argv.slice(2)); }
  catch (error) { process.stderr.write(error.message + '\n'); process.exitCode = 1; }
}
