import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";
import { developmentPages, readPages } from "./page-build.mjs";

const root = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  root,
  base: "./",
  plugins: [developmentPages(root)],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    manifest: true,
    rollupOptions: { input: Object.fromEntries(readPages(root)) }
  }
});
