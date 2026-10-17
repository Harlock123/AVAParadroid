namespace AVAParadroid.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. On a deck it picks a droid: one a class or two above
/// what it's in, to take over; anything it can outgun, to shoot. It finds its way by the deck's map, lines up along
/// one of the eight directions and shoots, or turns on the transfer charge and rams. When its energy runs low it
/// recharges; when a deck is clear it takes the lift to one that isn't. In a duel it saves its pulses for the
/// end and spends them where they take the most cells.
/// </summary>
public sealed class Pilot
{
    private Droid? _target;
    private int _chosenAt;
    private readonly Dictionary<int, int> _given = [];
    private bool _transfer;
    private int _tick;
    private bool _actPrev;
    private (int Col, int Row)[] _path = [];
    private int _pathTick = -999;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _target = null;
        _given.Clear();
        _tick = 0;
        _path = [];
        _pathTick = -999;
        Doing = "";
    }

    /// <summary>Press the button once (it acts on the press, not on holding).</summary>
    private Controls Press(Controls c)
    {
        bool act = c.Act && !_actPrev;
        _actPrev = c.Act && act;
        return c with { Act = act };
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var w = g.World;
        switch (g.Phase)
        {
            case Phase.Duel: return Duel(g.Duel!);
            case Phase.Lift: return Lift(g, w);
            case Phase.Play: break;
            default: _actPrev = false; return default;
        }
        var p = w.Player;
        if (p.Dead) return default;
        var d = w.Deck;
        // Low on energy (and, bare, not being watched): to the recharger, and stay till full.
        bool watched = w.Here.Any(x => x.Angry && Classes.Gun(x.Class).Damage > 0 && Sim.Dist(x.X, x.Y, p.X, p.Y) < Sim.Sight && Sim.Sees(w, x.X, x.Y, p.X, p.Y));
        bool low = p.Hp < p.MaxHp * (p.Class == 0 ? 0.4 : 0.3) || Doing == "charging" && p.Hp < p.MaxHp * 0.95;
        if (low && (p.Class != 0 || !watched || w.Here.Count == 0))
        {
            Doing = "charging";
            var (ex, ey) = d.Centre(d.Energizer.Col, d.Energizer.Row);
            if (Sim.Dist(p.X, p.Y, ex, ey) < 5) return Fight(w) ?? default;
            return Fight(w) ?? Go(w, d.Energizer);
        }
        if (w.Here.Count == 0)
        {
            // To a lift on a shaft that leads to a deck still with droids.
            var lift = BestLift(w);
            if (lift == null) return default;
            Doing = "to the lift";
            var (lx, ly) = d.Centre(lift.Value.Col, lift.Value.Row);
            if (Sim.Dist(p.X, p.Y, lx, ly) < 6) return Press(new Controls(0, 0, false, true));
            return Go(w, (lift.Value.Col, lift.Value.Row));
        }
        // Choose what to go for, now and then (and something else, after chasing one too long).
        if (_target != null && _tick - _chosenAt > 60 * 25) { _given[_target.Id] = _tick; _target = null; }
        if (_target == null || _target.Dead || !w.Here.Contains(_target) || _tick % 120 == 0) Choose(w);
        var t = _target!;
        int k = t.Class, mine = p.Class;
        bool armed = Classes.Gun(mine).Damage > 0;
        _transfer = !armed || k > mine && k <= mine + 2 || p.Hp < p.MaxHp * 0.45 && k >= mine;
        if (_transfer)
        {
            Doing = "transfer";
            // (Not on a lift pad: the button there is the lift.)
            if (p.Transfer <= 0 && d.AtPoint(p.X, p.Y) != Tile.Lift) return Press(new Controls(0, 0, false, true));
            if (Sim.Sees(w, p.X, p.Y, t.X, t.Y) && Sim.Dist(p.X, p.Y, t.X, t.Y) < 220) return Toward(p.X, p.Y, t.X, t.Y);
            return Go(w, ((int)(t.X / Field.Tile), (int)(t.Y / Field.Tile)));
        }
        Doing = "shoot";
        return Fight(w, t) ?? Go(w, ((int)(t.X / Field.Tile), (int)(t.Y / Field.Tile)), t);
    }

    /// <summary>The droid to go for: the nearest worth taking over, or else the nearest.</summary>
    private void Choose(World w)
    {
        var p = w.Player;
        int mine = p.Class;
        bool armed = Classes.Gun(mine).Damage > 0;
        double Score(Droid x)
        {
            double dist = Sim.Dist(p.X, p.Y, x.X, x.Y);
            // Better a step up to take over; bare, something modest first; armed, anything not far above can be shot.
            if (x.Class > mine && x.Class <= mine + 2) dist *= 0.5;
            if (!armed && x.Class > Math.Max(mine + 2, 4)) dist *= 4;
            if (armed && x.Class > mine + 3) dist *= 3;
            return dist;
        }
        var pick = w.Here.Where(x => !_given.TryGetValue(x.Id, out int at) || _tick - at > 60 * 20).OrderBy(Score).FirstOrDefault() ?? w.Here.OrderBy(Score).FirstOrDefault();
        if (pick != _target) _chosenAt = _tick;
        _target = pick;
        _path = [];
    }

    /// <summary>A shot at a droid in sight along one of the eight directions, or a step to line one up.</summary>
    private Controls? Fight(World w, Droid? only = null)
    {
        var p = w.Player;
        if (Classes.Gun(p.Class).Damage <= 0) return null;
        foreach (var x in w.Here.OrderBy(x => Sim.Dist(p.X, p.Y, x.X, x.Y)))
        {
            if (only != null && x != only) continue;
            double dx = x.X - p.X, dy = x.Y - p.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 380 || !Sim.Sees(w, p.X, p.Y, x.X, x.Y)) continue;
            // Lined up: shoot.
            if (Math.Abs(dx) < 10) return new Controls(0, Math.Sign(dy), true);
            if (Math.Abs(dy) < 10) return new Controls(Math.Sign(dx), 0, true);
            if (Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < 12) return new Controls(Math.Sign(dx), Math.Sign(dy), true);
            if (only == null) continue;
            if (dist > 300) return null;
            // Not lined up: step to the nearest line.
            double ax = Math.Abs(dx), ay = Math.Abs(dy), diag = Math.Abs(ax - ay);
            if (ax <= ay && ax <= diag) return new Controls(Math.Sign(dx), 0);
            if (ay <= ax && ay <= diag) return new Controls(0, Math.Sign(dy));
            // Toward the diagonal: shorten the longer side.
            return ax > ay ? new Controls(Math.Sign(dx), 0) : new Controls(0, Math.Sign(dy));
        }
        return null;
    }

    private static Controls Toward(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int sx = Math.Abs(dx) > 3 ? Math.Sign(dx) : 0, sy = Math.Abs(dy) > 3 ? Math.Sign(dy) : 0;
        return new Controls(sx, sy);
    }

    /// <summary>Along the map to a square, square by square: on along a straight run, lining up on a square's middle to turn.</summary>
    private Controls Go(World w, (int Col, int Row) to, Droid? chasing = null)
    {
        var p = w.Player;
        var d = w.Deck;
        int pc = (int)(p.X / Field.Tile), pr = (int)(p.Y / Field.Tile);
        int at = Array.IndexOf(_path, (pc, pr));
        if (_path.Length == 0 || _path[^1] != to || at < 0 || _tick - _pathTick > 45)
        {
            _path = Path(d, (pc, pr), to);
            _pathTick = _tick;
            at = 0;
        }
        if (at >= _path.Length - 1)
        {
            var (tx, ty) = d.Centre(to.Col, to.Row);
            return Toward(p.X, p.Y, tx, ty);
        }
        var step = (_path[at + 1].Col - _path[at].Col, _path[at + 1].Row - _path[at].Row);
        int k = at + 1;
        while (k + 1 < _path.Length && k - at < 4 && (_path[k + 1].Col - _path[k].Col, _path[k + 1].Row - _path[k].Row) == step) k++;
        var (ax, ay) = d.Centre(_path[k].Col, _path[k].Row);
        var (cx, cy) = d.Centre(pc, pr);
        if (step.Item1 != 0)
        {
            int sy = Math.Abs(cy - p.Y) > 2 ? Math.Sign(cy - p.Y) : 0;
            return new Controls(Math.Abs(cy - p.Y) > 9 ? 0 : Math.Sign(ax - p.X), sy);
        }
        int sx = Math.Abs(cx - p.X) > 2 ? Math.Sign(cx - p.X) : 0;
        return new Controls(sx, Math.Abs(cx - p.X) > 9 ? 0 : Math.Sign(ay - p.Y));
    }

    /// <summary>Whether the player's body can go straight between two points.</summary>
    private static bool Clear(DeckDef d, double x0, double y0, double x1, double y1)
    {
        int n = (int)(Sim.Dist(x0, y0, x1, y1) / 6) + 1;
        for (int i = 0; i <= n; i++)
            if (!Sim.Free(d, x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, Player.Radius + 1)) return false;
        return true;
    }

    /// <summary>The shortest way across the deck's squares.</summary>
    public static (int Col, int Row)[] Path(DeckDef d, (int, int) from, (int, int) to)
    {
        var prev = new Dictionary<(int, int), (int, int)>();
        var q = new Queue<(int, int)>();
        q.Enqueue(from);
        prev[from] = from;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to) break;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (cur.Item1 + dc, cur.Item2 + dr);
                if (prev.ContainsKey(n) || !DeckDef.Walkable(d.At(n.Item1, n.Item2))) continue;
                prev[n] = cur;
                q.Enqueue(n);
            }
        }
        if (!prev.ContainsKey(to)) return [to];
        var path = new List<(int, int)>();
        for (var c = to; c != from; c = prev[c]) path.Add(c);
        path.Add(from);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>A lift on this deck whose shaft reaches a deck with droids (or reaches one that can reach one).</summary>
    private static (int Col, int Row, int Shaft)? BestLift(World w)
    {
        var ship = w.Ship;
        // Decks reachable by shafts, breadth first, to find the first step toward one with droids.
        foreach (var l in w.Deck.Lifts)
            if (ship.Stops(l.Shaft).Any(s => !w.Cleared(s))) return l;
        foreach (var l in w.Deck.Lifts)
            foreach (int s in ship.Stops(l.Shaft))
                foreach (var l2 in ship.Decks[s].Lifts)
                    if (ship.Stops(l2.Shaft).Any(s2 => !w.Cleared(s2))) return l;
        return w.Deck.Lifts.Count > 0 ? w.Deck.Lifts[0] : null;
    }

    private Controls Lift(GameEngine g, World w)
    {
        var stops = w.Ship.Stops(g.LiftShaft).ToList();
        int want = stops.FirstOrDefault(s => !w.Cleared(s), -1);
        if (want < 0)
            // On to a deck from which another shaft leads somewhere uncleared.
            want = stops.FirstOrDefault(s => s != w.DeckIndex && w.Ship.Decks[s].Lifts.Any(l => l.Shaft != g.LiftShaft && w.Ship.Stops(l.Shaft).Any(x => !w.Cleared(x))), stops[0]);
        if (g.LiftDeck != want) return _tick % 8 < 4 ? new Controls(0, Math.Sign(want - g.LiftDeck)) : default;
        return Press(new Controls(0, 0, false, _tick % 8 < 4));
    }

    /// <summary>In the duel: wait, then spend the pulses late, each where it takes the most.</summary>
    private Controls Duel(Duel d)
    {
        if (d.PlayerPulses <= 0 || d.Clock < Duel_Wait(d)) return default;
        int row = d.BestRow(out int gain);
        if (row < 0 || gain == 0) return d.Clock > global::AVAParadroid.Engine.Duel.Time - 0.8 ? new Controls(0, 0, _tick % 2 == 0) : default;
        if (d.Cursor != row) return _tick % 4 < 2 ? new Controls(0, Math.Sign(row - d.Cursor)) : default;
        return new Controls(0, 0, _tick % 4 < 2);
    }

    private static double Duel_Wait(Duel d) => global::AVAParadroid.Engine.Duel.Time - 1.2 - d.PlayerPulses * 0.55;
}
