# Test Assets

GeoJSON files used by the loader test suite. All files are public-domain
Natural Earth data (or synthetic test geometry) plus authored game-seeding
properties used by Phase A country/province data loading.

## `grid4.geojson`

Handcrafted 2x2 grid of unit squares, four features (`A`, `B`, `C`, `D`).
Drives most loader unit tests: it has hand-computable centroids, four
shared edges, and zero corner-only contacts. Edit by hand if you change
the grid layout.

Each feature also carries:
- `province_id` (stable authored ProvinceId)
- `population` (initial province population for game seeding)

## `germany_admin1.geojson`

The 16 Bundesländer of Germany, derived from
[`ne_10m_admin_1_states_provinces.geojson`](https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_10m_admin_1_states_provinces.geojson)
in the public-domain Natural Earth v5.x dataset. Used by
`GeoJsonWorldLoaderIntegrationTests` to exercise the loader on real-world
polygon data with non-watertight borders, MultiPolygon islands, and the
typical coordinate density of admin_1 features.

Each feature now includes:
- `province_id` (stable authored ProvinceId)
- `population` (initial province population)

## Countries files

`grid4.countries.json` and `germany_admin1.countries.json` define country
ownership/capital data consumed by `GameWorldSeeder` in Phase A.

### Reproduction

To regenerate this file from the upstream source (e.g. after a Natural
Earth release bumps coordinates), run from a working directory that has
the full upstream file as `admin1.geojson`:

```powershell
$keep = @('name','name_en','name_alt','adm1_code','admin','iso_3166_2')
$fc = Get-Content -Raw -LiteralPath admin1.geojson | ConvertFrom-Json -Depth 100
$out = foreach ($f in $fc.features) {
  if ($f.properties.admin -ne 'Germany') { continue }
  $props = [ordered]@{}
  foreach ($k in $keep) {
    if ($f.properties.PSObject.Properties.Name -contains $k) {
      $props[$k] = $f.properties.$k
    }
  }
  [ordered]@{ type='Feature'; properties=$props; geometry=$f.geometry }
}
$out = @($out | Sort-Object { $_.properties.adm1_code })
$i = 1
foreach ($feature in $out) {
  $feature.properties.province_id = $i
  $feature.properties.population = 1000000
  $i++
}
[ordered]@{ type='FeatureCollection'; features=$out } |
  ConvertTo-Json -Depth 100 -Compress |
  Set-Content -LiteralPath germany_admin1.geojson -NoNewline
```

Equivalent ogr2ogr command (if GDAL is available):

```bash
ogr2ogr -f GeoJSON germany_admin1.geojson admin1.geojson -where "admin='Germany'"
```

The reproduction sorts features by `adm1_code` and then assigns
`province_id` sequentially so ProvinceIds remain stable across upstream
releases.
