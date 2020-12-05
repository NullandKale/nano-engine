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

        private bool isLinux = false;

        private Action<Index1, dFrameData, ArrayView<ulong>> InitPerPixelRngData;
        private Action<Index1, Camera, dFrameData> GenerateRays;
        private Action<Index1, Camera, dFrameData, dRenderData> ColorRay;
        private Action<Index1, ArrayView<float>> NormalizeLighting;
        private Action<Index1, Camera, dFrameData, int> CombineLightingAndColor;
        private Action<Index1, dFrameData, float, int, int> TAA;
        private Action<Index1, Camera, ArrayView<float>, ArrayView<byte>, bool> DrawToBitmap;
        public GPU(bool forceCPU, bool isLinux)
        {
            this.isLinux = isLinux;

            context = new Context();
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
            GenerateRays = device.LoadAutoGroupedStreamKernel<Index1, Camera, dFrameData>(GPUKernels.GenerateRays);
            ColorRay = device.LoadAutoGroupedStreamKernel<Index1, Camera, dFrameData, dRenderData>(GPUKernels.ColorRay);
            NormalizeLighting = device.LoadAutoGroupedStreamKernel<Index1, ArrayView<float>>(GPUKernels.NormalizeLighting);
            CombineLightingAndColor = device.LoadAutoGroupedStreamKernel<Index1, Camera, dFrameData, int>(GPUKernels.CombineLightingAndColor);
            TAA = device.LoadAutoGroupedStreamKernel<Index1, dFrameData, float, int, int>(GPUKernels.NULLTAA);
            DrawToBitmap = device.LoadAutoGroupedStreamKernel<Index1, Camera, ArrayView<float>, ArrayView<byte>, bool>(GPUKernels.DrawToBitmap);
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

        public void Render(ByteFrameBuffer output, Camera camera, RenderDataManager renderDataManager, FrameData frameData, int tick, int ticksSinceCameraMovement)
        {
            long outputLength = output.memoryBuffer.Length / 4;

            GenerateRays(outputLength, camera, frameData.deviceFrameData);
            ColorRay(outputLength, camera, frameData.deviceFrameData, renderDataManager.getDeviceRenderData());
            NormalizeLighting(outputLength, frameData.lightBuffer);
            CombineLightingAndColor(outputLength, camera, frameData.deviceFrameData, camera.mode);
            TAA(outputLength, frameData.deviceFrameData, camera.TAAExponent, tick, ticksSinceCameraMovement);
            DrawToBitmap(outputLength, camera, frameData.TAABuffer, output.frameBuffer.frame, isLinux);
           
            device.Synchronize();
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
            int x = ((int)pixel) % camera.width;
            int y = ((int)pixel) / camera.width;

            float xJittered = x + frameData.rngBuffer[pixel].NextFloat();
            float yJittered = y + frameData.rngBuffer[pixel].NextFloat();

            frameData.rayBuffer[pixel] = camera.GetRay(xJittered, yJittered);
        }

        public static void ColorRay(Index1 pixel, Camera camera, dFrameData frameData, dRenderData renderData)
        {
            Vec3 attenuation = new Vec3(1, 1, 1);
            Vec3 lighting = new Vec3(1, 1, 1);

            int lightsToSample = XMath.Min((int)renderData.lightSphereIDs.Length, camera.lightsPerSample);
            Ray currentRay = frameData.rayBuffer[pixel];
            XorShift128Plus rng = frameData.rngBuffer[pixel];

            float minT = 0.001f;

            for (int i = 0; i < camera.maxColorBounces; i++)
            {
                HitRecord hitRecord = GetWorldHit(renderData, currentRay, minT);

                if (hitRecord.materialID != -1)
                {
                    if (i == 0)
                    {
                        WriteFirstHitData(pixel, frameData, hitRecord);
                    }

                    attenuation *= (ColorHit(pixel, renderData, frameData, hitRecord, ref currentRay, ref rng, minT));
                    if (i < camera.lightBounces && frameData.metaBuffer[pixel] != -1)
                    {
                        lighting *= (SampleLights(lightsToSample, renderData, hitRecord, ref rng));
                    }
                    else
                    {
                        lighting *= new Vec3(0.05f, 0.05f, 0.05f);
                    }
                }
                else
                {
                    if(i == 0)
                    {
                        WriteColorToFrameBuffer(pixel, frameData, camera.noHitColor, new Vec3());
                        frameData.rngBuffer[pixel] = rng;
                        frameData.metaBuffer[pixel] = -1;
                        return;
                    }    
                }
            }
            WriteColorToFrameBuffer(pixel, frameData, attenuation, lighting);
            frameData.rngBuffer[pixel] = rng;
        }

        private static Vec3 ColorHit(int pixel, dRenderData renderData, dFrameData frameData, HitRecord hitRecord, ref Ray currentRay, ref XorShift128Plus rng, float minT)
        {
            ScatterRecord sRec = Scatter(currentRay, hitRecord, ref rng, renderData.mats, minT);
            if (sRec.materialID != -1)
            {
                currentRay = sRec.scatterRay;
                return sRec.attenuation;
            }
            else
            {
                frameData.metaBuffer[pixel] = -1;
                return new Vec3();
            }
        }

        private static Vec3 SampleLights(int lightsToSample, dRenderData renderData, HitRecord hitRecord, ref XorShift128Plus rng)
        {
            Vec3 luminance = new Vec3(1, 1, 1);
            int hit = 0;

            for (int j = 0; j < lightsToSample; j++)
            {
                Sphere light = renderData.spheres[renderData.lightSphereIDs[rng.Next(0, renderData.lightSphereIDs.Length)]];
                MaterialData lightMat = renderData.mats[light.materialIndex];
                Vec3 lightDir = light.center - (hitRecord.p + (hitRecord.normal * (rng.NextFloat() * 0.1f)));

                HitRecord shadowRec = GetWorldHit(renderData, new Ray(hitRecord.p, lightDir), 0.00001f);
                if(shadowRec.materialID != -1 && renderData.mats[shadowRec.materialID].type == MaterialData.LIGHT)
                {
                    luminance *= (lightMat.color * XMath.Max(0f, Vec3.dot(hitRecord.normal, lightDir)));
                    hit++;
                }
            }

            if(hit > 0)
            {
                return luminance;
            }
            else
            {
                return new Vec3();
            }
        }

        private static void WriteFirstHitData(int pixel, dFrameData frameData, HitRecord hitRecord)
        {
            frameData.depthBuffer[pixel] = hitRecord.t;
            frameData.metaBuffer[pixel] = hitRecord.drawableID;
        }

        private static void WriteColorToFrameBuffer(int pixel, dFrameData frameData, Vec3 color, Vec3 light)
        {
            int rIndex = pixel * 3;
            int gIndex = rIndex + 1;
            int bIndex = rIndex + 2;

            frameData.colorBuffer[rIndex] = color.x;
            frameData.colorBuffer[gIndex] = color.y;
            frameData.colorBuffer[bIndex] = color.z;

            frameData.lightBuffer[rIndex] = light.x;
            frameData.lightBuffer[gIndex] = light.y;
            frameData.lightBuffer[bIndex] = light.z;
        }

        public static void NormalizeLighting(Index1 index, ArrayView<float> data)
        {
            int rIndex = (int)index * 3;
            int gIndex = rIndex + 1;
            int bIndex = rIndex + 2;

            if (data[rIndex] != -1)
            {
                //Vec3 color = Vec3.reinhard(new Vec3(data[rIndex], data[gIndex], data[bIndex]));
                Vec3 color = Vec3.aces_approx(new Vec3(data[rIndex], data[gIndex], data[bIndex]));

                data[rIndex] = color.x;
                data[gIndex] = color.y;
                data[bIndex] = color.z;
            }
        }

        public static void CombineLightingAndColor(Index1 index, Camera camera, dFrameData frameData, int mode)
        {
            int rIndex = (int)index * 3;
            int gIndex = rIndex + 1;
            int bIndex = rIndex + 2;

            Vec3 col = new Vec3(frameData.colorBuffer[rIndex], frameData.colorBuffer[gIndex], frameData.colorBuffer[bIndex]);
            Vec3 light = new Vec3(frameData.lightBuffer[rIndex], frameData.lightBuffer[gIndex], frameData.lightBuffer[bIndex]);

            switch (mode)
            {
                case 0:
                    {
                        if (frameData.metaBuffer[index] == -2)
                        {
                            frameData.colorBuffer[rIndex] = col.x;
                            frameData.colorBuffer[gIndex] = col.y;
                            frameData.colorBuffer[bIndex] = col.z;
                        }
                        else if (frameData.metaBuffer[index] == -1 || light.x == -1)
                        {
                            frameData.colorBuffer[rIndex] = col.x * camera.minLight;
                            frameData.colorBuffer[gIndex] = col.y * camera.minLight;
                            frameData.colorBuffer[bIndex] = col.z * camera.minLight;
                        }
                        else
                        {
                            frameData.colorBuffer[rIndex] = col.x * (light.x < camera.minLight ? light.x + camera.minLight : light.x);
                            frameData.colorBuffer[gIndex] = col.y * (light.y < camera.minLight ? light.y + camera.minLight : light.y);
                            frameData.colorBuffer[bIndex] = col.z * (light.z < camera.minLight ? light.z + camera.minLight : light.z);
                        }
                        return;
                    }
                case 1:
                    {
                        frameData.colorBuffer[rIndex] = col.x;
                        frameData.colorBuffer[gIndex] = col.y;
                        frameData.colorBuffer[bIndex] = col.z;
                        return;
                    }
                case 2:
                    {
                        frameData.colorBuffer[rIndex] = (light.x < camera.minLight ? light.x + camera.minLight : light.x);
                        frameData.colorBuffer[gIndex] = (light.y < camera.minLight ? light.y + camera.minLight : light.y);
                        frameData.colorBuffer[bIndex] = (light.z < camera.minLight ? light.z + camera.minLight : light.z);
                        return;
                    }
            }
        }

        public static void NULLTAA(Index1 index, dFrameData frameData, float exponent, int tick, int ticksSinceCameraMovement)
        {
            int rIndex = (int)index * 3;
            int gIndex = rIndex + 1;
            int bIndex = gIndex + 1;

            if (tick == 0)
            {
                frameData.TAABuffer[rIndex] = frameData.colorBuffer[rIndex];
                frameData.TAABuffer[gIndex] = frameData.colorBuffer[gIndex];
                frameData.TAABuffer[bIndex] = frameData.colorBuffer[bIndex];
            }
            else
            {
                if (ticksSinceCameraMovement == 0)
                {
                    exponent = 1;
                }
                else if (ticksSinceCameraMovement > 1)
                {
                    exponent = exponent / (ticksSinceCameraMovement / 2);
                }

                frameData.TAABuffer[rIndex] = (exponent * frameData.colorBuffer[rIndex]) + ((1 - exponent) * frameData.TAABuffer[rIndex]);
                frameData.TAABuffer[gIndex] = (exponent * frameData.colorBuffer[gIndex]) + ((1 - exponent) * frameData.TAABuffer[gIndex]);
                frameData.TAABuffer[bIndex] = (exponent * frameData.colorBuffer[bIndex]) + ((1 - exponent) * frameData.TAABuffer[bIndex]);
            }
        }

        public static void DrawToBitmap(Index1 index, Camera camera, ArrayView<float> data, ArrayView<byte> bitmapData, bool isLinux)
        {
            int x = (((int)index) % camera.width);
            int y = ((index) / camera.width);

            int newIndex = ((y * camera.width) + x) * 4;
            int oldIndexStart = (int)index * 3;

            if (isLinux)
            {
                bitmapData[newIndex] = (byte)(255.99f * data[oldIndexStart + 2]);
                bitmapData[newIndex + 1] = (byte)(255.99f * data[oldIndexStart + 1]);
                bitmapData[newIndex + 2] = (byte)(255.99f * data[oldIndexStart]);
                bitmapData[newIndex + 3] = 255;
            }
            else
            {
                bitmapData[newIndex] = (byte)(255.99f * data[oldIndexStart]);
                bitmapData[newIndex + 1] = (byte)(255.99f * data[oldIndexStart + 1]);
                bitmapData[newIndex + 2] = (byte)(255.99f * data[oldIndexStart + 2]);
                bitmapData[newIndex + 3] = 255;
            }
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

        public static (float min, float max) ReduceMax(Accelerator device, ArrayView<float> map)
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

        private static Vec3 RandomUnitVector(ref XorShift128Plus rng)
        {
            return RandomUnitVectorInaccurate(ref rng);
            //return RandomUnitVectorSlow(ref rng);
        }

        public static Vec3 RandomUnitVectorInaccurate(ref XorShift128Plus rng)
        {
            Vec3 random = default;

            for (int i = 0; i < 4; i++)
            {
                random = new Vec3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat());
                if (random.lengthSquared() <= 1)
                {
                    return random;
                }
            }

            return Vec3.unitVector(random);
        }

        public static Vec3 RandomUnitVectorSlow(ref XorShift128Plus rng)
        {
            float a = 2f * XMath.PI * rng.NextFloat();
            float z = (rng.NextFloat() * 2f) - 1f;
            float r = XMath.Sqrt(1f - z * z);
            return new Vec3(r * XMath.Cos(a), r * XMath.Sin(a), z);
        }

        private static HitRecord GetWorldHit(dRenderData renderData, Ray r, float minT)
        {
            HitRecord rec = GetSphereHit(renderData, r, renderData.spheres, minT);
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


        private static HitRecord GetSphereHit(dRenderData renderData, Ray r, ArrayView<Sphere> spheres, float minT)
        {
            float closestT = float.MaxValue;
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

                if (discr > 0f)
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


        private static ScatterRecord Scatter(Ray r, HitRecord rec, ref XorShift128Plus rng, ArrayView<MaterialData> materials, float minT)
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
                refracted = rec.normal + RandomUnitVector(ref rng);
                return new ScatterRecord(rec.materialID, new Ray(rec.p, refracted), material.color, false);
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
                    ray = new Ray(rec.p, reflected + (material.reflectionConeAngleRadians * RandomUnitVector(ref rng)));
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
            //else if (material.type == 3) //Lights
            //{
            //    //refracted = rec.p + rec.normal;
            //    //return new ScatterRecord(rec.materialID, new Ray(rec.p, refracted - rec.p), material.color, false);
            //}

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
        public bool hitSky;

        public ScatterRecord(int materialID, Ray scatterRay, Vec3 attenuation, bool mirrorSkyLightingFix)
        {
            this.materialID = materialID;
            this.scatterRay = scatterRay;
            this.attenuation = attenuation;
            this.hitSky = mirrorSkyLightingFix;
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
