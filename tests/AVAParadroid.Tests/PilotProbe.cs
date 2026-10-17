using AVAParadroid.Engine;

namespace AVAParadroid.Tests;

/// <summary>
/// How the computer player gets on clearing the ship (set PARADROID_PROBE to run it, for tuning: "1" for the
/// pilot, "lazy" for a player who only wanders on and fires).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        string? mode = Environment.GetEnvironmentVariable("PARADROID_PROBE");
        if (mode is null) return;
        var seeds = (Environment.GetEnvironmentVariable("PARADROID_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "paradroid-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.SetLives(30);
            g.Pilot.Reset();
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            int t = 0, duels = 0, won = 0, hostsLost = 0;
            var last = g.Phase;
            int lastNumber = 1;
            int lastLeft = g.World.Left;
            for (; t < 60 * 1200 && g.State == GameState.Playing && g.World.Round == 1; t++)
            {
                var c = mode == "lazy" ? new Controls(t / 120 % 4 == 0 ? 1 : t / 120 % 4 == 2 ? -1 : 0, t / 120 % 4 == 1 ? 1 : t / 120 % 4 == 3 ? -1 : 0, t % 30 < 10, false) : g.Pilot.Decide(g);
                g.Tick(c);
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.DuelOver && last != Phase.DuelOver) { duels++; if (g.DuelWon) won++; }
                if (g.Phase == Phase.Dying && last != Phase.Dying) { var key = $"{g.World.Cause} on deck {g.World.DeckIndex + 1}"; deaths[key] = deaths.GetValueOrDefault(key) + 1; }
                if (lastNumber != 1 && g.World.Player.Number == 1 && g.Phase != Phase.Dying) hostsLost++;
                lastNumber = g.World.Player.Number;
                if (g.World.Left < lastLeft) lastLeft = g.World.Left;
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: {(g.World.Round > 1 ? "SHIP CLEARED" : "not cleared")} after {t / 60}s, {deaths.Values.Sum()} deaths, {g.World.Left} droids left, duels {won}/{duels} won, hosts lost {hostsLost}, score {g.Score}, deck {g.World.DeckIndex + 1}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
