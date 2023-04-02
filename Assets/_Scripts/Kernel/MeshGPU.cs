/* MeshGPU.cs
 * To enable physics, delete "//" before "#define BUFFER" in <MeshGPU.cs>, <Spectrum.compute> and <FFT.compute>.
 * This will use <StructedBuffer> as input in compute shaders, instead of <Texture>.
 * Transfering data from GPU(VRAM) to CPU(RAM) can greatly affect performance.
 */

// #define BUFFER
using UnityEngine;
using System.Collections.Generic;

public class MeshGPU : MonoBehaviour
{
    public static MeshGPU instance;

    // 高度图尺寸 受限于GPU线程数 最大可取N=1024
    public int mapSize = 1024;
    // 网格尺寸 受限于Unity引擎网格顶点数限制 最大可取255
    public int meshSize = 128;
    // 单块网格大小
    public float tileSize = 8;
    // 海面网格数
    public float seaSize = 8;

    // 网格数据
    private Mesh mesh;
    // 网格组件
    private List<MeshFilter> meshFilter = new List<MeshFilter>();
    // 网格渲染器
    private List<MeshRenderer> meshRenderer = new List<MeshRenderer>();
    // 海水材质
    public Material material;

    // 光源颜色
    public Color lightColor = new Color(0.5f, 0.4f, 0.3f, 1);

    #region Foam 相关参数
    // Foam 生长速度
    public float upSpeed = 1;
    // Foam 消散速度
    public float downSpeed = 1;
    #endregion

    #region OceanShader 相关参数
    public Color waterColor = new Color(0, 0.17f, 0.34f, 1);
    public float specularity = 128;
    public float foamSize = 4;
    public float fresnelPower = 20;
    #endregion

    #region 波谱相关参数
    // 风速
    public float windSpeed = 16.0f;
    // 全局速度
    public float speed = 1.0f;
    #endregion

    #region ComputeShader Related
    // Compute Shader
    [SerializeField]
    private ComputeShader shaderSpec;
    private int specInit;
    private int specUpdate;
    [SerializeField]
    private ComputeShader shaderFFT;
    private int fftX = 0;
    private int fftY = 1;
    [SerializeField]
    private ComputeShader shaderNormal;
    private int calNormal;

    public struct Data
    {
        public float x;
        public float y;
    }

    // Compute Shader Vars
    private Texture2D texButterfly;

#if BUFFER
    private RenderTexture specH0;
    private RenderTexture specHFRT;
    private RenderTexture specDxFRT;
    private RenderTexture specDyFRT;
    private RenderTexture normalRT;
    private RenderTexture foamRT;
    private Data[] fftData;
    private Data[] specHData;
    private Data[] specDxData;
    private Data[] specDyData;
    private Data[] specHFData;
    private Data[] specDxFData;
    private Data[] specDyFData;
    private ComputeBuffer fftTemp;
    private ComputeBuffer specH;
    private ComputeBuffer specDx;
    private ComputeBuffer specDy;
    private ComputeBuffer specHF;
    private ComputeBuffer specDxF;
    private ComputeBuffer specDyF;
#else
    private RenderTexture specH0;
    private RenderTexture specH;
    private RenderTexture specDx;
    private RenderTexture specDy;
    private RenderTexture normalRT;
    private RenderTexture foamRT;
    private RenderTexture specHFRT;
    private RenderTexture specDxFRT;
    private RenderTexture specDyFRT;
    private RenderTexture fftTemp;
#endif

    private void InitComputeTexture(ref RenderTexture src, RenderTextureFormat format)
    {
        src = new RenderTexture(mapSize, mapSize, 0, format)
        {
            enableRandomWrite = true
        };
        src.Create();
    }
#endregion

