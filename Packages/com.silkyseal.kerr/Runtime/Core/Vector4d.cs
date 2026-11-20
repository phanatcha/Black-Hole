namespace SilkySeal.Kerr
{
    /// <summary>
    /// Double-precision 4-vector (t, x, y, z). Whether it holds upper (vector) or lower (covector)
    /// components is up to the caller; <see cref="KerrSchild.Lower"/> and <see cref="KerrSchild.Raise"/> convert.
    /// </summary>
    public readonly struct Vector4d
    {
        public readonly double T, X, Y, Z;

        public Vector4d(double t, double x, double y, double z) { T = t; X = x; Y = y; Z = z; }

        public Vector4d(double t, Vector3d spatial) : this(t, spatial.X, spatial.Y, spatial.Z) { }

        public Vector3d Spatial => new Vector3d(X, Y, Z);

        public static Vector4d operator +(Vector4d a, Vector4d b) => new Vector4d(a.T + b.T, a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector4d operator -(Vector4d a, Vector4d b) => new Vector4d(a.T - b.T, a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector4d operator *(Vector4d a, double s) => new Vector4d(a.T * s, a.X * s, a.Y * s, a.Z * s);
        public static Vector4d operator *(double s, Vector4d a) => a * s;

        public override string ToString() => $"({T:R}, {X:R}, {Y:R}, {Z:R})";
    }
}
