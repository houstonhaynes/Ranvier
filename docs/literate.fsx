// Build the traced dependencies with: dotnet build docs/maps/model/Ranvier.Docs.MapModel.fsproj -p:RanvierTrace=true
#r "nuget: Fable.Core, 5.3.0"
#r "nuget: Partas.Solid, 3.0.0-local.e08ad85"
#r "nuget: Xantham.Fable.Core.TS, 0.2.1"
#r "maps/model/bin/Debug/net10.0/Ranvier.dll"
#r "maps/model/bin/Debug/net10.0/Ranvier.Docs.MapModel.dll"
// The animation bindings load as source, against the traced Ranvier.dll above.
#load "../src/Fable.Ranvier.Anime/Values.fs"
#load "../src/Fable.Ranvier.Anime/Tickables.fs"
#load "../src/Fable.Ranvier.Anime/Params.fs"
#load "../src/Fable.Ranvier.Anime/Anime.fs"
#load "../src/Fable.Ranvier.Anime/Reactive.fs"
#load "maps/SignalMap.fs"
