// The five pages and the index as one multi-page app. The story page is served as the document the .NET server rendered
// with Server.renderDocument (npm run server writes generated-server/story.html), so the client hydrates it; without that
// file the page's own index.html is served and the story mounts on the client. Only npm run server writes the render:
// after an edit to src/Story.fs, re-run it, or strict hydration reports the stale document.
import { defineConfig } from "vite";
import { existsSync, readFileSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = fileURLToPath(new URL(".", import.meta.url));
const pages = ["kanban", "dashboard", "story", "physics", "routes"].filter(page => existsSync(`${here}${page}/index.html`));
const served = new URL("./generated-server/story.html", import.meta.url);
const source = new URL("./src/Story.fs", import.meta.url);

const serverRender = {
  name: "ranvier-server-render",
  transformIndexHtml: {
    order: "pre",
    handler(html, context) {
      if (!context.path.includes("/story/") || !existsSync(served)) return html;
      if (statSync(served).mtimeMs < statSync(source).mtimeMs)
        console.warn("generated-server/story.html is older than src/Story.fs: run npm run server");
      return readFileSync(served, "utf8");
    }
  }
};

export default defineConfig({
  root: here,
  plugins: [serverRender],
  // Pre-bundled at start, so the dev server does not reload a page mid-test when it discovers the dependency.
  optimizeDeps: { include: ["animejs"] },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    manifest: true,
    rollupOptions: {
      input: Object.fromEntries([["index", `${here}index.html`], ...pages.map(page => [page, `${here}${page}/index.html`])])
    }
  }
});
