using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class DeinterleavedAmbientObscurance : MonoBehaviour
{
    
    Camera mainCamera;
    CommandBuffer commandBuffer, debugCommandBuffer;
    ComputeShader shader;
    int deinterleavingKernel;
    int evaluationKernelXNear;
    int evaluationKernelYNear;
    int assembleKernel;
    int blurXKernel, blurYKernel;
    //RenderTexture debugTexture;

    [Range(0.5f, 2.0f)]
    [Tooltip("World-space sample radius")]
    public float worldspaceRadius = 0.5f;
    [Range(0.0f, 0.1f)]
    [Tooltip("Notice that depth bias is increased for ranged fragments.")]
    public float baselineDepthBias = 0.001f;
    [Range(0.5f, 2.0f)]
    public float intensityModifier = 1.0f;
    [Tooltip("If enabled, distant samples are ignored, resulting white halos at depth discontinuities.\nIf disabled, black halos appear instead.")]
    public bool rangeCutoff = true;
    [Tooltip("Activate to show AO only. Reactive the script to apply the change.")]
    public bool showAOOnly = false;
    [Range(0.5f, 2.0f)]
    [Tooltip("Bilateral filter radius of influence.")]
    public float bilateralFilterRadius = 0.5f;
    
    // rotation matrices for each half-res depth texture.
    Vector4 rotationMatrix0;
    Vector4 rotationMatrix1; 
    Vector4 rotationMatrix2; 
    Vector4 rotationMatrix3;

    // Sample offset vectors
    Vector4[] nearFieldSampleVectors;

    // Helper function. Convert a Vector4 to be fitted into a ARGB32 format, whose values are between [0, 1]
    static void NormalizeVector4(ref Vector4 i)
    {
        i.x = (i.x + 1) / 2;
        i.y = (i.y + 1) / 2;
        i.z = (i.z + 1) / 2;
        i.w = (i.w + 1) / 2;
    }

    void Awake()
    {
        shader = (ComputeShader)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Shaders/DeinterleavedAmbientObscurance.compute", typeof(ComputeShader));

        mainCamera = GetComponent<Camera>();
        mainCamera.depthTextureMode = DepthTextureMode.Depth;

        if (mainCamera.renderingPath != RenderingPath.DeferredShading || mainCamera.allowHDR != true)
        {
            Debug.LogError("Deinterleaved Ambient Obscurance requires the parent camera to operate on the deferred shading path and HDR activated.");
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Deinterleaved Ambient Obscurance requires the system to support compute shaders.");
        }

        if (shader == null)
        {
            Debug.LogError("Shader not found. The expected path of the shader is Assets/Shaders/DeinterleavedAmbientObscurance.compute. Move the shader over there, or change ths path in this script.");
        }

        deinterleavingKernel = shader.FindKernel("DeinterleaveDepthTexture");
        evaluationKernelXNear = shader.FindKernel("EvaluateObscuranceX");
        evaluationKernelYNear = shader.FindKernel("EvaluateObscuranceY");
        assembleKernel = shader.FindKernel("Assemble");
        blurXKernel = shader.FindKernel("BlurX");
        blurYKernel = shader.FindKernel("BlurY");

        rotationMatrix0 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.0f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.0f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.0f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.0f * 90.0f));
        NormalizeVector4(ref rotationMatrix0);
        rotationMatrix1 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.25f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.25f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.25f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.25f * 90.0f));
        NormalizeVector4(ref rotationMatrix1);
        rotationMatrix2 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.5f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.5f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.5f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.5f * 90.0f));
        NormalizeVector4(ref rotationMatrix2);
        rotationMatrix3 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.75f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.75f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.75f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.75f * 90.0f));
        NormalizeVector4(ref rotationMatrix3);

        // 16 spp, with 8 along each axis.
        // Each axis reads 8 values from 2 Vector4, [a0, a1, a2, a3] [b0, b1, b2, b3]
        // Eg for x-pass, the sample offset vectors would be [x+a0,y], [x+a1,y], ... , [x+b2,y], [x+b3,y]
        nearFieldSampleVectors = new Vector4[16];
        for (int inx = 0; inx < 16; ++inx)
        {
            nearFieldSampleVectors[inx] = new Vector4(
            Mathf.Clamp01(0.25f + Random.Range(0.0f, 0.125f)),
                Mathf.Clamp01(0.50f + Random.Range(-0.125f, 0.125f)),
                Mathf.Clamp01(0.75f + Random.Range(-0.125f, 0.125f)),
                Mathf.Clamp01(1.0f + Random.Range(-0.125f, 0.0f)));
        }
    }

    void OnEnable()
    {
        
        shader.SetVectorArray("_SampleData", nearFieldSampleVectors);
        shader.SetVector("_IDToUVHalfRes", new Vector4(1.0f / (mainCamera.pixelWidth / 2), 1.0f / (mainCamera.pixelHeight / 2), 0, 0));
        shader.SetVector("_IDToUVFullRes", new Vector4(1.0f / (mainCamera.pixelWidth ), 1.0f / (mainCamera.pixelHeight ), 0, 0));
        shader.SetMatrix("_InvProjMatrix", Matrix4x4.Inverse(mainCamera.projectionMatrix));
        shader.SetFloat("_AspectRatio", mainCamera.aspect);
        shader.SetFloat("_TanHalfFoV", Mathf.Tan(mainCamera.fieldOfView * Mathf.Deg2Rad));

        int zBufferVol = Shader.PropertyToID("_DepthTexVol");
        int xBlur = Shader.PropertyToID("_XFilteredResult");
        int yBlur = Shader.PropertyToID("_YFilteredResult");
        int rawResultVol = Shader.PropertyToID("_RawResultVol");
        int assembledUnfilteredResult = Shader.PropertyToID("_RawResult");



        commandBuffer = new CommandBuffer();
        commandBuffer.name = "DeinterleavedAmbientObscuranceCommandBuffer";

        Material blitMat = new Material(Shader.Find("Hidden/AmbientObscuranceBlit"));
        
        commandBuffer.GetTemporaryRTArray(zBufferVol, mainCamera.pixelWidth / 2, mainCamera.pixelHeight / 2, 4,
            0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_DepthTex", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_NormalTex", BuiltinRenderTextureType.GBuffer2);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_DepthTexVol", zBufferVol);
        commandBuffer.DispatchCompute(shader, deinterleavingKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);

        commandBuffer.GetTemporaryRTArray(rawResultVol, mainCamera.pixelWidth / 2, mainCamera.pixelHeight / 2, 4,
            0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
        
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_DepthTexVol", zBufferVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_RawResultVol", rawResultVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_NormalTex", BuiltinRenderTextureType.GBuffer2);
        
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_DepthTexVol", zBufferVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_RawResultVol", rawResultVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_NormalTex", BuiltinRenderTextureType.GBuffer2);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 0);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix0);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 0);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 1);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix1);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 4);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 2);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix2);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 8);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 3);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix3);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 12);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        
        
        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 0);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix0);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 2);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 1);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix1);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 6);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 2);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix2);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 10);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_VolSliceInx", 3);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix3);
        commandBuffer.SetComputeIntParam(shader, "_SampleDataInxOffset", 14);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.ReleaseTemporaryRT(zBufferVol);

        commandBuffer.GetTemporaryRT(assembledUnfilteredResult, mainCamera.pixelWidth, mainCamera.pixelHeight,
            0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, assembleKernel, "_RawResult", assembledUnfilteredResult);
        commandBuffer.SetComputeTextureParam(shader, assembleKernel, "_RawResultVol", rawResultVol);
        commandBuffer.DispatchCompute(shader, assembleKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(rawResultVol);

        commandBuffer.GetTemporaryRT(xBlur, mainCamera.pixelWidth, mainCamera.pixelHeight,
            0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_RawResult", assembledUnfilteredResult);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_XFilteredResult", xBlur);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_DepthTex", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.DispatchCompute(shader, blurXKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(assembledUnfilteredResult);

        commandBuffer.GetTemporaryRT(yBlur, mainCamera.pixelWidth, mainCamera.pixelHeight,
            0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_YFilteredResult", yBlur);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_XFilteredResult", xBlur);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_DepthTex", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.DispatchCompute(shader, blurYKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(xBlur);
        
        if (showAOOnly)
        {
            var debugTexture = RenderTexture.GetTemporary(mainCamera.pixelWidth, mainCamera.pixelHeight, 0, RenderTextureFormat.RHalf);
            debugTexture.enableRandomWrite = true;
            debugTexture.Create();

            commandBuffer.Blit(yBlur, debugTexture);

            debugCommandBuffer = new CommandBuffer();
            debugCommandBuffer.name = "DebugBlitCommandBuffer";
            debugCommandBuffer.Blit(debugTexture, BuiltinRenderTextureType.CameraTarget, blitMat, 1);

            debugTexture.Release();

            mainCamera.AddCommandBuffer(CameraEvent.AfterEverything, debugCommandBuffer);
        }
        else
        {
            commandBuffer.SetGlobalTexture("_FilteredObscuranceTex", yBlur);
            RenderTargetIdentifier[] compositeRenderTargets = {
                BuiltinRenderTextureType.GBuffer0,
                BuiltinRenderTextureType.CameraTarget
            };
            commandBuffer.SetRenderTarget(compositeRenderTargets, BuiltinRenderTextureType.CameraTarget);
            commandBuffer.DrawProcedural(Matrix4x4.identity, blitMat, 0, MeshTopology.Triangles, 3);
        }
        commandBuffer.ReleaseTemporaryRT(yBlur);

        mainCamera.AddCommandBuffer(CameraEvent.BeforeLighting, commandBuffer);
    }

    void OnDisable()
    {
        mainCamera.RemoveCommandBuffer(CameraEvent.BeforeLighting, commandBuffer);
        if (showAOOnly)
            mainCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, debugCommandBuffer);

    }

    private void OnPreRender()
    {
        shader.SetMatrix("_ViewMatrix", mainCamera.worldToCameraMatrix);
        shader.SetInt("_CuttOffDistantSamples", rangeCutoff ? 1 : 0);
        shader.SetFloat("_Sigma", intensityModifier);
        shader.SetFloat("_Beta", baselineDepthBias); 
        shader.SetFloat("_WorldSpaceRoI", worldspaceRadius);
        shader.SetFloat("_BilateralFilterRadius", bilateralFilterRadius);
    }

}
