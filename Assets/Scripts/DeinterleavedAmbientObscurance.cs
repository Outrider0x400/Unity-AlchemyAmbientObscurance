using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class DeinterleavedAmbientObscurance : MonoBehaviour
{
    
    Camera mainCamera;
    CommandBuffer commandBuffer;
    RenderTexture debugTexture;
    public ComputeShader shader;
    int deinterleavingKernel;
    int evaluationKernelXNear;
    int evaluationKernelYNear;
    int assembleKernel;
    int blurXKernel, blurYKernel;

    [Range(0.5f, 2.0f)]
    [Tooltip("World-space sample radius")]
    public float worldspaceRadius;
    [Range(0.0f, 0.1f)]
    [Tooltip("Notice that depth bias is increased for ranged fragments.")]
    public float baselineDepthBias;
    [Range(0.5f, 2.0f)]
    public float intensityModifier;
    [Range(0.01f, 10.0f)]
    [Tooltip("Samples that is further away than this value do not contribute.")]
    public float filterCutoffRadius;
    [Tooltip("If enabled, distant samples are ignored, resulting white halos at depth discontinuities.\nIf disabled, black halos appear instead.")]
    public bool rangeCutoff;
    readonly float epsilon = 0.001f;
    
    Vector4 rotationMatrix0;
    Vector4 rotationMatrix90; 
    Vector4 rotationMatrix180; 
    Vector4 rotationMatrix270;

    static void NormalizeVector4(ref Vector4 i)
    {
        i.x = (i.x + 1) / 2;
        i.y = (i.y + 1) / 2;
        i.z = (i.z + 1) / 2;
        i.w = (i.w + 1) / 2;
    }

    void OnEnable()
    {
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
            Debug.LogError("Shader not set. Please drag and drop the correct shader and reactive the script.");
        }

        deinterleavingKernel = shader.FindKernel("DeinterleaveDepthTexture");
        evaluationKernelXNear = shader.FindKernel("EvaluateObscuranceX");
        evaluationKernelYNear = shader.FindKernel("EvaluateObscuranceY");
        assembleKernel = shader.FindKernel("Assemble");
        blurXKernel = shader.FindKernel("BlurX");
        blurYKernel = shader.FindKernel("BlurY");


        debugTexture = new RenderTexture(mainCamera.pixelWidth, mainCamera.pixelHeight, 24, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
        };
        debugTexture.Create();


        rotationMatrix0 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.0f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.0f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.0f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.0f * 90.0f));
        NormalizeVector4(ref rotationMatrix0);
        rotationMatrix90 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.25f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.25f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.25f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.25f * 90.0f));
        NormalizeVector4(ref rotationMatrix90);
        rotationMatrix180 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.5f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.5f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.5f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.5f * 90.0f));
        NormalizeVector4(ref rotationMatrix180);
        rotationMatrix270 = new Vector4(
            Mathf.Cos(Mathf.Deg2Rad * 0.75f * 90.0f),
            -Mathf.Sin(Mathf.Deg2Rad * 0.75f * 90.0f),
            Mathf.Sin(Mathf.Deg2Rad * 0.75f * 90.0f),
            Mathf.Cos(Mathf.Deg2Rad * 0.75f * 90.0f));
        NormalizeVector4(ref rotationMatrix270);

        var nearFieldSampleVectors = new Vector4[16];
        for (int inx = 0; inx < 16; ++inx)
        {
            nearFieldSampleVectors[inx] = new Vector4(
            Mathf.Clamp01(0.25f + Random.Range(0.0f, 0.125f)),
                Mathf.Clamp01(0.50f + Random.Range(-0.125f, 0.125f)),
                Mathf.Clamp01(0.75f + Random.Range(-0.125f, 0.125f)),
                Mathf.Clamp01(1.0f + Random.Range(-0.125f, 0.0f)));
        }

        shader.SetVectorArray("_NearFieldSampleVectors", nearFieldSampleVectors);
        shader.SetVector("_IDToUV", new Vector4(1.0f / (mainCamera.pixelWidth / 2), 1.0f / (mainCamera.pixelHeight / 2), 0, 0));
        shader.SetVector("_IDToUVFull", new Vector4(1.0f / (mainCamera.pixelWidth ), 1.0f / (mainCamera.pixelHeight ), 0, 0));
        shader.SetMatrix("_InvProjMatrix", Matrix4x4.Inverse(mainCamera.projectionMatrix));
        shader.SetFloat("_AspectRatio", mainCamera.aspect);
        shader.SetFloat("_TanHalfFoV", Mathf.Tan(mainCamera.fieldOfView * Mathf.Deg2Rad));

        int zBufferVol = Shader.PropertyToID("_DeinterleavedDepthTextures");
        int xBlur = Shader.PropertyToID("_BlurXResult");
        int yBlur = Shader.PropertyToID("_FinalObscuranceResult");
        int rawResultVol = Shader.PropertyToID("_RawObscuranceResultNear");
        int assembledUnfilteredResult = Shader.PropertyToID("_AssembledRawObscuranceResult");

        commandBuffer = new CommandBuffer();
        commandBuffer.name = "DeinterleavedAmbientObscuranceCommandBuffer";

        Material blitMat = new Material(Shader.Find("Hidden/AmbientObscuranceBlit"));
        
        commandBuffer.GetTemporaryRTArray(zBufferVol, mainCamera.pixelWidth / 2, mainCamera.pixelHeight / 2, 4,
            16, FilterMode.Point, RenderTextureFormat.R16, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_DepthTexture", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_NormalTexture", BuiltinRenderTextureType.GBuffer2);
        commandBuffer.SetComputeTextureParam(shader, deinterleavingKernel, "_DeinterleavedDepthTextures", zBufferVol);
        commandBuffer.DispatchCompute(shader, deinterleavingKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);

        commandBuffer.GetTemporaryRTArray(rawResultVol, mainCamera.pixelWidth / 2, mainCamera.pixelHeight / 2, 4,
            16, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Default, 1, true);
        
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_DeinterleavedDepthTextures", zBufferVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_RawObscuranceResultNear", rawResultVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelXNear, "_NormalTexture", BuiltinRenderTextureType.GBuffer2);
        
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_DeinterleavedDepthTextures", zBufferVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_RawObscuranceResultNear", rawResultVol);
        commandBuffer.SetComputeTextureParam(shader, evaluationKernelYNear, "_NormalTexture", BuiltinRenderTextureType.GBuffer2);


        commandBuffer.SetComputeIntParam(shader, "_ActiveTextureInx", 0);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix0);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 0);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 2);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_ActiveTextureInx", 1);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix90);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 4);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 6);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_ActiveTextureInx", 2);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix180);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 8);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 10);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.SetComputeIntParam(shader, "_ActiveTextureInx", 3);
        commandBuffer.SetComputeVectorParam(shader, "_VectorizedRotationMatrix", rotationMatrix270);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 12);
        commandBuffer.DispatchCompute(shader, evaluationKernelXNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);
        commandBuffer.SetComputeIntParam(shader, "_SampleVectorStartingInxOffset", 14);
        commandBuffer.DispatchCompute(shader, evaluationKernelYNear, (mainCamera.pixelWidth / 2) / 16, (mainCamera.pixelHeight / 2) / 12, 1);

        commandBuffer.ReleaseTemporaryRT(zBufferVol);

        commandBuffer.GetTemporaryRT(assembledUnfilteredResult, mainCamera.pixelWidth, mainCamera.pixelHeight,
            16, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, assembleKernel, "_AssembledRawObscuranceResult", assembledUnfilteredResult);
        commandBuffer.SetComputeTextureParam(shader, assembleKernel, "_RawObscuranceResultNear", rawResultVol);
        commandBuffer.DispatchCompute(shader, assembleKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(rawResultVol);

        commandBuffer.GetTemporaryRT(xBlur, mainCamera.pixelWidth, mainCamera.pixelHeight,
            16, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_AssembledRawObscuranceResult", assembledUnfilteredResult);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_BlurXResult", xBlur);
        commandBuffer.SetComputeTextureParam(shader, blurXKernel, "_DepthTexture", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.DispatchCompute(shader, blurXKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(assembledUnfilteredResult);

        commandBuffer.GetTemporaryRT(yBlur, mainCamera.pixelWidth, mainCamera.pixelHeight,
            16, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Default, 1, true);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_FinalObscuranceResult", yBlur);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_BlurXResult", xBlur);
        commandBuffer.SetComputeTextureParam(shader, blurYKernel, "_DepthTexture", BuiltinRenderTextureType.ResolvedDepth);
        commandBuffer.DispatchCompute(shader, blurYKernel, mainCamera.pixelWidth / 16, mainCamera.pixelHeight / 12, 1);
        commandBuffer.ReleaseTemporaryRT(xBlur);
        

        commandBuffer.SetGlobalTexture("_FilteredObscuranceTex", yBlur);
        RenderTargetIdentifier[] compositeRenderTargets = {
            BuiltinRenderTextureType.GBuffer0,    
            BuiltinRenderTextureType.CameraTarget 
        };
        commandBuffer.SetRenderTarget(compositeRenderTargets, BuiltinRenderTextureType.CameraTarget);
        commandBuffer.DrawProcedural(Matrix4x4.identity, blitMat, 0, MeshTopology.Triangles, 3);
        commandBuffer.ReleaseTemporaryRT(yBlur);


        mainCamera.AddCommandBuffer(CameraEvent.BeforeLighting, commandBuffer);
    }

    void OnDisable()
    {
        mainCamera.RemoveCommandBuffer(CameraEvent.BeforeLighting, commandBuffer);
    }

    private void OnPreRender()
    {
        shader.SetMatrix("_ViewMatrix", mainCamera.worldToCameraMatrix);
        shader.SetInt("_RangeCutoffNearfield", rangeCutoff ? 1 : 0);
        shader.SetFloat("_Sigma", intensityModifier);
        shader.SetFloat("_Beta", baselineDepthBias); 
        shader.SetFloat("_WorldSpaceRoI", worldspaceRadius); 
        shader.SetFloat("_FilterRadiusCutoff", filterCutoffRadius);
        shader.SetFloat("_Epsilon", epsilon); 
    }

}
