using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ExtinguisherController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable safetyPin;
    [SerializeField] private Transform holdPoint;
    [SerializeField] private Transform safetyPinTransform;
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable operatingHandle;
    [SerializeField] private Transform operatingHandleTransform;

    [Header("Pin Pull")]
    [SerializeField] private float pullDistance = 0.1f;
    [SerializeField] private float pullDuration = 0.5f;

    [Header("Handle Press")]
    [SerializeField] private float handlePressAngle = -30f;
    [SerializeField] private float handlePressDuration = 0.5f;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    private Vector3 pinStartLocalPosition;
    private Quaternion handleStartLocalRotation;

    private Coroutine handleRoutine;

    private bool pinRemoved;

    private void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        pinStartLocalPosition = safetyPinTransform.localPosition;
        handleStartLocalRotation = operatingHandleTransform.localRotation;

        safetyPin.enabled = false;
        operatingHandle.enabled = false;

        grabInteractable.selectEntered.AddListener(OnExtinguisherGrabbed);

        safetyPin.selectEntered.AddListener(OnSafetyPinSelected);

        operatingHandle.selectEntered.AddListener(OnOperatingHandlePressed);
        operatingHandle.selectExited.AddListener(OnOperatingHandleReleased);
    }

    private void OnExtinguisherGrabbed(SelectEnterEventArgs args)
    {
        transform.SetPositionAndRotation(
            holdPoint.position,
            holdPoint.rotation
        );

        grabInteractable.trackPosition = false;
        grabInteractable.trackRotation = false;

        safetyPin.enabled = true;
    }

    private void OnSafetyPinSelected(SelectEnterEventArgs args)
    {
        if (pinRemoved)
            return;

        pinRemoved = true;

        StartCoroutine(PullPin());
    }

    private IEnumerator PullPin()
    {
        Vector3 start = pinStartLocalPosition;
        Vector3 end = start + Vector3.right * pullDistance;

        float elapsed = 0f;

        while (elapsed < pullDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / pullDuration);

            // Smooth motion
            t = t * t * (3f - 2f * t);

            safetyPinTransform.localPosition =
                Vector3.Lerp(start, end, t);

            yield return null;
        }

        safetyPinTransform.localPosition = end;

        yield return new WaitForSeconds(0.15f);

        safetyPin.gameObject.SetActive(false);

        // Handle becomes available after pin is removed.
        operatingHandle.enabled = true;
    }

    private void OnOperatingHandlePressed(SelectEnterEventArgs args)
    {
        if (!pinRemoved)
            return;

        StartHandleAnimation(
            handleStartLocalRotation *
            Quaternion.Euler(0f, 0f, handlePressAngle)
        );
    }

    private void OnOperatingHandleReleased(SelectExitEventArgs args)
    {
        if (!pinRemoved)
            return;

        StartHandleAnimation(handleStartLocalRotation);
    }

    private void StartHandleAnimation(Quaternion targetRotation)
    {
        if (handleRoutine != null)
            StopCoroutine(handleRoutine);

        handleRoutine = StartCoroutine(
            AnimateHandle(targetRotation)
        );
    }

    private IEnumerator AnimateHandle(Quaternion targetRotation)
    {
        Quaternion startRotation =
            operatingHandleTransform.localRotation;

        float elapsed = 0f;

        while (elapsed < handlePressDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / handlePressDuration
            );

            // Smooth motion
            t = t * t * (3f - 2f * t);

            operatingHandleTransform.localRotation =
                Quaternion.Slerp(
                    startRotation,
                    targetRotation,
                    t
                );

            yield return null;
        }

        operatingHandleTransform.localRotation = targetRotation;

        handleRoutine = null;
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
            grabInteractable.selectEntered.RemoveListener(
                OnExtinguisherGrabbed
            );

        if (safetyPin != null)
            safetyPin.selectEntered.RemoveListener(
                OnSafetyPinSelected
            );

        if (operatingHandle != null)
        {
            operatingHandle.selectEntered.RemoveListener(
                OnOperatingHandlePressed
            );

            operatingHandle.selectExited.RemoveListener(
                OnOperatingHandleReleased
            );
        }
    }
}