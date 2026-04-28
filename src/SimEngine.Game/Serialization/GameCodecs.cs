using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public static class GameCodecs
{
    public static IReadOnlyList<IComponentSectionCodec> All { get; } =
    [
        new CountryComponentCodec(),
        new EconomyComponentCodec(),
        new PopulationComponentCodec(),
        new TreasuryComponentCodec(),
    ];
}
