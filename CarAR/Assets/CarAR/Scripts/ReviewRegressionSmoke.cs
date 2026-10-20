using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace CarAR
{
    // Runs only through the explicit --carar-smoke entry point. Provider callbacks
    // are simulated; vehicle/UI behaviour uses the scene.
    internal static class ReviewRegressionSmoke
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Review regression: " + message); }

        public static IEnumerator Run(CarShowroom showroom)
        {
            var placement = showroom.placement;
            var car = showroom.car;
            var position = car.position;
            float ground = placement.GroundHeight;
            float pivot = position.y - ground;
            // A door may move only its verified assembly, never mixed seat/dashboard meshes.
            var allowedDoorSources = new System.Collections.Generic.HashSet<string> {
                "Cube.002", "Cube.135", "Cube.017", "Cube.083", "Cube.106", "Cube.139", "Cube.140", "Cube.141", "Cube.142",
                "DoorInterior_plastic", "DoorInterior_Leather Black", "DoorInterior_gril" };
            foreach (var panel in new[] { showroom.leftDoor, showroom.rightDoor })
            {
                var actual = new System.Collections.Generic.HashSet<string>();
                foreach (var renderer in panel.GetComponentsInChildren<MeshRenderer>())
                    actual.Add(renderer.name.Split(new[] { "__" }, StringSplitOptions.None)[0]);
                Check(actual.IsSubsetOf(allowedDoorSources) && (actual.Contains("Cube.002") || actual.Contains("Cube.135")) && actual.Contains("Cube.017") && actual.Contains("Cube.139") &&
                    actual.Contains("DoorInterior_plastic") && actual.Contains("DoorInterior_Leather Black") && actual.Contains("DoorInterior_gril"),
                    "door contains unrelated parts or misses shell/glass/mirror/interior: " + panel.name);
            }
            var staticParts = Array.FindAll(car.GetComponentsInChildren<MeshRenderer>(),
                r => !r.transform.IsChildOf(showroom.leftDoor.transform));
            var worldBefore = Array.ConvertAll(staticParts, r => r.transform.localToWorldMatrix);
            var doorPoint = showroom.leftDoor.GetComponentInChildren<MeshRenderer>().bounds.center;
            showroom.leftDoor.Toggle();
            yield return new WaitForSeconds(.8f);
            Check(Vector3.Distance(doorPoint, showroom.leftDoor.GetComponentInChildren<MeshRenderer>().bounds.center) > .05f,
                "left door geometry did not move");
            for (int i = 0; i < staticParts.Length; i++)
                Check(staticParts[i].transform.localToWorldMatrix == worldBefore[i],
                    "opening left door moved a body/interior/opposite-door part: " + staticParts[i].name);
            placement.PlaceAt(new Vector3(2, ground + .3f, -3), true);
            Check(Vector3.Distance(car.position, new Vector3(2, ground + .3f + pivot, -3)) < .0001f,
                "estimated placement lost horizontal coordinates or the ground pivot");
            placement.PlaceOnFloorPoint(new Vector3(-2, ground - .2f, 3));
            placement.PlaceAt(new Vector3(1, ground + .1f, 2));
            Check(Vector3.Distance(car.position, new Vector3(1, ground + .1f + pivot, 2)) < .0001f,
                "repeated placement remeasured open geometry");
            Check(placement.Status == "Tracked horizontal plane", "tracked placement retained the previous manual-floor status");
            placement.PlaceAt(new Vector3(position.x, ground, position.z), true);
            showroom.leftDoor.Close();
            yield return new WaitForSeconds(.8f);

            var input = showroom.GetComponent<MRTKControllerInput>();
            input.BeginPlacement();
            input.enabled = false;
            Check(!input.IsPlacing && !input.LeftRay.gameObject.activeInHierarchy && !input.RightRay.gameObject.activeInHierarchy,
                "disabling input left live interactions or a placement preview");
            input.enabled = true;

            var teardownFixture = new GameObject("Input teardown order regression fixture");
            teardownFixture.SetActive(false);
            var teardownInput = teardownFixture.AddComponent<MRTKControllerInput>();
            teardownInput.trackingOrigin = teardownFixture.transform;
            teardownInput.ui = input.ui; teardownInput.rayMaterial = input.rayMaterial;
            teardownFixture.SetActive(true);
            // Reproduce shutdown ordering: the native Unity object is gone while
            // the owning component still holds its managed controller reference.
            UnityEngine.Object.DestroyImmediate(teardownInput.LeftRay.gameObject);
            teardownFixture.SetActive(false);
            UnityEngine.Object.Destroy(teardownFixture);

            // Inject provider frames into a separate light component so this suite
            // does not change the showroom's lighting or rely on a physical camera.
            var originalAmbient = RenderSettings.ambientProbe;
            var originalMode = RenderSettings.ambientMode;
            float originalReflection = RenderSettings.reflectionIntensity;
            showroom.lighting.enabled = false;
            var go = new GameObject("Light provider regression fixture");
            var light = go.AddComponent<Light>();
            var estimation = go.AddComponent<ARLightEstimation>();
            typeof(ARLightEstimation).GetField("sun", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(estimation, light);
            typeof(ARLightEstimation).GetField("cameraManager", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(estimation, Camera.main.GetComponent<ARCameraManager>());
            void Frame(ARLightEstimationData data) => typeof(ARLightEstimation)
                .GetMethod("OnFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(estimation, new object[] { new ARCameraFrameEventArgs { lightEstimation = data } });
            try
            {
                yield return null;
                Check(!estimation.HasCameraProvider && estimation.DisplayStatus.Contains("现实测光未接入"),
                    "missing AR camera provider was presented as real-world lighting");
                var ambient = new SphericalHarmonicsL2(); ambient[0, 0] = .8f;
                Frame(new ARLightEstimationData { mainLightIntensityLumens = 2000, mainLightColor = Color.red,
                    mainLightDirection = Vector3.right, ambientSphericalHarmonics = ambient });
                yield return new WaitForSeconds(.7f);
                Check(estimation.HasLiveData && light.intensity > 1.5f, "live provider lighting was not applied");
                yield return new WaitForSeconds(3.1f);
                yield return new WaitForSeconds(1);
                Check(!estimation.HasLiveData && light.intensity < .08f && light.color.g > .9f &&
                    RenderSettings.ambientMode == AmbientMode.Custom && RenderSettings.customReflectionTexture != null, "stale live lighting did not return to HDR with zero direct fill");
                float ambientError = 0;
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++)
                    ambientError = Mathf.Max(ambientError, Mathf.Abs(RenderSettings.ambientProbe[c, i] - originalAmbient[c, i]));
                Check(ambientError < .08f, $"stale live SH did not restore the original HDR ambient (max error={ambientError}, expected DC={originalAmbient[0, 0]}, actual DC={RenderSettings.ambientProbe[0, 0]})");
                Frame(new ARLightEstimationData { mainLightColor = new Color(float.NaN, 0, 0) });
                yield return null;
                Check(!estimation.HasLiveData && float.IsFinite(light.color.r), "invalid provider values contaminated lighting");
                Frame(new ARLightEstimationData { mainLightIntensityLumens = 1000, ambientSphericalHarmonics = ambient });
                estimation.RotateManual(15);
                yield return null;
                Check(estimation.ManualLighting && !estimation.HasLiveData && RenderSettings.ambientMode == AmbientMode.Custom,
                    "manual direction retained stale automatic ambient data");
                estimation.RequestAuto();
                Check(!estimation.ManualLighting && !estimation.HasLiveData, "automatic mode reused old provider evidence");
            }
            finally
            {
                estimation.enabled = false;
                UnityEngine.Object.Destroy(go);
                RenderSettings.ambientProbe = originalAmbient; RenderSettings.ambientMode = originalMode;
                RenderSettings.reflectionIntensity = originalReflection;
                showroom.lighting.enabled = true;
            }
            Debug.Log("[CarAR Review Regression] PASS: verified door sources, independent door motion, repeated floor placement, ground pivot, input disable/re-enable and teardown order, stale/invalid light data and manual/auto transitions.");
        }
    }
}
