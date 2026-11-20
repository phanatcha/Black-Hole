using System;

namespace SilkySeal.Kerr
{
    /// <summary>Double-precision 3-vector. Core has no UnityEngine reference, and Unity's Vector3 is float.</summary>
    public readonly struct Vector3d
    {
        public readonly double X, Y, Z;

        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator -(Vector3d a) => new Vector3d(-a.X, -a.Y, -a.Z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator *(double s, Vector3d a) => a * s;
        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.X / s, a.Y / s, a.Z / s);

        /// <summary>Euclidean dot product of the coordinate components (not the spacetime metric).</summary>
        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public double Length => Math.Sqrt(Dot(this, this));

        public Vector3d Normalized => this / Length;

        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }
}
