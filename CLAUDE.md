# OnlineSeverGame

8-player 2D side-view survival game: knives fall from above (with a warning marker), players dodge by moving, jumping and dashing (2 s cooldown); a dash that hits another player knocks them back, landing on someone's head stuns them, holding Down crouches (Down + Jump drops through a platform), and some maps have boxes to push and decorations that sway. Last survivor wins. Hazards hit the drawn shapes (`Shared/Simulation/HitShapes.cs`), not the movement box.

- Server: C# .NET 10 console app, raw TCP sockets + threads, 20 ticks/s, server-authoritative.
- Client: Unity 6000.6.3f1 (2D) in `Client/`. Sends inputs only; predicts its own player, interpolates others.
- Shared: `Shared/` holds packets, framing, stage data and the deterministic physics step used by both sides.
- Protocol, units, physics constants, round rules and the prediction/sequence rules: `docs/protocol.md`.

## Layout and ownership

| Path | What | Owner session |
|---|---|---|
| `Shared/` | Packets, framing, stage, physics. Unity local package (`package.json`, `.asmdef`) | Core game server |
| `Server/GameServer/` | Server: accept thread, per-connection receive/send threads, game loop, `GameRoom` | Core game server |
| `Server/GameServer.Tests/` | xUnit tests (protocol, framing, physics, knives, pushes, rounds) | Core game server |
| `Tools/BotClient/` | Console bots that connect N times and send random input | Core game server |
| `docs/protocol.md` | Protocol spec | Core game server |
| `docs/README.md` | Doc index (each session edits only its own row) | Shared |
| `docs/work/<task>.md` | Per-task work document | The session doing that task |
| `Client/` | Unity project | Unity client |
| `OnlineSeverGame.slnx`, `Directory.Build.props`, `.gitignore`, `CLAUDE.md` | Build / repo setup | Core game server |

Unity generates `.meta` files inside `Shared/`; commit them together with `Shared/`.

## Commands (repo root)

- Build: `dotnet build`
- Tests: `dotnet test`
- Run server: `dotnet run --project Server/GameServer -- --port 7777 --min-players 2`
  - options: `--port N`, `--min-players N` (1..8, default 2), `--seed N`, `--exit-after-rounds N`, `--verbose` (log every dash, push, stomp, box hit, near miss and hit-rule disagreement; default logs only the per-round counts), `--maps cycle|random` (starting rotation, default cycle), `--map N` (start on map 1..4, fixed), `--legacy-hitbox` (judge hazards with the old boxes, for before/after comparisons)
  - In game the host (earliest player still connected) changes the next map and rotation: buttons on the waiting and result screens, or keys `1`~`4` (next map) and `R` (cycle → random → fixed). It applies from the next round; see `docs/work/map-select-hit.md`.
- Run 8 bots: `dotnet run --project Tools/BotClient -- --count 8`
  - options: `--host`, `--port`, `--count N`, `--name Bot`, `--seed N`, `--rounds N` (exit after N rounds), `--drop-one-after SECONDS` (abruptly disconnect the last bot that many seconds into the first round), `--duration SECONDS`, `--ai dodge|random` (default `dodge`: bots notice knife warnings after a 2-8 tick reaction time and get out from under them; both wander, jump, dash, crouch and drop off platforms at random)
- Scripted check (two terminals): server with `--exit-after-rounds 2`, bots with `--rounds 2 --drop-one-after 5`.
- Client test switches: `Client/Build/Windows/Client.exe -server 127.0.0.1:7777 -autoconnect -autoplay [chase|stomp|mash]` (see `docs/work/gameplay-2.md`).

Build output goes to `artifacts/` (see `Directory.Build.props`), never into `Shared/`, so Unity does not pick up generated files.

## Rules

- No high-level networking libraries: sockets, framing, threads and queues are hand-written. NuGet is used only for the test framework.
- `Shared/` must stay Unity-compatible: C# 9, netstandard2.1 APIs only, no `record`/`init`/global usings/file-scoped namespaces, no `UnityEngine`. The csproj enforces the language version and target.
- Physics is integer milli-units (1 world unit = 1000) so server and client results are bit-identical. Never introduce floats into `Shared/Simulation`.
- Changing a packet layout: bump `ProtocolConstants.Version` and update `docs/protocol.md` in the same commit.
- `GameRoom` is pure game logic driven by the game-loop thread only; keep sockets and locks out of it so it stays unit-testable.
- Tests live in `Server/GameServer.Tests/` (xUnit).
- Every task keeps a work document in `docs/work/<task>.md` (Korean), created when the task starts and updated in the same commit as each change: how it plays, how it is implemented and why, how to run it, what was verified vs. not, and a dated change log. Register it in `docs/README.md` (each session edits only its own row). The user reads these to follow the work.
- Commits: stage only your own paths (`git add <path>`), never `git add -A`; do not push unless the user asks.
