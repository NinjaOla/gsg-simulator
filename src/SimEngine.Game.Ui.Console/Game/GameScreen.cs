using System.Text;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game.Commands;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Live in-game screen. Redraws whenever the synced session state changes and
/// reads keys without blocking, so the game keeps ticking while the player
/// reads output or types a command.
/// </summary>
internal sealed class GameScreen
{
    private const int EventRows = 6;
    private static readonly TimeSpan FrameWait = TimeSpan.FromMilliseconds(50);

    private readonly GameSession _session;
    private readonly CommandRegistry _registry;
    private readonly StringBuilder _input = new();

    private Mode _mode = Mode.Live;
    private string[] _output = [];
    private int _scroll;
    private string? _message;
    private long _renderedSequence = -1;
    private bool _dirty = true;
    private (int Width, int Height) _size;

    public GameScreen(GameSession session, CommandRegistry registry)
    {
        _session = session;
        _registry = registry;
    }

    private enum Mode
    {
        Live,
        Command,
        Menu,
    }

    public void Run()
    {
        System.Console.CursorVisible = false;
        System.Console.Clear();
        try
        {
            while (!_session.ShouldQuit)
            {
                while (System.Console.KeyAvailable && !_session.ShouldQuit)
                {
                    HandleKey(System.Console.ReadKey(intercept: true));
                    _dirty = true;
                }

                if (_session.ShouldQuit)
                {
                    break;
                }

                if (_session.State.EndReason is not null)
                {
                    _session.ShouldQuit = true;
                    break;
                }

                var sequence = _session.State.Sequence;
                if (_dirty || sequence != _renderedSequence || CurrentSize() != _size)
                {
                    _renderedSequence = sequence;
                    _dirty = false;
                    Render();
                }

                _session.State.WaitForChange(_renderedSequence, FrameWait);
            }
        }
        finally
        {
            System.Console.CursorVisible = true;
            System.Console.Clear();
        }
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        switch (_mode)
        {
            case Mode.Live:
                HandleLiveKey(key);
                break;
            case Mode.Command:
                HandleCommandKey(key);
                break;
            case Mode.Menu:
                HandleMenuKey(key);
                break;
        }
    }

    private void HandleLiveKey(ConsoleKeyInfo key)
    {
        if (key.KeyChar is '+' or '-')
        {
            ChangeSpeed(key.KeyChar == '+' ? +1 : -1);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Spacebar:
                TogglePause();
                return;
            case ConsoleKey.Add or ConsoleKey.OemPlus:
                ChangeSpeed(+1);
                return;
            case ConsoleKey.Subtract or ConsoleKey.OemMinus:
                ChangeSpeed(-1);
                return;
            case ConsoleKey.Enter:
                BeginCommand(string.Empty);
                return;
            case ConsoleKey.Escape:
                _mode = Mode.Menu;
                return;
            case ConsoleKey.PageUp or ConsoleKey.PageDown:
                Scroll(key.Key);
                return;
        }

        // Typing a letter starts a command line with it.
        if (char.IsLetterOrDigit(key.KeyChar))
        {
            BeginCommand(new string(key.KeyChar, 1));
        }
    }

    private void HandleCommandKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                var input = _input.ToString().Trim();
                _mode = Mode.Live;
                _input.Clear();
                if (input.Length > 0)
                {
                    ExecuteCommand(input);
                }

                return;
            case ConsoleKey.Escape:
                _mode = Mode.Live;
                _input.Clear();
                return;
            case ConsoleKey.Backspace:
                if (_input.Length > 0)
                {
                    _input.Length--;
                }

