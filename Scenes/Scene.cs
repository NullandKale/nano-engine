using NullEngine.Rendering.DataStructures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NullEngine.Scenes
{
    public class Scene
    {
        public string filename;
        public SceneData sceneData;

        public Scene(string filename, SceneData sceneData)
        {
            this.filename = filename;
            this.sceneData = sceneData;
        }

        public void Save()
        {
            SceneData.SaveToJson(sceneData, filename);
        }

        public static Scene Load(string filename)
        {
            if(File.Exists(filename))
            {
                return new Scene(filename, SceneData.LoadJsonFromFile(filename));
            }

            return new Scene(filename, new SceneData());
        }
    }

    public struct SceneData
    {
        public Camera mainCamera { get; set; }
        public List<Sphere> spheres { get; set; }
        public List<MaterialData> materials { get; set; }

        public SceneData(Camera mainCamera)
        {
            this.mainCamera = mainCamera;
            spheres = new List<Sphere>();
            materials = new List<MaterialData>();
        }

        public SceneData(Camera mainCamera, List<Sphere> spheres, List<Sphere> lights, List<MaterialData> materials)
        {
            this.mainCamera = mainCamera;
            this.spheres = spheres;
            this.materials = materials;
        }

        public void addSphereAndMat(Sphere sphere, MaterialData material)
        {
            sphere.materialIndex = materials.Count;
            spheres.Add(sphere);
            materials.Add(material);
        }

        public static SceneData LoadJsonFromFile(string filename)
        {
            return JsonSerializer.Deserialize<SceneData>(File.ReadAllText(filename));
        }

        public static void SaveToJson(SceneData scene, string filename)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(filename, JsonSerializer.Serialize(scene, options));
        }
    }
}
