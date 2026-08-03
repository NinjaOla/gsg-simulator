using System.Text.Json;
using SimEngine.Ids;

namespace SimEngine.Game.Seeding;

internal static class CountryDefinitionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<CountryDefinition> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var root = JsonSerializer.Deserialize<CountryDefinitionsRootDto>(
            stream,
            SerializerOptions)
            ?? throw new InvalidDataException($"Countries file '{path}' was empty.");
        if (root.Countries is null)
        {
            throw new InvalidDataException($"Countries file '{path}' did not contain a 'countries' array.");
        }

        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<CountryDefinition>(root.Countries.Count);
        foreach (var dto in root.Countries)
        {
            if (dto is null)
            {
                throw new InvalidDataException($"Countries file '{path}' contained a null country entry.");
            }

            if (string.IsNullOrWhiteSpace(dto.Tag))
            {
                throw new InvalidDataException($"Countries file '{path}' had a country with an empty tag.");
            }

            if (!seenTags.Add(dto.Tag))
            {
                throw new InvalidDataException($"Countries file '{path}' contains duplicate country tag '{dto.Tag}'.");
            }

            if (dto.Capital is null || dto.Capital <= 0)
            {
                throw new InvalidDataException(
                    $"Country '{dto.Tag}' in '{path}' must define a capital province id (> 0).");
            }

            if (dto.Owns is null)
            {
                throw new InvalidDataException($"Country '{dto.Tag}' in '{path}' is missing owns[].");
            }

            var ownedProvinceIds = dto.Owns.Select(static id =>
            {
                if (id <= 0)
                {
                    throw new InvalidDataException($"Province ids in owns[] must be >= 1 (got {id}).");
                }

                return new ProvinceId((uint)id);
            }).ToArray();

            output.Add(new CountryDefinition(
                dto.Tag,
                dto.Name ?? dto.Tag,
                new ProvinceId((uint)dto.Capital.Value),
                ownedProvinceIds));
        }

        return output;
    }

    private sealed record CountryDefinitionsRootDto(List<CountryDefinitionDto>? Countries);

    private sealed record CountryDefinitionDto(
        string? Tag,
        string? Name,
        int? Capital,
        int[]? Owns);
}
