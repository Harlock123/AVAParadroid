namespace AVAParadroid.Engine;

/// <summary>Up, down, left and right (up the screen is -1), fire (with a direction, to shoot that way), and the action button (transfer, or the lift).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Act = false);

/// <summary>The screen: the readout along the top, and a window onto the deck below, following the player.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 56;
    public const double Tile = 32;
    public const double Top = Hud;
}

/// <summary>What a square of a deck is.</summary>
public enum Tile : byte
{
    /// <summary>Outside the hull, or solid between rooms.</summary>
    Void,
    Floor,
    Wall,
    /// <summary>A door: it slides open when anything comes near, and shuts out shots when closed.</summary>
    Door,
    /// <summary>A lift pad: from here, up or down the ship.</summary>
    Lift,
    /// <summary>A recharging pad.</summary>
    Energizer,
    /// <summary>A console against a wall (just furniture, but it stands in the way).</summary>
    Console,
}

/// <summary>A droid placed on a deck: its number (the hundreds are its class), and where.</summary>
public readonly record struct Placed(int Number, int Col, int Row);

/// <summary>A deck of the ship: its squares, its rooms, its lift pads (which shaft each is on), and its droids.</summary>
public sealed class DeckDef
{
    public string Name = "";
    public int Index, Cols, Rows;
    public Tile[,] Grid = new Tile[0, 0];
    public List<(int C0, int R0, int C1, int R1)> Rooms = [];
    public List<(int Col, int Row, int Shaft)> Lifts = [];
    public List<Placed> Droids = [];
    public (int Col, int Row) Energizer;

    public Tile At(int c, int r) => c < 0 || r < 0 || c >= Cols || r >= Rows ? Tile.Void : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor(y / Field.Tile));
    /// <summary>Whether a droid can stand on a square.</summary>
    public static bool Walkable(Tile t) => t is Tile.Floor or Tile.Door or Tile.Lift or Tile.Energizer;
    public (double X, double Y) Centre(int c, int r) => ((c + 0.5) * Field.Tile, (r + 0.5) * Field.Tile);
}

/// <summary>A lift shaft: the decks it runs between.</summary>
public readonly record struct Shaft(int Top, int Bottom);

/// <summary>The ship: its decks, and the lift shafts that join them.</summary>
public sealed class ShipDef
{
    public string Name = "";
    public DeckDef[] Decks = [];
    public Shaft[] Shafts = [];

    /// <summary>The decks a shaft stops at, top to bottom.</summary>
    public IEnumerable<int> Stops(int shaft) => Enumerable.Range(Shafts[shaft].Top, Shafts[shaft].Bottom - Shafts[shaft].Top + 1);
}

/// <summary>What each class of droid is: its name, how much it can take, how fast it goes, and its gun.</summary>
public static class Classes
{
    public static readonly string[] Names = ["LINK UNIT", "CLEANER", "COURIER", "SERVICER", "ENGINEER", "STEWARD", "SENTRY", "GUARD", "WARDEN", "COMMANDER"];

    /// <summary>The numbers each class goes by (the player is 001).</summary>
    public static readonly int[][] Numbers =
    [
        [1], [108, 135], [212, 264], [317, 340], [426, 458], [503, 539], [611, 657], [702, 744], [815, 861], [920],
    ];

    public static double MaxHp(int k) => k == 0 ? 40 : 30 + 18 * k;
    public static double Speed(int k) => k == 0 ? 150 : 104 + 8 * k;
    /// <summary>A shot's damage (none for the lowest classes), how often, and how fast.</summary>
    public static (double Damage, double Every, double Speed) Gun(int k) => k switch
    {
        <= 2 => (0, 0, 0),
        <= 4 => (8, 0.75, 360),
        <= 6 => (14, 0.65, 420),
        <= 8 => (22, 0.55, 460),
        _ => (28, 0.55, 480),
    };
    /// <summary>How fast a host of this class burns out under the player's control, in energy a second.</summary>
    public static double Burn(int k) => k == 0 ? 0 : 0.35 + 0.22 * k;
    /// <summary>Pulses in the transfer duel: the player's for the class it's in, and a droid's for its own.</summary>
    public static int PlayerPulses(int k) => 4 + k / 2;
    public static int DroidPulses(int k) => 1 + k / 2;
}

/// <summary>
/// Building the ship: each deck a grid of rooms joined by corridors with doors, its lift pads on the shafts that
/// serve it, a recharging pad, consoles, and droids of the classes that belong that deep in the ship.
/// </summary>
public static class Shipyard
{
    public const int DeckCount = 6, Cols = 44, Rows = 31;
    public static readonly string[] DeckNames = ["BRIDGE", "QUARTERS", "STORES", "LABORATORY", "MACHINERY", "REACTOR"];
    /// <summary>Three shafts, overlapping, so every deck can be reached.</summary>
    public static readonly Shaft[] Shafts = [new(0, 3), new(2, 5), new(1, 4)];

