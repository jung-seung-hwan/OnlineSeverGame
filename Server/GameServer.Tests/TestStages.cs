using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// Plain arenas for tests of general rules (physics, knives, pushes, rounds) that need fixed, simple geometry and
/// no map event. Arena is the first kitchen layout (before docs/work/map-layout.md): two low platforms (x 3~9 and
/// 15~21, top 2.4), one high platform (x 9~15, top 4.8), a butter block at x 12. The real maps are tested in
/// MapTests / MapLayoutTests.
/// </summary>
public static class TestStages
{
    public static readonly Stage Arena = new Stage(
        id: 1,
        name: "Arena",
        width: 24000,
        viewHeight: 13500,
        knifeSpawnY: 14000,
        platforms: new[]
        {
            new Platform(3000, 9000, 2400),
            new Platform(15000, 21000, 2400),
            new Platform(9000, 15000, 4800),
        },
        spawnPoints: new[]
        {
            new SpawnPoint(1500, 0, 1),
            new SpawnPoint(22500, 0, -1),
            new SpawnPoint(4500, 0, 1),
            new SpawnPoint(19500, 0, -1),
            new SpawnPoint(7500, 0, 1),
            new SpawnPoint(16500, 0, -1),
            new SpawnPoint(10500, 0, 1),
            new SpawnPoint(13500, 0, -1),
        },
        pushables: new[] { new PushableSpawn(12000, 0, 1200, 1000) });
}