    // HideFlags设为DontSave的物体需要手动删除避免内存泄漏
    private void OnDisable()
    {
        if (reflectionTexture)
        {
            DestroyImmediate(reflectionTexture);
            reflectionTexture = null;
        }
        if (refractionTexture)
        {
            DestroyImmediate(refractionTexture);
            refractionTexture = null;
        }
        foreach (KeyValuePair<Camera, Camera> kvp in reflectionCameras)
            DestroyImmediate(kvp.Value.gameObject);
        reflectionCameras.Clear();
        foreach (KeyValuePair<Camera, Camera> kvp in refractionCameras)
            DestroyImmediate(kvp.Value.gameObject);
        refractionCameras.Clear();

        // 释放GPU计算资源 如不释放会造城显存占用越来越大
        fftTemp.Release();
        specH.Release();
        specDx.Release();
        specDy.Release();
#if BUFFER
        specHF.Release();
        specDxF.Release();
        specDyF.Release();
#endif
    }

#region 离屏渲染
    public bool renderReflection = true;
    public bool renderRefraction = true;
    public LayerMask renderLayers = -1;
    public int renderTexWidth = 1024;
    public int renderTexHeight = 1024;
    public float clipPlaneOffset = 0.07f;
    private RenderTexture reflectionTexture = null;
    private RenderTexture refractionTexture = null;
    private Dictionary<Camera, Camera> reflectionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table
    private Dictionary<Camera, Camera> refractionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table

