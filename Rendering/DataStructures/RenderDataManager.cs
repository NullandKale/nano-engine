﻿using ILGPU;
using ILGPU.Runtime;
using NullEngine.Rendering.Implementation;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace NullEngine.Rendering.DataStructures
{
    public class RenderDataManager
    {
        public List<float> rawTextureData;
        public List<dTexture> textures;

        public List<int> rawTriangleBuffers;
        public List<float> rawVertexBuffers;
        public List<float> rawUVBuffers;
        public List<hMesh> hMeshes;
        public List<dMesh> meshBuffers;
        public List<Sphere> spheres;
        public List<int> lightSphereIDs;
        public List<MaterialData> mats;

        public RenderData renderData;
        private GPU gpu;
        private bool isDirty;

        public RenderDataManager(GPU gpu)
        {
            this.gpu = gpu;
            setupDummyData();
        }

        public dRenderData getDeviceRenderData()
        {
            if(isDirty)
            {
                if(renderData != null)
                {
                    renderData.Dispose();
                }

                //maybe one day do this async from the render thread
                renderData = new RenderData(gpu.device, this);
            }

            return renderData.deviceRenderData;
        }

        public hMesh LoadMesh(Vec3 pos, string filename)
        {
            hMesh toReturn = Utils.FileUtils.LoadMeshFromFile(pos, filename);

            toReturn.ID = addDMeshForID(toReturn.aabb, toReturn.origin, toReturn.rotation, toReturn.triangles, toReturn.verticies, toReturn.uvs);
            toReturn.active = true;

            return toReturn;
        }

        public int addMaterialForID(MaterialData material)
        {
            int id = mats.Count;
            mats.Add(material);
            isDirty = true;
            return id;
        }

        public int addSphereForID(Sphere sphere)
        {
            int id = spheres.Count;
            spheres.Add(sphere);
            if(sphere.materialIndex != -1 && mats[sphere.materialIndex].type == 3)
            {
                lightSphereIDs.Add(id);
            }
            isDirty = true;
            return id;
        }

        public int addDMeshForID(AABB boundingBox, Vec3 origin, Vec3 rotation, List<int> triangles, List<float> verts, List<float> uvs)
        {
            int Voffset = rawVertexBuffers.Count;
            int Uoffset = rawUVBuffers.Count;
            int Toffset = rawTriangleBuffers.Count;
            int id = meshBuffers.Count;

            rawVertexBuffers.AddRange(verts);
            rawUVBuffers.AddRange(uvs);
            rawTriangleBuffers.AddRange(triangles);
            meshBuffers.Add(new dMesh(boundingBox, origin, rotation, Voffset, Uoffset, Toffset, triangles.Count));

            isDirty = true;
            return id;
        }

        public int addGTextureForID(int width, int height, List<float> pixels)
        {
            int offset = rawTextureData.Count;
            rawTextureData.AddRange(pixels);

            int id = textures.Count;
            textures.Add(new dTexture(width, height, offset));

            isDirty = true;
            return id;
        }

        private void setupDummyData()
        {
            rawTextureData = new List<float>(new float[3]);
            textures = new List<dTexture>(new dTexture[1]);

            rawTriangleBuffers = new List<int>(new int[3]);
            rawVertexBuffers = new List<float>(new float[3]);
            rawUVBuffers = new List<float>(new float[2]);
            meshBuffers = new List<dMesh>(new dMesh[1]);
            spheres = new List<Sphere>(new Sphere[1]);
            lightSphereIDs = new List<int>(new int[1]);
            mats = new List<MaterialData>(new MaterialData[1]);
            isDirty = true;
        }
    }

    public class RenderData
    {
        public MemoryBuffer<float> rawTextureData;
        public MemoryBuffer<dTexture> textures;

        public MemoryBuffer<int> rawTriangleBuffers;
        public MemoryBuffer<float> rawVertexBuffers;
        public MemoryBuffer<float> rawUVBuffers;
        public MemoryBuffer<dMesh> meshBuffers;
        public MemoryBuffer<Sphere> spheres;
        public MemoryBuffer<int> lightSphereIDs;
        public MemoryBuffer<MaterialData> mats;

        public dRenderData deviceRenderData;

        public RenderData(Accelerator device, RenderDataManager dataManager)
        {
            rawTextureData = device.Allocate<float>(dataManager.rawTextureData.Count);
            rawTextureData.CopyFrom(dataManager.rawTextureData.ToArray(), 0, 0, dataManager.rawTextureData.Count);

            textures = device.Allocate<dTexture>(dataManager.textures.Count);
            textures.CopyFrom(dataManager.textures.ToArray(), 0, 0, dataManager.textures.Count);

            rawTriangleBuffers = device.Allocate<int>(dataManager.rawTriangleBuffers.Count);
            rawTriangleBuffers.CopyFrom(dataManager.rawTriangleBuffers.ToArray(), 0, 0, dataManager.rawTriangleBuffers.Count);

            rawUVBuffers = device.Allocate<float>(dataManager.rawUVBuffers.Count);
            rawUVBuffers.CopyFrom(dataManager.rawUVBuffers.ToArray(), 0, 0, dataManager.rawUVBuffers.Count);

            rawVertexBuffers = device.Allocate<float>(dataManager.rawVertexBuffers.Count);
            rawVertexBuffers.CopyFrom(dataManager.rawVertexBuffers.ToArray(), 0, 0, dataManager.rawVertexBuffers.Count);

            meshBuffers = device.Allocate<dMesh>(dataManager.meshBuffers.Count);
            meshBuffers.CopyFrom(dataManager.meshBuffers.ToArray(), 0, 0, dataManager.meshBuffers.Count);

            spheres = device.Allocate<Sphere>(dataManager.spheres.Count);
            spheres.CopyFrom(dataManager.spheres.ToArray(), 0, 0, dataManager.spheres.Count);

            lightSphereIDs = device.Allocate<int>(dataManager.lightSphereIDs.Count);
            lightSphereIDs.CopyFrom(dataManager.lightSphereIDs.ToArray(), 0, 0, dataManager.lightSphereIDs.Count);

            mats = device.Allocate<MaterialData>(dataManager.mats.Count);
            mats.CopyFrom(dataManager.mats.ToArray(), 0, 0, dataManager.mats.Count);

            deviceRenderData = new dRenderData(this);
        }

        public void Dispose()
        {
            rawTextureData.Dispose();
            rawVertexBuffers.Dispose();
            textures.Dispose();
            meshBuffers.Dispose();
            spheres.Dispose();
            lightSphereIDs.Dispose();
            mats.Dispose();
        }
    }

    public struct dRenderData
    {
        public ArrayView<float> rawTextureData;
        public ArrayView<dTexture> textures;

        public ArrayView<int> rawTriangleBuffers;
        public ArrayView<float> rawVertexBuffers;
        public ArrayView<float> rawUVBuffers;
        public ArrayView<dMesh> meshBuffers;
        public ArrayView<Sphere> spheres;
        public ArrayView<int> lightSphereIDs;
        public ArrayView<MaterialData> mats;

        public dRenderData(RenderData renderData)
        {
            rawTextureData = renderData.rawTextureData;
            textures = renderData.textures;
            rawTriangleBuffers = renderData.rawTriangleBuffers;
            rawVertexBuffers = renderData.rawVertexBuffers;
            rawUVBuffers = renderData.rawUVBuffers;
            meshBuffers = renderData.meshBuffers;
            spheres = renderData.spheres;
            lightSphereIDs = renderData.lightSphereIDs;
            mats = renderData.mats;
        }
    }

    public struct dTexture
    {
        public int width;
        public int height;
        public int offset;

        public dTexture(int width, int height, int offset)
        {
            this.width = width;
            this.height = height;
            this.offset = offset;
        }
    }

    public struct hMesh
    {
        public bool active;
        public AABB aabb;
        public Vec3 origin;
        public Vec3 rotation;
        public List<float> verticies;
        public List<int> triangles;
        public List<float> uvs;
        public int triangleCount;
        public int ID;

        public hMesh(Vec3 position, List<float> verticies, List<int> triangles, List<float> uvs)
        {
            active = false;
            aabb = AABB.CreateFromVerticies(verticies, position);
            origin = position;
            rotation = new Vec3();
            this.verticies = verticies;
            this.triangles = triangles;
            this.uvs = uvs;
            triangleCount = triangles.Count / 3;
            ID = -1;
        }
    }

    public struct dMesh
    {
        public AABB boundingBox;
        public Vec3 origin;
        public Vec3 rotation;

        public int vertsOffset;
        public int uvOffset;
        public int triangleOffset;
        public int triangleLength;

        public dMesh(AABB boundingBox, Vec3 origin, Vec3 rotation, int vertsOffset, int uvOffset, int triangleOffset, int triangleLength)
        {
            this.boundingBox = boundingBox;
            this.origin = origin;
            this.rotation = rotation;
            this.vertsOffset = vertsOffset;
            this.uvOffset = uvOffset;
            this.triangleOffset = triangleOffset;
            this.triangleLength = triangleLength;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Triangle GetTriangle(int index, dRenderData renderData)
        {
            int triangleIndex = index * 3;
            int vertexStartIndex0 = renderData.rawTriangleBuffers[triangleIndex] * 3;
            int vertexStartLongIndex1 = renderData.rawTriangleBuffers[triangleIndex + 1] * 3;
            int vertexStartIndex2 = renderData.rawTriangleBuffers[triangleIndex + 2] * 3;

            Vec3 Vert0 = new Vec3(renderData.rawVertexBuffers[vertexStartIndex0], renderData.rawVertexBuffers[vertexStartIndex0 + 1], renderData.rawVertexBuffers[vertexStartIndex0 + 2]) + origin;
            Vec3 Vert1 = new Vec3(renderData.rawVertexBuffers[vertexStartLongIndex1], renderData.rawVertexBuffers[vertexStartLongIndex1 + 1], renderData.rawVertexBuffers[vertexStartLongIndex1 + 2]) + origin;
            Vec3 Vert2 = new Vec3(renderData.rawVertexBuffers[vertexStartIndex2], renderData.rawVertexBuffers[vertexStartIndex2 + 1], renderData.rawVertexBuffers[vertexStartIndex2 + 2]) + origin;

            return new Triangle(Vert0, Vert1, Vert2);
        }
    }
}
