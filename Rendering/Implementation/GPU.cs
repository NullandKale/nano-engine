using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.CPU;
using System;
using System.Collections.Generic;
using System.Text;
using NullEngine.Rendering.DataStructures;
using ILGPU.Algorithms;
using ILGPU.Algorithms.Random;
using ILGPU.Algorithms.ScanReduceOperations;

namespace NullEngine.Rendering.Implementation
{
    public class GPU
    {
        public Context context;
        public Accelerator device;

        private Action<Index1, dFrameData, ArrayView<ulong>> InitPerPixelRngData;
        private Action<Index1, Camera, dFrameData> GenerateRays;
        private Action<Index1, Camera, dFrameData, dRenderData> ColorRay;
        public GPU(bool forceCPU)
        {
            context = new Context(ContextFlags.FastMath | ContextFlags.EnableDebugSymbols);
            context.EnableAlgorithms();

            if(forceCPU || CudaAccelerator.CudaAccelerators.Length < 1)
            {
                device = new CPUAccelerator(context);
            }
            else
            {
                device = new CudaAccelerator(context);
            }

            initRenderKernels();
        }

        private void initRenderKernels()
        {
            InitPerPixelRngData = device.LoadAutoGroupedStreamKernel<Index1, dFrameData, ArrayView<ulong>>(GPUKernels.InitPerPixelRngData);
            //GenerateRays = device.LoadAutoGroupedStreamKernel<Index1, Camera, dFrameData>(GPUKernels.GenerateRays);
            //ColorRay = device.LoadAutoGroupedStreamKernel<Index1, Camera, dFrameData, dRenderData>(GPUKernels.ColorRay);
        }

        public void Dispose()
        {
            device.Dispose();
            context.Dispose();
        }

        public void InitRNG(FrameData frameData)
        {
            long seedLength = frameData.rngBuffer.Length * 2;
            ulong[] seeds = new ulong[seedLength];

            Random rng = new Random();
            byte[] bytes = new byte[16];

            //probably always aligns to N % 4 == 0 so may be able to do sets of 4 ulongs but 2 will for sure work

            for (long i = 0; i < seedLength; i += 2)
            {
                rng.NextBytes(bytes);
                seeds[i] = BitConverter.ToUInt64(bytes, 0);
                seeds[i + 1] = BitConverter.ToUInt64(bytes, 8);
            }

            MemoryBuffer<ulong> deviceSeedBuffer = device.Allocate(seeds);
            InitPerPixelRngData(frameData.rngBuffer.Length, frameData.deviceFrameData, deviceSeedBuffer);
            device.Synchronize();
            deviceSeedBuffer.Dispose();
        }

        public void Render(ByteFrameBuffer output, Camera camera, RenderDataManager renderDataManager, FrameData frameData)
        {
            long outputLength = output.memoryBuffer.Length / 4;
            //GenerateRays(outputLength, camera, frameData.deviceFrameData);
            //ColorRay(outputLength, camera, frameData.deviceFrameData, renderDataManager.getDeviceRenderData());
            device.Synchronize();
        }

        private (float min, float max) ReduceMax(ArrayView<float> map)
        {
            using (var target = device.Allocate<float>(1))
            {
                // This overload requires an explicit output buffer but
                // uses an implicit temporary cache from the associated accelerator.
                // Call a different overload to use a user-defined memory cache.
                device.Reduce<float, MinFloat>(
                    device.DefaultStream,
                    map,
                    target.View);

                device.Synchronize();

                var min = target.GetAsArray();

                device.Reduce<float, MaxFloat>(
                device.DefaultStream,
                map,
                target.View);

                device.Synchronize();

                var max = target.GetAsArray();
                return (min[0], max[0]);
            }
        }
    }

    public static class GPUKernels
    {
        public static void InitPerPixelRngData(Index1 pixel, dFrameData frameData, ArrayView<ulong> rngSeeds)
        {
            frameData.rngBuffer[pixel] = new XorShift128Plus(rngSeeds[(pixel * 2)], rngSeeds[(pixel * 2) + 1]);
        }

        public static void GenerateRays(Index1 pixel, Camera camera, dFrameData frameData)
        {
            int x = pixel % camera.width;
            int y = pixel / camera.width;

            //float casts required
            frameData.rayBuffer[pixel] = camera.GetRay(((float)x) + frameData.rngBuffer[pixel].NextFloat(), ((float)y) + frameData.rngBuffer[pixel].NextFloat());
        }

