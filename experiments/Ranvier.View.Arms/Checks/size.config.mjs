// Bundles one Arms.Match size entry for size.mjs: ARMS_SIZE_ENTRY names it, ARMS_SIZE_MINIFY=false keeps it readable.
const entry = process.env.ARMS_SIZE_ENTRY;
const minify = process.env.ARMS_SIZE_MINIFY !== "false";
export default {
  logLevel: "warn",
  build: {
    outDir: `generated-size/${entry}${minify ? "" : "-unminified"}`, emptyOutDir: true, minify, modulePreload: false,
    rollupOptions: { input: `generated-size/entry-${entry}.mjs`, preserveEntrySignatures: "strict", output: { entryFileNames: "bundle.js", inlineDynamicImports: true } }
  }
};
