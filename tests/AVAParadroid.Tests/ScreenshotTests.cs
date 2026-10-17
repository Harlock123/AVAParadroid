using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAParadroid.Engine;
using AVAParadroid.Views;
using Controls = AVAParadroid.Engine.Controls;

namespace AVAParadroid.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PARADROID_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaparadroid-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PARADROID_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAParadroid-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(3.4);
        Shot(w, c, "title");
        g.HighScores.Insert("LNK", 98400, 12);
        g.HighScores.Insert("AVA", 41200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(string name, Func<GameEngine, bool> moment, int seconds = 600)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(30);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_first_deck() => Play("deck", g => g.Phase == Phase.Play && g.World.Player.Transfer > 0 && g.World.Here.Count >= 3 && g.World.Here.Any(x => Sim.Dist(x.X, x.Y, g.World.Player.X, g.World.Player.Y) < 260));

    [AvaloniaFact]
    public void A_duel() => Play("duel", g => g.Phase == Phase.Duel && g.Duel!.Clock > 8.6 && g.Duel.Flying.Count > 0);

    [AvaloniaFact]
    public void Transfer_complete() => Play("taken", g => g.Phase == Phase.DuelOver && g.DuelWon && g.PhaseTime > 0.4);

    [AvaloniaFact]
    public void A_gunfight() => Play("gunfight", g => g.Phase == Phase.Play && g.World.Bullets.Count(b => b.FromPlayer) >= 1 && g.World.Bullets.Count(b => !b.FromPlayer) >= 1);

    [AvaloniaFact]
    public void The_lift() => Play("lift", g => g.Phase == Phase.Lift && g.World.DeckIndex >= 1 && g.PhaseTime > 0.2);

    [AvaloniaFact]
    public void Recharging() => Play("recharge", g => g.Phase == Phase.Play && g.World.Player.Charging > 0 && g.World.Player.Number != 1 && g.World.DeckIndex >= 1);

    [AvaloniaFact]
    public void A_cleared_deck() => Play("cleared", g => g.Phase == Phase.Play && g.World.Cleared(g.World.DeckIndex) && g.World.DeckIndex >= 1 && g.Message == null);

    [AvaloniaFact]
    public void Deep_in_the_ship() => Play("machinery", g => g.Phase == Phase.Play && g.World.DeckIndex == 4 && g.World.Here.Count >= 5 && g.World.Here.Any(x => x.Class >= 5 && Sim.Dist(x.X, x.Y, g.World.Player.X, g.World.Player.Y) < 300));

    [AvaloniaFact]
    public void The_commander() => Play("commander", g => g.Phase == Phase.Play && g.World.Here.Any(x => x.Number == 920 && Sim.Dist(x.X, x.Y, g.World.Player.X, g.World.Player.Y) < 330), 1100);

    [AvaloniaFact]
    public void Host_lost() => Play("host-lost", g => g.World.Effects.Any(e => e.Text == "HOST LOST" && e.Age > 0.2 && e.Age < 0.5), 1100);
}
