using NullEngine.Rendering.DataStructures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NullEngine.Utils
{
    public static class FileUtils
    {
        public static hMesh LoadMeshFromFile(Vec3 pos, string filename)
        {
            //Dictionary<string, MaterialData> materials = LoadMaterialsFromFile(filename + ".mtl");

            string[] lines = File.ReadAllLines(filename + ".obj");

            List<float> uvs = new List<float>();
            List<float> verticies = new List<float>();
            List<int> triangles = new List<int>();
            //List<int> mats = new List<int>();

            int mat = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string[] split = line.Split(" ");

                if (line.Length > 0 && line[0] != '#' && split.Length >= 2)
                {
                    switch (split[0])
                    {
                        case "v":
                            {
                                if (double.TryParse(split[1], out double v0) && double.TryParse(split[2], out double v1) && double.TryParse(split[3], out double v2))
                                {
                                    verticies.Add((float)v0);
                                    verticies.Add((float)-v1);
                                    verticies.Add((float)v2);
                                }
                                break;
                            }
                        case "f":
                            {
                                List<int> indexes = new List<int>();
                                for (int j = 1; j < split.Length; j++)
                                {
                                    string[] indicies = split[j].Split("/");

                                    if (indicies.Length >= 1)
                                    {
                                        if (int.TryParse(indicies[0], out int i0))
                                        {
                                            indexes.Add(i0 < 0 ? i0 + verticies.Count : i0 - 1);
                                        }
                                    }
                                }

                                for (int j = 1; j < indexes.Count - 1; ++j)
                                {
                                    triangles.Add(indexes[0]);
                                    triangles.Add(indexes[j]);
                                    triangles.Add(indexes[j + 1]);
                                    //mats.Add(mat);
                                }

                                break;
                            }
                        //case "usemtl":
                        //    {
                        //        if (materials.ContainsKey(split[1]))
                        //        {
                        //            MaterialData material = materials[split[1]];
                        //            mat = worldData.worldBuffer.addMaterial(material);
                        //        }
                        //        else
                        //        {
                        //            mat = worldData.worldBuffer.addMaterial(MaterialData.makeDiffuse(new Vec3(1, 0, 1)));
                        //        }

                        //        break;
                        //    }
                    }

                }
            }

            return new hMesh(pos, verticies, triangles, uvs);
        }
    }
}
