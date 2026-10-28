# Ranvier.Dom

DOM contracts shared by Ranvier's browser and native view backends. The source is generated from WebKit's
IDL and the curated native UI scope in `ranvier-org/Ranvier.WebCore`, rather than TypeScript's browser-wide
DOM declarations.

Reference `Ranvier.Dom` for .NET or `Fable.Ranvier.Dom` for a browser build. Both projects compile the same
source file. Browser-only conversion code handles typed unions at the JavaScript boundary. Components use
the same public member names on both targets.

Members outside the generated scope remain available in browser code through an explicit cast to the
corresponding Xantham interface. The generated native report records omitted members and their reasons.
