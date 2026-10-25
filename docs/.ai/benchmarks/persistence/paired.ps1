# Alternates base and head: counter-bench workers (allocations, every scenario) and the Stopwatch probe.
$s = $PSScriptRoot
$rounds = 5
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_TieredPGO = '0'
$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_gcServer = '0'
$env:BENCH_ONLY = 'create,update,chain,cutoff,update-on,derive-on,mvu-dispatch,project-edit,persist-write'
New-Item -ItemType Directory -Force "$s\runs" | Out-Null
function Load { (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average }
"cpu-before: $(Load)%" | Set-Content "$s\runs\cpu.txt"
for ($i = 0; $i -lt $rounds; $i++) {
    foreach ($side in 'base', 'head') {
        dotnet "$s\$side\worker\Ranvier.Counters.dll" --worker "$s\runs\$side-$i.json" --scale 1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$side worker round $i failed" }
        dotnet fsi "$s\timing-$side.fsx" | Set-Content "$s\runs\$side-$i.txt"
        "cpu after $side $i : $(Load)%" | Add-Content "$s\runs\cpu.txt"
    }
}
"cpu-after: $(Load)%" | Add-Content "$s\runs\cpu.txt"
