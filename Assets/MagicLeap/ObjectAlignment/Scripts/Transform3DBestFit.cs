using System.Collections.Generic;
using Transform3DBestFit;
using UnityEngine;

namespace MagicLeap.ObjectAlignment
{
    class Transform3DBestFit
    {
        public static Matrix4x4 Solve(List<Vector3> fromPoints, List<Vector3> toPoints)
        {
            Transform3D t3d = new Transform3D(ToMathNetMatrix(fromPoints),
                ToMathNetMatrix(toPoints));
            if (!t3d.CalcTransform(t3d.actualsMatrix, t3d.nominalsMatrix))
            {
                return Matrix4x4.identity;
            }

            double[,] r = t3d.TransformMatrix;
            return new Matrix4x4(
                new Vector4((float) r[0, 0], (float) r[1, 0], (float) r[2, 0], (float) r[3, 0]),
                new Vector4((float) r[0, 1], (float) r[1, 1], (float) r[2, 1], (float) r[3, 1]),
                new Vector4((float) r[0, 2], (float) r[1, 2], (float) r[2, 2], (float) r[3, 2]),
                new Vector4((float) r[0, 3], (float) r[1, 3], (float) r[2, 3], (float) r[3, 3]));
        }

        private static double[,] ToMathNetMatrix(List<Vector3> points)
        {
            double[,] matrix = new double[points.Count,3];
            for (int i = 0; i < points.Count; i++)
            {
                matrix[i, 0] = points[i].x;
                matrix[i, 1] = points[i].y;
                matrix[i, 2] = points[i].z;
            }

            return matrix;
        }
    }
}