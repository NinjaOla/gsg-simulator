using System.Text.Json;
using SimEngine.Ids;
using SimEngine.Random;
using SimEngine.State.Components;
using SimEngine.Systems;

namespace SimEngine.State.Serialization;

internal static class SimulationSaveSerializer
{
    internal const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void Save(SimulationEngine engine, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(stream));
        }

        var saveFile = CreateSaveFile(engine);
        JsonSerializer.Serialize(stream, saveFile, JsonOptions);
    }

    public static SimulationEngine Load(
        Stream stream,
        IEnumerable<ISimulationSystem> systems,
        IReadOnlyList<IComponentSectionCodec>? componentCodecs)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(systems);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(stream));
        }

        var systemList = systems.ToArray();
        var codecs = componentCodecs ?? [];
        SimulationSaveFile? saveFile;

        try
        {
            saveFile = JsonSerializer.Deserialize<SimulationSaveFile>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Save file JSON is invalid.", ex);
        }

        if (saveFile is null)
        {
            throw new InvalidDataException("Save file was empty.");
        }

        var allowedSectionTypes = BuildAllowedSectionTypes(codecs);
        ValidateSaveFile(saveFile, systemList, allowedSectionTypes);

        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = saveFile.Engine.StartDate,
                Seed = saveFile.Engine.Seed,
                DefaultTickDelta = TimeSpan.FromTicks(saveFile.Engine.DefaultTickDeltaTicks),
                EnableParallelBatches = saveFile.Engine.EnableParallelBatches,
                MaxDegreeOfParallelism = saveFile.Engine.MaxDegreeOfParallelism,
                ComponentCodecs = codecs,
                InitialState = RestoreState(saveFile.Engine.State, codecs),
            },
            systemList);

        engine.RestoreRuntime(
            saveFile.Engine.CurrentTime,
            saveFile.Engine.PreviousTick,
            saveFile.Engine.TickNumber,
            saveFile.Engine.RootRandom,
            saveFile.Engine.SystemRandoms);

        return engine;
    }

    private static SimulationSaveFile CreateSaveFile(SimulationEngine engine)
    {
        var entities = engine.State.Entities.All
            .Select(id => new EntitySnapshot(id.Value))
            .ToArray();

        var provinceComponents = engine.State.Entities.Query<ProvinceComponent>()
            .Select(entry => new ProvinceComponentEntrySnapshot(
                entry.Id.Value,
                entry.Component.Name,
                (int)entry.Component.Terrain,
                entry.Component.CentroidLatE6,
                entry.Component.CentroidLonE6))
            .ToArray();

        var relationships = engine.State.Relationships.EnumerateAll()
            .Select(edge => new RelationshipSnapshot(edge.From.Value, (int)edge.Label, edge.To.Value))
            .ToArray();

        var adjacency = provinceComponents
            .Select(entry => new AdjacencyNodeSnapshot(
                entry.EntityId,
                engine.State.Adjacency
                    .NeighborsOf(new ProvinceId(entry.EntityId))
                    .Select(neighbor => neighbor.Value)
                    .ToArray()))
            .ToArray();

        var builtinSection = new ComponentSectionSnapshot(
            SimulationComponentSections.ProvinceComponent,
            JsonSerializer.SerializeToElement(provinceComponents, JsonOptions));

        var codecSections = engine.Options.ComponentCodecs
            .Select(codec => new ComponentSectionSnapshot(
                codec.SectionType,
                codec.WriteSection(engine.State, JsonOptions)))
            .ToArray();

        var componentSections = new[] { builtinSection }
            .Concat(codecSections)
            .OrderBy(s => s.SectionType, StringComparer.Ordinal)
            .ToArray();

        var saveFile = new SimulationSaveFile(
            CurrentFormatVersion,
            new EngineSnapshot(
                engine.Options.StartDate,
                engine.Time.Current,
                engine.Time.PreviousTick,
                engine.TickNumber,
                engine.Options.DefaultTickDelta.Ticks,
                engine.Options.Seed,
                engine.Options.EnableParallelBatches,
                engine.Options.MaxDegreeOfParallelism,
                ToSnapshot(engine.SnapshotRootRandomState()),
                engine.SnapshotSystemRandomStates()
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new SystemRandomSnapshot(entry.Key, ToSnapshot(entry.Value)))
                    .ToArray(),
                new SimulationStateSnapshot(
                    entities,
                    relationships,
                    adjacency,
                    componentSections)));

        var allowedSectionTypes = BuildAllowedSectionTypes(engine.Options.ComponentCodecs);
        ValidateSaveFile(saveFile, engine.SystemKeys, allowedSectionTypes);
        return saveFile;
    }

    private static SimulationState RestoreState(
        SimulationStateSnapshot snapshot,
        IReadOnlyList<IComponentSectionCodec> codecs)
    {
        var state = new SimulationState();

        foreach (var entity in snapshot.Entities)
        {
            state.Entities.Create(new EntityId(entity.Id));
        }

        foreach (var province in GetProvinceComponents(snapshot.ComponentSections))
        {
            state.Entities.Attach(
                new EntityId(province.EntityId),
                new ProvinceComponent(
                    province.Name,
                    (Terrain)province.Terrain,
                    province.CentroidLatE6,
                    province.CentroidLonE6));
        }

        if (codecs.Count > 0)
        {
            var sectionsByType = snapshot.ComponentSections
                .ToDictionary(s => s.SectionType, s => s, StringComparer.Ordinal);

            foreach (var codec in codecs)
            {
                if (sectionsByType.TryGetValue(codec.SectionType, out var section))
                {
                    codec.ReadSection(state, section.Payload, JsonOptions);
                }
            }
        }

        foreach (var relationship in snapshot.Relationships)
        {
            state.Relationships.Add(
                new EntityId(relationship.FromEntityId),
                (RelationshipLabel)relationship.Label,
                new EntityId(relationship.ToEntityId));
        }

        var adjacencyBuilder = new AdjacencyGraph.Builder();
        foreach (var node in snapshot.Adjacency)
        {
            var provinceId = new ProvinceId(node.ProvinceId);
            adjacencyBuilder.AddProvince(provinceId);

            foreach (var neighbor in node.Neighbors)
            {
                if (neighbor > node.ProvinceId)
                {
                    adjacencyBuilder.AddUndirectedEdge(provinceId, new ProvinceId(neighbor));
                }
            }
        }

        state.Adjacency = adjacencyBuilder.Build();
        return state;
    }

    private static ProvinceComponentEntrySnapshot[] GetProvinceComponents(ComponentSectionSnapshot[] componentSections)
    {
        var section = componentSections.Single(entry =>
            string.Equals(entry.SectionType, SimulationComponentSections.ProvinceComponent, StringComparison.Ordinal));

        try
        {
            return section.Payload.Deserialize<ProvinceComponentEntrySnapshot[]>(JsonOptions)
                   ?? throw new InvalidDataException("Province component section payload was missing.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Province component section payload was invalid.", ex);
        }
    }

    private static void ValidateSaveFile(
        SimulationSaveFile saveFile,
        IEnumerable<ISimulationSystem> systems,
        IReadOnlyCollection<string> allowedSectionTypes)
    {
        ValidateSaveFile(saveFile, systems.Select(system => system.Key), allowedSectionTypes);
    }

    private static void ValidateSaveFile(
        SimulationSaveFile saveFile,
        IEnumerable<string> expectedSystemKeys,
        IReadOnlyCollection<string> allowedSectionTypes)
    {
        if (saveFile.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"Unsupported save format version {saveFile.FormatVersion}.");
        }

        var engine = saveFile.Engine ?? throw new InvalidDataException("Save file engine payload was missing.");
        if (engine.TickNumber < 0)
        {
            throw new InvalidDataException("Tick number cannot be negative.");
        }

        if (engine.DefaultTickDeltaTicks <= 0)
        {
            throw new InvalidDataException("Default tick delta must be positive.");
        }

        if (engine.MaxDegreeOfParallelism is <= 0)
        {
            throw new InvalidDataException("MaxDegreeOfParallelism must be positive when present.");
        }

        if (engine.PreviousTick > engine.CurrentTime)
        {
            throw new InvalidDataException("PreviousTick cannot be later than CurrentTime.");
        }

        ValidateRandomSnapshot(engine.RootRandom, "root RNG");

        var expectedKeys = expectedSystemKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var savedKeys = engine.SystemRandoms
            .Select(snapshot => snapshot.SystemKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (!expectedKeys.SequenceEqual(savedKeys, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Save file system RNGs do not match the registered systems.");
        }

        EnsureUnique(engine.SystemRandoms.Select(snapshot => snapshot.SystemKey), "Duplicate system RNG snapshots are not allowed.");
        EnsureAscending(engine.SystemRandoms.Select(snapshot => snapshot.SystemKey), StringComparer.Ordinal, "System RNG snapshots must be sorted by system key.");

        foreach (var systemRandom in engine.SystemRandoms)
        {
            if (string.IsNullOrWhiteSpace(systemRandom.SystemKey))
            {
                throw new InvalidDataException("System RNG snapshots require a system key.");
            }

            ValidateRandomSnapshot(systemRandom.Random, $"system RNG '{systemRandom.SystemKey}'");
        }

        ValidateState(engine.State, allowedSectionTypes);
    }

    private static void ValidateState(SimulationStateSnapshot snapshot, IReadOnlyCollection<string> allowedSectionTypes)
    {
        var entities = snapshot.Entities ?? throw new InvalidDataException("Entity snapshot section was missing.");
        var relationships = snapshot.Relationships ?? throw new InvalidDataException("Relationship snapshot section was missing.");
        var adjacency = snapshot.Adjacency ?? throw new InvalidDataException("Adjacency snapshot section was missing.");
        var componentSections = snapshot.ComponentSections ?? throw new InvalidDataException("Component section snapshot was missing.");

        EnsureAscending(entities.Select(entity => entity.Id), Comparer<uint>.Default, "Entity ids must be sorted ascending.");
        EnsureUnique(entities.Select(entity => entity.Id), "Duplicate entity ids are not allowed.");

        var entityIds = new HashSet<uint>();
        foreach (var entity in entities)
        {
            if (entity.Id == 0)
            {
                throw new InvalidDataException("Entity id 0 is not allowed.");
            }

            entityIds.Add(entity.Id);
        }

        EnsureAscending(
            relationships.Select(relationship => (relationship.FromEntityId, relationship.Label, relationship.ToEntityId)),
            Comparer<(uint FromEntityId, int Label, uint ToEntityId)>.Default,
            "Relationships must be sorted by from entity, label, and to entity.");
        EnsureUnique(relationships.Select(relationship => (relationship.FromEntityId, relationship.Label, relationship.ToEntityId)), "Duplicate relationships are not allowed.");

        foreach (var relationship in relationships)
        {
            if (relationship.FromEntityId == 0 || relationship.ToEntityId == 0)
            {
                throw new InvalidDataException("Relationship endpoints must be non-zero entity ids.");
            }

            if (relationship.Label < 0)
            {
                throw new InvalidDataException("Relationship labels must be non-negative.");
            }

            if (!entityIds.Contains(relationship.FromEntityId) || !entityIds.Contains(relationship.ToEntityId))
            {
                throw new InvalidDataException("Relationships cannot reference missing entities.");
            }
        }

        EnsureAscending(componentSections.Select(section => section.SectionType), StringComparer.Ordinal, "Component sections must be sorted by section type.");
        EnsureUnique(componentSections.Select(section => section.SectionType), "Duplicate component sections are not allowed.");

        foreach (var section in componentSections)
        {
            if (!allowedSectionTypes.Contains(section.SectionType))
            {
                throw new InvalidDataException($"Unknown component section '{section.SectionType}'.");
            }
        }

        var provinceComponents = GetProvinceComponents(componentSections);
        EnsureAscending(provinceComponents.Select(component => component.EntityId), Comparer<uint>.Default, "Province component entries must be sorted by entity id.");
        EnsureUnique(provinceComponents.Select(component => component.EntityId), "Duplicate province component entries are not allowed.");

        var provinceIds = new HashSet<uint>();
        foreach (var province in provinceComponents)
        {
            if (!entityIds.Contains(province.EntityId))
            {
                throw new InvalidDataException("Province components must reference existing entities.");
            }

            if (string.IsNullOrWhiteSpace(province.Name))
            {
                throw new InvalidDataException("Province component names cannot be blank.");
            }

            if (!Enum.IsDefined((Terrain)province.Terrain))
            {
                throw new InvalidDataException($"Invalid terrain value {province.Terrain}.");
            }

            provinceIds.Add(province.EntityId);
        }

        EnsureAscending(adjacency.Select(node => node.ProvinceId), Comparer<uint>.Default, "Adjacency entries must be sorted by province id.");
        EnsureUnique(adjacency.Select(node => node.ProvinceId), "Duplicate adjacency entries are not allowed.");

        var adjacencyMap = new Dictionary<uint, uint[]>();
        foreach (var node in adjacency)
        {
            if (!provinceIds.Contains(node.ProvinceId))
            {
                throw new InvalidDataException("Adjacency entries must reference province entities with ProvinceComponent.");
            }

            EnsureAscending(node.Neighbors, Comparer<uint>.Default, "Adjacency neighbor lists must be sorted ascending.");
            EnsureUnique(node.Neighbors, "Adjacency neighbor lists cannot contain duplicates.");

            foreach (var neighbor in node.Neighbors)
            {
                if (neighbor == 0)
                {
                    throw new InvalidDataException("Adjacency neighbor ids must be non-zero.");
                }

                if (neighbor == node.ProvinceId)
                {
                    throw new InvalidDataException("Adjacency graph cannot contain self loops.");
                }

                if (!provinceIds.Contains(neighbor))
                {
                    throw new InvalidDataException("Adjacency neighbor lists cannot reference missing provinces.");
                }
            }

            adjacencyMap[node.ProvinceId] = node.Neighbors;
        }

        if (adjacencyMap.Count != provinceIds.Count)
        {
            throw new InvalidDataException("Every province component must have an adjacency entry.");
        }

        foreach (var node in adjacency)
        {
            foreach (var neighbor in node.Neighbors)
            {
                if (!adjacencyMap.TryGetValue(neighbor, out var reverseNeighbors)
                    || Array.BinarySearch(reverseNeighbors, node.ProvinceId) < 0)
                {
                    throw new InvalidDataException("Adjacency graph must be symmetric.");
                }
            }
        }
    }

    private static HashSet<string> BuildAllowedSectionTypes(IReadOnlyList<IComponentSectionCodec> codecs)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            SimulationComponentSections.ProvinceComponent,
        };

        foreach (var codec in codecs)
        {
            allowed.Add(codec.SectionType);
        }

        return allowed;
    }

    private static RandomSnapshot ToSnapshot((ulong s0, ulong s1, ulong s2, ulong s3) state)
        => new(state.s0, state.s1, state.s2, state.s3);

    private static void ValidateRandomSnapshot(RandomSnapshot snapshot, string name)
    {
        try
        {
            var rng = new Xoshiro256StarStar(0);
            rng.RestoreState(snapshot.S0, snapshot.S1, snapshot.S2, snapshot.S3);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Invalid {name} snapshot.", ex);
        }
    }

    private static void EnsureAscending<T>(IEnumerable<T> values, IComparer<T> comparer, string message)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return;
        }

        var previous = enumerator.Current;
        while (enumerator.MoveNext())
        {
            if (comparer.Compare(previous, enumerator.Current) >= 0)
            {
                throw new InvalidDataException(message);
            }

            previous = enumerator.Current;
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string message)
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
