using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Loading;

namespace SimEngine.Game.Seeding;

/// <summary>
/// Seeds game-layer state (country, population, economy) onto a world that
/// has already been loaded into a <see cref="SimulationState"/> by the engine's
/// geo-JSON loader.
/// </summary>
public static class GameWorldSeeder
{
    private const long InitialPopulation = 1_000_000;
    private const int AnnualGrowthRateE6 = 5_000;   // 0.5 %/year
    private const int ProductionE2 = 100;             // 1.00 units/month per province

    public static void Seed(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SeedProvinceEconomyAndPopulationFromState(state);
        SeedPlaceholderCountry(state);
    }

    public static void Seed(SimulationState state, WorldLoadResult world, string countriesPath)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(countriesPath);

        SeedProvinceEconomyAndPopulationFromSeeds(state, world.Provinces);

        var definitions = CountryDefinitionsLoader.Load(countriesPath);
        if (definitions.Count == 0)
        {
            throw new InvalidDataException($"Countries file '{countriesPath}' contained no countries.");
        }

        SeedCountries(state, definitions);
    }

    private static void SeedProvinceEconomyAndPopulationFromState(SimulationState state)
    {
        foreach (var (provinceId, _) in state.Entities.Query<SimEngine.State.Components.ProvinceComponent>())
        {
            state.Entities.Attach(provinceId, new PopulationComponent(InitialPopulation, AnnualGrowthRateE6));
            state.Entities.Attach(provinceId, new EconomyComponent(ProductionE2));
        }
    }

    private static void SeedProvinceEconomyAndPopulationFromSeeds(
        SimulationState state,
        IReadOnlyList<ProvinceSeed> seeds)
    {
        foreach (var seed in seeds)
        {
            var provinceEntityId = seed.ProvinceId.AsEntity();
            if (!state.Entities.Exists(provinceEntityId))
            {
                throw new InvalidDataException(
                    $"Province {seed.ProvinceId} from world seed did not exist in SimulationState.");
            }

            state.Entities.Attach(provinceEntityId, new PopulationComponent(seed.InitialPopulation, AnnualGrowthRateE6));
            state.Entities.Attach(provinceEntityId, new EconomyComponent(ProductionE2));
        }
    }

    private static void SeedPlaceholderCountry(SimulationState state)
    {
        var country = state.Entities.Create();
        state.Entities.Attach(country, new CountryComponent("DEU", "Germany"));
        state.Entities.Attach(country, new TreasuryComponent(0L));

        foreach (var (provinceId, _) in state.Entities.Query<SimEngine.State.Components.ProvinceComponent>())
        {
            state.Relationships.Add(country, RelationshipLabel.Owns, provinceId);
        }
    }

    private static void SeedCountries(SimulationState state, IReadOnlyList<CountryDefinition> definitions)
    {
        var provinceEntityIds = state.Entities.Query<SimEngine.State.Components.ProvinceComponent>()
            .Select(static x => x.Id)
            .ToHashSet();
        var ownedByCountry = new Dictionary<ProvinceId, string>();

        foreach (var def in definitions)
        {
            if (string.IsNullOrWhiteSpace(def.Tag))
            {
                throw new InvalidDataException("Country definition had an empty tag.");
            }

            if (string.IsNullOrWhiteSpace(def.DisplayName))
            {
                throw new InvalidDataException($"Country '{def.Tag}' had an empty display name.");
            }

            if (def.CapitalProvinceId == ProvinceId.None)
            {
                throw new InvalidDataException($"Country '{def.Tag}' is missing required capital province id.");
            }

            if (def.OwnedProvinceIds.Count == 0)
            {
                throw new InvalidDataException($"Country '{def.Tag}' must own at least one province.");
            }

            if (!def.OwnedProvinceIds.Contains(def.CapitalProvinceId))
            {
                throw new InvalidDataException(
                    $"Country '{def.Tag}' capital {def.CapitalProvinceId} must be listed in owns[].");
            }

            var country = state.Entities.Create();
            state.Entities.Attach(country, new CountryComponent(def.Tag, def.DisplayName));
            state.Entities.Attach(country, new TreasuryComponent(0L));

            foreach (var provinceId in def.OwnedProvinceIds)
            {
                var provinceEntity = provinceId.AsEntity();
                if (!provinceEntityIds.Contains(provinceEntity))
                {
                    throw new InvalidDataException(
                        $"Country '{def.Tag}' references unknown province {provinceId}.");
                }

                if (ownedByCountry.TryGetValue(provinceId, out var previousOwnerTag))
                {
                    throw new InvalidDataException(
                        $"Province {provinceId} is owned by both '{previousOwnerTag}' and '{def.Tag}'.");
                }

                ownedByCountry.Add(provinceId, def.Tag);
                state.Relationships.Add(country, RelationshipLabel.Owns, provinceEntity);
            }

            state.Relationships.Add(country, RelationshipLabel.HasCapital, def.CapitalProvinceId.AsEntity());
        }
    }
}