    // 镜面反射矩阵系数自行推导
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }

    // 该函数将反射-折射平面变换到相机空间并用标准四维向量形式来表示并返回
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        // 这里w分量为平面到点的距离d
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    private void RenderReflectionAndRefraction()
    {
        // 渲染层级设置
        int cullingMask = ~(1 << 4) & renderLayers.value;

        Camera cam = Camera.current;
        if (!cam)
            return;

        // 建立反射和透射相机
        Camera reflectionCamera, refractionCamera;
        CreateWaterObjects(cam, out reflectionCamera, out refractionCamera);

        // find out the reflection plane: position and normal in world space
        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        UpdateCameraModes(cam, reflectionCamera);
        UpdateCameraModes(cam, refractionCamera);

        // Render reflection if needed
        if (this.renderReflection)
        {
            // Reflect camera around reflection plane
            float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
            Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

            Matrix4x4 reflection = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection, reflectionPlane);
            Vector3 oldpos = cam.transform.position;
            Vector3 newpos = reflection.MultiplyPoint(oldpos);
            reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

            // Setup oblique projection matrix so that near plane is our reflection
            // plane. This way we clip everything below/above it for free.
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 0.01f);
            reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

            reflectionCamera.cullingMask = cullingMask; // never render water layer
            reflectionCamera.targetTexture = reflectionTexture;
            GL.invertCulling = true;
            reflectionCamera.transform.position = newpos;
            Vector3 euler = cam.transform.eulerAngles;
            reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);
            reflectionCamera.Render();
            reflectionCamera.transform.position = oldpos;
            GL.invertCulling = false;
            material.SetTexture("_Reflection", reflectionTexture);
        }

        // Render refraction
        if (this.renderRefraction)
        {
            refractionCamera.worldToCameraMatrix = cam.worldToCameraMatrix;

            // Setup oblique projection matrix so that near plane is our reflection
            // plane. This way we clip everything below/above it for free.
            Vector4 clipPlane = CameraSpacePlane(refractionCamera, pos, normal, -0.01f);
            refractionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

            refractionCamera.cullingMask = cullingMask; // never render water layer
            refractionCamera.targetTexture = refractionTexture;
            refractionCamera.transform.position = cam.transform.position;
            refractionCamera.transform.rotation = cam.transform.rotation;
            refractionCamera.Render();
            material.SetTexture("_Refraction", refractionTexture);
        }
    }

    private void CreateWaterObjects(Camera currentCamera, out Camera reflectionCamera, out Camera refractionCamera)
    {
        reflectionCamera = null;
        refractionCamera = null;

        if (this.renderReflection)
        {
            // Reflection render texture
            // 定义纹理
            if (!reflectionTexture)
            {
                if (reflectionTexture)
                    DestroyImmediate(reflectionTexture);
                reflectionTexture = new RenderTexture(renderTexWidth, renderTexHeight, 16)
                {
                    name = "__WaterReflection" + GetInstanceID(),
                    isPowerOfTwo = true,
                    hideFlags = HideFlags.DontSave
                };
            }

            // Camera for reflection
            // 设置渲染用相机参数
            reflectionCameras.TryGetValue(currentCamera, out reflectionCamera);
            if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
            {
                GameObject go = new GameObject("Water Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
                reflectionCamera = go.GetComponent<Camera>();
                reflectionCamera.enabled = false;
                reflectionCamera.transform.position = transform.position;
                reflectionCamera.transform.rotation = transform.rotation;
                reflectionCamera.gameObject.AddComponent<FlareLayer>();
                go.hideFlags = HideFlags.HideAndDontSave;
                reflectionCameras[currentCamera] = reflectionCamera;
            }
        }

        if (this.renderRefraction)
        {
            // Refraction render texture
            // 定义纹理
            if (!refractionTexture)
            {
                if (refractionTexture)
                    DestroyImmediate(refractionTexture);
                refractionTexture = new RenderTexture(renderTexWidth, renderTexHeight, 16)
                {
                    name = "__WaterRefraction" + GetInstanceID(),
                    isPowerOfTwo = true,
                    hideFlags = HideFlags.DontSave
                };
            }

            // Camera for refraction
            // 设置渲染用相机参数
            refractionCameras.TryGetValue(currentCamera, out refractionCamera);
            if (!refractionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
            {
                GameObject go = new GameObject("Water Refr Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
                refractionCamera = go.GetComponent<Camera>();
                refractionCamera.enabled = false;
                refractionCamera.transform.position = transform.position;
                refractionCamera.transform.rotation = transform.rotation;
                refractionCamera.gameObject.AddComponent<FlareLayer>();
                go.hideFlags = HideFlags.HideAndDontSave;
                refractionCameras[currentCamera] = refractionCamera;
            }
        }
    }

    // 渲染纹理相机设置
    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
            return;
        // set water camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
            Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }
#endregion

    private void Start()
    {
        UIManager.instance.Init();
    }

    private void Awake()
    {
        instance = this;

#region 函数查找
        specInit = shaderSpec.FindKernel("SpecInit");
        specUpdate = shaderSpec.FindKernel("SpecUpdate");
        calNormal = shaderNormal.FindKernel("CalNormal");
#endregion

        Init();
    }

    private void Update()
    {
        SettingsUpdate();

        if (this.renderReflection || this.renderRefraction)
            RenderReflectionAndRefraction();

        CalculateMap();
    }

    private void FixedUpdate()
    {
        #region 获取物理模拟所需要的数据 对性能损耗大
#if BUFFER
        if (enablePhysics && Time.realtimeSinceStartup > 5.0f)
        {
            specHF.GetData(specHFData);
            specDxF.GetData(specDxFData);
            specDyF.GetData(specDyFData);
        }
#endif
        #endregion
    }

    [HideInInspector]
    public bool reinit = false;
    private void SettingsUpdate()
    {
        GameObject.Find("Directional Light").GetComponent<Light>().color = lightColor;

        if (reinit)
        {
            reinit = false;
            SpecInit();
        }
    }

    private void Init()
    {
        #region 计算纹理初始化
#if BUFFER
        InitComputeTexture(ref specH0, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref specHFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref specDxFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref specDyFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref normalRT, RenderTextureFormat.RGFloat);
        InitComputeTexture(ref foamRT, RenderTextureFormat.RFloat);
        fftData = new Data[mapSize * mapSize]; fftTemp = new ComputeBuffer(fftData.Length, sizeof(float) * 2); fftTemp.SetData(fftData);
        specHData = new Data[mapSize * mapSize]; specH = new ComputeBuffer(specHData.Length, sizeof(float) * 2); specH.SetData(specHData);
        specDxData = new Data[mapSize * mapSize]; specDx = new ComputeBuffer(specDxData.Length, sizeof(float) * 2); specDx.SetData(specDxData);
        specDyData = new Data[mapSize * mapSize]; specDy = new ComputeBuffer(specDyData.Length, sizeof(float) * 2); specDy.SetData(specDyData);
        specHFData = new Data[mapSize * mapSize]; specHF = new ComputeBuffer(specHFData.Length, sizeof(float) * 2); specHF.SetData(specHFData);
        specDxFData = new Data[mapSize * mapSize]; specDxF = new ComputeBuffer(specDxFData.Length, sizeof(float) * 2); specDxF.SetData(specDxFData);
        specDyFData = new Data[mapSize * mapSize]; specDyF = new ComputeBuffer(specDyFData.Length, sizeof(float) * 2); specDyF.SetData(specDyFData);
#else
        InitComputeTexture(ref specH0, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref fftTemp, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref specH, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref specDx, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref specDy, RenderTextureFormat.ARGBFloat);
        InitComputeTexture(ref specHFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref specDxFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref specDyFRT, RenderTextureFormat.RFloat);
        InitComputeTexture(ref normalRT, RenderTextureFormat.RGFloat);
        InitComputeTexture(ref foamRT, RenderTextureFormat.RFloat);
#endif
        #endregion

        // 蝶形运算表初始化
        Butterfly();

        // 选取FFT内核计算函数代号
        int baseLog2Size = Mathf.RoundToInt(Mathf.Log(128, 2));
        int log2Size = Mathf.RoundToInt(Mathf.Log(mapSize, 2));
        fftX = (log2Size - baseLog2Size) * 2;
        fftY = fftX + 1;

        // 波谱初始化
        SpecInit();

        // 网格初始化
        MeshInit();
    }

#region DEBUG
    public bool debugH = false;
    public bool debugN = false;
    public bool debugRefl = false;
    public bool debugRefr = false;
    public bool debugDx = false;
    public bool debugDy = false;
    public bool debugFoam = false;
    private void OnGUI()
    {
        if (debugRefr)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), refractionTexture, ScaleMode.StretchToFill, false);
        if (debugRefl)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), reflectionTexture, ScaleMode.StretchToFill, false);
        if (debugH)
            GUI.DrawTexture(new Rect(Screen.width / 2, 0, mapSize, mapSize), specHFRT, ScaleMode.StretchToFill, false);
        if (debugN)
            GUI.DrawTexture(new Rect(Screen.width / 2, 0, mapSize, mapSize), normalRT, ScaleMode.StretchToFill, false);
        if (debugDx)
            GUI.DrawTexture(new Rect(Screen.width / 2, 0, mapSize, mapSize), specDxFRT, ScaleMode.StretchToFill, false);
        if (debugDy)
            GUI.DrawTexture(new Rect(Screen.width / 2, 0, mapSize, mapSize), specDyFRT, ScaleMode.StretchToFill, false);
        if (debugFoam)
            GUI.DrawTexture(new Rect(Screen.width / 2, 0, mapSize, mapSize), foamRT, ScaleMode.StretchToFill, false);
    }
