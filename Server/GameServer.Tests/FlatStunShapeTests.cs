using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// docs/work/char-motion.md: HitShapes.Stomped is the flat-stun drawing (the jelly sheet's stun row as the client
/// draws it), not the knockback bands squashed 1.35 x 0.4 any more, which reached past the drawing in front.
/// </summary>
public class FlatStunShapeTests
{
    private static PlayerState Flat(int facing)
    {
        var p = PlayerState.SpawnAt(10000, 0, (sbyte)facing);
        p.StunTicks = 10;
        p.Stomped = true;
        Assert.Same(HitShapes.Stomped, HitShapes.OfPlayer(p));
        return p;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Empty_space_in_front_of_the_flattened_body_is_safe(int facing)
    {
        var p = Flat(facing);
        // A knife stopping with its tip at 0.29 just in front: the drawing reaches 0.46 forward at that height,
        // the knife tip starts at 0.50. The old squashed knockback bands reached 0.54 there and killed.
        Assert.False(HitShapes.Hits(p, HazardKind.Knife, 10000 + facing * 580, 3000, 290));
        var old = HitShapes.Scale(HitShapes.Stunned, 135, 40);
        Assert.Equal(540, old[4].Right);
        Assert.True(HitShapes.Hits(p, HazardKind.Knife, 10000 + facing * 500, 3000, 290));
    }

    [Fact]
    public void Lying_backwards_reaches_far_behind_and_is_low()
    {
        var p = Flat(1);
        Assert.True(HitShapes.Hits(p, HazardKind.Knife, 10000 - 1100, 3000, 0));   // tail end of the drawing
        Assert.False(HitShapes.Hits(p, HazardKind.Knife, 10000 - 1250, 3000, 0));
        Assert.False(HitShapes.Hits(p, HazardKind.Knife, 10000, 3000, 530));      // above the 0.52 top
        Assert.True(HitShapes.Hits(p, HazardKind.Knife, 10000, 3000, 400));
    }

    [Fact]
    public void Bands_are_ordered_and_contiguous()
    {
        var bands = HitShapes.Stomped;
        Assert.Equal(0, bands[0].Bottom);
        for (int i = 1; i < bands.Length; i++) Assert.Equal(bands[i - 1].Top, bands[i].Bottom);
        Assert.All(bands, b => Assert.True(b.Left < b.Right));
    }
}