        public static void ColorRay(Index1 pixel, Camera camera, dFrameData frameData, dRenderData renderData)
        {
            Vec3 attenuation = new Vec3(1f, 1f, 1f);
            Vec3 lighting = new Vec3();

            Ray working = frameData.rayBuffer[pixel];
            bool attenuationHasValue = false;

            XorShift128Plus rng = frameData.rngBuffer[pixel];

            float minT = 0.1f;

            for (int i = 0; i < camera.maxBounces; i++)
            {
                HitRecord rec = GetWorldHit(working, renderData, minT);

                if (rec.materialID == -1)
                {
                    if (i == 0 || attenuationHasValue)
                    {
                        frameData.metaBuffer[pixel] = -2;
                    }

                    float t = 0.5f * (working.b.y + 1.0f);
                    attenuation *= (1.0f - t) * new Vec3(1.0f, 1.0f, 1.0f) + t * new Vec3(0.5f, 0.7f, 1.0f);
                    break;
                }
                else
                {
                    if (i == 0)
                    {
                        frameData.depthBuffer[pixel] = rec.t;
                        frameData.metaBuffer[pixel] = rec.drawableID;
                    }

                    ScatterRecord sRec = Scatter(working, rec, rng, renderData.mats, minT);
                    if (sRec.materialID != -1)
                    {
                        attenuationHasValue = sRec.mirrorSkyLightingFix;
                        attenuation *= sRec.attenuation;
                        working = sRec.scatterRay;
                    }
                    else
                    {
                        frameData.metaBuffer[pixel] = -1;
                        break;
                    }
                }

                for (int j = 0; j < renderData.lightSphereIDs.Length; j++)
                {
                    Sphere s = renderData.spheres[renderData.lightSphereIDs[j]];
                    Vec3 lightDir = s.center - rec.p;
                    HitRecord shadowRec = GetWorldHit(new Ray(rec.p, lightDir), renderData, minT);

                    if (shadowRec.materialID != -1 && (shadowRec.p - rec.p).length() > lightDir.length() - (s.radius * 1.1f)) // the second part of this IF could probably be much more efficent
                    {
                        MaterialData material = renderData.mats[shadowRec.materialID];
                        if (material.type != 1)
                        {
                            lightDir = Vec3.unitVector(lightDir);
                            lighting += material.color * XMath.Max(0.0f, Vec3.dot(lightDir, rec.normal));
                            lighting *= XMath.Pow(XMath.Max(0.0f, Vec3.dot(-Vec3.reflect(rec.normal, -lightDir), frameData.rayBuffer[pixel].b)), material.reflectivity) * material.color;
                        }
                    }
                }
            }

            int rIndex = pixel * 3;
            int gIndex = rIndex + 1;
            int bIndex = rIndex + 2;

            frameData.colorBuffer[rIndex] = attenuation.x;
            frameData.colorBuffer[gIndex] = attenuation.y;
            frameData.colorBuffer[bIndex] = attenuation.z;

            frameData.lightBuffer[rIndex] = lighting.x;
            frameData.lightBuffer[gIndex] = lighting.y;
            frameData.lightBuffer[bIndex] = lighting.z;
        }

        public static void CreatBitmap(Index1 index, ArrayView<float> data, ArrayView<byte> bitmapData, Camera camera)
        {
            //FLIP Y
            //int x = (camera.width - 1) - ((index) % camera.width);
            int y = (camera.height - 1) - (((int)index) / camera.width);

            //NORMAL X
            int x = (((int)index) % camera.width);
            //int y = ((index) / camera.width);

            int newIndex = ((y * camera.width) + x) * 3;
            int oldIndexStart = (int)index * 3;

            bitmapData[newIndex] = (byte)(255.99f * data[oldIndexStart]);
            bitmapData[newIndex + 1] = (byte)(255.99f * data[oldIndexStart + 1]);
            bitmapData[newIndex + 2] = (byte)(255.99f * data[oldIndexStart + 2]);
        }

        public static void CreateGrayScaleBitmap(Index1 index, ArrayView<float> data, ArrayView<byte> bitmapData, Camera camera)
        {
            //FLIP Y
            //int x = (camera.width - 1) - ((index) % camera.width);
            int y = (camera.height - 1) - (((int)index) / camera.width);

            //NORMAL X
            int x = (((int)index) % camera.width);
            //int y = ((index) / camera.width);

            int newIndex = ((y * camera.width) + x);

            bitmapData[(newIndex * 3)] = (byte)(255.99f * data[(index)]);
            bitmapData[(newIndex * 3) + 1] = (byte)(255.99f * data[(index)]);
            bitmapData[(newIndex * 3) + 2] = (byte)(255.99f * data[(index)]);
        }