#endregion

    //  From GX-Encino Waves
    private void Butterfly()
    {
        int log2Size = Mathf.RoundToInt(Mathf.Log(mapSize, 2));

        var butterflyData = new Vector2[mapSize * log2Size];

        int offset = 1, numIterations = mapSize >> 1;
        for (int rowIndex = 0; rowIndex < log2Size; rowIndex++)
        {
            int rowOffset = rowIndex * mapSize;

            // Weights
            {
                int start = 0, end = 2 * offset;
                for (int iteration = 0; iteration < numIterations; iteration++)
                {
                    float bigK = 0.0f;
                    for (int K = start; K < end; K += 2)
                    {
                        float phase = 2.0f * Mathf.PI * bigK * numIterations / mapSize;
                        float cos = Mathf.Cos(phase);
                        float sin = Mathf.Sin(phase);

                        butterflyData[rowOffset + K / 2].x = cos;
                        butterflyData[rowOffset + K / 2].y = -sin;

                        butterflyData[rowOffset + K / 2 + offset].x = -cos;
                        butterflyData[rowOffset + K / 2 + offset].y = sin;

                        bigK += 1.0f;
                    }
                    start += 4 * offset;
                    end = start + 2 * offset;
                }
            }

            numIterations >>= 1;
            offset <<= 1;
        }

        var butterflyBytes = new byte[butterflyData.Length * sizeof(ushort) * 2];
        for (uint i = 0; i < butterflyData.Length; i++)
        {
            uint byteOffset = i * sizeof(ushort) * 2;
            HalfHelper.SingleToHalf(butterflyData[i].x, butterflyBytes, byteOffset);
            HalfHelper.SingleToHalf(butterflyData[i].y, butterflyBytes, byteOffset + sizeof(ushort));
        }

        texButterfly = new Texture2D(mapSize, log2Size, TextureFormat.RGHalf, false);
        texButterfly.LoadRawTextureData(butterflyBytes);
        texButterfly.Apply(false, true);
    }

    private void SpecInit()
    {
        shaderSpec.SetInt("size", mapSize);
        shaderSpec.SetFloat("domainSize", meshSize * tileSize);
        shaderSpec.SetFloat("windSpeed", windSpeed);
        shaderSpec.SetTexture(specInit, "outputH0", specH0);
        shaderSpec.Dispatch(specInit, mapSize / 32, mapSize / 32, 1);
    }

