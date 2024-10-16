using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AdditionalPostProcessingRenderFeature : ScriptableRendererFeature
{
    private AdditionalPostProcessingRenderPass additionalPostProcessingRenderPass;
    
    public override void Create()
    {
        additionalPostProcessingRenderPass = new AdditionalPostProcessingRenderPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer,
        in RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            return;
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Normal);
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            return;

        renderer.EnqueuePass(additionalPostProcessingRenderPass);
    }

    protected override void Dispose(bool disposing)
    {
        additionalPostProcessingRenderPass.Dispose();
    }

    private class MaterialLibrary
    {
        public static readonly Material outlineMat = CoreUtils.CreateEngineMaterial("Addition/Post-processing/Outline");
    }

    private class AdditionalPostProcessingRenderPass : ScriptableRenderPass
    {
        private static readonly int width = Shader.PropertyToID("_Width");
        private static readonly int distanceThreshold = Shader.PropertyToID("_DistanceThreshold");
        private static readonly int normalsThreshold = Shader.PropertyToID("_NormalThreshold");
        private static readonly int normalThresholdScale = Shader.PropertyToID("_NormalThresholdScale");
        private static readonly int color = Shader.PropertyToID("_Color");
        private static readonly int onlyOutline = Shader.PropertyToID("_OnlyOutline");

        private OutlineVolume outlineVolume;

        private RTHandle[] frameBufferHandles;
        private RenderTextureDescriptor backBufferDesc;
        private int currentFramebufferIndex;

        private bool enableOutline;
        private bool enableVolumeFog;
        private bool enableExponentialFog;

        private RTHandle CurrentFrameBufferHandle => frameBufferHandles[currentFramebufferIndex];
        private RTHandle CurrentBackBufferHandle => frameBufferHandles[(currentFramebufferIndex + 1) % 2];

        public AdditionalPostProcessingRenderPass()
        {
            profilingSampler = new ProfilingSampler("Additional Post Processing");
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            backBufferDesc = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.ARGBFloat, 0);
            frameBufferHandles = new RTHandle[2];
            currentFramebufferIndex = 0;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            backBufferDesc.width = cameraTextureDescriptor.width;
            backBufferDesc.height = cameraTextureDescriptor.height;

            RenderingUtils.ReAllocateIfNeeded(ref frameBufferHandles[0], backBufferDesc);
            RenderingUtils.ReAllocateIfNeeded(ref frameBufferHandles[1], backBufferDesc);
            ConfigureColorStoreAction(RenderBufferStoreAction.StoreAndResolve);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            CameraData cameraData = renderingData.cameraData;
            RTHandle cameraTargetHandle = cameraData.renderer.cameraColorTargetHandle;

            cmd.SetRenderTarget(CurrentFrameBufferHandle);
            Blitter.BlitTexture(cmd, cameraTargetHandle, new Vector4(1, 1, 0, 0), 0, false);
            currentFramebufferIndex = (currentFramebufferIndex + 1) % 2;

            var stack = VolumeManager.instance.stack;
            outlineVolume =
                stack.GetComponent<OutlineVolume>(); // volume stack会自动实例化所有的VolumeComponent, 即使没有添加到stack中, 因此可以直接使用IsActived()方法判断是否启用

            
            enableOutline = outlineVolume.IsActive() && MaterialLibrary.outlineMat;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (enableOutline)
                {
                    DoOutline(cmd, CurrentFrameBufferHandle, CurrentBackBufferHandle, ref renderingData);
                    currentFramebufferIndex = (currentFramebufferIndex + 1) % 2;
                }
                
                DoFinalBlit(cmd, cameraTargetHandle, CurrentBackBufferHandle);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DoFinalBlit(CommandBuffer cmd, RTHandle cameraTargetHandle, RTHandle source)
        {
            cmd.SetRenderTarget(cameraTargetHandle);
            Blitter.BlitTexture(cmd, source, new Vector4(1, 1, 0, 0), 0, false);
        }

        private void DoOutline(CommandBuffer cmd, RTHandle currentFrameBufferHandle, RTHandle currentBackBufferHandle,
            ref RenderingData renderingData)
        {
            var outlineMat = MaterialLibrary.outlineMat;
            outlineMat.SetFloat(width, outlineVolume.outlineWidth.value);
            outlineMat.SetFloat(distanceThreshold, outlineVolume.distanceThreshold.value);
            outlineMat.SetFloat(normalsThreshold, outlineVolume.normalThreshold.value);
            outlineMat.SetColor(color, outlineVolume.outlineColor.value);
            outlineMat.SetFloat(normalThresholdScale, outlineVolume.normalThresholdScale.value);
            if (outlineVolume.onlyOutline.value)
            {
                outlineMat.EnableKeyword("Outline_Only");
            }
            else
            {
                outlineMat.DisableKeyword("Outline_Only");
            }

            cmd.SetRenderTarget(currentFrameBufferHandle);
            Blitter.BlitTexture(cmd, currentBackBufferHandle, new Vector4(1, 1, 0, 0), outlineMat, 0);
        }
        
        public void Dispose()
        {
            frameBufferHandles[0]?.Release();
            frameBufferHandles[1]?.Release();
        }
    }
}