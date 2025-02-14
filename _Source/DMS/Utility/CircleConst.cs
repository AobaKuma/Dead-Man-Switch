using System.Collections.Generic;
using UnityEngine;

namespace DMS
{
    public static class CircleConst
    {
        public static Vector3 GetAngle(float ang)
        {
            return circleAngleVector[Mathf.Abs((int)((ang % 360) / 10f))];
        }
        public static readonly Vector3[] circleAngleVector = new Vector3[]
        { // x , 0 , y       
        new Vector3 (0.000f,0,  1.000f),//0
        new Vector3 (0.174f,0,  0.985f),
        new Vector3 (0.342f,0,  0.940f),
        new Vector3 (0.500f,0,  0.866f),
        new Vector3 (0.643f,0,  0.766f),
        new Vector3 (0.766f,0,  0.643f),
        new Vector3 (0.866f,0,  0.500f),
        new Vector3 (0.940f,0,  0.342f),
        new Vector3 (0.985f,0,  0.174f),
        new Vector3 (1.000f,0,  0.000f),//90
        new Vector3 (0.985f,0,  -0.174f),
        new Vector3 (0.940f,0,  -0.342f),
        new Vector3 (0.866f,0,  -0.500f),
        new Vector3 (0.766f,0,  -0.643f),
        new Vector3 (0.643f,0,  -0.766f),
        new Vector3 (0.500f,0,  -0.866f),
        new Vector3 (0.342f,0,  -0.940f),
        new Vector3 (0.174f,0,  -0.985f),
        new Vector3 (0.000f,0,  -1.000f),//180
        new Vector3 (-0.174f,0,  -0.985f),
        new Vector3 (-0.342f,0,  -0.940f),
        new Vector3 (-0.500f,0,  -0.866f),
        new Vector3 (-0.643f,0,  -0.766f),
        new Vector3 (-0.766f,0,  -0.643f),
        new Vector3 (-0.866f,0,  -0.500f),
        new Vector3 (-0.940f,0,  -0.342f),
        new Vector3 (-0.985f,0,  -0.174f),
        new Vector3 (-1.000f,0,  0.000f),//270
        new Vector3 (-0.985f,0,  0.174f),
        new Vector3 (-0.940f,0,  0.342f),
        new Vector3 (-0.866f,0,  0.500f),
        new Vector3 (-0.766f,0,  0.643f),
        new Vector3 (-0.643f,0,  0.766f),
        new Vector3 (-0.500f,0,  0.866f),
        new Vector3 (-0.342f,0,  0.940f),
        new Vector3 (-0.174f,0, 0.985f),
        };
    }
}
