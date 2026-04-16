# Test Assets

GeoJSON files used by the loader test suite. All files are public-domain
Natural Earth data, trimmed to the property fields the loader actually
consults so we don't ship unrelated metadata in the repo.

## `grid4.geojson`

Handcrafted 2x2 grid of unit squares, four features (`A`, `B`, `C`, `D`).
Drives most loader unit tests: it has hand-computable centroids, four
shared edges, and zero corner-only contacts. Edit by hand if you change
the grid layout.

## `germany_admin1.geojson`

The 16 Bundesländer of Germany, derived from
[`ne_10m_admin_1_states_provinces.geojson`](https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_10m_admin_1_states_provinces.geojson)
in the public-domain Natural Earth v5.x dataset. Used by
`GeoJsonWorldLoaderIntegrationTests` to exercise the loader on real-world
polygon data with non-watertight borders, MultiPolygon islands, and the
typical coordinate density of admin_1 features.

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
[ordered]@{ type='FeatureCollection'; features=$out } |
  ConvertTo-Json -Depth 100 -Compress |
  Set-Content -LiteralPath germany_admin1.geojson -NoNewline
```

Equivalent ogr2ogr command (if GDAL is available):

```bash
ogr2ogr -f GeoJSON germany_admin1.geojson admin1.geojson -where "admin='Germany'"
```

The reproduction sorts features by `adm1_code` so the resulting
`ProvinceId` assignments are stable across upstream releases (they would
otherwise track Natural Earth's source-file order, which is not a
documented stability contract).
