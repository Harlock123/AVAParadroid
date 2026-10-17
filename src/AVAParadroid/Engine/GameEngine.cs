namespace AVAParadroid.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The deck named; a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>The transfer duel.</summary>
    Duel,
    /// <summary>The duel decided: who won, for a moment.</summary>
    DuelOver,
    /// <summary>In the lift: up or down to a deck the shaft stops at.</summary>
    Lift,
    /// <summary>The link unit destroyed: a moment, then again by the recharger, or the game is over.</summary>
    Dying,
    /// <summary>Every droid on board dealt with: the ship is clear, then the next one.</summary>
    ShipClear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, DroidFire, Hit, Boom, Die, HostLost, Charge, TransferOn, TransferOff, DeckClear, ExtraLife,
    DuelStart, Pulse, Land, DuelWin, DuelLose, LiftMove, LiftGo,
    SayTransfer, SayDeckClear, SayShipClear,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2, DyingSeconds = 2.4, DuelOverSeconds = 1.8, ShipClearSeconds = 6;
    public const int Lives0 = 3, FirstExtra = 10000, ExtraEvery = 20000;
    public static readonly string[] ShipNames = ["MERIDIAN", "HALCYON STAR", "VANTAGE", "OUTWARD BOUND"];

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _actPrev, _firePrev;
    private int _dyPrev;
    private readonly int _seed;
    private int _nextExtra, _duels;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Decks cleared this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The transfer duel under way (or just decided), and whether the player won it.</summary>
    public Duel? Duel { get; private set; }
    public bool DuelWon { get; private set; }
    /// <summary>In the lift: the shaft, and the deck chosen.</summary>
    public int LiftShaft { get; private set; }
    public int LiftDeck { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || (c.Fire || c.Act) && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = FirstExtra;
        _duels = 0;
        Duel = null;
        NewShip(1);
    }

    private void NewShip(int round)
    {
        int score = World.Score;
        var ship = Shipyard.Build(ShipNames[(round - 1) % ShipNames.Length], _seed * 7919 + round * 101, round);
        Sim.Start(World, ship, round, _seed * 1000 + round);
        World.Score = score;
        SetPhase(Phase.Start);
        Say("THE " + ship.Name, round > 1 ? $"ROUND {round}" : "CLEAR EVERY DECK OF ITS DROIDS", StartSeconds + 0.5);
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase is Phase.Play or Phase.Lift) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        bool act = c.Act && !_actPrev, fire = c.Fire && !_firePrev;
        int dyEdge = c.Dy != _dyPrev ? c.Dy : 0;
        _actPrev = c.Act;
        _firePrev = c.Fire;
        _dyPrev = c.Dy;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int before = w.Score, left = w.Here.Count;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                if (left > 0 && w.Here.Count == 0) DeckCleared();
                if (w.Player.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.TransferWith is { } x)
                {
                    Duel = new Duel(x, w.Player.Class, _seed * 31 + _duels++);
                    SetPhase(Phase.Duel);
                    Music(false);
                    Play(Sfx.DuelStart);
                    Play(Sfx.SayTransfer);
                }
                else if (w.LiftRequest)
                {
                    var lift = w.Deck.Lifts.First(l => (int)(w.Player.X / Field.Tile) == l.Col && (int)(w.Player.Y / Field.Tile) == l.Row);
                    LiftShaft = lift.Shaft;
                    LiftDeck = w.DeckIndex;
                    SetPhase(Phase.Lift);
                    Play(Sfx.LiftGo);
                }
                else if (w.Left == 0) ShipCleared();
                break;
            }
            case Phase.Duel:
            {
                var d = Duel!;
                if (dyEdge != 0) d.Move(dyEdge);
                if ((fire || act) && d.Send()) Play(Sfx.Pulse);
                if (d.Step(dt)) Play(Sfx.Land);
                if (d.Over)
                {
                    int mine = d.Held(0), theirs = d.Held(1);
                    if (mine == theirs)
                    {
                        // Deadlock: again.
                        Duel = new Duel(d.Droid, w.Player.Class, _seed * 31 + _duels++);
                        Say("DEADLOCK", "AGAIN", 1.5);
                        break;
                    }
                    DuelWon = mine > theirs;
                    SetPhase(Phase.DuelOver);
                    Play(DuelWon ? Sfx.DuelWin : Sfx.DuelLose);
                }
                break;
            }
            case Phase.DuelOver:
                if (PhaseTime > DuelOverSeconds)
                {
                    var x = Duel!.Droid;
                    int left = w.Here.Count;
                    if (DuelWon) Sim.Take(w, x);
                    else
                    {
                        // Lost: a host is burnt out, the bare link unit is destroyed.
                        w.Player.Safe = 0;
                        Sim.Hurt(w, 9999, "lost a transfer");
                        x.Angry = x.Class >= 3;
                        x.FireCool = 1.5;
                    }
                    Duel = null;
                    if (w.Player.Dead) { SetPhase(Phase.Dying); break; }
                    w.Player.Safe = 1.2;
                    SetPhase(Phase.Play);
                    Music(true);
                    if (left > 0 && w.Here.Count == 0) DeckCleared();
                    if (w.Left == 0) ShipCleared();
                }
                break;
            case Phase.Lift:
            {
                var stops = w.Ship.Stops(LiftShaft).ToList();
                if (dyEdge != 0)
                {
                    int i = Math.Clamp(stops.IndexOf(LiftDeck) + dyEdge, 0, stops.Count - 1);
                    if (stops[i] != LiftDeck) { LiftDeck = stops[i]; Play(Sfx.LiftMove); }
                }
                if ((act || fire) && PhaseTime > 0.3)
                {
                    if (LiftDeck != w.DeckIndex) Sim.Board(w, LiftDeck, LiftShaft);
                    w.Player.ActPrev = true;
                    SetPhase(Phase.Play);
                    if (LiftDeck != w.DeckIndex || true) Say("DECK " + (LiftDeck + 1) + "  " + w.Deck.Name, w.Cleared(LiftDeck) ? "CLEARED" : $"{w.Here.Count} DROIDS", 1.6);
                    Play(Sfx.LiftGo);
                }
                break;
            }
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { GameOver(); return; }
                    w.Player = new Player();
                    Sim.Board(w, w.DeckIndex, null);
                    w.Player.Safe = 2;
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.ShipClear:
                if (PhaseTime > ShipClearSeconds) NewShip(w.Round + 1);
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void DeckCleared()
    {
        Cleared++;
        Say("DECK CLEARED", $"BONUS {1000 * (World.DeckIndex + 1)}", 2.2);
        Play(Sfx.SayDeckClear);
        ExtraLives();
    }

    private void ShipCleared()
    {
        World.Score += 5000 * World.Round;
        ExtraLives();
        SetPhase(Phase.ShipClear);
        Music(false);
        Play(Sfx.Victory);
        Play(Sfx.SayShipClear);
        Say("THE " + World.Ship.Name + " IS CLEAR", $"BONUS {5000 * World.Round}", ShipClearSeconds);
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight onto the ship, past its name.</summary>
    public void Go(int round = 1)
    {
        NewGame();
        State = GameState.Playing;
        if (round > 1) NewShip(round);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
