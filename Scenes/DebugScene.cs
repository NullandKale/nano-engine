using NullEngine.Rendering.DataStructures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Scenes
{
    public static class DebugScene
    {
        public static Scene Load()
        {
            SceneData sceneData = new SceneData(new Camera(new Vec3(-0.25, 1, 7.5), new Vec3(-0.25, 1.12, 6.5), Vec3.unitVector(new Vec3(0, 1, 0)), 300, 300, 5, 3, 4, 40f, 15f / 255f, 0.90f, new Vec3(0.2, 0.7, 1)));

            MaterialData redMat = MaterialData.makeDiffuse(new Vec3(0.9999, 0, 0));
            MaterialData greenMat = MaterialData.makeDiffuse(new Vec3(0, 0.9999, 0));
            MaterialData blueMat = MaterialData.makeDiffuse(new Vec3(0, 0, 0.9999));
            MaterialData lightMat = MaterialData.makeLight(new Vec3(1, 1, 1));
            MaterialData floorMat = MaterialData.makeDiffuse(new Vec3(1, 1, 1));
            MaterialData metalMat = MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999), 0.25f);
            MaterialData mirrorMat = MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999));
            MaterialData redLightMat = MaterialData.makeLight(new Vec3(0.9999, 0.1, 0.1));
            MaterialData blueLightMat = MaterialData.makeLight(new Vec3(0.1, 0.1, 0.9999));

            Sphere red = new Sphere(new Vec3(0, 1, 0), 0.5f, 0);
            Sphere green = new Sphere(new Vec3(1, 1, 0), 0.5f, 0);
            Sphere blue = new Sphere(new Vec3(-1, 1, 0), 0.5f, 0);
            Sphere light = new Sphere(new Vec3(0, 15, 5), 0.1f, 0);
            Sphere redlight = new Sphere(new Vec3(-25, 5, 0), 1f, 0);
            Sphere bluelight = new Sphere(new Vec3(25, 5, 0), 1f, 0);
            Sphere floor = new Sphere(new Vec3(-1, -100000, 0), 100000f, 0);
            Sphere mirror0 = new Sphere(new Vec3(7, 5, 0), 5f, 0);
            Sphere mirror1 = new Sphere(new Vec3(-7, 5, 0), 5f, 0);
            Sphere metal = new Sphere(new Vec3(0, 5, -7), 5f, 0);

            sceneData.addSphereAndMat(red, redMat);
            sceneData.addSphereAndMat(green, greenMat);
            sceneData.addSphereAndMat(blue, blueMat);
            sceneData.addSphereAndMat(light, lightMat);
            sceneData.addSphereAndMat(redlight, redLightMat);
            sceneData.addSphereAndMat(bluelight, blueLightMat);
            sceneData.addSphereAndMat(floor, floorMat);
            sceneData.addSphereAndMat(mirror0, mirrorMat);
            sceneData.addSphereAndMat(mirror1, mirrorMat);
            sceneData.addSphereAndMat(metal, metalMat);

            Random rng = new Random(0);

            for (int i = 0; i < 50; i++)
            {
                MaterialData mat = new MaterialData();

                if (rng.NextDouble() < 0.50)
                {
                    mat = MaterialData.makeDiffuse(new Vec3(rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9));
                }
                else
                {
                    mat = MaterialData.makeMirror(new Vec3(rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9));
                }

                float size = (float)(rng.NextDouble() * 2);
                Sphere r = new Sphere(new Vec3(rng.Next(-25, 25), size, rng.Next(5, 25)), size, 0);

                sceneData.addSphereAndMat(r, mat);
            }

            return new Scene("debug_scene.json", sceneData);
        }
    }
}
