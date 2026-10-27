namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>Axis-aligned box in milli-units. Touching edges do not count as overlap.</summary>
    public readonly struct Aabb
    {
        public readonly int Left, Bottom, Right, Top;

        public Aabb(int left, int bottom, int right, int top)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
        }

        public bool Overlaps(in Aabb o) => Left < o.Right && o.Left < Right && Bottom < o.Top && o.Bottom < Top;
    }
}
