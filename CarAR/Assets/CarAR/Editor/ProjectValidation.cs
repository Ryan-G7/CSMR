using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarAR.EditorTools
{
    internal static class ProjectValidation
    {
        internal static void ValidateScene(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects())
            foreach(var component in root.GetComponentsInChildren<Component>(true))
            {
                if(component==null)throw new BuildFailedException("Missing script in "+scene.path);
                var serialized=new SerializedObject(component);
                var property=serialized.GetIterator();
                while(property.NextVisible(true))
                    if(property.propertyType==SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue==null && property.objectReferenceEntityIdValue!=default)
                    {
                        string hierarchy=component.name;
                        for(var parent=component.transform.parent;parent!=null;parent=parent.parent)hierarchy=parent.name+"/"+hierarchy;
                        throw new BuildFailedException($"Missing reference: {scene.path}/{hierarchy}/{component.GetType().Name}/{property.propertyPath}");
                    }
                if(component is Renderer renderer)
                    foreach(var material in renderer.sharedMaterials)
                        if(material==null || material.shader==null || ShaderUtil.ShaderHasError(material.shader))
                            throw new BuildFailedException($"Invalid material: {scene.path}/{renderer.name}");
            }
        }

        public static void Audit()
        {
            try
            {
                var scenes=new[]{"Assets/CarAR/Scenes/Launch.unity","Assets/CarAR/Scenes/CarAR.unity","Assets/CarAR/Scenes/CarVR.unity"};
                foreach(string path in scenes)ValidateScene(EditorSceneManager.OpenScene(path));
                var dependencies=AssetDatabase.GetDependencies(scenes,true).ToHashSet();
                Directory.CreateDirectory("tmp");
                File.WriteAllLines("tmp/unreferenced-assets.txt",AssetDatabase.GetAllAssetPaths()
                    .Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p) &&
                        !dependencies.Contains(p)).OrderBy(p=>p));
                Debug.Log("[CarAR Project Audit] PASS: three scenes, scripts, serialized references and material shaders");
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
