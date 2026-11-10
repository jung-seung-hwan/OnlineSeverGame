using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>Per-map side hazards, v6 (docs/work/side-hazards-i18n.md): knife, snowball, crate, macaron.</summary>
public class SideHazardTests
{
    private static GameRoom PlayingRoom(int players, out byte[] ids, Stage stage, bool autoSide = false)
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 1, AutoSpawnKnives = false, AutoSpawnSideKnives = autoSide, Seed = 3, Stage = stage,
        });
        ids = Enumerable.Range(0, players).Select(i => room.AddPlayer("P" + i)).ToArray();
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    [Fact]
    public void Every_map_has_its_own_side_hazard()
    {
        Assert.Equal(SideHazardKind.Knife, Stage.Kitchen.SideHazard);
        Assert.Equal(SideHazardKind.Snowball, Stage.Winter.SideHazard);
        Assert.Equal(SideHazardKind.Crate, Stage.Warehouse.SideHazard);
        Assert.Equal(SideHazardKind.Macaron, Stage.Dessert.SideHazard);
        Assert.Equal(4, Stage.All.Select(s => s.SideHazard).Distinct().Count());
        // Melted copies of the dessert shop keep it.
        Assert.Equal(SideHazardKind.Macaron, Stage.Dessert.AtRoundTick(3000).SideHazard);
    }

    [Fact]
    public void The_four_kinds_differ_in_size_and_speed()
    {
        var specs = SideHazardSpec.All;
        Assert.Equal(4, specs.Select(s => s.Speed).Distinct().Count());
        Assert.Equal(4, specs.Select(s => s.Length).Distinct().Count());
        Assert.Equal(4, specs.Select(s => s.HalfThickness).Distinct().Count());
        // Snowball fastest (and warned longest), crate slowest.
        Assert.Equal(SideHazardKind.Snowball, specs.OrderByDescending(s => s.Speed).First().Kind);
        Assert.Equal(SideHazardKind.Crate, specs.OrderBy(s => s.Speed).First().Kind);
        Assert.True(SideHazardSpec.Snowball.WarningTicks > SideHazardSpec.Knife.WarningTicks);
    }

    [Fact]
    public void Shapes_start_at_the_tip_and_span_the_length()
    {
        foreach (var spec in SideHazardSpec.All)
        {
            Assert.Equal(0, spec.Shape.Max(b => b.Right));
            Assert.Equal(-spec.Length, spec.Shape.Min(b => b.Left));
            Assert.All(spec.Shape, b => Assert.True(b.Bottom < b.Top && b.Left < b.Right, $"{spec.Kind} {b}"));
        }
        // The knife lying flat is exactly the v5 rule: the falling knife's bands turned a quarter.
        Assert.Equal(130, SideHazardSpec.Knife.HalfThickness);
        Assert.Equal(new HitBand(-120, 130, -700, -200), HitShapes.SideKnife[2]);
    }

    [Fact]
    public void Kitchen_knife_judges_exactly_like_v5()
    {
        // v5 SideKnifeSeparation, inlined, against the new generic rule.
        static int V5(in PlayerState p, int dir, int y, int fromX, int toX)
        {
            HitBand[] body = HitShapes.OfPlayer(p);
            bool right = p.Facing >= 0;
            int best = int.MaxValue;
            foreach (var h in HitShapes.Knife)
            {
                int hl, hr, hb, ht;
                if (dir > 0) { hl = fromX - h.Top; hr = toX - h.Bottom; hb = y + h.Left; ht = y + h.Right; }
                else { hl = toX + h.Bottom; hr = fromX + h.Top; hb = y - h.Right; ht = y - h.Left; }
                foreach (var b in body)
                {
                    int bl = right ? p.X + b.Left : p.X - b.Right;
                    int br = right ? p.X + b.Right : p.X - b.Left;
                    int bb = p.Y + b.Bottom, bt = p.Y + b.Top;
                    best = Math.Min(best, Math.Max(Math.Max(bl - hr, hl - br), Math.Max(bb - ht, hb - bt)));
                }
            }
            return best;
        }
        var poses = new[]
        {
            PlayerState.SpawnAt(12000, 0, 1),
            new PlayerState { X = 11700, Y = 300, Facing = -1 },
            new PlayerState { X = 12000, Y = 0, Grounded = true, Facing = 1, Crouching = true },
            new PlayerState { X = 12000, Y = 0, Grounded = true, Facing = -1, StunTicks = 9, Stomped = true },
        };
        foreach (var p in poses)
            for (int y = 0; y <= 1600; y += 70)
                foreach (int dir in new[] { 1, -1 })
                    for (int x = 9000; x <= 15000; x += 230)
                        Assert.Equal(V5(p, dir, y, x, x + dir * 400), HitShapes.SideHazardSeparation(p, SideHazardKind.Knife, dir, y, x, x + dir * 400));
    }

    [Theory]
    [InlineData(SideHazardKind.Knife)]
    [InlineData(SideHazardKind.Snowball)]
    [InlineData(SideHazardKind.Crate)]
    [InlineData(SideHazardKind.Macaron)]
    public void Each_kind_warns_then_flies_across_at_its_own_speed(SideHazardKind kind)
    {
        var stage = Stage.All.Single(s => s.SideHazard == kind);
        var spec = SideHazardSpec.Of(kind);
        var k = SideKnifeState.Warn(1, 1, 1000, stage);
        Assert.Equal(kind, k.Kind);
        Assert.Equal(-spec.Length, k.X);
        Assert.Equal(spec.WarningTicks, k.Timer);
        for (int i = 0; i < spec.WarningTicks; i++)
        {
            Assert.True(SideKnifePhysics.Step(ref k, stage.Width, out bool dangerous, out _));
            Assert.False(dangerous);
        }
        Assert.Equal(SideKnifePhase.Flying, k.Phase);
        int flown = 0;
        while (SideKnifePhysics.Step(ref k, stage.Width, out _, out int from))
        {
            Assert.Equal(spec.Speed, k.X - from);
            flown++;
        }
        // From one length outside one wall until the tail is one length outside the other.
        int distance = stage.Width + 3 * spec.Length;
        Assert.InRange(flown, distance / spec.Speed - 1, distance / spec.Speed + 1);

        var left = SideKnifeState.Warn(2, -1, 1000, stage);
        Assert.Equal(stage.Width + spec.Length, left.X);
        Assert.Equal(left.X - 2 * spec.Speed, SideKnifePhysics.Advance(left, spec.WarningTicks + 2, stage.Width).X);
    }

    [Theory]
    [InlineData(SideHazardKind.Knife)]
    [InlineData(SideHazardKind.Snowball)]
    [InlineData(SideHazardKind.Crate)]
    [InlineData(SideHazardKind.Macaron)]
    public void Head_lane_kills_standing_spares_crouching_and_low_lane_kills_both(SideHazardKind kind)
    {
        var spec = SideHazardSpec.Of(kind);
        var stand = PlayerState.SpawnAt(12000, 0, 1);
        var crouch = stand;
        crouch.Crouching = true;
        foreach (int dir in new[] { 1, -1 })
        {
            int from = 12000 - dir * 3000, to = 12000 + dir * 3000;
            Assert.True(HitShapes.SideHazardHits(stand, kind, dir, spec.HeadOffset, from, to));
            Assert.False(HitShapes.SideHazardHits(crouch, kind, dir, spec.HeadOffset, from, to));
            Assert.True(HitShapes.SideHazardHits(stand, kind, dir, spec.LowOffset, from, to));
            Assert.True(HitShapes.SideHazardHits(crouch, kind, dir, spec.LowOffset, from, to));
            // Clearance under a head-height one for a crouching body: at least 0.08.
            Assert.True(HitShapes.SideHazardSeparation(crouch, kind, dir, spec.HeadOffset, from, to) >= 80);
        }
    }

    [Theory]
    [InlineData(SideHazardKind.Snowball)]
    [InlineData(SideHazardKind.Crate)]
    [InlineData(SideHazardKind.Macaron)]
    public void The_tip_hits_only_when_it_reaches_the_drawn_body(SideHazardKind kind)
    {
        var spec = SideHazardSpec.Of(kind);
        var p = PlayerState.SpawnAt(12000, 0, 1);
        int y = spec.LowOffset;
        // Belly is 0.51 from the centre at the lane's height.
        Assert.False(HitShapes.SideHazardHits(p, kind, 1, y, 9000, 12000 - 520));
        Assert.True(HitShapes.SideHazardHits(p, kind, 1, y, 9000, 12000 - 400));
        Assert.False(HitShapes.SideHazardHits(p, kind, -1, y, 15000, 12000 + 520));
        Assert.True(HitShapes.SideHazardHits(p, kind, -1, y, 15000, 12000 + 400));
        // Passed through in one tick: still a hit (the whole move is swept).
        Assert.True(HitShapes.SideHazardHits(p, kind, 1, y, 9000, 16000));
        // Gone past: the tail has left the body.
        int tail = 12000 + 520 + spec.Length + 10;
        Assert.False(HitShapes.SideHazardHits(p, kind, 1, y, tail, tail + spec.Speed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Room_spawns_the_maps_own_side_hazard_on_its_lanes(int stageId)
    {
        var stage = Stage.ById((byte)stageId);
        var room = PlayingRoom(1, out var ids, stage, autoSide: true);
        var p = room.GetPlayer(ids[0]);
        var seen = new Dictionary<ushort, SideKnifeState>();
        for (int i = 0; i < 70 * TickRate; i++)
        {
            p.Alive = true;
            p.State = PlayerState.SpawnAt(12000, 9000, 1); // high above every lane
            room.Tick();
            foreach (var k in room.SideKnives) seen.TryAdd(k.Id, k);
        }
        Assert.NotEmpty(seen);
        Assert.All(seen.Values, k => Assert.Equal(stage.SideHazard, k.Kind));
        Assert.All(seen.Values, k => Assert.Contains(k.Y, SideKnifePhysics.Lanes(stage)));
    }

    [Fact]
    public void Snowball_kills_and_the_round_records_a_side_death()
    {
        var room = PlayingRoom(2, out var ids, Stage.Winter);
        var standing = room.GetPlayer(ids[0]);
        var ducking = room.GetPlayer(ids[1]);
        standing.State = PlayerState.SpawnAt(3000, 0, 1);
        ducking.State = PlayerState.SpawnAt(20000, 0, 1);
        var k = room.SpawnSideKnife(1, SideHazardSpec.Snowball.HeadOffset);
        Assert.Equal(SideHazardKind.Snowball, k.Kind);
        for (int i = 0; i < 120 && room.State == RoomState.Playing; i++)
        {
            room.QueueInput(ids[1], (uint)(i + 1), InputButtons.Down);
            room.Tick();
        }
        Assert.False(standing.Alive);
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Equal(EliminationCause.SideKnife, room.LastRoundEnd.Results.Single(r => r.PlayerId == ids[0]).Cause);
        Assert.Contains(ids[1], room.LastRoundEnd.Winners);
    }

    [Fact]
    public void Snapshot_carries_the_kind()
    {
        var s = new SnapshotPacket();
        s.SideKnives.Add(new SideKnifeState { Id = 9, Kind = SideHazardKind.Macaron, Dir = 1, Phase = SideKnifePhase.Warning, X = -600, Y = 1020, Timer = 20 });
        var back = SnapshotPacket.Read(new PacketReader(PacketCodec.EncodeFrame(s).AsSpan(3).ToArray()));
        var k = Assert.Single(back.SideKnives);
        Assert.Equal((SideHazardKind.Macaron, 1, SideKnifePhase.Warning, -600, 1020, 20),
            (k.Kind, (int)k.Dir, k.Phase, k.X, k.Y, (int)k.Timer));
        Assert.Equal(6, ProtocolConstants.Version);
    }

    [Fact]
    public void Zero_kind_reads_as_the_knife()
    {
        var k = new SideKnifeState { Dir = 1, Phase = SideKnifePhase.Flying, X = 0 };
        Assert.Same(SideHazardSpec.Knife, k.Spec);
        Assert.Equal(-SideKnifeLength, k.TailX);
    }
}
