import { defineConfig } from "vite";

const entry = process.env.SUGAR_SIZE_ENTRY;
const minify = process.env.SUGAR_SIZE_MINIFY !== "false";
export default defineConfig({
  build: {
    outDir: `generated-sugar-size/${entry}${minify ? "" : "-unminified"}`, emptyOutDir: true, minify, modulePreload: false,
    rollupOptions: { input: `benchmarks/frameworks/sugar-size-${entry}.mjs`, preserveEntrySignatures: "strict", output: { entryFileNames: "bundle.js", inlineDynamicImports: true } }
  }
});
