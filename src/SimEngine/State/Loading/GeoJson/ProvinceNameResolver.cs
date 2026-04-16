using NetTopologySuite.Features;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// Resolves the canonical display name for a Natural Earth admin_1 feature.
/// Tries <c>name</c>, <c>name_en</c>, <c>name_alt</c>, and finally
/// <c>adm1_code</c>. Throws if every candidate is null/empty — that indicates
/// a malformed feature, not something to paper over.
/// </summary>
internal static class ProvinceNameResolver
{
    private static readonly string[] FallbackOrder =
    {
        "name",
        "name_en",
        "name_alt",
        "adm1_code",
    };

    public static string Resolve(IAttributesTable attributes, int featureIndex)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var availableNames = attributes.GetNames();
        var nameSet = new HashSet<string>(availableNames, StringComparer.Ordinal);

        foreach (var key in FallbackOrder)
        {
            if (!nameSet.Contains(key))
            {
                continue;
            }

            var raw = attributes[key];
            if (raw is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        throw new InvalidDataException(
            $"GeoJSON feature at index {featureIndex} has no usable name "
            + "(checked: name, name_en, name_alt, adm1_code).");
    }
}