    public static ShipDef Build(string name, int seed, int round = 1)
    {
        var d = new Dice(seed);
        var ship = new ShipDef { Name = name, Shafts = Shafts, Decks = new DeckDef[DeckCount] };
        for (int i = 0; i < DeckCount; i++) ship.Decks[i] = Deck(i, d.Next(1 << 30), round);
        return ship;
    }

    private static DeckDef Deck(int index, int seed, int round)
    {
        var d = new Dice(seed);
        var s = new DeckDef { Name = DeckNames[index], Index = index, Cols = Cols, Rows = Rows, Grid = new Tile[Cols, Rows] };
        // A room in most cells of a 4 by 3 grid.
        const int gx = 4, gy = 3, cw = 11, ch = 10;
        var room = new (int C0, int R0, int C1, int R1)?[gx, gy];
        for (int i = 0; i < gx; i++)
        for (int j = 0; j < gy; j++)
        {
            if (d.Next() < 0.12 && !(i == 0 && j == 0)) continue;
            int w = 5 + d.Next(4), h = 4 + d.Next(3);
            int c0 = i * cw + 1 + d.Next(cw - w - 1), r0 = j * ch + 1 + d.Next(ch - h - 1);
            room[i, j] = (c0, r0, c0 + w - 1, r0 + h - 1);
        }
        foreach (var r in room) if (r is { } q) s.Rooms.Add(q);
        foreach (var (c0, r0, c1, r1) in s.Rooms)
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
                s.Grid[c, r] = Tile.Floor;
        // Corridors two squares wide: a tree through the rooms, and a couple more links.
        var cells = new List<(int, int)>();
        for (int i = 0; i < gx; i++) for (int j = 0; j < gy; j++) if (room[i, j] != null) cells.Add((i, j));
        var joined = new HashSet<(int, int)> { cells[0] };
        var links = new List<((int, int), (int, int))>();
        while (joined.Count < cells.Count)
        {
            var options = new List<((int, int), (int, int))>();
            foreach (var a in joined)
            foreach (var b in cells)
                if (!joined.Contains(b) && Math.Abs(a.Item1 - b.Item1) + Math.Abs(a.Item2 - b.Item2) <= 2 && (a.Item1 == b.Item1 || a.Item2 == b.Item2 || Math.Abs(a.Item1 - b.Item1) + Math.Abs(a.Item2 - b.Item2) == 2))
                    options.Add((a, b));
            if (options.Count == 0)
            {
                // Out of reach of what's joined: join it to the nearest.
                var b = cells.First(x => !joined.Contains(x));
                var a = joined.OrderBy(x => Math.Abs(x.Item1 - b.Item1) + Math.Abs(x.Item2 - b.Item2)).First();
                options.Add((a, b));
            }
            var pick = options[d.Next(options.Count)];
            links.Add(pick);
            joined.Add(pick.Item2);
        }
        for (int extra = 0; extra < 2; extra++)
        {
            var a = cells[d.Next(cells.Count)];
            var b = cells.Where(x => x != a && Math.Abs(x.Item1 - a.Item1) + Math.Abs(x.Item2 - a.Item2) == 1).ToList();
            if (b.Count > 0) links.Add((a, b[d.Next(b.Count)]));
        }
        var corridor = new bool[Cols, Rows];
        foreach (var (a, b) in links)
        {
            var ra = room[a.Item1, a.Item2]!.Value;
            var rb = room[b.Item1, b.Item2]!.Value;
            int ax = (ra.C0 + ra.C1) / 2, ay = (ra.R0 + ra.R1) / 2, bx = (rb.C0 + rb.C1) / 2, by = (rb.R0 + rb.R1) / 2;
            bool across = d.Next(2) == 0;
            int mx = across ? bx : ax, my = across ? ay : by;
            Dig(corridor, ax, ay, mx, my);
            Dig(corridor, mx, my, bx, by);
        }
        bool InRoom(int c, int r) => s.Rooms.Any(q => c >= q.C0 && c <= q.C1 && r >= q.R0 && r <= q.R1);
        for (int c = 0; c < Cols; c++)
        for (int r = 0; r < Rows; r++)
            if (corridor[c, r] && !InRoom(c, r)) s.Grid[c, r] = Tile.Floor;
        // Doors where a corridor two squares wide meets a room: the squares of the opening, walled in either side.
        bool Open(int c, int r) => s.At(c, r) != Tile.Void;
        var doors = new List<(int, int)>();
        for (int c = 0; c < Cols; c++)
        for (int r = 0; r < Rows; r++)
        {
            if (!corridor[c, r] || InRoom(c, r)) continue;
            bool vertical = InRoom(c, r - 1) || InRoom(c, r + 1), horizontal = InRoom(c - 1, r) || InRoom(c + 1, r);
            // Entering through a room's top or bottom: blocked within two squares to the left and to the right.
            if (vertical && (!Open(c - 1, r) || !Open(c - 2, r) && Open(c - 1, r) && corridor[c - 1, r]) && (!Open(c + 1, r) || !Open(c + 2, r) && Open(c + 1, r) && corridor[c + 1, r])) doors.Add((c, r));
            else if (horizontal && (!Open(c, r - 1) || !Open(c, r - 2) && Open(c, r - 1) && corridor[c, r - 1]) && (!Open(c, r + 1) || !Open(c, r + 2) && Open(c, r + 1) && corridor[c, r + 1])) doors.Add((c, r));
        }
        foreach (var (c, r) in doors) s.Grid[c, r] = Tile.Door;
        // Walls round everything.
        for (int c = 0; c < Cols; c++)
        for (int r = 0; r < Rows; r++)
        {
            if (s.Grid[c, r] != Tile.Void) continue;
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                if (s.At(c + dc, r + dr) is not (Tile.Void or Tile.Wall)) s.Grid[c, r] = Tile.Wall;
        }
        // Lift pads, the recharger, and consoles, in rooms: each in a different room where it can be.
        var free = new List<int>(Enumerable.Range(0, s.Rooms.Count));
        (int, int) Spot(bool centre)
        {
            int i = free.Count > 0 ? free[d.Next(free.Count)] : d.Next(s.Rooms.Count);
            free.Remove(i);
            var q = s.Rooms[i];
            if (centre) return ((q.C0 + q.C1) / 2, (q.R0 + q.R1) / 2);
            return (q.C0 + 1 + d.Next(Math.Max(1, q.C1 - q.C0 - 1)), q.R0 + 1 + d.Next(Math.Max(1, q.R1 - q.R0 - 1)));
        }
        for (int sh = 0; sh < Shafts.Length; sh++)
            if (index >= Shafts[sh].Top && index <= Shafts[sh].Bottom)
            {
                var (c, r) = Spot(true);
                s.Grid[c, r] = Tile.Lift;
                s.Lifts.Add((c, r, sh));
            }
        {
            var (c, r) = Spot(true);
            s.Grid[c, r] = Tile.Energizer;
            s.Energizer = (c, r);
        }
        foreach (var q in s.Rooms)
            if (d.Next() < 0.5)
            {
                // A console against the room's top wall, away from doors.
                int c = q.C0 + 1 + d.Next(Math.Max(1, q.C1 - q.C0 - 1));
                if (s.At(c, q.R0 - 1) == Tile.Wall && s.At(c, q.R0) == Tile.Floor && s.At(c, q.R0 + 1) == Tile.Floor) s.Grid[c, q.R0] = Tile.Console;
            }
        // Droids: more, and of higher classes, deeper in the ship; the commander waits in the reactor.
        int count = 5 + index + (round - 1);
        int lo = 1 + index / 2, hi = Math.Min(8, 2 + index + (round - 1));
        var startRoom = index == 0 ? s.Rooms[0] : (-1, -1, -1, -1);
        for (int i = 0, tries = 0; i < count && tries < 2000; tries++)
        {
            var q = s.Rooms[d.Next(s.Rooms.Count)];
            if (q == startRoom) continue;
            int c = q.C0 + d.Next(q.C1 - q.C0 + 1), r = q.R0 + d.Next(q.R1 - q.R0 + 1);
            if (s.Grid[c, r] != Tile.Floor || s.Droids.Any(p => Math.Abs(p.Col - c) < 2 && Math.Abs(p.Row - r) < 2)) continue;
            int k = index == DeckCount - 1 && i == 0 ? 9 : lo + d.Next(hi - lo + 1);
            var nums = Classes.Numbers[k];
            s.Droids.Add(new Placed(nums[d.Next(nums.Length)], c, r));
            i++;
        }
        return s;
    }

    private static void Dig(bool[,] corridor, int x0, int y0, int x1, int y1)
    {
        int c0 = Math.Min(x0, x1), c1 = Math.Max(x0, x1), r0 = Math.Min(y0, y1), r1 = Math.Max(y0, y1);
        for (int c = c0; c <= c1 + 1; c++)
        for (int r = r0; r <= r1 + 1; r++)
            if (c >= 1 && r >= 1 && c < Cols - 1 && r < Rows - 1) corridor[c, r] = true;
    }
}
