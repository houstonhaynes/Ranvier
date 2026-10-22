// Bundles one probe entry for the size table: PROBE_ENTRY is explicit, lens or plugin.

const entry = process.env.PROBE_ENTRY;
export default ({
  logLevel: "warn",
  build: {
    outDir: `bundle-${entry}`, emptyOutDir: true, minify: true, modulePreload: false,
    rollupOptions: { input: `size-entry-${entry}.mjs`, preserveEntrySignatures: "strict", output: { entryFileNames: "bundle.js", inlineDynamicImports: true } }
  }
});
