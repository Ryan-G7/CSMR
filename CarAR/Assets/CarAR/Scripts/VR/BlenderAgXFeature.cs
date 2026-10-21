using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace CarAR
{
    // A source-generated 3D LUT keeps the .blend's actual view transform and look.
    public sealed class BlenderAgXFeature : ScriptableRendererFeature
    {
        public Material material;
        AgXPass pass;
        public override void Create()
        {
            pass=new AgXPass {renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing,requiresIntermediateTexture=true};
            pass.ConfigureInput(ScriptableRenderPassInput.Color);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            if(material==null || data.cameraData.isPreviewCamera || data.cameraData.cameraType==CameraType.Reflection)return;
            pass.material=material;renderer.EnqueuePass(pass);
        }
        sealed class AgXPass : ScriptableRenderPass
        {
            public Material material;
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var resources=frame.Get<UniversalResourceData>();
                if(resources.isActiveTargetBackBuffer)return;
                var source=resources.activeColorTexture;
                var description=graph.GetTextureDesc(source);
                description.name="Authored Blender AgX";description.clearBuffer=false;
                var output=graph.CreateTexture(description);
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source,output,material,0),passName:"Blender authored AgX / exposure / look");
                resources.cameraColor=output;
            }
        }
    }
}
