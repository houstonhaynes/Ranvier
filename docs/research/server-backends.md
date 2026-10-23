# Backend selection for the hydration example

Checked on 2026-10-23. The selection prioritizes recent download activity rather than repository stars or lifetime downloads.

NuGet Trends reports weekly averages of cumulative daily package download counts. Subtracting two weekly points estimates recent download growth; it is not an exact count for a calendar interval or a count of distinct applications. The latest point (week of October 19) is partial. All five series are flat in the week of October 12 and show unusually small changes the preceding week, so isolated weekly movements should not drive the choice.

| Package | Approximate growth, September 21–October 19 | Approximate growth, July 27–October 19 |
|---|---:|---:|
| Giraffe | 49,876 | 276,519 |
| Suave | 5,664 | 35,882 |
| Oxpecker | 3,173 | 13,652 |
| Saturn | 3,134 | 16,183 |
| Falco | 2,125 | 13,999 |

Giraffe leads both windows by a wide margin, making it the primary runnable example. Suave and Oxpecker are provided as equivalent server source. Downloads include automated restores and transitive dependencies; Saturn also builds on Giraffe, so these are ecosystem activity signals rather than mutually exclusive market shares.

Sources:

- [NuGet Trends comparison](https://nugettrends.com/?months=6&ids=Giraffe&ids=Suave&ids=Oxpecker&ids=Saturn&ids=Falco)
- Download history API: `https://nugettrends.com/api/package/history/{package}?months=6`
- [History aggregation implementation](https://github.com/dotnet/nuget-trends/blob/main/src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs), `GetWeeklyDownloadsAsync`
- [Giraffe package](https://www.nuget.org/packages/Giraffe)

The raw responses used for the calculation are in [server-backend-downloads.json](server-backend-downloads.json).
