using System.Text.Json;

// Offline content-pipeline tool. Generates a multi-country world asset
// (province GeoJSON + country ownership) from public-domain Natural Earth
// admin-1 data. It streams the upstream file with System.Text.Json so the
// ~100 MB 10m dataset parses quickly, unlike PowerShell's ConvertFrom-Json.
//
// Usage (from repo root):
//   dotnet run --project tools/SimEngine.WorldGen -- <outputDir> [sourceFile]
//   dotnet run --project tools/SimEngine.WorldGen -- --all <outputDir> [sourceFile]
//
// Without --all it emits the curated "europe_west_admin1" subset. With --all it
// emits "world_admin1" containing every country present in the source, deriving
// each country's display name from the Natural Earth `admin` property.
//
// If <sourceFile> is omitted it defaults to ne_10m_admin_1_states_provinces.geojson
// in <outputDir>, downloading it from the Natural Earth mirror when absent.

const string UpstreamUrl =
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson";
const long SeedPopulation = 1_000_000;

var positional = new List<string>();
var allCountries = false;
foreach (var arg in args)
{
    if (string.Equals(arg, "--all", StringComparison.OrdinalIgnoreCase))
    {
        allCountries = true;
    }
    else
    {
        positional.Add(arg);
    }
}

var worldId = allCountries ? "world_admin1" : "europe_west_admin1";

// Germany + immediate neighbors, keyed by Natural Earth adm0_a3, insertion-ordered.
// Used only for the curated (non --all) subset.
var curatedCountryNames = new (string Code, string Name)[]
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
var wanted = curatedCountryNames.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);

// Province properties preserved on each feature (superset of the loader's needs:
// name -> name_en -> name_alt -> adm1_code fallback chain, plus province_id/population).
string[] keep = ["name", "name_en", "name_alt", "adm1_code", "admin", "adm0_a3", "iso_3166_2"];

var outputDir = positional.Count > 0 ? positional[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);
var sourceFile = positional.Count > 1
    ? positional[1]
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

// Country display names, insertion-ordered. For the curated subset this is the
// hand-authored list; for --all it is discovered from the data (adm0_a3 -> admin).
var countryOrder = new List<string>();
var countryDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal);
if (!allCountries)
{
    foreach (var (code, name) in curatedCountryNames)
    {
        countryOrder.Add(code);
        countryDisplayNames[code] = name;
    }
}

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
    if (!allCountries && !wanted.Contains(code))
    {
        continue;
    }

    if (allCountries && !countryDisplayNames.ContainsKey(code))
    {
        var displayName = props.TryGetProperty("admin", out var adminEl) && adminEl.ValueKind == JsonValueKind.String
            ? adminEl.GetString()!
            : code;
        countryOrder.Add(code);
        countryDisplayNames[code] = displayName;
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

Console.WriteLine($"Selected {selected.Count} provinces across {countryOrder.Count} countries.");
if (selected.Count == 0)
{
    Console.Error.WriteLine("No provinces matched; aborting without writing.");
    return 1;
}

var geoJsonPath = Path.Combine(outputDir, $"{worldId}.geojson");
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

var countriesPath = Path.Combine(outputDir, $"{worldId}.countries.json");
await using (var countryStream = File.Create(countriesPath))
await using (var writer = new Utf8JsonWriter(countryStream, new JsonWriterOptions { Indented = true }))
{
    writer.WriteStartObject();
    writer.WriteStartArray("countries");
    foreach (var code in countryOrder)
    {
        var name = countryDisplayNames[code];
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
