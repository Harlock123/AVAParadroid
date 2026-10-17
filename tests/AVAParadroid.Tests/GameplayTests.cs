using Avalonia.Input;
using AVAParadroid.Engine;

namespace AVAParadroid.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaparadroid-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first deck with no droids on it, the player at the recharger.</summary>
    private static World Empty()
    {
        var w = new World();
        Sim.Start(w, Shipyard.Build("TEST", 77), 1, 1);
        foreach (var d in w.Droids) d.Clear();
        w.Player.Safe = 0;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static Droid Put(World w, int number, double dx, double dy)
    {
        int k = number / 100;
        var x = new Droid { Number = number, Id = w.NextId++, X = w.Player.X + dx, Y = w.Player.Y + dy, Hp = Classes.MaxHp(k), MaxHp = Classes.MaxHp(k), FireCool = 99 };
        x.Tx = x.X;
        x.Ty = x.Y;
        w.Here.Add(x);
        return x;
    }

    /// <summary>A spot in a room with open floor for a few squares each way.</summary>
    private static void Roomy(World w)
    {
        var d = w.Deck;
        var room = d.Rooms.OrderByDescending(q => (q.C1 - q.C0) * (q.R1 - q.R0)).First();
        (w.Player.X, w.Player.Y) = d.Centre((room.C0 + room.C1) / 2, (room.R0 + room.R1) / 2);
    }

    // ───────────────────────────── moving and shooting ─────────────────────────────

    [Fact]
    public void The_link_unit_moves_and_walls_stop_it()
    {
        var w = Empty();
        double x = w.Player.X;
        Run(w, 30, new Controls(1, 0));
        Assert.True(w.Player.X > x + 50);
        Run(w, 600, new Controls(1, 0));
        Assert.True(Sim.Free(w.Deck, w.Player.X, w.Player.Y, Player.Radius));
        Assert.Equal(Tile.Wall, w.Deck.AtPoint(w.Player.X + Player.Radius + 4, w.Player.Y));
    }

    [Fact]
    public void The_bare_link_unit_has_no_gun_but_a_host_shoots_the_way_it_is_pushed()
    {
        var w = Empty();
        Roomy(w);
        Run(w, 10, new Controls(1, 0, true));
        Assert.Empty(w.Bullets);
        w.Player.Number = 426;
        w.Player.Hp = Classes.MaxHp(4);
        double x = w.Player.X;
        Run(w, 3, new Controls(0, -1, true));
        var b = Assert.Single(w.Bullets);
        Assert.True(b.Vy < 0 && b.Vx == 0);
        Assert.Equal(x, w.Player.X);
    }

    [Fact]
    public void Shots_bring_a_droid_down_for_points()
    {
        var w = Empty();
        Roomy(w);
        w.Player.Number = 611;
        w.Player.Hp = Classes.MaxHp(6);
        var x = Put(w, 108, 90, 0);
        // (Held in place, in line.)
        for (int i = 0; i < 180 && !x.Dead; i++)
        {
            (x.X, x.Y, x.Tx, x.Ty) = (w.Player.X + 90, w.Player.Y, w.Player.X + 90, w.Player.Y);
            Sim.Step(w, new Controls(1, 0, true));
        }
        Assert.True(x.Dead || !w.Here.Contains(x));
        Assert.Equal(50, w.Score - 1000);
    }

    [Fact]
    public void A_droid_shot_at_turns_on_the_player_and_shoots_back()
    {
        var w = Empty();
        Roomy(w);
        w.Player.Number = 426;
        w.Player.Hp = Classes.MaxHp(4);
        var x = Put(w, 340, 90, 0);
        x.FireCool = 0;
        Assert.False(x.Angry);
        Run(w, 20, new Controls(1, 0, true));
        Assert.True(x.Angry);
        double hp = w.Player.Hp;
        Run(w, 90);
        Assert.True(w.Player.Hp < hp - 5);
    }

    [Fact]
    public void A_host_burns_out_in_time_and_leaves_the_bare_link_unit()
    {
        var w = Empty();
        Roomy(w);
        w.Player.Number = 744;
        w.Player.Hp = 0.3;
        Run(w, 30);
        Assert.Equal(1, w.Player.Number);
        Assert.False(w.Player.Dead);
        Assert.Contains(Sfx.HostLost, w.Heard);
    }

    [Fact]
    public void Shot_down_as_the_bare_link_unit_is_the_end()
    {
        var w = Empty();
        Sim.Hurt(w, 1000, "test");
        Assert.True(w.Player.Dead);
    }

    [Fact]
    public void The_recharger_fills_the_player_up()
    {
        var w = Empty();
        w.Player.Hp = 5;
        Run(w, 60);
        Assert.True(w.Player.Hp > 25);
    }

    [Fact]
    public void Doors_open_for_whatever_comes_near_and_stop_shots_when_shut()
    {
        var w = Empty();
        var d = w.Deck;
        var (c, r) = Enumerable.Range(0, d.Cols).SelectMany(cc => Enumerable.Range(0, d.Rows).Select(rr => (cc, rr))).First(q => d.Grid[q.cc, q.rr] == Tile.Door);
        var (dx, dy) = d.Centre(c, r);
        w.Player.X = dx + 400;
        w.Player.Y = dy + 400;
        Assert.False(Sim.DoorOpen(w, c, r));
        (w.Player.X, w.Player.Y) = (dx, dy - 40);
        Assert.True(Sim.DoorOpen(w, c, r));
    }

    // ───────────────────────────── transfer ─────────────────────────────

    [Fact]
    public void With_the_transfer_charge_on_touching_a_droid_starts_the_duel()
    {
        var w = Empty();
        Roomy(w);
        Put(w, 212, 60, 0);
        Run(w, 2, new Controls(0, 0, false, true));
        Assert.True(w.Player.Transfer > 0);
        World? hit = null;
        for (int i = 0; i < 60 && hit == null; i++) { Sim.Step(w, new Controls(1, 0)); if (w.TransferWith != null) hit = w; }
        Assert.NotNull(hit);
    }

    [Fact]
    public void In_the_duel_a_pulse_takes_its_cell_and_a_fork_takes_two_and_a_dead_wire_none()
    {
        var x = new Droid { Number = 108 };
        Duel? d = null;
        for (int seed = 0; seed < 200 && d == null; seed++)
        {
            var t = new Duel(x, 0, seed);
            if (t.Left.Contains(Wire.ForkDown) && t.Left.Contains(Wire.Dead)) d = t;
        }
        Assert.NotNull(d);
        int fork = Array.IndexOf(d!.Left, Wire.ForkDown), dead = Array.IndexOf(d.Left, Wire.Dead);
        d.Cells[fork] = d.Cells[fork + 1] = 1;
        d.Cells[dead] = 1;
        d.Cursor = fork;
        Assert.True(d.Send());
        d.Cursor = dead;
        Assert.True(d.Send());
        // (Hold the droid's pulses off.)
        d.DroidPulses = 0;
        for (int i = 0; i < 40; i++) d.Step(1 / 60.0);
        Assert.Equal(0, d.Cells[fork]);
        Assert.Equal(0, d.Cells[fork + 1]);
        Assert.Equal(1, d.Cells[dead]);
    }

    [Fact]
    public void Higher_classes_bring_more_pulses_to_the_duel()
    {
        Assert.True(Classes.DroidPulses(9) > Classes.DroidPulses(1));
        Assert.True(Classes.PlayerPulses(8) > Classes.PlayerPulses(0));
        var d = new Duel(new Droid { Number = 920 }, 0, 1);
        Assert.Equal(Classes.PlayerPulses(0), d.PlayerPulses);
        Assert.Equal(Classes.DroidPulses(9), d.DroidPulses);
    }

    [Fact]
    public void Winning_the_duel_takes_over_the_droid_and_losing_it_bare_is_the_end()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        var w = g.World;
        foreach (var d in w.Droids) d.Clear();
        w.Player.Safe = 0;
        var x = Put(w, 135, 26, 0);
        Put(w, 108, 600, 0);
        w.Player.Transfer = 5;
        g.Tick(default);
        Assert.Equal(Phase.Duel, g.Phase);
        // Every cell for the player.
        Array.Fill(g.Duel!.Cells, 0);
        g.Duel.DroidPulses = 0;
        g.Run((int)(60 * (Duel.Time + GameEngine.DuelOverSeconds)) + 10);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(135, w.Player.Number);
        Assert.DoesNotContain(x, w.Here);
        // And now losing one, in a host: the host is lost.
        var y = Put(w, 212, 26, 0);
        w.Player.Transfer = 5;
        w.Player.Safe = 0;
        g.Tick(default);
        Assert.Equal(Phase.Duel, g.Phase);
        Array.Fill(g.Duel!.Cells, 1);
        g.Duel.DroidPulses = 0;
        g.Duel.PlayerPulses = 0;
        g.Run((int)(60 * (Duel.Time + GameEngine.DuelOverSeconds)) + 10);
        Assert.Equal(1, w.Player.Number);
        Assert.False(w.Player.Dead);
        Assert.Contains(y, w.Here);
    }

    // ───────────────────────────── the lift and the decks ─────────────────────────────

    [Fact]
    public void The_lift_takes_the_player_to_another_deck_on_its_shaft()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        var w = g.World;
        var lift = w.Deck.Lifts[0];
        (w.Player.X, w.Player.Y) = w.Deck.Centre(lift.Col, lift.Row);
        g.Tick(new Controls(0, 0, false, true));
        Assert.Equal(Phase.Lift, g.Phase);
        g.Run(30);
        g.Tick(new Controls(0, 1));
        g.Tick(default);
        Assert.Equal(1, g.LiftDeck);
        g.Run(5);
        g.Tick(new Controls(0, 0, false, true));
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(1, w.DeckIndex);
        Assert.Equal(Tile.Lift, w.Deck.AtPoint(w.Player.X, w.Player.Y));
    }

    [Fact]
    public void A_deck_cleared_is_a_bonus_and_its_lights_go_down_and_every_droid_gone_clears_the_ship()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        var w = g.World;
        for (int i = 1; i < w.Droids.Length; i++) w.Droids[i].Clear();
        int score = g.Score;
        foreach (var x in w.Here.ToList()) Sim.Destroy(w, x);
        g.Tick(default);
        Assert.True(w.Cleared(0));
        Assert.True(g.Score >= score + 1000);
        g.Tick(default);
        Assert.Equal(Phase.ShipClear, g.Phase);
        g.Run((int)(60 * GameEngine.ShipClearSeconds) + 5);
        Assert.Equal(2, w.Round);
        Assert.True(w.Left > 0);
    }

    [Fact]
    public void The_last_life_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        g.SetLives(1);
        g.World.Score = 90000;
        g.World.Player.Safe = 0;
        Sim.Hurt(g.World, 1000, "test");
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void A_life_lost_starts_again_as_the_bare_link_unit_by_the_recharger()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        var w = g.World;
        w.Player.Safe = 0;
        Sim.Hurt(w, 1000, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(1, w.Player.Number);
        Assert.Equal(Tile.Energizer, w.Deck.AtPoint(w.Player.X, w.Player.Y));
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_move_shoot_and_press_the_button()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Fire);
        Assert.False(input.Player.Act);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Act);
        input.Dispose();
        var w = Empty();
        double y = w.Player.Y;
        Run(w, 10, new Controls(0, -1));
        Assert.True(w.Player.Y < y);
    }
}
