import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
import { developmentPages, readPages } from './page-build.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));

export default defineConfig({
  root: here,
  base: './',
  plugins: [developmentPages(here)],
  optimizeDeps: { include: ['animejs'] },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    manifest: true,
    rollupOptions: { input: Object.fromEntries(readPages(here)) }
  }
});
