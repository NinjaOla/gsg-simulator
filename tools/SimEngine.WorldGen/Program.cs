using System.Text.Json;

// Offline content-pipeline tool. Generates a multi-country world asset
// (province GeoJSON + country ownership) from public-domain Natural Earth
// admin-1 data. It streams the upstream file with System.Text.Json so the
// ~100 MB 10m dataset parses quickly, unlike PowerShell's ConvertFrom-Json.
//
// Usage (from repo root):
//   dotnet run --project tools/SimEngine.WorldGen -- <outputDir> [sourceFile]
//
// If <sourceFile> is omitted it defaults to ne_10m_admin_1_states_provinces.geojson
// in <outputDir>, downloading it from the Natural Earth mirror when absent.

const string UpstreamUrl =
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson";
const string WorldId = "europe_west_admin1";
const long SeedPopulation = 1_000_000;

// Germany + immediate neighbors, keyed by Natural Earth adm0_a3, insertion-ordered.
var countryNames = new (string Code, string Name)[]
{
    ("DEU", "Germany"),
    ("FRA", "France"),
    ("POL", "Poland"),
    ("CZE", "Czechia"),
    ("AUT", "Austria"),
    ("CHE", "Switzerland"),
    ("NLD", "Netherlands"),
    ("BEL", "Belgium"),
    ("DNK", "Denmark"),
    ("LUX", "Luxembourg"),
};
var wanted = countryNames.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);

// Province properties preserved on each feature (superset of the loader's needs:
// name -> name_en -> name_alt -> adm1_code fallback chain, plus province_id/population).
string[] keep = ["name", "name_en", "name_alt", "adm1_code", "admin", "adm0_a3", "iso_3166_2"];

var outputDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);
var sourceFile = args.Length > 1
    ? args[1]
    : Path.Combine(outputDir, "ne_10m_admin_1_states_provinces.geojson");

if (!File.Exists(sourceFile))
{
    Console.WriteLine($"Downloading upstream admin-1 data to {sourceFile} ...");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    await using var download = await http.GetStreamAsync(UpstreamUrl).ConfigureAwait(false);
    await using var fileOut = File.Create(sourceFile);
    await download.CopyToAsync(fileOut).ConfigureAwait(false);
}

Console.WriteLine($"Reading {sourceFile} ...");
var bytes = await File.ReadAllBytesAsync(sourceFile).ConfigureAwait(false);
using var doc = JsonDocument.Parse(bytes);

// Materialize matching features (cloned so we can dispose the source document).
var selected = new List<SelectedFeature>();
foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
{
    var props = feature.GetProperty("properties");
    if (!props.TryGetProperty("adm0_a3", out var codeEl) || codeEl.ValueKind != JsonValueKind.String)
    {
        continue;
    }

    var code = codeEl.GetString()!;
    if (!wanted.Contains(code))
    {
        continue;
    }

    var kept = new List<KeyValuePair<string, JsonElement>>();
    foreach (var name in keep)
    {
        if (props.TryGetProperty(name, out var value))
        {
            kept.Add(new(name, value.Clone()));
        }
    }

    var adm1 = props.TryGetProperty("adm1_code", out var adm1El) && adm1El.ValueKind == JsonValueKind.String
        ? adm1El.GetString()!
        : string.Empty;

    selected.Add(new SelectedFeature(code, adm1, kept, feature.GetProperty("geometry").Clone()));
}

// Deterministic ordering by adm1_code, then sequential province_id assignment so
// ProvinceIds stay stable across upstream releases.
selected.Sort((a, b) => string.CompareOrdinal(a.Adm1Code, b.Adm1Code));
for (var i = 0; i < selected.Count; i++)
{
    selected[i].ProvinceId = i + 1;
}

Console.WriteLine($"Selected {selected.Count} provinces across {countryNames.Length} countries.");
if (selected.Count == 0)
{
    Console.Error.WriteLine("No provinces matched; aborting without writing.");
    return 1;
}

var geoJsonPath = Path.Combine(outputDir, $"{WorldId}.geojson");
await using (var geoStream = File.Create(geoJsonPath))
await using (var writer = new Utf8JsonWriter(geoStream))
{
    writer.WriteStartObject();
    writer.WriteString("type", "FeatureCollection");
    writer.WriteStartArray("features");
    foreach (var feature in selected)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");

        writer.WriteStartObject("properties");
        foreach (var (name, value) in feature.Properties)
        {
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }
        writer.WriteNumber("province_id", feature.ProvinceId);
        writer.WriteNumber("population", SeedPopulation);
        writer.WriteEndObject();

        writer.WritePropertyName("geometry");
        feature.Geometry.WriteTo(writer);
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
}

var countriesPath = Path.Combine(outputDir, $"{WorldId}.countries.json");
await using (var countryStream = File.Create(countriesPath))
await using (var writer = new Utf8JsonWriter(countryStream, new JsonWriterOptions { Indented = true }))
{
    writer.WriteStartObject();
    writer.WriteStartArray("countries");
    foreach (var (code, name) in countryNames)
    {
        var owns = selected
            .Where(f => string.Equals(f.Adm0A3, code, StringComparison.Ordinal))
            .Select(f => f.ProvinceId)
            .OrderBy(id => id)
            .ToArray();
        if (owns.Length == 0)
        {
            continue;
        }

        writer.WriteStartObject();
        writer.WriteString("tag", code);
        writer.WriteString("name", name);
        // Heuristic: no city-point join, so the capital defaults to the
        // country's lowest owned province_id. Documented in TestAssets/README.md.
        writer.WriteNumber("capital", owns[0]);
        writer.WriteStartArray("owns");
        foreach (var id in owns)
        {
            writer.WriteNumberValue(id);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
}

Console.WriteLine($"Wrote {geoJsonPath} and {countriesPath}.");
return 0;

internal sealed class SelectedFeature(
    string adm0A3,
    string adm1Code,
    IReadOnlyList<KeyValuePair<string, JsonElement>> properties,
    JsonElement geometry)
{
    public string Adm0A3 { get; } = adm0A3;
    public string Adm1Code { get; } = adm1Code;
    public IReadOnlyList<KeyValuePair<string, JsonElement>> Properties { get; } = properties;
    public JsonElement Geometry { get; } = geometry;
    public int ProvinceId { get; set; }
}