                return;
            case ConsoleKey.PageUp or ConsoleKey.PageDown:
                Scroll(key.Key);
                return;
        }

        if (!char.IsControl(key.KeyChar))
        {
            _input.Append(key.KeyChar);
        }
    }

    private void HandleMenuKey(ConsoleKeyInfo key)
    {
        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 's':
                BeginCommand("save ");
                return;
            case 'l':
                BeginCommand("load ");
                return;
            case 'q':
                _session.ShouldQuit = true;
                return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            _mode = Mode.Live;
        }
    }

    private void BeginCommand(string initial)
    {
        _mode = Mode.Command;
        _input.Clear().Append(initial);
    }

    private void TogglePause()
    {
        switch (_session.State.Stage)
        {
            case SessionStage.Running:
                Send(new PausePlayerCommand(), "Paused.");
                break;
            case SessionStage.Paused:
                Send(new ResumePlayerCommand(), "Resumed.");
                break;
            default:
                _message = "[yellow]The game is not running.[/]";
                break;
        }
    }

    private void ChangeSpeed(int delta)
    {
        var speed = _session.State.Speed + delta;
        if (!GameSettings.IsValidSpeed(speed))
        {
            _message = $"[yellow]Already at the {(delta > 0 ? "fastest" : "slowest")} speed.[/]";
            return;
        }

        Send(new SetSpeedPlayerCommand(speed), $"Speed {speed}.");
    }

    private void Send(PlayerCommand command, string success)
    {
        try
        {
            _session.SendCommand(command);
            _message = $"[green]{Markup.Escape(success)}[/]";
        }
        catch (CommandRejectedException ex)
        {
            _message = $"[red]Rejected:[/] {Markup.Escape(ex.Message)}";
        }
    }

    private void ExecuteCommand(string input)
    {
        _message = null;
        _scroll = 0;
        _output = ConsoleCapture.Run(
            () =>
            {
                AnsiConsole.MarkupLine($"[grey]>[/] {Markup.Escape(input)}");
                try
                {
                    if (!_registry.TryExecute(_session, input))
                    {
                        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(input)}. Type [yellow]help[/].");
                    }
                }
                catch (CommandRejectedException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Rejected:[/] {Markup.Escape(ex.Message)}");
                }
            },
            ContentWidth(CurrentSize().Width));
    }

    private void Scroll(ConsoleKey key)
    {
        var page = Math.Max(1, CurrentSize().Height / 2);
        _scroll = key == ConsoleKey.PageUp
            ? Math.Max(0, _scroll - page)
            : Math.Clamp(_scroll + page, 0, Math.Max(0, _output.Length - 1));
    }

    private void Render()
    {
        var size = CurrentSize();
        if (size != _size)
        {
            System.Console.Clear();
            _size = size;
        }

        var width = ContentWidth(size.Width);
        var height = size.Height;

        var lines = new List<string>(height);
        lines.AddRange(ConsoleCapture.Render(BuildHeader(), width));
        lines.AddRange(ConsoleCapture.Render(BuildEvents(), width).Take(EventRows + 1));

        var bottom = new[] { RenderLine(_message ?? string.Empty, width), BuildPromptLine(width) };
        var outputRows = Math.Max(0, height - lines.Count - bottom.Length - 1);
        var visible = _output.Skip(_scroll).Take(outputRows).ToArray();
        var hidden = _output.Length - _scroll - visible.Length;
        var title = _scroll > 0 || hidden > 0
            ? $"[dim]Output  (line {_scroll + 1}/{_output.Length} · PgUp/PgDn)[/]"
            : "[dim]Output[/]";
        lines.AddRange(ConsoleCapture.Render(new Rule(title).LeftJustified().RuleStyle("dim"), width));
        lines.AddRange(visible);

        while (lines.Count < height - bottom.Length)
        {
            lines.Add(string.Empty);
        }

        lines = [.. lines.Take(height - bottom.Length), .. bottom];

        var frame = new StringBuilder("\u001b[H");
        for (var i = 0; i < lines.Count; i++)
        {
            frame.Append(lines[i]).Append("\u001b[0m\u001b[K");
            if (i < lines.Count - 1)
            {
                frame.Append("\r\n");
            }
        }

        System.Console.Out.Write(frame.ToString());
        System.Console.Out.Flush();

        if (_mode == Mode.Command)
        {
            System.Console.SetCursorPosition(Math.Min(2 + VisibleInput(width).Length, width), height - 1);
            System.Console.CursorVisible = true;
        }
        else
        {
            System.Console.CursorVisible = false;
        }
    }

    private Rows BuildHeader()
    {
        var state = _session.State;
        var stage = state.Stage switch
        {
            SessionStage.Running => "[green]Running[/]",
            SessionStage.Paused => "[yellow]Paused[/]",
            SessionStage.Lobby => "[grey]Lobby[/]",
            _ => "[red]Ended[/]",
        };
        var role = state.HostPlayerId == _session.PlayerId ? "host" : "player";

        return new Rows(
            new Rule($"[bold gold1]{Markup.Escape(_session.WorldName)}[/]").RuleStyle("gold1 dim"),
            new Markup(
                $"[bold]{state.CurrentDate:yyyy-MM-dd}[/]  [dim]tick {state.TickNumber}[/]   {stage}   " +
                $"speed [bold]{state.Speed}[/][dim]/{GameSettings.MaxSpeed}[/]   " +
                $"[dim]{Markup.Escape(_session.PlayerId)} ({role}) · {_session.ProvinceCount} provinces[/]"));
    }

    private Rows BuildEvents()
    {
        var events = _session.EventLog;
        var items = new List<IRenderable> { new Rule("[dim]Events[/]").LeftJustified().RuleStyle("dim") };
        if (events.Count == 0)
        {
            items.Add(new Markup("[dim]No events yet.[/]"));
        }
        else
        {
            items.AddRange(events.Skip(Math.Max(0, events.Count - EventRows)).Select(e => new Text(e)));
        }

        return new Rows(items);
    }

    private string BuildPromptLine(int width) => _mode switch
    {
        Mode.Command => "> " + VisibleInput(width),
        Mode.Menu => RenderLine(
            "[bold]Menu[/]  [yellow]s[/] save  [yellow]l[/] load  [yellow]q[/] quit to main menu  [yellow]Esc[/] back", width),
        _ => RenderLine(
            "[yellow]Space[/] pause/resume  [yellow]+/-[/] speed  [yellow]Enter[/] command  " +
            "[yellow]Esc[/] menu  [yellow]PgUp/PgDn[/] scroll", width),
    };

    /// <summary>Tail of the input buffer that fits after the prompt.</summary>
    private string VisibleInput(int width)
    {
        var max = Math.Max(1, width - 3);
        var text = _input.ToString();
        return text.Length <= max ? text : text[^max..];
    }

    private static string RenderLine(string markup, int width)
    {
        var lines = ConsoleCapture.Render(new Markup(markup), width);
        return lines.Length > 0 ? lines[0] : string.Empty;
    }

    // One column spare so writing a full line never triggers an auto-wrap.
    private static int ContentWidth(int windowWidth) => Math.Max(20, windowWidth - 1);

    private static (int Width, int Height) CurrentSize() =>
        (Math.Max(20, System.Console.WindowWidth), Math.Max(12, System.Console.WindowHeight));
}
