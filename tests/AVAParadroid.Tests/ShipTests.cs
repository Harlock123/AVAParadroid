using AVAParadroid.Engine;

namespace AVAParadroid.Tests;

public class ShipTests
{
    private static readonly ShipDef Ship = Shipyard.Build("TEST", 77);

    [Fact]
    public void Every_square_of_every_deck_that_can_be_stood_on_can_be_reached_from_every_other()
    {
        foreach (var d in Ship.Decks)
        {
            var start = d.Lifts[0];
            var seen = new HashSet<(int, int)> { (start.Col, start.Row) };
            var q = new Queue<(int, int)>(seen);
            while (q.Count > 0)
            {
                var (c, r) = q.Dequeue();
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (DeckDef.Walkable(d.At(c + dc, r + dr)) && seen.Add((c + dc, r + dr))) q.Enqueue((c + dc, r + dr));
            }
            for (int c = 0; c < d.Cols; c++)
            for (int r = 0; r < d.Rows; r++)
                if (DeckDef.Walkable(d.Grid[c, r])) Assert.True(seen.Contains((c, r)), $"{d.Name}: {c},{r} cut off");
            foreach (var p in d.Droids) Assert.Contains((p.Col, p.Row), seen);
        }
    }

    [Fact]
    public void Every_deck_has_a_recharger_and_a_lift_on_each_shaft_that_stops_there_and_the_shafts_join_every_deck()
    {
        foreach (var d in Ship.Decks)
        {
            Assert.Equal(Tile.Energizer, d.Grid[d.Energizer.Col, d.Energizer.Row]);
            for (int s = 0; s < Ship.Shafts.Length; s++)
                Assert.Equal(Ship.Stops(s).Contains(d.Index), d.Lifts.Any(l => l.Shaft == s));
        }
        var seen = new HashSet<int> { 0 };
        for (int round = 0; round < 6; round++)
            foreach (var s in Enumerable.Range(0, Ship.Shafts.Length))
                if (Ship.Stops(s).Any(seen.Contains)) foreach (int x in Ship.Stops(s)) seen.Add(x);
        Assert.Equal(Shipyard.DeckCount, seen.Count);
    }

    [Fact]
    public void Deeper_decks_have_more_droids_of_higher_classes_and_the_commander_waits_in_the_reactor()
    {
        for (int i = 0; i < Ship.Decks.Length; i++)
        {
            Assert.Equal(5 + i, Ship.Decks[i].Droids.Count);
            foreach (var p in Ship.Decks[i].Droids.Where(p => p.Number != 920))
                Assert.InRange(p.Number / 100, 1 + i / 2, Math.Min(8, 2 + i));
        }
        Assert.Contains(Ship.Decks[^1].Droids, p => p.Number == 920);
        Assert.DoesNotContain(Ship.Decks[0].Droids, p => p.Number / 100 > 4);
    }

    [Fact]
    public void The_computer_player_clears_the_ship()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "paradroid-ship-" + Guid.NewGuid().ToString("N"))), 5) { Silent = true };
        g.Go();
        g.SetLives(30);
        g.Pilot.Reset();
        int deaths = 0;
        var last = g.Phase;
        for (int t = 0; t < 60 * 1200 && g.World.Round == 1; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) deaths++;
            last = g.Phase;
        }
        Assert.True(g.World.Round == 2, $"{g.World.Left} droids left");
        Assert.True(deaths <= 6, $"{deaths} lost");
    }
}
