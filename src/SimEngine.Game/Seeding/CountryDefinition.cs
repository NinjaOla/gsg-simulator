using SimEngine.Ids;

namespace SimEngine.Game.Seeding;

public sealed record CountryDefinition(
    string Tag,
    string DisplayName,
    ProvinceId CapitalProvinceId,
    IReadOnlyList<ProvinceId> OwnedProvinceIds);
