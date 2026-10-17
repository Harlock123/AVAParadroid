using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAParadroid.Engine;
using Effect = AVAParadroid.Engine.Effect;
using DroidClasses = AVAParadroid.Engine.Classes;

namespace AVAParadroid.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAParadroid/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;
    private double _camX, _camY;

    /// <summary>A droid's colour by class: pale for the low ones, through amber, to red for the commander.</summary>
    private static uint ClassColour(int k) => k switch
    {
        0 => 0xFFF0F0F0,
        1 or 2 => 0xFFB8C8D8,
        3 or 4 => 0xFF80C0A0,
        5 or 6 => 0xFFE0C060,
        7 or 8 => 0xFFF08040,
        _ => 0xFFFF4040,
    };

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            if (g.Phase is Phase.Duel or Phase.DuelOver && g.Duel is { } duel) DrawDuel(dc, duel);
            else if (g.Phase == Phase.Lift) DrawLift(dc);
            else DrawDeck(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 18), 24, Yellow, center: true);
    }

    // ───────────────────────────── the deck ─────────────────────────────

    private void DrawDeck(DrawingContext dc)
    {
        var w = Engine.World;
        var d = w.Deck;
        var p = w.Player;
        double viewH = Field.Height - Field.Top;
        double tx = Math.Clamp(p.X - Field.Width / 2, -40, d.Cols * T - Field.Width + 40);
        double ty = Math.Clamp(p.Y - viewH / 2, -40, d.Rows * T - viewH + 40);
        _camX = tx;
        _camY = ty;
        bool dim = w.Cleared(w.DeckIndex);
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, viewH)))
        using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(tx), Field.Top - Math.Round(ty))))
        {
            int c0 = Math.Max(0, (int)(tx / T) - 1), c1 = Math.Min(d.Cols, (int)((tx + Field.Width) / T) + 2);
            int r0 = Math.Max(0, (int)(ty / T) - 1), r1 = Math.Min(d.Rows, (int)((ty + viewH) / T) + 2);
            for (int c = c0; c < c1; c++)
            for (int r = r0; r < r1; r++)
                DrawTile(dc, w, d, c, r, dim);
            foreach (var x in w.Here) DrawDroid(dc, x.X, x.Y, x.Number, x.Hurt > 0, x.Angry && x.Class >= 3, x.T);
            if (!p.Dead) DrawPlayer(dc, p);
            foreach (var b in w.Bullets) DrawBullet(dc, b);
            foreach (var e in w.Effects) DrawEffect(dc, e);
        }
    }

    private void DrawTile(DrawingContext dc, World w, DeckDef d, int c, int r, bool dim)
    {
        var t = d.Grid[c, r];
        double x = c * T, y = r * T;
        uint floor = dim ? 0xFF181A20 : 0xFF1C2638, line = dim ? 0xFF22252C : 0xFF26324A;
        switch (t)
        {
            case Tile.Void:
                return;
            case Tile.Wall:
            {
                uint wall = dim ? 0xFF4A4E58 : 0xFF6A7898;
                dc.FillRectangle(Brush(wall), new Rect(x, y, T + 0.5, T + 0.5));
                if (!(d.At(c, r - 1) is Tile.Wall)) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.35)), new Rect(x, y, T + 0.5, 3));
                if (!(d.At(c, r + 1) is Tile.Wall)) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.4)), new Rect(x, y + T - 4, T + 0.5, 4));
                if (Hash(c, r, 2) % 7 == 0) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.2)), new Rect(x + 6, y + 10, 20, 3));
                return;
            }
        }
        dc.FillRectangle(Brush(floor), new Rect(x, y, T + 0.5, T + 0.5));
        dc.DrawRectangle(null, Pen(line, 1), new Rect(x + 0.5, y + 0.5, T - 1, T - 1));
        switch (t)
        {
            case Tile.Door:
            {
                bool open = Sim.DoorOpen(w, c, r);
                bool across = d.At(c - 1, r) == Tile.Wall || d.At(c + 1, r) == Tile.Wall;
                uint panel = dim ? 0xFF6A6040 : 0xFFD0A030;
                if (open) break;
                if (across) dc.FillRectangle(Brush(panel), new Rect(x, y + 10, T + 0.5, 12));
                else dc.FillRectangle(Brush(panel), new Rect(x + 10, y, 12, T + 0.5));
                break;
            }
            case Tile.Lift:
            {
                dc.DrawEllipse(Brush(0xFF2A3A5A), Pen(0xFF80B0FF, 2), new Point(x + T / 2, y + T / 2), 14, 14);
                Poly(dc, 0xFF80B0FF, null, 0, new Point(x + T / 2, y + 6), new Point(x + T / 2 + 6, y + 13), new Point(x + T / 2 - 6, y + 13));
                Poly(dc, 0xFF80B0FF, null, 0, new Point(x + T / 2, y + T - 6), new Point(x + T / 2 + 6, y + T - 13), new Point(x + T / 2 - 6, y + T - 13));
                break;
            }
            case Tile.Energizer:
            {
                double gl = 0.5 + 0.5 * Math.Sin(_clock * 5);
                dc.FillEllipse(x + T / 2, y + T / 2, 15, Alpha(0xFF40FF80, 0.25 + 0.2 * gl));
                dc.DrawEllipse(null, Pen(0xFF60FFA0, 2), new Point(x + T / 2, y + T / 2), 12, 12);
                dc.DrawEllipse(null, Pen(Alpha(0xFFC0FFD0, gl), 1.5), new Point(x + T / 2, y + T / 2), 6 + 6 * gl, 6 + 6 * gl);
                break;
            }
            case Tile.Console:
                dc.FillRectangle(Brush(0xFF3A4458), new Rect(x + 2, y + 2, T - 4, T - 6), 3);
                for (int k = 0; k < 3; k++)
                    dc.FillRectangle(Brush((int)(_clock * 3 + k + c) % 3 == 0 ? 0xFF60FF80 : 0xFF205030), new Rect(x + 6 + k * 7, y + 8, 5, 4));
                dc.FillRectangle(Brush(0xFF80C0FF), new Rect(x + 6, y + 16, 20, 6));
                break;
        }
    }

    /// <summary>A droid: a round body with its number across it, the dome on top, its colour its class.</summary>
    private void DrawDroid(DrawingContext dc, double x, double y, int number, bool hurt, bool angry, double t, uint? colour = null, double scale = 1)
    {
        int k = number / 100;
        uint body = hurt && (int)(_clock * 30) % 2 == 0 ? 0xFFFFFFFF : colour ?? ClassColour(k);
        double r = 17 * scale;
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x + 3, y + 5), r, r * 0.8);
        dc.DrawEllipse(Brush(body), Pen(0xFF101420, 2), new Point(x, y), r, r);
        // The dome, turning.
        double a = t * 2;
        dc.DrawEllipse(Brush(0xFF283040), null, new Point(x, y - r * 0.15), r * 0.55, r * 0.45);
        dc.FillEllipse(x + Math.Cos(a) * r * 0.3, y - r * 0.2, 2.5 * scale, angry ? 0xFFFF3030 : 0xFF60E0FF);
        // The band with the number.
        dc.FillRectangle(Brush(0xFF101420), new Rect(x - r * 0.95, y + r * 0.12, r * 1.9, r * 0.66), 3);
        Text(dc, number.ToString("000"), new Point(x, y + r * 0.45), 20 * scale, body == 0xFFFFFFFF ? 0xFFFFE040 : body, center: true, shadow: false);
    }

    private void DrawPlayer(DrawingContext dc, Player p)
    {
        if (p.Safe > 0 && (int)(_clock * 14) % 2 == 0) return;
        if (p.Transfer > 0)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 16);
            dc.DrawEllipse(null, Pen(Alpha(0xFFFFE040, 0.5 + 0.5 * pulse), 3), new Point(p.X, p.Y), 20 + pulse * 4, 20 + pulse * 4);
        }
        DrawDroid(dc, p.X, p.Y, p.Number, p.Hurt > 0, false, _clock, p.Class == 0 ? 0xFFF0F0F0 : 0xFFFFE040);
    }

    private void DrawBullet(DrawingContext dc, Bullet b)
    {
        double a = Math.Atan2(b.Vy, b.Vx);
        uint c = b.FromPlayer ? 0xFFFFF080 : 0xFFFF6060;
        dc.DrawLine(Pen(Alpha(c, 0.4), 7), new Point(b.X - Math.Cos(a) * 10, b.Y - Math.Sin(a) * 10), new Point(b.X, b.Y));
        dc.DrawLine(Pen(c, 3), new Point(b.X - Math.Cos(a) * 8, b.Y - Math.Sin(a) * 8), new Point(b.X + Math.Cos(a) * 3, b.Y + Math.Sin(a) * 3));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 40), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 26), 24, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Burst:
            {
                int n = int.TryParse(e.Text, out int v) ? v : 8;
                for (int i = 0; i < n; i++)
                {
                    double ang = i * Math.PI * 2 / n, r = 3 + e.Age * 60;
                    dc.FillEllipse(e.X + Math.Cos(ang) * r, e.Y + Math.Sin(ang) * r, 2 * a + 0.5, Alpha(e.Colour, a));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = 8 + e.Age * 90;
                dc.FillEllipse(e.X, e.Y, r * 0.6, Alpha(0xFFFFF0C0, a * 0.8));
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, a), 5 * a + 1), new Point(e.X, e.Y), r, r);
                for (int i = 0; i < 12; i++)
                {
                    double ang = i * 0.52 + e.X, rr = r * (0.6 + (i % 3) * 0.25);
                    dc.FillEllipse(e.X + Math.Cos(ang) * rr, e.Y + Math.Sin(ang) * rr, 3.5 * a + 1, Alpha(0xFFFF8030, a));
                }
                break;
            }
        }
    }

    // ───────────────────────────── the duel ─────────────────────────────

    private void DrawDuel(DrawingContext dc, Duel d)
    {
        var g = Engine;
        double top = Field.Top + 120, rowH = 40, cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0812), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TRANSFER", new Point(cx, Field.Top + 34), 48, White, center: true);
        DrawDroid(dc, 150, Field.Top + 64, g.World.Player.Number, false, false, _clock, 0xFFFFE040, 1.6);
        DrawDroid(dc, Field.Width - 150, Field.Top + 64, d.Droid.Number, false, true, _clock, 0xFFC080FF, 1.6);
        // Time left.
        double left = Math.Max(0, 1 - d.Clock / Duel.Time);
        dc.FillRectangle(Brush(0xFF202030), new Rect(cx - 200, Field.Top + 70, 400, 10));
        dc.FillRectangle(Brush(left < 0.25 ? Red : 0xFF60C0FF), new Rect(cx - 200, Field.Top + 70, 400 * left, 10));
        for (int r = 0; r < Duel.Rows; r++)
        {
            double y = top + r * rowH + rowH / 2;
            DrawWire(dc, d.Left[r], r, y, rowH, 0, d.Lit[0, r] > -1 && d.Clock - d.Lit[0, r] < 1.2 ? d.Clock - d.Lit[0, r] : -1);
            DrawWire(dc, d.Right[r], r, y, rowH, 1, d.Lit[1, r] > -1 && d.Clock - d.Lit[1, r] < 1.2 ? d.Clock - d.Lit[1, r] : -1);
            uint cell = d.Cells[r] == 0 ? 0xFFFFE040 : 0xFFB070FF;
            dc.DrawRectangle(Brush(cell), Pen(0xFF101010, 2), new Rect(cx - 26, y - rowH / 2 + 3, 52, rowH - 6), 4, 4);
        }
        // The cursor, and the pulses left.
        double cy = top + d.Cursor * rowH + rowH / 2;
        Poly(dc, 0xFFFFE040, null, 0, new Point(36, cy - 12), new Point(58, cy), new Point(36, cy + 12));
        for (int i = 0; i < d.PlayerPulses; i++) dc.FillEllipse(40 + i * 22, top + Duel.Rows * rowH + 30, 8, 0xFFFFE040);
        for (int i = 0; i < d.DroidPulses; i++) dc.FillEllipse(Field.Width - 40 - i * 22, top + Duel.Rows * rowH + 30, 8, 0xFFB070FF);
        Text(dc, $"{d.Held(0)}", new Point(cx - 80, top + Duel.Rows * rowH + 30), 36, 0xFFFFE040, center: true);
        Text(dc, $"{d.Held(1)}", new Point(cx + 80, top + Duel.Rows * rowH + 30), 36, 0xFFB070FF, center: true);
        if (g.Phase == Phase.DuelOver)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 300, Field.Top + 290, 600, 100), 10);
            Text(dc, g.DuelWon ? "TRANSFER COMPLETE" : "TRANSFER FAILED", new Point(cx, Field.Top + 340), 56, g.DuelWon ? 0xFFFFE040 : Red, center: true);
        }
        else Text(dc, "UP / DOWN TO CHOOSE A WIRE  -  FIRE TO SEND A PULSE", new Point(cx, Field.Height - 40), 22, Dim, center: true);
    }

    /// <summary>One side's wire on a row: straight, burnt out, or forking up or down to the middle.</summary>
    private void DrawWire(DrawingContext dc, Wire wire, int r, double y, double rowH, int side, double lit)
    {
        double cx = Field.Width / 2;
        double x0 = side == 0 ? 70 : Field.Width - 70, x1 = side == 0 ? cx - 26 : cx + 26, xm = (x0 + x1) / 2;
        uint baseC = side == 0 ? 0xFF6A5A20 : 0xFF4A2A6A, litC = side == 0 ? 0xFFFFE040 : 0xFFC080FF;
        uint c = lit >= 0 ? litC : baseC;
        double wdt = lit >= 0 ? 4 : 2.5;
        switch (wire)
        {
            case Wire.Dead:
                dc.DrawLine(Pen(baseC, 2.5), new Point(x0, y), new Point(xm, y));
                dc.DrawLine(Pen(0xFF803030, 3), new Point(xm - 6, y - 6), new Point(xm + 6, y + 6));
                dc.DrawLine(Pen(0xFF803030, 3), new Point(xm - 6, y + 6), new Point(xm + 6, y - 6));
                break;
            case Wire.ForkUp:
            case Wire.ForkDown:
            {
                double dy = wire == Wire.ForkUp ? -rowH : rowH;
                dc.DrawLine(Pen(c, wdt), new Point(x0, y), new Point(xm, y));
                dc.DrawLine(Pen(c, wdt), new Point(xm, y), new Point(x1, y));
                dc.DrawLine(Pen(c, wdt), new Point(xm, y), new Point(xm + (x1 - xm) * 0.5, y + dy));
                dc.DrawLine(Pen(c, wdt), new Point(xm + (x1 - xm) * 0.5, y + dy), new Point(x1, y + dy));
                dc.FillEllipse(xm, y, 5, c);
                break;
            }
            default:
                dc.DrawLine(Pen(c, wdt), new Point(x0, y), new Point(x1, y));
                break;
        }
        // The pulse running along.
        if (lit >= 0 && lit < Duel.Travel)
        {
            double f = lit / Duel.Travel;
            dc.FillEllipse(x0 + (x1 - x0) * f, y, 7, 0xFFFFFFFF);
        }
        dc.FillRectangle(Brush(baseC), new Rect(side == 0 ? x0 - 14 : x0, y - 8, 14, 16));
    }

    // ───────────────────────────── the lift ─────────────────────────────

    private void DrawLift(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var ship = w.Ship;
        double cx = Field.Width / 2, top = Field.Top + 110, deckH = 70;
        dc.FillRectangle(Brush(0xFF04060C), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "THE " + ship.Name, new Point(cx, Field.Top + 40), 40, White, center: true);
        // The hull in section.
        Poly(dc, 0xFF141C2C, 0xFF4A5A7A, 2, new Point(160, top - 20), new Point(840, top - 20), new Point(940, top + 3 * deckH), new Point(840, top + ship.Decks.Length * deckH + 10), new Point(160, top + ship.Decks.Length * deckH + 10), new Point(100, top + 3 * deckH));
        for (int i = 0; i < ship.Decks.Length; i++)
        {
            double y = top + i * deckH;
            bool clear = w.Cleared(i), here = i == w.DeckIndex, chosen = i == g.LiftDeck;
            uint fill = chosen ? 0xFF3A5A2A : clear ? 0xFF1A1C22 : 0xFF2A3A5A;
            dc.DrawRectangle(Brush(fill), Pen(chosen ? 0xFFFFE040 : 0xFF4A5A7A, chosen ? 3 : 1.5), new Rect(200, y + 6, 600, deckH - 12), 4, 4);
            Text(dc, $"DECK {i + 1}  {ship.Decks[i].Name}", new Point(230, y + deckH / 2), 26, clear ? 0xFF5A6070 : White);
            Text(dc, clear ? "CLEARED" : $"{w.Droids[i].Count} DROIDS", new Point(770, y + deckH / 2), 24, clear ? 0xFF5A6070 : 0xFFFF9060, right: true);
            if (here) DrawDroid(dc, 560, y + deckH / 2, w.Player.Number, false, false, _clock, w.Player.Class == 0 ? 0xFFF0F0F0 : 0xFFFFE040, 0.9);
        }
        // The shafts.
        for (int s = 0; s < ship.Shafts.Length; s++)
        {
            double x = 640 + s * 40;
            var sh = ship.Shafts[s];
            uint c = s == g.LiftShaft ? 0xFFFFE040 : 0xFF3A4A6A;
            dc.DrawLine(Pen(c, s == g.LiftShaft ? 5 : 3), new Point(x, top + sh.Top * deckH + 12), new Point(x, top + sh.Bottom * deckH + deckH - 12));
        }
        double ly = top + g.LiftDeck * deckH + deckH / 2;
        dc.DrawRectangle(Brush(0xFFFFE040), null, new Rect(640 + g.LiftShaft * 40 - 12, ly - 14, 24, 28), 3, 3);
        Text(dc, "UP / DOWN TO CHOOSE A DECK  -  FIRE OR SPACE TO GO", new Point(cx, Field.Height - 40), 22, Dim, center: true);
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF06080E), new Rect(0, 0, Field.Width, Field.Hud));
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "SCORE", new Point(16, 18), 18, Dim);
        Text(dc, g.Score.ToString("000000"), new Point(16, 40), 28, White);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("000000"), new Point(150, 40), 22, Yellow);
        if (!InGame) return;
        var p = w.Player;
        // What the player is, and its energy.
        Text(dc, $"{p.Number:000} {DroidClasses.Names[p.Class]}", new Point(Field.Width / 2 - 40, 18), 24, p.Class == 0 ? White : Yellow, center: true);
        double e = Math.Clamp(p.Hp / p.MaxHp, 0, 1);
        dc.FillRectangle(Brush(0xFF202430), new Rect(Field.Width / 2 - 170, 34, 260, 12), 3);
        dc.FillRectangle(Brush(e < 0.3 ? Red : e < 0.6 ? 0xFFE0C040 : 0xFF60E080), new Rect(Field.Width / 2 - 170, 34, 260 * e, 12), 3);
        if (p.Transfer > 0 && (int)(_clock * 4) % 2 == 0) Text(dc, "TRANSFER", new Point(Field.Width / 2 + 160, 40), 22, Yellow, center: true);
        Text(dc, $"DECK {w.DeckIndex + 1} {w.Deck.Name}", new Point(Field.Width - 16, 18), 20, 0xFF9AB0E0, right: true);
        Text(dc, w.Cleared(w.DeckIndex) ? "CLEARED" : $"{w.Here.Count} HERE  {w.Left} ON BOARD", new Point(Field.Width - 16, 40), 20, w.Cleared(w.DeckIndex) ? 0xFF60E080 : 0xFFFF9060, right: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 5); i++) dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(0xFF101420, 1), new Point(Field.Width / 2 + 230 + i * 18, 18), 6, 6);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 140;
        dc.FillRectangle(Brush(0xA0000000), new Rect(cx - 400, cy - 52, 800, 108), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 22 ? 44 : 56, 0xFFFFC040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06080E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        // A strip of deck floor, with droids going about.
        for (int c = 0; c < 33; c++)
        for (int r = 0; r < 4; r++)
        {
            double x = c * T, y = 580 + r * T;
            dc.FillRectangle(Brush(0xFF1C2638), new Rect(x, y, T + 0.5, T + 0.5));
            dc.DrawRectangle(null, Pen(0xFF26324A, 1), new Rect(x + 0.5, y + 0.5, T - 1, T - 1));
        }
        dc.FillRectangle(Brush(0xFF6A7898), new Rect(0, 572, Field.Width, 8));
        dc.FillRectangle(Brush(0xFF6A7898), new Rect(0, 708, Field.Width, 8));
        int[] parade = [920, 744, 611, 503, 426, 317, 212, 108];
        for (int i = 0; i < parade.Length; i++)
            DrawDroid(dc, ((_clock * 60 + i * 130) % 1180) - 80, 644 + Math.Sin(_clock * 2 + i) * 18, parade[i], false, false, _clock + i);
        DrawDroid(dc, ((_clock * 60 + 8 * 130) % 1180) - 80, 644, 1, false, false, _clock, 0xFFF0F0F0);
        string title = "AVA PARADROID";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60;
            double ty = 150 + Math.Sin(_clock * 2.2 + i * 0.45) * 4;
            Text(dc, title[i].ToString(), new Point(tx, ty), 110, i < 3 ? 0xFFFFE040 : (i % 2 == 0 ? 0xFFB070FF : 0xFFC8A0FF), center: true);
        }
        Text(dc, "AFTER GRAFTGOLD'S 1985 COMMODORE 64 GAME", new Point(cx, 222), 24, Dim, center: true);
        Text(dc, "THE CREW OF THE FREIGHTER MERIDIAN ARE GONE, AND ITS DROIDS HAVE TURNED.", new Point(cx, 280), 26, White, center: true);
        Text(dc, "YOU ARE 001, A LINK UNIT: WEAK ON YOUR OWN, BUT ABLE TO TAKE OVER ANY DROID", new Point(cx, 310), 26, White, center: true);
        Text(dc, "THAT LOSES A TRANSFER DUEL TO YOU. CLEAR ALL SIX DECKS.", new Point(cx, 340), 26, White, center: true);
        Text(dc, "THE HUNDREDS OF A DROID'S NUMBER ARE ITS CLASS. A HOST BURNS OUT IN TIME.", new Point(cx, 386), 22, 0xFFFFA070, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO BEGIN", new Point(cx, 456), 44, Yellow, center: true);
        Text(dc, "ARROWS: MOVE   X OR CTRL + ARROW: SHOOT   SPACE OR Z: TRANSFER / LIFT   P: PAUSE   M: SOUND", new Point(cx, 510), 20, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06080E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE BEST LINK UNITS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  DECKS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06080E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
