using System;

namespace ShapesKinect.Core
{
    /// <summary>Small dense linear algebra for fitting planes and cameras.</summary>
    public static class LinearAlgebra
    {
        /// <summary>
        /// The unit eigenvector of a symmetric matrix with the smallest eigenvalue, by cyclic Jacobi
        /// rotations. Good for the small matrices here (up to 12 x 12).
        /// </summary>
        public static double[] SmallestEigenvector(double[,] matrix)
        {
            var n = matrix.GetLength(0);
            var a = (double[,])matrix.Clone();
            var v = new double[n, n];
            for (var i = 0; i < n; i++) v[i, i] = 1;
            for (var sweep = 0; sweep < 100; sweep++)
            {
                double off = 0, scale = 0;
                for (var p = 0; p < n; p++)
                {
                    scale += Math.Abs(a[p, p]);
                    for (var q = p + 1; q < n; q++) off += Math.Abs(a[p, q]);
                }
                if (off <= 1e-15 * Math.Max(scale, 1e-300)) break;
                for (var p = 0; p < n - 1; p++)
                {
                    for (var q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        var t = theta == 0 ? 1 : Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        var c = 1 / Math.Sqrt(t * t + 1);
                        var s = t * c;
                        for (var k = 0; k < n; k++)
                        {
                            var akp = a[k, p];
                            var akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (var k = 0; k < n; k++)
                        {
                            var apk = a[p, k];
                            var aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (var k = 0; k < n; k++)
                        {
                            var vkp = v[k, p];
                            var vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }
            var smallest = 0;
            for (var i = 1; i < n; i++)
            {
                if (a[i, i] < a[smallest, smallest]) smallest = i;
            }
            var result = new double[n];
            double length = 0;
            for (var i = 0; i < n; i++)
            {
                result[i] = v[i, smallest];
                length += result[i] * result[i];
            }
            length = Math.Sqrt(length);
            for (var i = 0; i < n; i++) result[i] /= length;
            return result;
        }
    }
}
