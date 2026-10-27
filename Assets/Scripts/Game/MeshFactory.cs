using System.Collections.Generic;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>The shapes' meshes, built in code so there are no model files.</summary>
    public static class MeshFactory
    {
        private static Mesh cube;
        private static Mesh pyramid;
        private static Mesh star;
        private static Mesh sphere;
        private static Mesh disc;

        /// <summary>Cube from -1 to 1 on every axis.</summary>
        public static Mesh Cube
        {
            get
            {
                if (cube != null) return cube;
                var c = new[]
                {
                    new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
                    new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
                };
                var quads = new[]
                {
                    new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                    new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 },
                };
                var points = new List<Vector3>();
                foreach (var q in quads)
                {
                    points.AddRange(new[] { c[q[0]], c[q[1]], c[q[2]], c[q[0]], c[q[2]], c[q[3]] });
                }
                return cube = FlatShaded("Cube", points);
            }
        }

        /// <summary>Triangular pyramid with its apex up, centered so it spins in place.</summary>
        public static Mesh Pyramid
        {
            get
            {
                if (pyramid != null) return pyramid;
                var apex = new Vector3(0, 1, 0);
                var r = Mathf.Sqrt(8f) / 3f;
                var b = new Vector3[3];
                for (var i = 0; i < 3; i++)
                {
                    var a = 2 * Mathf.PI * i / 3;
                    b[i] = new Vector3(r * Mathf.Cos(a), -1f / 3f, r * Mathf.Sin(a));
                }
                return pyramid = FlatShaded("Pyramid", Tetrahedron(apex, b[0], b[1], b[2]));
            }
        }

        /// <summary>Two interlocked tetrahedra.</summary>
        public static Mesh Star
        {
            get
            {
                if (star != null) return star;
                var a = new[] { new Vector3(1, 1, 1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1) };
                var points = Tetrahedron(a[0], a[1], a[2], a[3]);
                points.AddRange(Tetrahedron(-a[0], -a[1], -a[2], -a[3]));
                return star = FlatShaded("Star", points);
            }
        }

        /// <summary>Unit-radius sphere, smooth shaded.</summary>
        public static Mesh Sphere
        {
            get
            {
                if (sphere != null) return sphere;
                const int rings = 16;
                const int segments = 24;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var ring = 0; ring <= rings; ring++)
                {
                    var theta = Mathf.PI * ring / rings;
                    for (var segment = 0; segment <= segments; segment++)
                    {
                        var phi = 2 * Mathf.PI * segment / segments;
                        vertices.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
                    }
                }
                for (var ring = 0; ring < rings; ring++)
                {
                    for (var segment = 0; segment < segments; segment++)
                    {
                        var a = ring * (segments + 1) + segment;
                        var b = a + segments + 1;
                        triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                    }
                }
                sphere = new Mesh { name = "Sphere" };
                sphere.SetVertices(vertices);
                sphere.SetNormals(vertices);
                sphere.SetTriangles(triangles, 0);
                sphere.RecalculateBounds();
                return sphere;
            }
        }

        /// <summary>Flat unit disc facing up, for soft shadows.</summary>
        public static Mesh Disc
        {
            get
            {
                if (disc != null) return disc;
                const int segments = 32;
                var vertices = new List<Vector3> { Vector3.zero };
                var triangles = new List<int>();
                for (var i = 0; i <= segments; i++)
                {
                    var a = 2 * Mathf.PI * i / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                    if (i > 0) triangles.AddRange(new[] { 0, i + 1, i });
                }
                disc = new Mesh { name = "Disc" };
                disc.SetVertices(vertices);
                disc.SetTriangles(triangles, 0);
                disc.RecalculateNormals();
                disc.RecalculateBounds();
                return disc;
            }
        }

        private static List<Vector3> Tetrahedron(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            new List<Vector3> { a, b, c, a, c, d, a, d, b, b, c, d };

        /// <summary>
        /// A mesh from a triangle list, each face with its own normal. The shapes are convex around
        /// the origin, so each face is wound to face away from it, whichever order its corners come in.
        /// </summary>
        private static Mesh FlatShaded(string name, List<Vector3> points)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var t = 0; t < points.Count; t += 3)
            {
                Vector3 p0 = points[t], p1 = points[t + 1], p2 = points[t + 2];
                var normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                var center = (p0 + p1 + p2) / 3f;
                // Unity shows a triangle's front on the side its corners' cross product points to,
                // so flip any face whose cross product points inward.
                if (Vector3.Dot(normal, center) < 0f)
                {
                    var swap = p1;
                    p1 = p2;
                    p2 = swap;
                    normal = -normal;
                }
                foreach (var p in new[] { p0, p1, p2 })
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(p);
                    normals.Add(normal);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