        private static Vec3 RandomUnitVector(XorShift128Plus rng)
        {
            float a = 2f * XMath.PI * rng.NextFloat();
            float z = (rng.NextFloat() * 2f) - 1f;
            float r = XMath.Sqrt(1f - z * z);
            return new Vec3(r * XMath.Cos(a), r * XMath.Sin(a), z);
        }

        private static HitRecord GetWorldHit(Ray r, dRenderData world, float minT)
        {
            HitRecord rec = GetSphereHit(r, world.spheres, minT);
            //HitRecord vRec = world.VoxelChunk.hit(r, minT, rec.t);
            //HitRecord triRec = GetMeshHit(r, world, vRec.t);

            //if (rec.t < vRec.t && rec.t < triRec.t)
            //{
                return rec;
            //}
            //else if (vRec.t < rec.t && vRec.t < triRec.t)
            //{
            //    return vRec;
            //}
            //else
            //{
            //    return triRec;
            //}
        }


        private static HitRecord GetSphereHit(Ray r, ArrayView<Sphere> spheres, float minT)
        {
            float closestT = 10000;
            int sphereIndex = -1;

            Sphere s;
            Vec3 oc;

            for (int i = 0; i < spheres.Length; i++)
            {
                s = spheres[i];
                oc = r.a - s.center;

                float b = Vec3.dot(oc, r.b);
                float c = Vec3.dot(oc, oc) - s.radiusSquared;
                float discr = (b * b) - (c);

                if (discr > 0.1f)
                {
                    float sqrtdisc = XMath.Sqrt(discr);
                    float temp = (-b - sqrtdisc);
                    if (temp < closestT && temp > minT)
                    {
                        closestT = temp;
                        sphereIndex = i;
                    }
                    else
                    {
                        temp = (-b + sqrtdisc);
                        if (temp < closestT && temp > minT)
                        {
                            closestT = temp;
                            sphereIndex = i;
                        }
                    }
                }
            }

            if (sphereIndex != -1)
            {
                oc = r.pointAtParameter(closestT);
                s = spheres[sphereIndex];
                return new HitRecord(closestT, oc, (oc - s.center) / s.radius, r.b, s.materialIndex, sphereIndex);
            }
            else
            {
                return new HitRecord(float.MaxValue, new Vec3(), new Vec3(), false, -1, -1);
            }
        }

        private static HitRecord GetMeshHit(Ray r, dRenderData world, float nearerThan)
        {
            float dist = nearerThan;
            HitRecord rec = new HitRecord(float.MaxValue, new Vec3(), new Vec3(), false, -1, -1);

            for (int i = 0; i < world.meshBuffers.Length; i++)
            {
                if (world.meshBuffers[i].boundingBox.hit(r, nearerThan, dist))
                {
                    HitRecord meshHit = GetTriangleHit(r, world, world.meshBuffers[i], dist);
                    if (meshHit.t < dist)
                    {
                        dist = meshHit.t;
                        rec = meshHit;
                    }
                }
            }

            return rec;
        }


        private static HitRecord GetTriangleHit(Ray r, dRenderData world, dMesh mesh, float nearerThan)
        {
            Triangle t = new Triangle();
            float currentNearestDist = nearerThan;
            int NcurrentIndex = -1;
            int material = 0;
            float Ndet = 0;

            for (int i = 0; i < mesh.triangleLength; i++)
            {
                t = mesh.GetTriangle(i, world);
                Vec3 tuVec = t.uVector();
                Vec3 tvVec = t.vVector();
                Vec3 pVec = Vec3.cross(r.b, tvVec);
                float det = Vec3.dot(tuVec, pVec);

                if (XMath.Abs(det) > nearerThan)
                {
                    float invDet = 1.0f / det;
                    Vec3 tVec = r.a - t.Vert0;
                    float u = Vec3.dot(tVec, pVec) * invDet;
                    Vec3 qVec = Vec3.cross(tVec, tuVec);
                    float v = Vec3.dot(r.b, qVec) * invDet;

                    if (u > 0 && u <= 1.0f && v > 0 && u + v <= 1.0f)
                    {
                        float temp = Vec3.dot(tvVec, qVec) * invDet;
                        if (temp > nearerThan && temp < currentNearestDist)
                        {
                            currentNearestDist = temp;
                            NcurrentIndex = i;
                            Ndet = det;
                            material = 0;
                        }
                    }
                }
            }

            if (NcurrentIndex == -1)
            {
                return new HitRecord(float.MaxValue, new Vec3(), new Vec3(), false, -1, -1);
            }
            else
            {
                if (Ndet < 0)
                {
                    return new HitRecord(currentNearestDist, r.pointAtParameter(currentNearestDist), -t.faceNormal(), true, material, NcurrentIndex);
                }
                else
                {
                    return new HitRecord(currentNearestDist, r.pointAtParameter(currentNearestDist), t.faceNormal(), false, material, NcurrentIndex);
                }
            }
        }


