using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Rendering.DataStructures
{
    public struct MaterialData
    {
        public const int DIFFUSE = 0;
        public const int GLASS = 1;
        public const int METAL = 2;
        public const int LIGHT = 3;

        public int type{ get; set; }
        public Vec3 color{ get; set; }
        public float ref_idx{ get; set; }
        public float reflectivity{ get; set; }
        public float reflectionConeAngleRadians{ get; set; }

        public MaterialData(Vec3 color, float ref_idx, float reflectivity, float reflectionConeAngleRadians, int type)
        {
            this.type = type;
            this.color = color;
            this.ref_idx = ref_idx;
            this.reflectivity = reflectivity;
            this.reflectionConeAngleRadians = reflectionConeAngleRadians;
        }

        public static MaterialData makeDiffuse(Vec3 diffuseColor)
        {
            return new MaterialData(diffuseColor, 0, 0, 0, DIFFUSE);
        }

        public static MaterialData makeGlass(Vec3 diffuseColor, float ref_idx)
        {
            return new MaterialData(diffuseColor, ref_idx, 0, 0, GLASS);
        }

        public static MaterialData makeMirror(Vec3 diffuseColor, float fuzz)
        {
            return new MaterialData(diffuseColor, 0, 0, (fuzz < 1 ? fuzz : 1), METAL);
        }

        public static MaterialData makeMirror(Vec3 diffuseColor)
        {
            return new MaterialData(diffuseColor, 0, 0, 0, METAL);
        }

        public static MaterialData makeLight(Vec3 emmissiveColor)
        {
            return new MaterialData(emmissiveColor, 0, 0, 0, LIGHT);
        }
    }
}