#if BUFFER
    private void SpecUpdate()
    {
        shaderSpec.SetFloat("time", Time.time * speed);
        shaderSpec.SetTexture(specUpdate, "inputH0", specH0);
        shaderSpec.SetBuffer(specUpdate, "outputH", specH);
        shaderSpec.SetBuffer(specUpdate, "outputDx", specDx);
        shaderSpec.SetBuffer(specUpdate, "outputDy", specDy);
        shaderSpec.Dispatch(specUpdate, mapSize / 32, mapSize / 32, 1);
    }

    private void FFT(ComputeBuffer spectrum, ComputeBuffer output, RenderTexture outputRT)
    {
        shaderFFT.SetBuffer(fftX, "input", spectrum);
        shaderFFT.SetTexture(fftX, "inputButterfly", texButterfly);
        shaderFFT.SetBuffer(fftX, "output", fftTemp);
        shaderFFT.Dispatch(fftX, 1, mapSize, 1);
        shaderFFT.SetBuffer(fftY, "input", fftTemp);
        shaderFFT.SetTexture(fftY, "inputButterfly", texButterfly);
        shaderFFT.SetBuffer(fftY, "output", output);
        shaderFFT.SetTexture(fftY, "outputRT", outputRT);
        shaderFFT.Dispatch(fftY, mapSize, 1, 1);
    }
#else
    private void SpecUpdate()
    {
        shaderSpec.SetFloat("time", Time.time * speed);
        shaderSpec.SetTexture(specUpdate, "inputH0", specH0);
        shaderSpec.SetTexture(specUpdate, "outputH", specH);
        shaderSpec.SetTexture(specUpdate, "outputDx", specDx);
        shaderSpec.SetTexture(specUpdate, "outputDy", specDy);
        shaderSpec.Dispatch(specUpdate, mapSize / 32, mapSize / 32, 1);
    }

    private void FFT(RenderTexture spectrum, RenderTexture output)
    {
        shaderFFT.SetTexture(fftX, "input", spectrum);
        shaderFFT.SetTexture(fftX, "inputButterfly", texButterfly);
        shaderFFT.SetTexture(fftX, "output", fftTemp);
        shaderFFT.Dispatch(fftX, 1, mapSize, 1);
        shaderFFT.SetTexture(fftY, "input", fftTemp);
        shaderFFT.SetTexture(fftY, "inputButterfly", texButterfly);
        shaderFFT.SetTexture(fftY, "output", output);
        shaderFFT.Dispatch(fftY, mapSize, 1, 1);
    }
