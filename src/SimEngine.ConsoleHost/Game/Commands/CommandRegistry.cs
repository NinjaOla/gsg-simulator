namespace SimEngine.ConsoleHost.Game.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _map;

    public IReadOnlyList<ICommand> All { get; }

    public CommandRegistry(IEnumerable<ICommand> commands)
    {
        All = commands.ToList();
        _map = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in All)
        {
            _map[cmd.Name] = cmd;
            foreach (var alias in cmd.Aliases)
                _map[alias] = cmd;
        }
    }

    public bool TryExecute(GameSession session, string input)
    {
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        if (!_map.TryGetValue(parts[0], out var cmd))
            return false;

        cmd.Execute(session, parts[1..]);
        return true;
    }
}
