using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class ExtinguisherController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private XRSimpleInteractable safetyPin;
    [SerializeField] private Transform holdPoint;
    [SerializeField] private Transform safetyPinTransform;
    [SerializeField] private XRSimpleInteractable operatingHandle;
    [SerializeField] private Transform operatingHandleTransform;
    [SerializeField] private ParticleSystem extinguisherSpray;
    [SerializeField] private FireController fireController;
    [SerializeField] private NozzleAutoAim nozzleAutoAim;

    [Header("Handle Input")]
    [Tooltip("Read a real screen press instead of the AR gesture Select events.")]
    [SerializeField] private bool useScreenPressForHandle = true;
    [Tooltip("AR camera used to hit-test the handle. Falls back to Camera.main.")]
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private LayerMask touchRaycastMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0.1f)] private float touchRaycastDistance = 10f;
    [SerializeField] private bool logInteractions;

    [Header("Pin Pull")]
    [SerializeField] private float pullDistance = 0.1f;
    [SerializeField, Min(0f)] private float pullDuration = 0.5f;

    [Header("Handle Press")]
    [SerializeField] private float handlePressAngle = -30f;
    [SerializeField, Min(0f)] private float handlePressDuration = 0.5f;

    private XRGrabInteractable grabInteractable;
    private Rigidbody rb;
    private Vector3 pinStartLocalPosition;
    private Quaternion handleStartLocalRotation;
    private Coroutine handleRoutine;
    private bool initialized;
    private bool isHeld;
    private bool ownsBody;
    private bool pinPulling;
    private bool pinRemoved;
    private bool isHandlePressed;
    private bool wasKinematic;
    private bool wasGravity;
    private bool originalTrackPosition;
    private bool originalTrackRotation;
    private bool originalTrackScale;
    private InteractionLayerMask originalInteractionLayers;
    private int activeTouchId = -1;
    private bool mousePressCaptured;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        if (nozzleAutoAim == null)
            nozzleAutoAim = GetComponent<NozzleAutoAim>();
        if (interactionCamera == null)
            interactionCamera = Camera.main;

        if (safetyPin == null || safetyPinTransform == null || operatingHandle == null ||
            operatingHandleTransform == null || extinguisherSpray == null || holdPoint == null ||
            holdPoint == transform || holdPoint.IsChildOf(transform))
        {
            Debug.LogError("ExtinguisherController: Assign the pin, handle, spray and an external camera hold point.", this);
            enabled = false;
            return;
        }

        pinStartLocalPosition = safetyPinTransform.localPosition;
        handleStartLocalRotation = operatingHandleTransform.localRotation;
        originalTrackPosition = grabInteractable.trackPosition;
        originalTrackRotation = grabInteractable.trackRotation;
        originalTrackScale = grabInteractable.trackScale;
        originalInteractionLayers = grabInteractable.interactionLayers;
        // This controller implements pickup/drop, not throwing. XRI defers its throw
        // until Late, so keep it off even when selection ends during handoff.
        grabInteractable.throwOnDetach = false;
        extinguisherSpray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized) return;
        safetyPin.enabled = false;
        operatingHandle.enabled = false;
        grabInteractable.selectEntered.AddListener(OnExtinguisherGrabbed);
        safetyPin.selectEntered.AddListener(OnSafetyPinSelected);
        operatingHandle.selectEntered.AddListener(OnOperatingHandlePressed);
        operatingHandle.selectExited.AddListener(OnOperatingHandleReleased);
    }

    private void Update()
    {
        if (!isHeld) return;
        if (holdPoint == null)
        {
            ReleaseExtinguisher();
            return;
        }

        // Finish the XR event before relinquishing its selection. This frees the
        // phone's interactor for the pin, while pickup remains latched.
        TakeOverBody();
        FollowHoldPoint();
        if (useScreenPressForHandle)
            UpdateScreenPress();
        if (isHandlePressed && !operatingHandle.isActiveAndEnabled)
            ReleaseHandle();

        // Existing prototype behavior; spray-contact gating is a separate milestone.
        if (isHandlePressed && fireController != null)
            fireController.ApplyExtinguishing(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (isHeld && holdPoint != null)
            FollowHoldPoint();
    }

    private void FollowHoldPoint()
    {
        transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    private void OnExtinguisherGrabbed(SelectEnterEventArgs args)
    {
        if (!isActiveAndEnabled || isHeld || holdPoint == null) return;
        isHeld = true;
        grabInteractable.trackPosition = false;
        grabInteractable.trackRotation = false;
        grabInteractable.trackScale = false;
        FollowHoldPoint();
        Log("Picked up; camera follow stays active after finger release.");
    }

    private void TakeOverBody()
    {
        if (ownsBody) return;
        grabInteractable.interactionLayers = 0;
        if (grabInteractable.isSelected)
            grabInteractable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grabInteractable);

        // XRI has now restored its pre-selection Rigidbody state.
        wasKinematic = rb.isKinematic;
        wasGravity = rb.useGravity;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true;
        rb.useGravity = false;
        ownsBody = true;
        safetyPin.enabled = !pinRemoved && !pinPulling;
        operatingHandle.enabled = pinRemoved;
    }

    // Optional UI Drop button can call this. Ending a pickup gesture must not drop
    // the extinguisher, otherwise the same finger cannot operate its controls.
    public void ReleaseExtinguisher()
    {
        if (!initialized) return;
        if (isHeld && !ownsBody) TakeOverBody();
        isHeld = false;
        StopAllCoroutines();
        handleRoutine = null;
        StopHandleImmediately();
        if (pinPulling)
        {
            pinPulling = false;
            safetyPinTransform.localPosition = pinStartLocalPosition;
        }
        safetyPin.enabled = false;
        operatingHandle.enabled = false;
        if (ownsBody)
        {
            rb.isKinematic = wasKinematic;
            rb.useGravity = wasGravity;
            ownsBody = false;
        }
        grabInteractable.trackPosition = originalTrackPosition;
        grabInteractable.trackRotation = originalTrackRotation;
        grabInteractable.trackScale = originalTrackScale;
        grabInteractable.interactionLayers = originalInteractionLayers;
    }

    private void OnSafetyPinSelected(SelectEnterEventArgs args)
    {
        if (!isActiveAndEnabled || !isHeld || pinRemoved || pinPulling) return;
        pinPulling = true;
        StartCoroutine(PullPin());
    }

    private IEnumerator PullPin()
    {
        Vector3 end = pinStartLocalPosition + Vector3.right * pullDistance;
        float elapsed = 0f;
        while (elapsed < pullDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pullDuration));
            safetyPinTransform.localPosition = Vector3.Lerp(pinStartLocalPosition, end, t);
            yield return null;
        }
        safetyPinTransform.localPosition = end;
        yield return new WaitForSeconds(0.15f);
        pinRemoved = true;
        pinPulling = false;
        safetyPin.gameObject.SetActive(false);
        operatingHandle.enabled = true;
        if (nozzleAutoAim != null) nozzleAutoAim.SetArmed(true);
        Log("Pin removed; handle armed.");
    }

    private void UpdateScreenPress()
    {
        var touchscreen = Touchscreen.current;
        if (activeTouchId >= 0)
        {
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                    if (touch.touchId.ReadValue() == activeTouchId && touch.press.isPressed)
                        return;
            }
            activeTouchId = -1;
            ReleaseHandle();
            return;
        }
        if (mousePressCaptured)
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return;
            mousePressCaptured = false;
            ReleaseHandle();
            return;
        }
        if (!pinRemoved || !operatingHandle.isActiveAndEnabled) return;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame && touch.press.isPressed && HitsHandle(touch.position.ReadValue()))
                {
                    activeTouchId = touch.touchId.ReadValue();
                    PressHandle();
                    return;
                }
            }
        }
        // Avoid processing mouse emulation in the Android player.
        if (Application.isEditor && Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame && Mouse.current.leftButton.isPressed &&
            HitsHandle(Mouse.current.position.ReadValue()))
        {
            mousePressCaptured = true;
            PressHandle();
        }
    }

    private bool HitsHandle(Vector2 screenPosition)
    {
        if (interactionCamera == null) interactionCamera = Camera.main;
        if (interactionCamera == null)
        {
            Log("Cannot test handle: assign Interaction Camera or tag the AR camera MainCamera.");
            return false;
        }
        if (EventSystem.current != null)
        {
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, uiHits);
            foreach (var hit in uiHits)
                if (hit.module is GraphicRaycaster)
                {
                    Log($"Handle press blocked by UI: {hit.gameObject.name}");
                    return false;
                }
        }
        // Sync the camera-follow pose before the press hit-test. Only do this on down.
        Physics.SyncTransforms();
        if (!Physics.Raycast(interactionCamera.ScreenPointToRay(screenPosition), out var physicsHit,
            touchRaycastDistance, touchRaycastMask, QueryTriggerInteraction.Ignore))
        {
            Log("Screen press did not hit a collider.");
            return false;
        }
        // Respect the explicitly configured handle colliders, and foreground occlusion.
        bool hitHandle = operatingHandle.colliders.Contains(physicsHit.collider);
        if (!hitHandle) Log($"Screen press hit {physicsHit.collider.name}, not a configured handle collider.");
        return hitHandle;
    }

    private void OnOperatingHandlePressed(SelectEnterEventArgs args)
    {
        Log("Handle selectEntered");
        if (!useScreenPressForHandle) PressHandle();
    }

    private void OnOperatingHandleReleased(SelectExitEventArgs args)
    {
        Log("Handle selectExited");
        if (!useScreenPressForHandle && operatingHandle.interactorsSelecting.Count == 0)
            ReleaseHandle();
    }

    private void PressHandle()
    {
        if (!isActiveAndEnabled || !isHeld || !pinRemoved || isHandlePressed) return;
        isHandlePressed = true;
        if (nozzleAutoAim != null) nozzleAutoAim.SetHandlePressed(true);
        StartHandleAnimation(handleStartLocalRotation * Quaternion.Euler(0f, 0f, handlePressAngle));
        extinguisherSpray.Play();
        Log("Handle pressed; spray started.");
    }

    private void ReleaseHandle()
    {
        if (!isHandlePressed) return;
        isHandlePressed = false;
        if (nozzleAutoAim != null) nozzleAutoAim.SetHandlePressed(false);
        StartHandleAnimation(handleStartLocalRotation);
        extinguisherSpray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Log("Handle released; spray stopped.");
    }

    private void StopHandleImmediately()
    {
        activeTouchId = -1;
        mousePressCaptured = false;
        isHandlePressed = false;
        if (nozzleAutoAim != null) nozzleAutoAim.SetHandlePressed(false);
        if (operatingHandleTransform != null) operatingHandleTransform.localRotation = handleStartLocalRotation;
        if (extinguisherSpray != null)
            extinguisherSpray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void StartHandleAnimation(Quaternion targetRotation)
    {
        if (handleRoutine != null) StopCoroutine(handleRoutine);
        handleRoutine = null;
        if (handlePressDuration <= 0f)
        {
            operatingHandleTransform.localRotation = targetRotation;
            return;
        }
        handleRoutine = StartCoroutine(AnimateHandle(targetRotation));
    }

    private IEnumerator AnimateHandle(Quaternion targetRotation)
    {
        Quaternion startRotation = operatingHandleTransform.localRotation;
        float elapsed = 0f;
        while (elapsed < handlePressDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / handlePressDuration));
            operatingHandleTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }
        operatingHandleTransform.localRotation = targetRotation;
        handleRoutine = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CancelHandleInput();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelHandleInput();
    }

    private void CancelHandleInput()
    {
        if (!initialized) return;
        if (handleRoutine != null) StopCoroutine(handleRoutine);
        handleRoutine = null;
        StopHandleImmediately();
    }

    private void OnDisable()
    {
        if (!initialized) return;
        grabInteractable.selectEntered.RemoveListener(OnExtinguisherGrabbed);
        safetyPin.selectEntered.RemoveListener(OnSafetyPinSelected);
        operatingHandle.selectEntered.RemoveListener(OnOperatingHandlePressed);
        operatingHandle.selectExited.RemoveListener(OnOperatingHandleReleased);
        ReleaseExtinguisher();
    }

    private void Log(string message)
    {
        if (logInteractions) Debug.Log($"[Extinguisher] {message} t={Time.time:F3}", this);
    }
}
