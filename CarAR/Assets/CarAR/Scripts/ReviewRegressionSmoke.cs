using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace CarAR
{
    // Runs only through the explicit --carar-smoke entry point. Provider callbacks
    // and pending calibration are simulated; vehicle/UI behaviour uses the scene.
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
            showroom.leftDoor.Toggle();
            yield return new WaitForSeconds(.8f);
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

            var ui = showroom.GetComponent<MRTKShowroomUI>();
            var input = showroom.GetComponent<MRTKControllerInput>();
            var vendor = showroom.GetComponent<Huijing.HuijingMRBridge>();
            showroom.gestures.SetManipulation(false);
            bool panelVisible = ui.ControlPanel.gameObject.activeSelf;
            typeof(Huijing.HuijingMRBridge).GetProperty(nameof(vendor.AlignmentPending)).SetValue(vendor, true);
            try
            {
                ui.buttons[4].OnClicked.Invoke();
                input.BeginPlacement();
                ui.buttons[3].OnClicked.Invoke();
                ui.buttons[14].OnClicked.Invoke();
                placement.Recenter();
                placement.PlaceOnFloorPoint(Vector3.one * 10);
                placement.PlaceAt(Vector3.one * 20);
                showroom.gestures.SetManipulation(true);
                Check(!showroom.gestures.ManipulationEnabled && !input.IsPlacing && !showroom.explode.IsBusy,
                    "calibration allowed unlock, placement or explosion");
                Check(Vector3.Distance(position, car.position) < .0001f && Mathf.Abs(placement.GroundHeight - ground) < .0001f,
                    "calibration allowed a parked transform/floor change");
            }
            finally { typeof(Huijing.HuijingMRBridge).GetProperty(nameof(vendor.AlignmentPending)).SetValue(vendor, false); }
            ui.ControlPanel.gameObject.SetActive(panelVisible);

            input.BeginPlacement();
            input.enabled = false;
            Check(!input.IsPlacing && !input.LeftRay.gameObject.activeInHierarchy && !input.RightRay.gameObject.activeInHierarchy,
                "disabling input left live interactions or a placement preview");
            input.enabled = true;

            // Inject provider frames into a separate light component so this suite
            // does not change the showroom's lighting or rely on a physical camera.
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
                var ambient = new SphericalHarmonicsL2(); ambient[0, 0] = .8f;
                Frame(new ARLightEstimationData { mainLightIntensityLumens = 2000, mainLightColor = Color.red,
                    mainLightDirection = Vector3.right, ambientSphericalHarmonics = ambient });
                yield return new WaitForSeconds(.7f);
                Check(estimation.HasLiveData && light.intensity > 1.5f, "live provider lighting was not applied");
                yield return new WaitForSeconds(3.1f);
                yield return new WaitForSeconds(1);
                Check(!estimation.HasLiveData && Mathf.Abs(light.intensity - .65f) < .1f && light.color.g > .9f &&
                    RenderSettings.ambientMode == AmbientMode.Flat, "stale live lighting did not return to the hall preset");
                Frame(new ARLightEstimationData { mainLightColor = new Color(float.NaN, 0, 0) });
                yield return null;
                Check(!estimation.HasLiveData && float.IsFinite(light.color.r), "invalid provider values contaminated lighting");
                Frame(new ARLightEstimationData { mainLightIntensityLumens = 1000, ambientSphericalHarmonics = ambient });
                estimation.RotateManual(15);
                yield return null;
                Check(estimation.ManualLighting && !estimation.HasLiveData && RenderSettings.ambientMode == AmbientMode.Flat,
                    "manual direction retained stale automatic ambient data");
                estimation.RequestAuto();
                Check(!estimation.ManualLighting && !estimation.HasLiveData, "automatic mode reused old provider evidence");
            }
            finally { UnityEngine.Object.Destroy(go); }
            Debug.Log("[CarAR Review Regression] PASS: repeated floor placement, ground pivot, calibration command exclusion, input disable/re-enable, stale/invalid light data and manual/auto transitions.");
        }
    }
}
