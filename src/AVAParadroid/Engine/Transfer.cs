namespace AVAParadroid.Engine;

/// <summary>What a wire on one side of the transfer duel does with a pulse sent down it.</summary>
public enum Wire
{
    /// <summary>Straight across to its cell in the middle.</summary>
    Straight,
    /// <summary>Burnt out: a pulse sent down it is wasted.</summary>
    Dead,
    /// <summary>Splits to its own cell and the one above.</summary>
    ForkUp,
    /// <summary>Splits to its own cell and the one below.</summary>
    ForkDown,
}

/// <summary>
/// The transfer duel: twelve rows of wiring each side, a column of cells down the middle, and a few pulses each.
/// A pulse sent along a row takes its cell (or two, on a fork) for its side; when time runs out, whoever holds
/// more cells takes control. The player is on the left, in yellow; the droid on the right, in violet.
/// </summary>
public sealed class Duel
{
    public const int Rows = 12;
    public const double Time = 10, Travel = 0.45;
    public readonly Wire[] Left = new Wire[Rows], Right = new Wire[Rows];
    /// <summary>Who holds each cell in the middle: 0 the player, 1 the droid.</summary>
    public readonly int[] Cells = new int[Rows];
    public int PlayerPulses, DroidPulses, Cursor = Rows / 2;
    public double Clock;
    /// <summary>Pulses on their way: which side, which row, and when they arrive.</summary>
    public readonly List<(int Side, int Row, double At)> Flying = [];
    /// <summary>When each row of each side was last powered (for the drawing).</summary>
    public readonly double[,] Lit = new double[2, Rows];
    public readonly Droid Droid;
    private Dice _dice;
    private readonly List<double> _droidAt = [];

    public Duel(Droid droid, int playerClass, int seed)
    {
        Droid = droid;
        _dice = new Dice(seed);
        Wire Roll(int r)
        {
            double p = _dice.Next();
            if (p < 0.16) return Wire.Dead;
            if (p < 0.27 && r > 0) return Wire.ForkUp;
            if (p < 0.38 && r < Rows - 1) return Wire.ForkDown;
            return Wire.Straight;
        }
        for (int r = 0; r < Rows; r++)
        {
            Left[r] = Roll(r);
            Right[r] = Roll(r);
            Cells[r] = r % 2;
            Lit[0, r] = Lit[1, r] = -9;
        }
        PlayerPulses = Classes.PlayerPulses(playerClass);
        DroidPulses = Classes.DroidPulses(droid.Class);
        // The droid sends its pulses at moments of its own choosing, mostly early.
        for (int i = 0; i < DroidPulses; i++) _droidAt.Add(_dice.Between(0.8, Time - 1.5) * (0.5 + 0.5 * _dice.Next()));
        _droidAt.Sort();
    }

    public bool Over => Clock >= Time && Flying.Count == 0;
    public int Held(int side) => Cells.Count(c => c == side);

    /// <summary>The cells a pulse down a row reaches.</summary>
    public static IEnumerable<int> Reach(Wire w, int r) => w switch
    {
        Wire.Dead => [],
        Wire.ForkUp => [r, r - 1],
        Wire.ForkDown => [r, r + 1],
        _ => [r],
    };

    /// <summary>The player sends a pulse along the row under the cursor.</summary>
    public bool Send()
    {
        if (PlayerPulses <= 0 || Clock >= Time) return false;
        PlayerPulses--;
        Flying.Add((0, Cursor, Clock + Travel));
        Lit[0, Cursor] = Clock;
        return true;
    }

    public void Move(int dy) => Cursor = Math.Clamp(Cursor + dy, 0, Rows - 1);

    /// <summary>A step of the duel; returns true when a pulse lands (for a sound).</summary>
    public bool Step(double dt)
    {
        Clock += dt;
        // The droid's pulses: at a row whose cell it doesn't hold, if it can see one (it's not too clever).
        while (_droidAt.Count > 0 && _droidAt[0] <= Clock && DroidPulses > 0 && Clock < Time)
        {
            _droidAt.RemoveAt(0);
            DroidPulses--;
            var good = Enumerable.Range(0, Rows).Where(r => Right[r] != Wire.Dead && Cells[r] == 0).ToList();
            int row = good.Count > 0 && _dice.Next() < 0.7 ? good[_dice.Next(good.Count)] : _dice.Next(Rows);
            Flying.Add((1, row, Clock + Travel));
            Lit[1, row] = Clock;
        }
        bool landed = false;
        for (int i = 0; i < Flying.Count; i++)
        {
            var (side, row, at) = Flying[i];
            if (at > Clock) continue;
            foreach (int r in Reach(side == 0 ? Left[row] : Right[row], row)) Cells[r] = side;
            Flying.RemoveAt(i--);
            landed = true;
        }
        return landed;
    }

    /// <summary>The row where a pulse now would gain the player most (for the computer player): new cells taken, net.</summary>
    public int BestRow(out int gain)
    {
        int best = -1;
        gain = 0;
        for (int r = 0; r < Rows; r++)
        {
            int g = Reach(Left[r], r).Count(c => Cells[c] == 1);
            if (g > gain) { gain = g; best = r; }
        }
        return best;
    }
}