        private static ScatterRecord Scatter(Ray r, HitRecord rec, XorShift128Plus rng, ArrayView<MaterialData> materials, float minT)
        {
            MaterialData material = materials[rec.materialID];
            Ray ray;
            Vec3 outward_normal;
            Vec3 refracted;
            Vec3 reflected;
            float ni_over_nt;
            float cosine;

            if (material.type == 0) //Diffuse
            {
                refracted = rec.p + rec.normal + RandomUnitVector(rng);
                return new ScatterRecord(rec.materialID, new Ray(rec.p, refracted - rec.p), material.color, false);
            }
            else if (material.type == 1) // dielectric
            {
                if (Vec3.dot(r.b, rec.normal) > minT)
                {
                    outward_normal = -rec.normal;
                    ni_over_nt = material.ref_idx;
                    cosine = Vec3.dot(r.b, rec.normal);
                    cosine = XMath.Sqrt(1.0f - material.ref_idx * material.ref_idx * (1f - cosine * cosine));
                }
                else
                {
                    outward_normal = rec.normal;
                    ni_over_nt = 1.0f / material.ref_idx;
                    cosine = -Vec3.dot(r.b, rec.normal);
                }

                //moved the refract code here because I need the if (discriminant > 0) check
                float dt = Vec3.dot(r.b, outward_normal);
                float discriminant = 1.0f - ni_over_nt * ni_over_nt * (1f - dt * dt);

                if (discriminant > minT)
                {

                    if (rng.NextFloat() < schlick(cosine, material.ref_idx))
                    {
                        return new ScatterRecord(rec.materialID, new Ray(rec.p, Vec3.reflect(rec.normal, r.b)), material.color, true);
                    }
                    else
                    {
                        return new ScatterRecord(rec.materialID, new Ray(rec.p, ni_over_nt * (r.b - (outward_normal * dt)) - outward_normal * XMath.Sqrt(discriminant)), material.color, true);
                    }
                }
                else
                {
                    return new ScatterRecord(rec.materialID, new Ray(rec.p, Vec3.reflect(rec.normal, r.b)), material.color, true);
                }

            }
            else if (material.type == 2) //Metal
            {
                reflected = Vec3.reflect(rec.normal, r.b);
                if (material.reflectionConeAngleRadians > minT)
                {
                    ray = new Ray(rec.p, reflected + (material.reflectionConeAngleRadians * RandomUnitVector(rng)));
                }
                else
                {
                    ray = new Ray(rec.p, reflected);
                }

                if ((Vec3.dot(ray.b, rec.normal) > minT))
                {
                    return new ScatterRecord(rec.materialID, ray, material.color, true);
                }
            }
            else if (material.type == 3) //Lights
            {
                refracted = rec.p + rec.normal;
                return new ScatterRecord(rec.materialID, new Ray(rec.p, refracted - rec.p), material.color, false);
            }

            return new ScatterRecord(-1, r, new Vec3(), true);
        }

        private static float schlick(float cosine, float ref_idx)
        {
            float r0 = (1.0f - ref_idx) / (1.0f + ref_idx);
            r0 = r0 * r0;
            return r0 + (1.0f - r0) * XMath.Pow((1.0f - cosine), 5.0f);
        }
    }

    internal struct ScatterRecord
    {
        public int materialID;
        public Ray scatterRay;
        public Vec3 attenuation;
        public bool mirrorSkyLightingFix;

        public ScatterRecord(int materialID, Ray scatterRay, Vec3 attenuation, bool mirrorSkyLightingFix)
        {
            this.materialID = materialID;
            this.scatterRay = scatterRay;
            this.attenuation = attenuation;
            this.mirrorSkyLightingFix = mirrorSkyLightingFix;
        }
    }

    internal struct HitRecord
    {
        public readonly float t;
        public readonly bool inside;
        public readonly Vec3 p;
        public readonly Vec3 normal;
        public readonly int materialID;
        public readonly int drawableID;

        public HitRecord(float t, Vec3 p, Vec3 normal, bool inside, int materialID, int drawableID)
        {
            this.t = t;
            this.inside = inside;
            this.p = p;
            this.normal = normal;
            this.materialID = materialID;
            this.drawableID = drawableID;
        }

        public HitRecord(float t, Vec3 p, Vec3 normal, Vec3 rayDirection, int materialID, int drawableID)
        {
            this.t = t;
            inside = Vec3.dot(normal, rayDirection) > 0;
            this.p = p;
            this.normal = normal;
            this.materialID = materialID;
            this.drawableID = drawableID;
        }
    }
}
