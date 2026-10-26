using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace CarAR.EditorTools
{
    // Set the final lightmap mode before Unity gathers shader variants and scene
    // textures, and repeat at runtime after the scene's lighting data is loaded.
    public sealed class VRSourceLightmapBuild : IProcessSceneWithReport
    {
        public int callbackOrder=>0;
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            ProjectValidation.ValidateScene(scene);
            foreach(var root in scene.GetRootGameObjects())
            {
                var app=root.GetComponent<VRShowroom>();if(app!=null)app.ApplySourceLightmaps();
            }
        }
    }
}
