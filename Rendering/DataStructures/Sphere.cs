using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Rendering.DataStructures
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Sphere
    {
        public Vec3 center{ get; set; }
        public float radius{ get; set; }
        public float radiusSquared{ get; set; }
        public int materialIndex{ get; set; }

        public Sphere(Vec3 center, float radius, int materialIndex)
        {
            this.center = center;
            this.radius = radius;
            radiusSquared = radius * radius;
            this.materialIndex = materialIndex;
        }
    }
}
