using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// A camera that projects 3D points into an image of <see cref="Width"/> by <see cref="Height"/>
    /// pixels, measured from the top-left corner: where it is, which way its image's right, down and
    /// forward point, and its lens (focal lengths, image center and skew, in pixels).
    ///
    /// Used for the Kinect's color camera, so the game can draw over its picture in the right place.
    /// The axes aren't forced to be right- or left-handed; a mirrored image just has them the other way.
    /// </summary>
    public sealed class PinholeCamera
    {
        public Vector3 Position { get; }
        public Vector3 Right { get; }
        public Vector3 Down { get; }
        public Vector3 Forward { get; }
        public float Fx { get; }
        public float Fy { get; }
        public float Cx { get; }
        public float Cy { get; }
        public float Skew { get; }
        public int Width { get; }
        public int Height { get; }

        public PinholeCamera(Vector3 position, Vector3 right, Vector3 down, Vector3 forward,
            float fx, float fy, float cx, float cy, int width, int height, float skew = 0f)
        {
            Position = position;
            Right = right;
            Down = down;
            Forward = forward;
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
            Skew = skew;
            Width = width;
            Height = height;
        }

        /// <summary>The camera's full field of view top to bottom, in degrees.</summary>
        public float VerticalFieldOfView => (float)(2 * Math.Atan(Height / (2.0 * Fy)) * 180 / Math.PI);

        /// <summary>Where <paramref name="point"/> appears in the image, in pixels from the top-left.</summary>
        public Vector2 Project(Vector3 point)
        {
            var d = point - Position;
            var z = Vector3.Dot(d, Forward);
            var x = Vector3.Dot(d, Right) / z;
            var y = Vector3.Dot(d, Down) / z;
            return new Vector2(Fx * x + Skew * y + Cx, Fy * y + Cy);
        }

        /// <summary>
        /// The projection matrix for drawing exactly what this camera sees, in OpenGL's convention
        /// (as Unity's Camera.projectionMatrix takes it), row by row: view space has X along
        /// <see cref="Right"/>, Y up (against <see cref="Down"/>) and looks along -Z, and the
        /// picture fills the viewport. The lens center needn't be the picture's middle. With
        /// <paramref name="mirrored"/>, for a picture shown flipped left to right and a view whose
        /// X runs against <see cref="Right"/>.
        /// </summary>
        public float[] ProjectionMatrix(float near, float far, bool mirrored = false)
        {
            var cx = mirrored ? Width - Cx : Cx;
            var skew = mirrored ? -Skew : Skew;
            return new[]
            {
                2 * Fx / Width, -2 * skew / Width, 1 - 2 * cx / Width, 0f,
                0f, 2 * Fy / Height, 2 * Cy / Height - 1, 0f,
                0f, 0f, -(far + near) / (far - near), -2 * far * near / (far - near),
                0f, 0f, -1f, 0f,
            };
        }

        /// <summary>
        /// The same camera in other coordinates, given how points and directions convert. The
        /// conversion must keep distances, like <see cref="TableFrame.ToTable"/>.
        /// </summary>
        public PinholeCamera Transformed(Func<Vector3, Vector3> point, Func<Vector3, Vector3> direction) =>
            new PinholeCamera(point(Position), direction(Right), direction(Down), direction(Forward),
                Fx, Fy, Cx, Cy, Width, Height, Skew);

        /// <summary>
        /// Works out the camera from matching 3D <paramref name="points"/> and image
        /// <paramref name="pixels"/>, by the direct linear transform. The points must not all lie
        /// on one plane. Returns null if there aren't enough of them.
        /// </summary>
        public static PinholeCamera Fit(IReadOnlyList<Vector3> points, IReadOnlyList<Vector2> pixels, int width, int height)
        {
            var n = Math.Min(points.Count, pixels.Count);
            if (n < 12) return null;

            // Normalize both sets of coordinates for a well-conditioned solve (Hartley).
            var c3 = Vector3.Zero;
            var c2 = Vector2.Zero;
            for (var i = 0; i < n; i++)
            {
                c3 += points[i];
                c2 += pixels[i];
            }
            c3 /= n;
            c2 /= n;
            double d3 = 0, d2 = 0;
            for (var i = 0; i < n; i++)
            {
                d3 += (points[i] - c3).Length();
                d2 += (pixels[i] - c2).Length();
            }
            var s3 = Math.Sqrt(3) / (d3 / n);
            var s2 = Math.Sqrt(2) / (d2 / n);

            // Sum of A^T A over the two equations each correspondence gives.
            var ata = new double[12, 12];
            var row = new double[12];
            for (var i = 0; i < n; i++)
            {
                var p = points[i] - c3;
                double x = p.X * s3, y = p.Y * s3, z = p.Z * s3;
                double u = (pixels[i].X - c2.X) * s2, v = (pixels[i].Y - c2.Y) * s2;
                for (var equation = 0; equation < 2; equation++)
                {
                    Array.Clear(row, 0, 12);
                    var offset = equation * 4;
                    var target = equation == 0 ? u : v;
                    row[offset] = x;
                    row[offset + 1] = y;
                    row[offset + 2] = z;
                    row[offset + 3] = 1;
                    row[8] = -target * x;
                    row[9] = -target * y;
                    row[10] = -target * z;
                    row[11] = -target;
                    for (var a = 0; a < 12; a++)
                    {
                        if (row[a] == 0) continue;
                        for (var b = 0; b < 12; b++) ata[a, b] += row[a] * row[b];
                    }
                }
            }
            var h = LinearAlgebra.SmallestEigenvector(ata);

            // Undo the normalization: P = T2^-1 * H * T3.
            var normalized = new double[3, 4];
            for (var r = 0; r < 3; r++) for (var c = 0; c < 4; c++) normalized[r, c] = h[r * 4 + c];
            var t3 = new double[4, 4]
            {
                { s3, 0, 0, -s3 * c3.X },
                { 0, s3, 0, -s3 * c3.Y },
                { 0, 0, s3, -s3 * c3.Z },
                { 0, 0, 0, 1 },
            };
            var t2Inverse = new double[3, 3]
            {
                { 1 / s2, 0, c2.X },
                { 0, 1 / s2, c2.Y },
                { 0, 0, 1 },
            };
            var p34 = Multiply(t2Inverse, Multiply(normalized, t3));

            // Scale so the third row's direction is a unit vector, with points in front of the camera.
            var m3Length = Math.Sqrt(p34[2, 0] * p34[2, 0] + p34[2, 1] * p34[2, 1] + p34[2, 2] * p34[2, 2]);
            var depth = p34[2, 0] * points[0].X + p34[2, 1] * points[0].Y + p34[2, 2] * points[0].Z + p34[2, 3];
            var k = (depth < 0 ? -1 : 1) / m3Length;
            for (var r = 0; r < 3; r++) for (var c = 0; c < 4; c++) p34[r, c] *= k;

            var m1 = new Vector3((float)p34[0, 0], (float)p34[0, 1], (float)p34[0, 2]);
            var m2 = new Vector3((float)p34[1, 0], (float)p34[1, 1], (float)p34[1, 2]);
            var m3 = new Vector3((float)p34[2, 0], (float)p34[2, 1], (float)p34[2, 2]);

            // P's left 3x3 is K * R with K upper triangular: split it row by row.
            var forward = Vector3.Normalize(m3);
            var cy = Vector3.Dot(m2, forward);
            var downScaled = m2 - cy * forward;
            var fy = downScaled.Length();
            var down = downScaled / fy;
            var cx = Vector3.Dot(m1, forward);
            var skew = Vector3.Dot(m1, down);
            var rightScaled = m1 - cx * forward - skew * down;
            var fx = rightScaled.Length();
            var right = rightScaled / fx;

            // The camera sits where P maps to nothing: M * C = -p4.
            var position = SolveCenter(p34);
            return new PinholeCamera(position, right, down, forward, fx, fy, cx, cy, width, height, skew);
        }

        private static Vector3 SolveCenter(double[,] p)
        {
            double a = p[0, 0], b = p[0, 1], c = p[0, 2];
            double d = p[1, 0], e = p[1, 1], f = p[1, 2];
            double g = p[2, 0], h = p[2, 1], i = p[2, 2];
            var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            double r0 = -p[0, 3], r1 = -p[1, 3], r2 = -p[2, 3];
            // Cramer's rule.
            var x = (r0 * (e * i - f * h) - b * (r1 * i - f * r2) + c * (r1 * h - e * r2)) / det;
            var y = (a * (r1 * i - f * r2) - r0 * (d * i - f * g) + c * (d * r2 - r1 * g)) / det;
            var z = (a * (e * r2 - r1 * h) - b * (d * r2 - r1 * g) + r0 * (d * h - e * g)) / det;
            return new Vector3((float)x, (float)y, (float)z);
        }

        private static double[,] Multiply(double[,] a, double[,] b)
        {
            int rows = a.GetLength(0), inner = a.GetLength(1), columns = b.GetLength(1);
            var result = new double[rows, columns];
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    double sum = 0;
                    for (var k = 0; k < inner; k++) sum += a[r, k] * b[k, c];
                    result[r, c] = sum;
                }
            }
            return result;
        }
    }
}
