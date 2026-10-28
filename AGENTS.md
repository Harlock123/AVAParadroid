# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A top-down shooter and strategy game in the style of Graftgold's 1985 Commodore 64 game Paradroid, in C# on
.NET 10 and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the ship and the graphics are made in code, and the sounds, tunes and the ship's
spoken voice are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAParadroid.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAParadroid.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVAParadroid             # play it; --no-audio for silence
PARADROID_SHOTS=$PWD/Images dotnet test tests/AVAParadroid.Tests --filter ScreenshotTests   # refresh the README's pictures
PARADROID_PROBE=1 dotnet test tests/AVAParadroid.Tests --filter PilotProbe   # how the computer player clears the ship ("lazy" too)
python3 tools/make_sounds.py                      # rebuild src/AVAParadroid/Sounds (standard-library Python only)
```

The probe plays seeds 1, 2 and 3 unless `PARADROID_SEEDS` lists others (for example `PARADROID_SEEDS=4,5`).

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAParadroid/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the steps between them: a
    deck starting, the transfer duel, the lift.
  - `Sim.cs` steps a deck: the player moving, shooting and charging, the droids, shots and doors. It only asks
    for transfers and lifts; the game does them.
  - `Transfer.cs` is the transfer duel (`Duel`).
  - `Ship.cs` has `Controls`, the screen measures (`Field`), the six decks (built from a seed), the lift shafts and
    the droid classes. `World.cs` has the world's state and its seeded random source, `Dice`.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAParadroid/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, a window onto
  the deck that follows the player, and runs the 60 Hz timer.
- `src/AVAParadroid/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAParadroid.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `ShipTests` checks every deck can be
  walked, every lift joins up, and the computer player clears the ship. `PilotProbe` does nothing unless
  `PARADROID_PROBE` is set.
- `tools/`: `make_sounds.py`, and `speech.py`, a small formant speech synthesiser it uses for the ship's voice.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the world's seeded `Dice` (`w.Dice`), or
  the duel's own, never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `dy` sign the engine
  expects (negative is up the screen). Test new controls through `InputHandler`, not by setting `Controls`
  directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAParadroid`); tests load
  the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change a deck, `ShipTests` must still pass.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds and speech go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