#endif

    private void CalculateNormal()
    {
        shaderNormal.SetInt("mapSize", mapSize);
        shaderNormal.SetFloat("tileSize", tileSize);
        shaderNormal.SetFloat("deltaTime", Time.deltaTime);
        shaderNormal.SetFloat("upSpeed", upSpeed);
        shaderNormal.SetFloat("downSpeed", downSpeed);
        shaderNormal.SetTexture(calNormal, "inputH", specHFRT);
        shaderNormal.SetTexture(calNormal, "inputDx", specDxFRT);
        shaderNormal.SetTexture(calNormal, "inputDy", specDyFRT);
        shaderNormal.SetTexture(calNormal, "outputNormal", normalRT);
        shaderNormal.SetTexture(calNormal, "foamRT", foamRT);
        shaderNormal.Dispatch(calNormal, mapSize / 32, mapSize / 32, 1);
    }

    private void CalculateMap()
    {
        SpecUpdate();

#if BUFFER
        FFT(specH, specHF, specHFRT);
        FFT(specDx, specDxF, specDxFRT);
        FFT(specDy, specDyF, specDyFRT);
#else
        FFT(specH, specHFRT);
        FFT(specDx, specDxFRT);
        FFT(specDy, specDyFRT);
#endif

        CalculateNormal();

        material.SetInt("mapSize", mapSize);
        material.SetFloat("sampleScale", 1.0f / tileSize);
        material.SetTexture("inputH", specHFRT);
        material.SetTexture("inputDx", specDxFRT);
        material.SetTexture("inputDy", specDyFRT);
        material.SetTexture("inputNormal", normalRT);
        material.SetTexture("foamRT", foamRT);

        material.SetColor("_WaterColor", waterColor);
        material.SetFloat("_Specularity", specularity);
        material.SetFloat("_FresnelPower", fresnelPower);
        material.SetFloat("_FoamSize", foamSize);
    }

    private void MeshInit()
    {
#region 组件初始化
        mesh = new Mesh();
        for (int i = 0; i < seaSize; ++i)
        {
            for (int j = 0; j < seaSize; ++j)
            {
                GameObject tile = new GameObject("Tile");
                tile.transform.parent = transform;
                tile.transform.position = transform.position + new Vector3(j * meshSize * tileSize, 0, i * meshSize * tileSize);
                tile.layer = 4;

                MeshFilter tileFilter = tile.AddComponent<MeshFilter>();
                tileFilter.mesh = mesh;
                meshFilter.Add(tileFilter);

                MeshRenderer tileRenderer = tile.AddComponent<MeshRenderer>();
                tileRenderer.receiveShadows = true;
                tileRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tileRenderer.material = material;
                meshRenderer.Add(tileRenderer);
            }
        }
#endregion

        // For Vertices
        Vector3[] vertices = new Vector3[(meshSize + 1) * (meshSize + 1)];
        Vector2[] uv = new Vector2[(meshSize + 1) * (meshSize + 1)];

        for (int y = 0; y < (meshSize + 1); ++y)
        {
            for (int x = 0; x < (meshSize + 1); ++x)
            {
                Vector3 vertex = new Vector3(x * tileSize, 0.0f, y * tileSize);
                vertices[y * (meshSize + 1) + x] = vertex;
                uv[y * (meshSize + 1) + x] = new Vector2((float)x / meshSize, (float)y / meshSize);
            }
        }

        // For Triangles
        int index = 0;
        // int[] triangles = new int[(meshSize + 1) * (meshSize + 1) * 6];  // Single surface
        int[] triangles = new int[(meshSize + 1) * (meshSize + 1) * 12];    // Double surface

        for (int y = 0; y < meshSize; ++y)
        {
            for (int x = 0; x < meshSize; ++x)
            {
                // Triangle 1
                triangles[index++] = (y * (meshSize + 1)) + x;
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x;
                triangles[index++] = (y * (meshSize + 1)) + x + 1;

                // Triangle 2
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x;
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x + 1;
                triangles[index++] = (y * (meshSize + 1)) + x + 1;

                // Triangle 3
                triangles[index++] = (y * (meshSize + 1)) + x + 1;
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x;
                triangles[index++] = (y * (meshSize + 1)) + x;

                // Triangle 4
                triangles[index++] = (y * (meshSize + 1)) + x + 1;
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x + 1;
                triangles[index++] = ((y + 1) * (meshSize + 1)) + x;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
    }

    // 存在ChoppyWave时暂无法得到正确的高度
    public float GetWaterHeightAtLocation(Vector3 pos)
    {
#if BUFFER
        int x1 = Mathf.Abs((int)(pos.x / tileSize) % mapSize);
        int z1 = Mathf.Abs((int)(pos.z / tileSize) % mapSize);
        return specHFData[x1 * mapSize + z1].x;
#else
        return 0.0f;
#endif
    }
}