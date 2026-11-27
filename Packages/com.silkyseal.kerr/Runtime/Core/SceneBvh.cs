using System;

namespace SilkySeal.Kerr
{
    /// <summary>Where a segment met a triangle: T along the segment in [0, 1], U and V the weights of its second and third vertex.</summary>
    public struct SceneHit
    {
        public double T, U, V;
        /// <summary>Index of the triangle in the mesh as given, not in the tree's order.</summary>
        public int Triangle;
    }

    /// <summary>
    /// A bounding-volume tree over a triangle soup, for testing a curved light path one straight step at a time.
    /// Laid out for the GPU: children of a node sit side by side, leaves hold a run of <see cref="Order"/>.
    /// Build O(n log² n); a query visits O(log n) nodes for a short segment.
    /// </summary>
    public sealed class SceneBvh
    {
        /// <summary>Count &gt; 0: a leaf over Order[First .. First + Count). Count = 0: children at First and First + 1.</summary>
        public struct Node
        {
            public Vector3d Min, Max;
            public int First, Count;
        }

        /// <summary>Leaves stop splitting at this many triangles.</summary>
        public const int LeafSize = 4;
        /// <summary>Deepest stack a query needs: one entry per level plus a sibling; a median split keeps depth near log₂ n.</summary>
        public const int StackSize = 64;

        public readonly Vector3d[] Vertices;
        public readonly int[] Indices;
        public readonly Node[] Nodes;
        /// <summary>Triangle index for each slot the leaves refer to.</summary>
        public readonly int[] Order;

        readonly Vector3d[] _centroids;
        int _used;

        public SceneBvh(Vector3d[] vertices, int[] indices)
        {
            if (indices.Length % 3 != 0) throw new ArgumentException("Indices must come in threes.", nameof(indices));
            Vertices = vertices;
            Indices = indices;
            int count = indices.Length / 3;
            Order = new int[count];
            _centroids = new Vector3d[count];
            for (int i = 0; i < count; i++)
            {
                Order[i] = i;
                _centroids[i] = (Vertex(i, 0) + Vertex(i, 1) + Vertex(i, 2)) / 3;
            }
            Nodes = new Node[Math.Max(1, 2 * count - 1)];
            _used = 1;
            Build(0, 0, count);
            Array.Resize(ref Nodes, _used);
        }

        Vector3d Vertex(int triangle, int corner) => Vertices[Indices[3 * triangle + corner]];

        // shortcut: splits at the median of the longest axis; switch to a binned surface-area split if traversal shows in a profile.
        void Build(int node, int first, int count)
        {
            Vector3d min = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), max = -min;
            Vector3d cMin = min, cMax = max;
            for (int i = first; i < first + count; i++)
            {
                for (int k = 0; k < 3; k++) Grow(ref min, ref max, Vertex(Order[i], k));
                Grow(ref cMin, ref cMax, _centroids[Order[i]]);
            }
            Nodes[node].Min = min;
            Nodes[node].Max = max;
            if (count <= LeafSize)
            {
                Nodes[node].First = first;
                Nodes[node].Count = count;
                return;
            }

            var extent = cMax - cMin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(Order, first, count, new AxisOrder(_centroids, axis));
            int left = _used;
            _used += 2;
            Nodes[node].First = left;
            Nodes[node].Count = 0;
            Build(left, first, count / 2);
            Build(left + 1, first + count / 2, count - count / 2);
        }

        static void Grow(ref Vector3d min, ref Vector3d max, Vector3d p)
        {
            min = new Vector3d(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vector3d(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }

        sealed class AxisOrder : System.Collections.Generic.IComparer<int>
        {
            readonly Vector3d[] _centroids;
            readonly int _axis;
            public AxisOrder(Vector3d[] centroids, int axis) { _centroids = centroids; _axis = axis; }
            public int Compare(int a, int b) => Coordinate(_centroids[a]).CompareTo(Coordinate(_centroids[b]));
            double Coordinate(Vector3d v) => _axis == 0 ? v.X : _axis == 1 ? v.Y : v.Z;
        }

        /// <summary>The nearest triangle the segment from → to passes through, from either side. Allocation-free.</summary>
        public bool Intersect(Vector3d from, Vector3d to, out SceneHit hit)
        {
            hit = default;
            hit.T = 1;
            hit.Triangle = -1;
            var dir = to - from;
            var inverse = new Vector3d(1 / dir.X, 1 / dir.Y, 1 / dir.Z);
            Span<int> stack = stackalloc int[StackSize];
            int top = 0;
            if (Entry(Nodes[0], from, inverse, hit.T) > 1) return false;
            stack[top++] = 0;
            while (top > 0)
            {
                var node = Nodes[stack[--top]];
                if (node.Count > 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                        if (Triangle(Order[i], from, dir, hit.T, out double t, out double u, out double v))
                        {
                            hit.T = t;
                            hit.U = u;
                            hit.V = v;
                            hit.Triangle = Order[i];
                        }
                    continue;
                }
                double near = Entry(Nodes[node.First], from, inverse, hit.T), far = Entry(Nodes[node.First + 1], from, inverse, hit.T);
                int nearChild = node.First, farChild = node.First + 1;
                if (far < near)
                {
                    (near, far) = (far, near);
                    (nearChild, farChild) = (farChild, nearChild);
                }
                // The nearer child goes on top so it is searched first and shortens the segment for the other.
                if (far <= 1) stack[top++] = farChild;
                if (near <= 1) stack[top++] = nearChild;
            }
            return hit.Triangle >= 0;
        }

        // Where the segment enters the box, as a fraction of its length; above 1 when it misses or enters after tMax.
        static double Entry(in Node box, Vector3d from, Vector3d inverse, double tMax)
        {
            double t1 = (box.Min.X - from.X) * inverse.X, t2 = (box.Max.X - from.X) * inverse.X;
            double enter = Math.Min(t1, t2), leave = Math.Max(t1, t2);
            t1 = (box.Min.Y - from.Y) * inverse.Y;
            t2 = (box.Max.Y - from.Y) * inverse.Y;
            enter = Math.Max(enter, Math.Min(t1, t2));
            leave = Math.Min(leave, Math.Max(t1, t2));
            t1 = (box.Min.Z - from.Z) * inverse.Z;
            t2 = (box.Max.Z - from.Z) * inverse.Z;
            enter = Math.Max(enter, Math.Min(t1, t2));
            leave = Math.Min(leave, Math.Max(t1, t2));
            return leave >= Math.Max(enter, 0) && enter <= tMax ? Math.Max(enter, 0) : 2;
        }

        // Möller & Trumbore (1997), both faces, t in [0, tMax).
        bool Triangle(int triangle, Vector3d from, Vector3d dir, double tMax, out double t, out double u, out double v)
        {
            t = u = v = 0;
            var v0 = Vertex(triangle, 0);
            var e1 = Vertex(triangle, 1) - v0;
            var e2 = Vertex(triangle, 2) - v0;
            var p = Vector3d.Cross(dir, e2);
            double det = Vector3d.Dot(e1, p);
            if (det == 0) return false;
            double inverse = 1 / det;
            var s = from - v0;
            u = Vector3d.Dot(s, p) * inverse;
            if (u < 0 || u > 1) return false;
            var q = Vector3d.Cross(s, e1);
            v = Vector3d.Dot(dir, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            t = Vector3d.Dot(e2, q) * inverse;
            return t >= 0 && t < tMax;
        }
    }
}
