using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace CarAR
{
    // Vehicle scale comes from a deliberate two-hand gesture, never a ray's
    // attach-transform scale. Rebase after degenerate/recovered controller poses.
    [UnityEngine.Scripting.Preserve]
    public sealed class VehicleScaleLogic : ManipulationLogic<Vector3>
    {
        const float MinimumHandSeparation = .08f;
        Vector3 startScale;
        float startDistance;
        bool hasBaseline;

        public override void Setup(List<IXRSelectInteractor> interactors, IXRSelectInteractable interactable, MixedRealityTransform currentTarget)
        {
            base.Setup(interactors, interactable, currentTarget);
            startScale = currentTarget.Scale;
            startDistance = Separation(interactors);
            hasBaseline = ValidSeparation(startDistance);
        }

        public override Vector3 Update(List<IXRSelectInteractor> interactors, IXRSelectInteractable interactable, MixedRealityTransform currentTarget, bool centeredAnchor)
        {
            base.Update(interactors, interactable, currentTarget, centeredAnchor);
            if (interactors.Count < 2) return currentTarget.Scale;
            float distance = Separation(interactors);
            if (!ValidSeparation(distance))
            {
                hasBaseline = false;
                return currentTarget.Scale;
            }
            if (!hasBaseline)
            {
                Setup(interactors, interactable, currentTarget);
                return currentTarget.Scale;
            }
            var result = startScale * (distance / startDistance);
            return float.IsFinite(result.x) && float.IsFinite(result.y) && float.IsFinite(result.z) ? result : currentTarget.Scale;
        }

        static bool ValidSeparation(float distance) => float.IsFinite(distance) && distance >= MinimumHandSeparation;
        static float Separation(List<IXRSelectInteractor> interactors)
        {
            if (interactors.Count < 2) return 0;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < interactors.Count; i++)
                for (int j = i + 1; j < interactors.Count; j++)
                {
                    float pair = Vector3.Distance(interactors[i].transform.position, interactors[j].transform.position);
                    if (!float.IsFinite(pair)) return float.NaN;
                    distance = Mathf.Min(distance, pair);
                }
            return distance;
        }
    }
}
