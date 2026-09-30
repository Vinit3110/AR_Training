
using UnityEngine;

public class HoseProceduralBend : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform[] bones;
    [SerializeField] private Transform hoseTip;
    [SerializeField] private Transform target;

    [Header("Solver")]
    [SerializeField, Min(1)] private int iterations = 10;
    [SerializeField, Min(0.001f)] private float tolerance = 0.01f;

    private Quaternion[] originalLocalRotations;

    private void Awake()
    {
        if (bones == null || bones.Length != 7 ||
            hoseTip == null || target == null)
        {
            Debug.LogError(
                "Assign all 7 hose bones, HoseTip, and HoseEndTarget.",
                this
            );
            enabled = false;
            return;
        }

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null)
            {
                Debug.LogError($"Bone slot {i} is empty.", this);
                enabled = false;
                return;
            }

            if (i > 0 && bones[i].parent != bones[i - 1])
            {
                Debug.LogError(
                    $"Bone chain is not directly connected at slot {i}.",
                    this
                );
                enabled = false;
                return;
            }
        }

        if (!hoseTip.IsChildOf(bones[6]))
        {
            Debug.LogError(
                "HoseTip must be a child of Bone.006.",
                this
            );
            enabled = false;
            return;
        }

        originalLocalRotations = new Quaternion[bones.Length];

        for (int i = 0; i < bones.Length; i++)
            originalLocalRotations[i] = bones[i].localRotation;
    }

    private void LateUpdate()
    {
        // Restore the original resting pose each frame.
        // Bone[0] stays fixed at the cylinder.
        for (int i = 1; i < bones.Length; i++)
            bones[i].localRotation = originalLocalRotations[i];

        Vector3 desiredPosition = target.position;

        // CCD position solver.
        // All six movable bones participate, from Bone.006
        // backward through Bone.001.
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (Vector3.Distance(
                hoseTip.position,
                desiredPosition
            ) <= tolerance)
                break;

            for (int i = 6; i >= 1; i--)
            {
                Transform joint = bones[i];

                Vector3 toEnd =
                    hoseTip.position - joint.position;

                Vector3 toTarget =
                    desiredPosition - joint.position;

                if (toEnd.sqrMagnitude < 0.000001f ||
                    toTarget.sqrMagnitude < 0.000001f)
                    continue;

                Quaternion delta =
                    Quaternion.FromToRotation(toEnd, toTarget);

                joint.rotation = delta * joint.rotation;

                if (Vector3.Distance(
                    hoseTip.position,
                    desiredPosition
                ) <= tolerance)
                    break;
            }
        }
    }
}
