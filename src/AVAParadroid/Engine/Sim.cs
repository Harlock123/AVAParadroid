namespace AVAParadroid.Engine;

/// <summary>
/// A deck, step by step: the player moving, shooting and charging up, the droids wandering or hunting, the shots,
/// the doors, the burning out of a host. Transfers and lifts are only asked for here; the game does them.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, T = Field.Tile;
    public const double Sight = 440, Recharge = 26;

    public static void Start(World w, ShipDef ship, int round, int seed)
    {
        w.Ship = ship;
        w.Round = round;
        w.Dice = new Dice(seed);
        w.Droids = ship.Decks.Select(d => d.Droids.Select(p => NewDroid(w, p)).ToList()).ToArray();
        w.Player = new Player();
        Board(w, 0, null);
    }

    private static Droid NewDroid(World w, Placed p)
    {
        int k = p.Number / 100;
        var (x, y) = w.Ship.Decks[0].Centre(p.Col, p.Row);
        return new Droid { Number = p.Number, Id = w.NextId++, X = x, Y = y, Tx = x, Ty = y, Hp = Classes.MaxHp(k), MaxHp = Classes.MaxHp(k), FireCool = 1 + (p.Col % 3) * 0.4, Angry = k >= 6 };
    }

    /// <summary>The player onto a deck: at a lift pad on the given shaft, or (at the start, or after a loss) by the recharger.</summary>
    public static void Board(World w, int deck, int? shaft)
    {
        w.DeckIndex = deck;
        var d = w.Deck;
        (int c, int r) at = d.Energizer;
        if (shaft is { } sh) foreach (var l in d.Lifts) if (l.Shaft == sh) at = (l.Col, l.Row);
        (w.Player.X, w.Player.Y) = d.Centre(at.c, at.r);
        w.Player.Safe = 1.5;
        w.Player.Transfer = 0;
        w.Bullets.Clear();
        w.Effects.Clear();
        w.TransferWith = null;
        w.LiftRequest = false;
        // The droids hold their fire a moment; those too near the way in step back.
        foreach (var x in w.Here)
        {
            x.FireCool = Math.Max(x.FireCool, 2);
            if (Math.Abs(x.X - w.Player.X) < 90 && Math.Abs(x.Y - w.Player.Y) < 90) x.FireCool = 3;
            if (x.Class < 6) x.Angry = false;
        }
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        w.LiftRequest = false;
        w.TransferWith = null;
        var p = w.Player;
        if (!p.Dead) MovePlayer(w, c);
        foreach (var x in w.Here) if (!x.Dead) Behave(w, x);
        Separate(w);
        MoveBullets(w);
        foreach (var e in w.Effects) e.Age += Dt;
        w.Effects.RemoveAll(e => e.Done);
        w.Bullets.RemoveAll(b => b.Dead);
        int before = w.Here.Count;
        w.Here.RemoveAll(x => x.Dead);
        if (before > 0 && w.Here.Count == 0)
        {
            w.Score += 1000 * (w.DeckIndex + 1);
            w.Heard.Add(Sfx.DeckClear);
        }
    }

    // ───────────────────────────── the player ─────────────────────────────

    private static void MovePlayer(World w, Controls c)
    {
        var p = w.Player;
        p.Safe -= Dt;
        p.Hurt -= Dt;
        p.FireCool -= Dt;
        p.Transfer = Math.Max(0, p.Transfer - Dt);
        if (c.Dx != 0 || c.Dy != 0) { p.Fx = c.Dx; p.Fy = c.Dy; }
        var gun = Classes.Gun(p.Class);
        bool shooting = c.Fire && (c.Dx != 0 || c.Dy != 0);
        if (shooting)
        {
            // Fire with a direction shoots that way, standing still.
            if (gun.Damage > 0 && p.FireCool <= 0)
            {
                double len = Math.Sqrt(c.Dx * c.Dx + c.Dy * c.Dy);
                w.Bullets.Add(new Bullet { X = p.X + c.Dx / len * 18, Y = p.Y + c.Dy / len * 18, Vx = c.Dx / len * gun.Speed * 1.2, Vy = c.Dy / len * gun.Speed * 1.2, Damage = gun.Damage, FromPlayer = true });
                p.FireCool = gun.Every * 0.7;
                w.Heard.Add(Sfx.Fire);
            }
        }
        else if (c.Dx != 0 || c.Dy != 0)
        {
            double len = Math.Sqrt(c.Dx * c.Dx + c.Dy * c.Dy), v = Classes.Speed(p.Class) * (p.Class == 0 ? 1 : 1.1);
            Move(w.Deck, ref p.X, ref p.Y, c.Dx / len * v * Dt, c.Dy / len * v * Dt, Player.Radius);
        }
        // The button: on a lift, the lift; anywhere else, the transfer charge.
        if (c.Act && !p.ActPrev)
        {
            if (w.Deck.AtPoint(p.X, p.Y) == Tile.Lift) w.LiftRequest = true;
            else
            {
                p.Transfer = p.Transfer > 0 ? 0 : Player.TransferTime;
                w.Heard.Add(p.Transfer > 0 ? Sfx.TransferOn : Sfx.TransferOff);
            }
        }
        p.ActPrev = c.Act;
        // The recharger.
        p.Charging = Math.Max(0, p.Charging - Dt);
        if (w.Deck.AtPoint(p.X, p.Y) == Tile.Energizer && p.Hp < p.MaxHp)
        {
            if (p.Charging <= 0) w.Heard.Add(Sfx.Charge);
            p.Charging = 0.6;
            p.Hp = Math.Min(p.MaxHp, p.Hp + Recharge * Dt);
        }
        // A host burns out under control.
        p.Hp -= Classes.Burn(p.Class) * Dt;
        if (p.Hp <= 0) Hurt(w, 0, "burnt out");
        if (p.Dead) return;
        // Touching a droid with the transfer charge on: the duel.
        if (p.Transfer > 0)
            foreach (var x in w.Here)
                if (!x.Dead && Dist(x.X, x.Y, p.X, p.Y) < Droid.Radius + Player.Radius + 3)
                {
                    w.TransferWith = x;
                    p.Transfer = 0;
                    break;
                }
    }

    /// <summary>
    /// The player hurt. In a host, losing all its energy throws the player back to the bare link unit; as the link
    /// unit, it's the end.
    /// </summary>
    public static void Hurt(World w, double damage, string cause)
    {
        var p = w.Player;
        if (p.Dead || damage > 0 && p.Safe > 0) return;
        p.Hp -= damage;
        p.Hurt = 0.15;
        if (damage > 0) w.Heard.Add(Sfx.Hit);
        if (p.Hp > 0) return;
        if (p.Number != 1)
        {
            // The host is lost.
            w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = p.X, Y = p.Y, Life = 0.9, Colour = 0xFFFFC040 });
            w.Effects.Add(new Effect { Kind = EffectKind.Word, X = p.X, Y = p.Y - 30, Text = "HOST LOST", Life = 1.4, Colour = 0xFFFF8060 });
            w.Heard.Add(Sfx.HostLost);
            p.Number = 1;
            p.Hp = Classes.MaxHp(0) * 0.6;
            p.Safe = 1.2;
            return;
        }
        p.Dead = true;
        w.Cause = cause;
        w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = p.X, Y = p.Y, Life = 1.2, Colour = 0xFFFFE080 });
        w.Heard.Add(Sfx.Die);
    }

    /// <summary>Moving a round body, sliding along walls.</summary>
    public static void Move(DeckDef d, ref double x, ref double y, double dx, double dy, double radius)
    {
        if (Free(d, x + dx, y, radius)) x += dx;
        if (Free(d, x, y + dy, radius)) y += dy;
    }

    public static bool Free(DeckDef d, double x, double y, double radius)
    {
        int c0 = (int)Math.Floor((x - radius) / T), c1 = (int)Math.Floor((x + radius) / T);
        int r0 = (int)Math.Floor((y - radius) / T), r1 = (int)Math.Floor((y + radius) / T);
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
        {
            if (DeckDef.Walkable(d.At(c, r))) continue;
            // The nearest point of the square to the body.
            double nx = Math.Clamp(x, c * T, c * T + T), ny = Math.Clamp(y, r * T, r * T + T);
            if ((nx - x) * (nx - x) + (ny - y) * (ny - y) < radius * radius) return false;
        }
        return true;
    }

    public static double Dist(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    // ───────────────────────────── the droids ─────────────────────────────

    /// <summary>Whether anything is near enough a door square to hold it open.</summary>
    public static bool DoorOpen(World w, int c, int r)
    {
        double x = (c + 0.5) * T, y = (r + 0.5) * T;
        if (!w.Player.Dead && Math.Abs(w.Player.X - x) < 52 && Math.Abs(w.Player.Y - y) < 52) return true;
        foreach (var d in w.Here) if (Math.Abs(d.X - x) < 52 && Math.Abs(d.Y - y) < 52) return true;
        return false;
    }

    /// <summary>A clear line between two points: no wall, no console, no closed door.</summary>
    public static bool Sees(World w, double x0, double y0, double x1, double y1)
    {
        double dist = Dist(x0, y0, x1, y1);
        int n = (int)(dist / 8) + 1;
        for (int i = 1; i < n; i++)
        {
            double x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
            int c = (int)Math.Floor(x / T), r = (int)Math.Floor(y / T);
            var t = w.Deck.At(c, r);
            if (t is Tile.Wall or Tile.Void or Tile.Console) return false;
            if (t == Tile.Door && !DoorOpen(w, c, r)) return false;
        }
        return true;
    }

    private static void Behave(World w, Droid x)
    {
        x.T += Dt;
        x.Hurt -= Dt;
        x.FireCool -= Dt;
        var p = w.Player;
        var d = w.Deck;
        double speed = Classes.Speed(x.Class) * (1 + 0.1 * (w.Round - 1));
        double dist = Dist(x.X, x.Y, p.X, p.Y);
        bool sees = !p.Dead && dist < Sight && Sees(w, x.X, x.Y, p.X, p.Y);
        var gun = Classes.Gun(x.Class);
        if (x.Angry && sees)
        {
            // Hunting: keep a fighting distance, and shoot.
            double want = gun.Damage > 0 ? 190 : 0;
            double dir = dist > want + 40 ? 1 : dist < want - 50 ? -1 : 0;
            if (dir != 0) Move(d, ref x.X, ref x.Y, (p.X - x.X) / dist * speed * dir * Dt, (p.Y - x.Y) / dist * speed * dir * Dt, Droid.Radius);
            if (gun.Damage > 0 && x.FireCool <= 0 && p.Safe <= 0)
            {
                double s = gun.Speed * (1 + 0.1 * (w.Round - 1));
                w.Bullets.Add(new Bullet { X = x.X + (p.X - x.X) / dist * 18, Y = x.Y + (p.Y - x.Y) / dist * 18, Vx = (p.X - x.X) / dist * s, Vy = (p.Y - x.Y) / dist * s, Damage = gun.Damage });
                x.FireCool = gun.Every * w.Dice.Between(1.6, 2.4) / (1 + 0.15 * (w.Round - 1));
                w.Heard.Add(Sfx.DroidFire);
            }
            x.Tx = x.X;
            x.Ty = x.Y;
            return;
        }
        // Wandering: to a spot in sight, a pause, another.
        double tdx = x.Tx - x.X, tdy = x.Ty - x.Y, td = Math.Sqrt(tdx * tdx + tdy * tdy);
        if (td < 4 || x.Stuck > 1.2)
        {
            if (x.Stuck > 1.2 || w.Dice.Next() < Dt * 1.5)
            {
                x.Stuck = 0;
                for (int k = 0; k < 6; k++)
                {
                    double a = w.Dice.Between(0, Math.PI * 2), r = w.Dice.Between(40, 200);
                    double nx = x.X + Math.Cos(a) * r, ny = x.Y + Math.Sin(a) * r;
                    if (Free(d, nx, ny, Droid.Radius) && Sees(w, x.X, x.Y, nx, ny)) { x.Tx = nx; x.Ty = ny; break; }
                }
            }
            return;
        }
        double ox = x.X, oy = x.Y, v = speed * 0.55;
        Move(d, ref x.X, ref x.Y, tdx / td * Math.Min(td, v * Dt), tdy / td * Math.Min(td, v * Dt), Droid.Radius);
        if (Dist(ox, oy, x.X, x.Y) < v * Dt * 0.3) x.Stuck += Dt; else x.Stuck = 0;
    }

    /// <summary>Bodies push apart.</summary>
    private static void Separate(World w)
    {
        var p = w.Player;
        var all = w.Here;
        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            for (int j = i + 1; j < all.Count; j++)
            {
                var b = all[j];
                double dd = Dist(a.X, a.Y, b.X, b.Y);
                if (dd >= Droid.Radius * 2 || dd < 0.01) continue;
                double push = (Droid.Radius * 2 - dd) / 2;
                Move(w.Deck, ref a.X, ref a.Y, (a.X - b.X) / dd * push, (a.Y - b.Y) / dd * push, Droid.Radius);
                Move(w.Deck, ref b.X, ref b.Y, (b.X - a.X) / dd * push, (b.Y - a.Y) / dd * push, Droid.Radius);
            }
            if (p.Dead) continue;
            double pd = Dist(a.X, a.Y, p.X, p.Y);
            if (pd < Droid.Radius + Player.Radius && pd > 0.01)
            {
                double push = Droid.Radius + Player.Radius - pd;
                Move(w.Deck, ref a.X, ref a.Y, (a.X - p.X) / pd * push * 0.6, (a.Y - p.Y) / pd * push * 0.6, Droid.Radius);
                Move(w.Deck, ref p.X, ref p.Y, (p.X - a.X) / pd * push * 0.4, (p.Y - a.Y) / pd * push * 0.4, Player.Radius);
                // A bump makes a droid that can fight angry.
                if (a.Class >= 3) a.Angry = true;
            }
        }
    }

    // ───────────────────────────── shots ─────────────────────────────

    private static void MoveBullets(World w)
    {
        var p = w.Player;
        foreach (var b in w.Bullets)
        {
            b.X += b.Vx * Dt;
            b.Y += b.Vy * Dt;
            b.Life -= Dt;
            int c = (int)Math.Floor(b.X / T), r = (int)Math.Floor(b.Y / T);
            var t = w.Deck.At(c, r);
            if (b.Life <= 0 || t is Tile.Wall or Tile.Void or Tile.Console || t == Tile.Door && !DoorOpen(w, c, r))
            {
                b.Dead = true;
                w.Effects.Add(new Effect { Kind = EffectKind.Burst, X = b.X, Y = b.Y, Text = "4", Life = 0.25, Colour = 0xFFFFE0A0 });
                continue;
            }
            if (b.FromPlayer)
            {
                foreach (var x in w.Here)
                {
                    if (x.Dead || Dist(x.X, x.Y, b.X, b.Y) > Droid.Radius + 3) continue;
                    b.Dead = true;
                    x.Hp -= b.Damage;
                    x.Hurt = 0.12;
                    if (x.Class >= 3) x.Angry = true;
                    if (x.Hp <= 0) Destroy(w, x);
                    else w.Heard.Add(Sfx.Hit);
                    break;
                }
            }
            else if (!p.Dead && Dist(p.X, p.Y, b.X, b.Y) < Player.Radius + 3)
            {
                b.Dead = true;
                Hurt(w, b.Damage, "a droid's shot");
            }
        }
    }

    public static void Destroy(World w, Droid x)
    {
        x.Dead = true;
        int pts = x.Class * 50;
        w.Score += pts;
        w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = x.X, Y = x.Y, Life = 0.8, Colour = 0xFFFFA040 });
        w.Effects.Add(new Effect { Kind = EffectKind.Score, X = x.X, Y = x.Y - 20, Text = pts.ToString(), Colour = 0xFFFFE070 });
        w.Heard.Add(Sfx.Boom);
        // The blast hurts what's close.
        var p = w.Player;
        if (!p.Dead && Dist(p.X, p.Y, x.X, x.Y) < 48) Hurt(w, 6, "a droid's blast");
    }

    /// <summary>The player takes over a droid that lost the duel: its body, at full energy.</summary>
    public static void Take(World w, Droid x)
    {
        var p = w.Player;
        p.Number = x.Number;
        p.Hp = Classes.MaxHp(x.Class);
        p.X = x.X;
        p.Y = x.Y;
        p.Safe = 1;
        x.Dead = true;
        w.Score += x.Class * 100;
        w.Here.Remove(x);
        w.Effects.Add(new Effect { Kind = EffectKind.Word, X = p.X, Y = p.Y - 30, Text = $"{x.Number:000} {Classes.Names[x.Class]}", Life = 1.6, Colour = 0xFFFFE040 });
        if (w.Here.Count == 0)
        {
            w.Score += 1000 * (w.DeckIndex + 1);
            w.Heard.Add(Sfx.DeckClear);
        }
    }
}
