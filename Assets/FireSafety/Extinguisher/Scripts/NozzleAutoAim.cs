
using UnityEngine;

public class NozzleAutoAim : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform nozzle;          // NozzleAimPivot
    [SerializeField] private Transform fireAimTarget;   // Target near the fire

    [Header("Deployment (Nozzle Parent Local Space)")]
    [SerializeField] private float nozzleLift = 0.08f;
    [SerializeField] private float nozzleForwardOffset = 0f;
    [SerializeField] private float deploymentSpeed = 4f;

    [Header("Aiming")]
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Spray Axis")]
    [Tooltip("The nozzle's local spray direction. Current setup uses local -Y.")]
    [SerializeField] private Vector3 localSprayDirection = Vector3.down;

    private Transform nozzleParent;

    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;

    private bool isArmed;
    private bool isHandlePressed;

    // Called after the safety pin has been pulled.
    public void SetArmed(bool armed)
    {
        isArmed = armed;

        if (!armed)
            isHandlePressed = false;
    }

    // Called by ExtinguisherController on handle press/release.
    public void SetHandlePressed(bool pressed)
    {
        isHandlePressed = pressed;
    }

    private void Awake()
    {
        if (nozzle == null || fireAimTarget == null)
        {
            Debug.LogError(
                "NozzleAutoAim: Assign Nozzle and Fire Aim Target.",
                this
            );

            enabled = false;
            return;
        }

        if (localSprayDirection.sqrMagnitude < 0.000001f)
        {
            Debug.LogError(
                "NozzleAutoAim: Local Spray Direction cannot be zero.",
                this
            );

            enabled = false;
            return;
        }

        nozzleParent = nozzle.parent;

        restLocalPosition = nozzle.localPosition;
        restLocalRotation = nozzle.localRotation;

        localSprayDirection.Normalize();
    }

    private void Update()
    {
        if (nozzle == null || fireAimTarget == null)
            return;

        bool shouldDeploy = isArmed && isHandlePressed;

        // Default target is the nozzle's original resting pose.
        Vector3 targetLocalPosition = restLocalPosition;
        Quaternion targetLocalRotation = restLocalRotation;

        if (shouldDeploy)
        {
            // Move upward and optionally forward in parent-local space.
            targetLocalPosition += new Vector3(
                0f,
                nozzleLift,
                nozzleForwardOffset
            );

            // Convert the intended deployed position to world space.
            Vector3 deployedWorldPosition = nozzleParent != null
                ? nozzleParent.TransformPoint(targetLocalPosition)
                : targetLocalPosition;

            Vector3 aimDirection =
                fireAimTarget.position - deployedWorldPosition;

            if (aimDirection.sqrMagnitude > 0.000001f)
            {
                aimDirection.Normalize();

                // Calculate the original world rotation.
                Quaternion restWorldRotation = nozzleParent != null
                    ? nozzleParent.rotation * restLocalRotation
                    : restLocalRotation;

                // Rotate the original spray axis toward the fire.
                Vector3 restWorldSprayDirection =
                    restWorldRotation * localSprayDirection;

                Quaternion aimCorrection =
                    Quaternion.FromToRotation(
                        restWorldSprayDirection,
                        aimDirection
                    );

                Quaternion desiredWorldRotation =
                    aimCorrection * restWorldRotation;

                // Convert desired rotation back to parent-local space.
                targetLocalRotation = nozzleParent != null
                    ? Quaternion.Inverse(nozzleParent.rotation)
                        * desiredWorldRotation
                    : desiredWorldRotation;
            }
        }

        // Smooth deployment and return, independent of hose settings.
        float positionT = 1f - Mathf.Exp(
            -deploymentSpeed * Time.deltaTime
        );

        float rotationT = 1f - Mathf.Exp(
            -rotationSpeed * Time.deltaTime
        );

        nozzle.localPosition = Vector3.Lerp(
            nozzle.localPosition,
            targetLocalPosition,
            positionT
        );

        nozzle.localRotation = Quaternion.Slerp(
            nozzle.localRotation,
            targetLocalRotation,
            rotationT
        );
    }
}
