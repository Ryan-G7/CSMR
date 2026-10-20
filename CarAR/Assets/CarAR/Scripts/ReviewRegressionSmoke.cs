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
                "Cube.002", "Cube.135", "Cube.013", "Cube.017", "Cube.083", "Cube.092", "Cube.106", "Cube.139", "Cube.140", "Cube.141", "Cube.142",
                "DoorInterior_plastic", "DoorInterior_Leather Black", "DoorInterior_gril" };
            foreach (var panel in new[] { showroom.leftDoor, showroom.rightDoor })
            {
                var actual = new System.Collections.Generic.HashSet<string>();
                foreach (var renderer in panel.GetComponentsInChildren<MeshRenderer>())
                    actual.Add(renderer.name.Split(new[] { "__" }, StringSplitOptions.None)[0]);
                Check(actual.IsSubsetOf(allowedDoorSources) && (actual.Contains("Cube.002") || actual.Contains("Cube.135")) && actual.Contains("Cube.017") && actual.Contains("Cube.139") &&
                    actual.Contains("Cube.013") && actual.Contains("Cube.092") && actual.Contains("DoorInterior_plastic") && actual.Contains("DoorInterior_Leather Black") && actual.Contains("DoorInterior_gril"),
                    "door contains unrelated parts or misses shell/glass/mirror/interior: " + panel.name);
            }
            var staticParts = Array.FindAll(car.GetComponentsInChildren<MeshRenderer>(),
                r => !r.transform.IsChildOf(showroom.leftDoor.transform));
            var worldBefore = Array.ConvertAll(staticParts, r => r.transform.localToWorldMatrix);
            var doorPoint = showroom.leftDoor.GetComponentInChildren<MeshRenderer>().bounds.center;
            var leftTrim = Array.FindAll(showroom.leftDoor.GetComponentsInChildren<MeshRenderer>(),
                r => r.name.StartsWith("Cube.013__") || r.name.StartsWith("Cube.092__"));
            Check(leftTrim.Length == 2, "missing upper/lower left door trim");
            var trimPoints = Array.ConvertAll(leftTrim, r => r.bounds.center);
            var fixedConsole = Array.Find(car.GetComponentsInChildren<MeshRenderer>(), r => r.name.StartsWith("Cube.092__Body"));
            Check(fixedConsole != null, "mixed source lost its static console geometry");
            showroom.leftDoor.Toggle();
            yield return new WaitForSeconds(.8f);
            Check(Vector3.Distance(doorPoint, showroom.leftDoor.GetComponentInChildren<MeshRenderer>().bounds.center) > .05f,
                "left door geometry did not move");
            for (int i = 0; i < leftTrim.Length; i++)
                Check(Vector3.Distance(trimPoints[i], leftTrim[i].bounds.center) > .1f, "door trim remained across the open doorway: " + leftTrim[i].name);
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

            TrackingReplay();
            // Suspend mid-explosion without stopping the coroutine, then reset
            // the origin. UI must still permit assembly before explicit placement.
            float now = Time.unscaledTime;
            var head = Camera.main.transform.position;
            placement.ObserveTracking(true, head, true, true, now);
            placement.ObserveTracking(true, head, true, true, now + 1.1f);
            showroom.explode.SetTarget(1);
            yield return new WaitForSeconds(.4f);
            placement.ObserveTracking(false, head, false, false, now + 1.5f);
            yield return new WaitForSeconds(1.8f);
            Check(car.gameObject.activeInHierarchy && showroom.explode.Progress > .99f,
                "tracking loss interrupted the explode coroutine");
            placement.ObserveTracking(true, head + Vector3.up * 14.7f, true, true, now + 3.3f);
            placement.ObserveTracking(true, head + Vector3.up * 14.7f, true, true, now + 4.4f);
            Check(!placement.IsPlaced && placement.TrackingReady, "origin reset failed to require placement");
            input.ui.buttons[3].OnClicked.Invoke();
            yield return new WaitForSeconds(1.8f);
            Check(!showroom.explode.IsBusy, "tracking reset blocked UI assembly and made placement unrecoverable");
            placement.PlaceAt(new Vector3(position.x, ground, position.z), true);
            Check(placement.IsPlaced, "assembled vehicle could not be parked after origin reset");

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
            Debug.Log("[CarAR Review Regression] PASS: recorded 14.7m HMD/controller mismatch, delayed placement and origin recovery; upper/lower door trim with static console; independent door motion, repeated floor placement, ground pivot, input lifecycle, stale/invalid lighting.");
        }
        static void TrackingReplay()
        {
            var fixture = new GameObject("Recorded tracking placement regression");
            var placement = fixture.AddComponent<CarPlacementManager>(); placement.enabled = false;
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.SetActive(false);
            car.transform.SetParent(fixture.transform);
            typeof(CarPlacementManager).GetField("carRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(placement, car.transform);
            var renderer = car.GetComponent<Renderer>(); var collider = car.GetComponent<Collider>();
            var cam = Camera.main.transform; var cameraPosition = cam.position;
            try
            {
                var head = new Vector3(7.7314f, -1.0923f, -2.0281f);
                placement.ObserveTracking(true, head, false, false, 110);
                Check(!placement.TrackingReady, "cached HMD pose allowed initial placement without an awake controller");
                placement.ObserveTracking(true, new Vector3(8.365f, -.110f, -2.006f), true, true, 113);
                placement.ObserveTracking(true, new Vector3(7.7043f, -1.1296f, -2.0725f), true, true, 114);
                // The real trace remains flagged tracked even though its HMD is
                // 14-16m from the tracked controller. It lasts longer than fallbackDelay.
                for (float t = 115; t < 130; t += .5f)
                {
                    placement.ObserveTracking(true, new Vector3(7.7294f, -15.807f, -2.0717f), false, true, t);
                    placement.Recenter(); placement.PlaceAt(Vector3.down * 17.306f, true);
                    Check(!placement.IsPlaced && !car.activeSelf && placement.GroundHeight == 0, "incoherent runtime pose allowed floor placement");
                }
                placement.ObserveTracking(true, head, true, true, 130.4631f);
                Check(!placement.TrackingReady, "recovered origin skipped settling interval");
                placement.ObserveTracking(true, head, true, true, 131.5f);
                cam.position = head;
                placement.Recenter();
                Check(placement.IsPlaced && Mathf.Abs(placement.GroundHeight - (head.y - 1.5f)) < .001f && car.transform.localScale == Vector3.one,
                    "recovered initial floor retained the bad cached height or changed scale");
                var parked = car.transform.position;
                placement.ObserveTracking(false, head + Vector3.down * 14.7f, false, false, 132);
                Check(!placement.IsPlaced && renderer.forceRenderingOff && !collider.enabled && car.activeSelf,
                    "tracking suspension left geometry/selectors visible or deactivated animations");
                placement.ObserveTracking(true, head, false, false, 133);
                placement.ObserveTracking(true, head, false, false, 134.1f);
                Check(placement.IsPlaced && !renderer.forceRenderingOff && collider.enabled && car.transform.position == parked,
                    "same-origin recovery moved the parked vehicle or required an active controller");
                placement.ObserveTracking(true, head + Vector3.down * 14.7f, false, true, 135);
                placement.ObserveTracking(true, head, true, true, 136);
                placement.ObserveTracking(true, head, true, true, 137.1f);
                Check(placement.IsPlaced && car.transform.position == parked, "incoherent transient poisoned the valid pose baseline");
                var resetHead = head + Vector3.up * 14.7113f;
                placement.ObserveTracking(true, resetHead, true, true, 138);
                placement.ObserveTracking(true, resetHead, true, true, 139.1f);
                cam.position = resetHead;
                Check(placement.TrackingReady && !placement.IsPlaced && renderer.forceRenderingOff &&
                    Mathf.Abs(placement.PlacementPreviewHeight - (resetHead.y - 1.5f)) < .001f,
                    "coherent origin reset retained the old parked floor/visibility");
                placement.PlaceOnFloorPoint(new Vector3(2, resetHead.y - 1.5f, 3));
                Check(placement.IsPlaced && !renderer.forceRenderingOff && collider.enabled && car.transform.localScale == Vector3.one,
                    "explicit placement did not recover after origin reset");
            }
            finally { cam.position = cameraPosition; UnityEngine.Object.Destroy(fixture); }
        }
    }
}
