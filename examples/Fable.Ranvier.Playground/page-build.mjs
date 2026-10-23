import { developmentPages as sharedDevelopmentPages } from "../page-build.mjs";
export { readPages, createRebuilder } from "../page-build.mjs";

export function developmentPages(root) {
  return sharedDevelopmentPages(root, {
    label: "Playground",
    sourceDirectories: ["."]
  });
}
