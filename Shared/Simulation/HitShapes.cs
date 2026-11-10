using System;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// One horizontal band of a drawn shape, relative to its anchor (a player's bottom-center, a hazard's tip),
    /// for a shape that faces right. Left/Right are signed x offsets, Bottom/Top are y offsets.
    /// </summary>
    public readonly struct HitBand
    {
        public readonly int Bottom, Top, Left, Right;

        public HitBand(int bottom, int top, int left, int right)
        {
            Bottom = bottom;
            Top = top;
            Left = left;
            Right = right;
        }

        public override string ToString() => $"[{Bottom}..{Top}) x {Left}..{Right}";
    }

    /// <summary>
    /// What hazards hit: the shapes as drawn, kept apart from the movement box (PlayerState.Bounds, 800 x 1200),
    /// which still does floors, walls, dash pushes, stomps and boxes.
    /// The numbers are the sprites measured in 100-milli bands (alpha &gt;= 128, outline included, rounded up to
    /// 10): the jelly body without the head decoration (smallest of the 8 colours per band, decorations only
    /// add pixels), and the hazard sprites around their tip pivots. The crouching body is the idle body scaled
    /// 1.15 x 0.6 around the feet (the crouch row is rendered with that squash). The flat-stunned body is measured
    /// from its own sheet row (docs/work/char-motion.md). Run, jump and fall frames use the idle body.
    /// docs/work/map-select-hit.md has the original tables.
    /// </summary>
    public static class HitShapes
    {
        /// <summary>Idle body (also run/jump/fall): 1.02 wide at the belly, narrower at the feet and the head.</summary>
        public static readonly HitBand[] Body =
        {
            new HitBand(0, 100, -390, 390),
            new HitBand(100, 200, -470, 470),
            new HitBand(200, 800, -510, 510),
            new HitBand(800, 900, -460, 460),
            new HitBand(900, 1000, -410, 410),
            new HitBand(1000, 1100, -360, 360),
            new HitBand(1100, 1200, -260, 260),
        };

        /// <summary>Dash frames (long low teardrop), facing right.</summary>
        public static readonly HitBand[] Dash =
        {
            new HitBand(0, 300, -810, 830),
            new HitBand(300, 400, -720, 830),
            new HitBand(400, 500, -630, 810),
            new HitBand(500, 600, -510, 740),
            new HitBand(600, 700, -360, 640),
            new HitBand(700, 800, -170, 460),
            new HitBand(800, 900, -90, 30),
        };

        /// <summary>Knockback frames (falling over backwards), facing right; all three frames together.</summary>
        public static readonly HitBand[] Stunned =
        {
            new HitBand(0, 100, -520, 70),
            new HitBand(100, 200, -620, 350),
            new HitBand(200, 300, -680, 390),
            new HitBand(300, 400, -740, 400),
            new HitBand(400, 900, -810, 400),
            new HitBand(900, 1000, -770, 220),
            new HitBand(1000, 1100, -720, 130),
            new HitBand(1100, 1200, -680, 0),
            new HitBand(1200, 1300, -680, -350),
            new HitBand(1300, 1400, -540, -390),
        };

        public static readonly HitBand[] Crouch = Scale(Body, 115, 60);
        /// <summary>
        /// Flat stun on the ground (stomped or dash-stunned), facing right: the jelly sheet's "stun" row (lying on
        /// its back, flattened) measured over all its frames as the client draws it (x 1.1, draw_scale), 40-milli
        /// bands, body only (no decoration, no stars). Blender: build_sprites.py -- stun_table. Until 2026-11-10 it
        /// was Stunned scaled 1.35 x 0.4, which was wider than the drawing at the front bottom and the top.
        /// </summary>
        public static readonly HitBand[] Stomped =
        {
            new HitBand(0, 40, -680, -110),
            new HitBand(40, 80, -810, 180),
            new HitBand(80, 120, -900, 370),
            new HitBand(120, 160, -980, 490),
            new HitBand(160, 240, -1050, 530),
            new HitBand(240, 280, -1060, 510),
            new HitBand(280, 320, -1060, 460),
            new HitBand(320, 360, -1040, 390),
            new HitBand(360, 400, -1010, 310),
            new HitBand(400, 440, -950, 190),
            new HitBand(440, 480, -840, 30),
            new HitBand(480, 520, -570, -300),
        };

        public static readonly HitBand[] Knife =
        {
            new HitBand(0, 100, -80, 80),
            new HitBand(100, 200, -110, 110),
            new HitBand(200, 700, -120, 130),
            new HitBand(700, 1000, -90, 90),
        };

        /// <summary>Thin at the tip, widest at the top.</summary>
        public static readonly HitBand[] Icicle =
        {
            new HitBand(0, 100, -30, 40),
            new HitBand(100, 200, -50, 60),
            new HitBand(200, 300, -70, 70),
            new HitBand(300, 500, -90, 110),
            new HitBand(500, 600, -110, 130),
            new HitBand(600, 700, -120, 150),
            new HitBand(700, 800, -150, 160),
            new HitBand(800, 1000, -180, 180),
            new HitBand(1000, 1240, -220, 220),
        };

        public static readonly HitBand[] Crate =
        {
            new HitBand(0, 1210, -720, 720),
        };

        /// <summary>Tines at the bottom, thin handle above.</summary>
        public static readonly HitBand[] Fork =
        {
            new HitBand(0, 500, -190, 190),
            new HitBand(500, 600, -170, 170),
            new HitBand(600, 1200, -100, 100),
        };

        // ---- side hazards (v6): bands for one flying right, Bottom/Top around the lane centre, Left/Right behind
        // the tip (leading edge). SideHazardSpec says how they fly. Measured like the falling ones: the sprite at
        // the size the client draws it (docs/work/side-hazards-i18n.md has the table).

        /// <summary>The knife lying tip-right: the falling knife turned a quarter counterclockwise.</summary>
        public static readonly HitBand[] SideKnife = Lie(Knife);

        /// <summary>Snowball sprite at 0.5 (0.52 across). It spins while flying, so this is the circle around it.</summary>
        public static readonly HitBand[] Snowball =
        {
            new HitBand(-260, -200, -430, -90),
            new HitBand(-200, -100, -500, -20),
            new HitBand(-100, 100, -520, 0),
            new HitBand(100, 200, -500, -20),
            new HitBand(200, 260, -430, -90),
        };

        /// <summary>Warehouse crate sprite at 0.5: a solid 0.72 x 0.62 box.</summary>
        public static readonly HitBand[] SideCrate =
        {
            new HitBand(-310, 310, -720, 0),
        };

        /// <summary>Macaron sprite at 0.6 (0.6 x 0.44), shells rounded at the top.</summary>
        public static readonly HitBand[] Macaron =
        {
            new HitBand(-220, -100, -600, 0),
            new HitBand(-100, 0, -600, -10),
            new HitBand(0, 100, -600, 0),
            new HitBand(100, 200, -590, -20),
            new HitBand(200, 220, -430, -180),
        };

        /// <summary>Falling-hazard bands (tip at the bottom) turned to lie tip-right.</summary>
        private static HitBand[] Lie(HitBand[] upright)
        {
            var result = new HitBand[upright.Length];
            for (int i = 0; i < upright.Length; i++)
            {
                var h = upright[i];
                result[i] = new HitBand(h.Left, h.Right, -h.Top, -h.Bottom);
            }
            return result;
        }

        /// <summary>Widest half-width of the plain body, for bots that keep clear of hazards.</summary>
        public const int BodyHalfWidth = 510;

        public static HitBand[] OfHazard(HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Icicle: return Icicle;
                case HazardKind.Crate: return Crate;
                case HazardKind.Fork: return Fork;
                default: return Knife;
            }
        }

        /// <summary>Widest half-width of a hazard's drawn shape.</summary>
        public static int HazardHalfWidth(HazardKind kind)
        {
            int w = 0;
            foreach (var b in OfHazard(kind)) w = Math.Max(w, Math.Max(-b.Left, b.Right));
            return w;
        }

        /// <summary>
        /// The pose the client draws for this state (same priority as PlayerView). A flat stun (stomp or dash hit)
        /// is drawn flattened only once the player is on the ground; flying back after a dash hit it is the
        /// knockback pose.
        /// </summary>
        public static HitBand[] OfPlayer(in PlayerState p)
        {
            if (p.StunTicks > 0) return p.Stomped && p.Grounded ? Stomped : Stunned;
            if (p.IsDashing) return Dash;
            return p.Crouching ? Crouch : Body;
        }

        /// <summary>
        /// A hazard whose tip went from fromTipY down to toTipY this tick hits the player's drawn body.
        /// Each hazard band sweeps the whole drop, so nothing tunnels.
        /// </summary>
        public static bool Hits(in PlayerState p, HazardKind kind, int x, int fromTipY, int toTipY) =>
            Separation(p, kind, x, fromTipY, toTipY) < 0;

        /// <summary>
        /// How far apart the swept hazard and the drawn body are: the smallest, over all band pairs, of the larger
        /// of the x gap and the y gap. Negative = overlapping (a hit), 0 = edges touch.
        /// </summary>
        public static int Separation(in PlayerState p, HazardKind kind, int x, int fromTipY, int toTipY)
        {
            HitBand[] body = OfPlayer(p);
            HitBand[] hazard = OfHazard(kind);
            bool right = p.Facing >= 0;
            int best = int.MaxValue;
            foreach (var h in hazard)
            {
                int hl = x + h.Left, hr = x + h.Right;
                int hb = toTipY + h.Bottom, ht = fromTipY + h.Top;
                foreach (var b in body)
                {
                    int bl = right ? p.X + b.Left : p.X - b.Right;
                    int br = right ? p.X + b.Right : p.X - b.Left;
                    int bb = p.Y + b.Bottom, bt = p.Y + b.Top;
                    int gx = Math.Max(bl - hr, hl - br);
                    int gy = Math.Max(bb - ht, hb - bt);
                    int gap = Math.Max(gx, gy);
                    if (gap < best) best = gap;
                }
            }
            return best;
        }

        /// <summary>
        /// A side hazard flying along the lane at centre height y, whose tip went from fromX to toX this tick, against
        /// the player's drawn body. Same band rule as Separation; every band sweeps the whole move (from its place
        /// at fromX to its place at toX), so nothing tunnels. Negative = a hit.
        /// </summary>
        public static int SideHazardSeparation(in PlayerState p, SideHazardKind kind, int dir, int y, int fromX, int toX)
        {
            HitBand[] body = OfPlayer(p);
            SideHazardSpec spec = SideHazardSpec.Of(kind);
            bool right = p.Facing >= 0;
            int best = int.MaxValue;
            foreach (var h in spec.Shape)
            {
                int hl, hr, hb, ht;
                if (dir > 0)
                {
                    hl = fromX + h.Left;
                    hr = toX + h.Right;
                    hb = y + h.Bottom;
                    ht = y + h.Top;
                }
                else
                {
                    // Mirrored in x; the knife is turned the other way, which also mirrors it in y.
                    hl = toX - h.Right;
                    hr = fromX - h.Left;
                    hb = spec.FlipYWhenLeft ? y - h.Top : y + h.Bottom;
                    ht = spec.FlipYWhenLeft ? y - h.Bottom : y + h.Top;
                }
                foreach (var b in body)
                {
                    int bl = right ? p.X + b.Left : p.X - b.Right;
                    int br = right ? p.X + b.Right : p.X - b.Left;
                    int bb = p.Y + b.Bottom, bt = p.Y + b.Top;
                    int gap = Math.Max(Math.Max(bl - hr, hl - br), Math.Max(bb - ht, hb - bt));
                    if (gap < best) best = gap;
                }
            }
            return best;
        }

        public static bool SideHazardHits(in PlayerState p, SideHazardKind kind, int dir, int y, int fromX, int toX) =>
            SideHazardSeparation(p, kind, dir, y, fromX, toX) < 0;

        /// <summary>The kitchen knife (v5 rule): SideHazardSeparation with SideHazardKind.Knife.</summary>
        public static int SideKnifeSeparation(in PlayerState p, int dir, int y, int fromX, int toX) =>
            SideHazardSeparation(p, SideHazardKind.Knife, dir, y, fromX, toX);

        public static bool SideKnifeHits(in PlayerState p, int dir, int y, int fromX, int toX) =>
            SideKnifeSeparation(p, dir, y, fromX, toX) < 0;

        /// <summary>Widest across-the-blade extent of the knife on either side of its centre line.</summary>
        public const int KnifeHalfThickness = 130;

        /// <summary>The old rule: the hazard's spec box swept over the drop against the 800 x 1200 movement box.</summary>
        public static int LegacySeparation(in PlayerState p, HazardKind kind, int x, int fromTipY, int toTipY)
        {
            HazardSpec spec = HazardSpec.Of(kind);
            Aabb a = p.Bounds;
            int gx = Math.Max(a.Left - (x + spec.HalfWidth), (x - spec.HalfWidth) - a.Right);
            int gy = Math.Max(a.Bottom - (fromTipY + spec.Height), toTipY - a.Top);
            return Math.Max(gx, gy);
        }

        /// <summary>Scales bands around the anchor (percent), rounding outwards to the next 10.</summary>
        public static HitBand[] Scale(HitBand[] bands, int xPercent, int yPercent)
        {
            var result = new HitBand[bands.Length];
            for (int i = 0; i < bands.Length; i++)
            {
                var b = bands[i];
                result[i] = new HitBand(RoundOut(b.Bottom * yPercent), RoundOut(b.Top * yPercent),
                    RoundOut(b.Left * xPercent), RoundOut(b.Right * xPercent));
            }
            return result;
        }

        /// <summary>value / 100 rounded away from zero to a multiple of 10.</summary>
        private static int RoundOut(int value)
        {
            int sign = value < 0 ? -1 : 1;
            int abs = Math.Abs(value);
            return sign * ((abs + 999) / 1000 * 10);
        }
    }
}
