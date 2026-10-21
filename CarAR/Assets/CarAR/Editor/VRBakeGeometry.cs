using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarAR.EditorTools
{
    public static class VRBakeGeometry
    {
        [Serializable] sealed class Atlas {public int width,height;}
        [Serializable] sealed class MeshInfo {public string name;public long offset;public int vertices,atlas;public float[] st;public string[] materials;}
        [Serializable] sealed class Manifest
        {
            public string source_sha256,gltf_sha256,layout_sha256;
            public Atlas[] atlases;public MeshInfo[] meshes;
        }
        // Changes to UV2 meshes, atlas placement or world transforms invalidate
        // Cycles data, even if the same .blend and GLB are still present.
        public static string LayoutHash(VRShowroom app)
        {
            var text=new StringBuilder();
            foreach(var filter in app.environment.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                text.Append(filter.name).Append('|').Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(filter.sharedMesh)));
                text.Append('|').Append(renderer.lightmapIndex);
                var st=renderer.lightmapScaleOffset;
                for(int i=0;i<4;i++)text.Append('|').Append(st[i].ToString("R",CultureInfo.InvariantCulture));
                var matrix=filter.transform.localToWorldMatrix;
                for(int i=0;i<16;i++)text.Append('|').Append(matrix[i].ToString("R",CultureInfo.InvariantCulture));
                text.AppendLine();
            }
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        public static void PrepareAndExport()
        {
            try {CarARSetupWizard.PrepareVRLighting();Export();}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Export()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/CarAR/Scenes/CarVR.unity");
                var app=UnityEngine.Object.FindFirstObjectByType<VRShowroom>();
                var source=JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Environment/VR/carroom.json"));
                var manifest=new Manifest {source_sha256=source.source_sha256,gltf_sha256=source.gltf_sha256,layout_sha256=LayoutHash(app),
                    atlases=LightmapSettings.lightmaps.Select(l=>new Atlas{width=l.lightmapColor.width,height=l.lightmapColor.height}).ToArray()};
                Directory.CreateDirectory("tmp");
                using var file=new BinaryWriter(File.Create("tmp/vr-bake-geometry.bin"));
                manifest.meshes=app.environment.GetComponentsInChildren<MeshFilter>().Select(filter=>
                {
                    var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();
                    var st=renderer.lightmapScaleOffset;
                    var info=new MeshInfo{name=filter.name,offset=file.BaseStream.Position,vertices=mesh.vertexCount,atlas=renderer.lightmapIndex,
                        st=new[]{st.x,st.y,st.z,st.w},materials=renderer.sharedMaterials.Select(m=>m.name).ToArray()};
                    foreach(var v in mesh.vertices){var p=filter.transform.TransformPoint(v);file.Write(p.x);file.Write(p.y);file.Write(p.z);}
                    foreach(var n in mesh.normals){var p=filter.transform.TransformDirection(n);file.Write(p.x);file.Write(p.y);file.Write(p.z);}
                    foreach(var uv in mesh.uv){file.Write(uv.x);file.Write(uv.y);}
                    foreach(var uv in mesh.uv2){file.Write(uv.x);file.Write(uv.y);}
                    file.Write(mesh.subMeshCount);
                    for(int i=0;i<mesh.subMeshCount;i++){var indices=mesh.GetIndices(i);file.Write(indices.Length);foreach(var index in indices)file.Write(index);}
                    return info;
                }).ToArray();
                File.WriteAllText("tmp/vr-bake-geometry.json",JsonUtility.ToJson(manifest));
                Debug.Log("[VR Cycles Bake] Exported "+manifest.meshes.Length+" meshes with final Unity lightmap UVs");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
