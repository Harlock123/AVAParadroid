namespace AVAParadroid.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>A droid of the ship's crew.</summary>
public sealed class Droid
{
    public const double Radius = 14;
    public int Number, Id;
    public int Class => Number / 100;
    public double X, Y, Hp, MaxHp, FireCool, T, Hurt, Stuck;
    /// <summary>Where it's wandering to.</summary>
    public double Tx, Ty;
    /// <summary>Shot at, and shooting back (the higher classes always are).</summary>
    public bool Angry, Dead;
}

/// <summary>The player: the link unit 001 on its own, or in control of a droid it has taken over.</summary>
public sealed class Player
{
    public const double Radius = 13, TransferTime = 8;
    public int Number = 1;
    public int Class => Number / 100;
    public double X, Y, Hp = Classes.MaxHp(0), FireCool, Transfer, Safe, Hurt, Charging;
    public int Fx = 1, Fy;
    public bool Dead, ActPrev;
    public double MaxHp => Classes.MaxHp(Class);
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy, Damage, Life = 2.5;
    public bool FromPlayer, Dead;
}

public enum EffectKind { Score, Burst, Word, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>Everything on board that moves or changes: the decks' droids (each deck keeps its own), shots, the score.</summary>
public sealed class World
{
    public ShipDef Ship = Shipyard.Build("MERIDIAN", 1);
    public int DeckIndex, Round = 1, Score, NextId;
    public DeckDef Deck => Ship.Decks[DeckIndex];
    public Player Player = new();
    /// <summary>Each deck's droids, as they are now.</summary>
    public List<Droid>[] Droids = [];
    public List<Droid> Here => Droids[DeckIndex];
    public List<Bullet> Bullets = [];
    public List<Effect> Effects = [];
    public double Clock;
    public Dice Dice;
    public List<Sfx> Heard = [];
    public string Cause = "";
    /// <summary>Set by a step: the player stepped on a lift and pressed the button; the player touched a droid in transfer.</summary>
    public bool LiftRequest;
    public Droid? TransferWith;

    public bool Cleared(int deck) => Droids[deck].Count == 0;
    public int Left => Droids.Sum(d => d.Count);
}
