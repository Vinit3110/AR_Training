using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARPlacementController : MonoBehaviour
{
    [Header("AR References")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private Camera arCamera;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARAnchorManager anchorManager;

    [Header("Placement")]
    [SerializeField] private GameObject reticle;
    [SerializeField] private GameObject environment;
    [SerializeField, Min(0f)] private float minDistance = 0.75f;
    [SerializeField, Min(0.1f)] private float maxDistance = 4f;
    [SerializeField] private Button placeTrainingButton;
    [Tooltip("Optional existing text element for placement status. No UI is created by this script.")]
    [SerializeField] private TMP_Text statusText;
    [Tooltip("Leave off for point-marker placement. Enable only if the entire configured footprint must fit.")]
    [SerializeField] private bool requireFullFootprint;
    [Tooltip("Metres in the environment root's X/Z axes, including clearance around the props.")]
    [SerializeField] private Vector2 footprintSize = new Vector2(1.3f, 1.1f);
    [SerializeField] private Vector2 footprintCenter = new Vector2(0.8f, 0.2f);
    [Tooltip("0 points the authored +Z away from the user, keeping the wall behind the training area.")]
    [SerializeField] private float orientationOffset;

    [Header("Preview")]
    [SerializeField, Min(0.001f)] private float surfaceOffset = 0.02f;
    [SerializeField] private bool hidePlaneVisualsAfterPlacement = true;

    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private readonly List<Vector2> boundary = new List<Vector2>();
    private readonly Vector2[] planeCorners = new Vector2[4];
    private readonly Vector3[] localCorners = new Vector3[4];
    private MaterialPropertyBlock previewProperties;
    private readonly List<Renderer> planeRenderers = new List<Renderer>();
    private Renderer[] markerRenderers;
    private string lastStatus;
    private float errorUntil;
    private ARPlane candidatePlane;
    private Pose placementPose;
    private bool initialized;
    private bool hasValidPlacement;
    private bool isPlacing;
    private bool isPlaced;
    private bool applicationPaused;
    private int requestVersion;

    private void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (raycastManager != null)
        {
            if (planeManager == null) planeManager = raycastManager.GetComponent<ARPlaneManager>();
            if (anchorManager == null) anchorManager = raycastManager.GetComponent<ARAnchorManager>();
        }
        if (raycastManager == null || arCamera == null || planeManager == null || anchorManager == null ||
            reticle == null || environment == null || placeTrainingButton == null ||
            (requireFullFootprint && (footprintSize.x <= 0f || footprintSize.y <= 0f)) || maxDistance <= minDistance ||
            transform.IsChildOf(environment.transform) || reticle.transform.IsChildOf(environment.transform) ||
            environment.transform.IsChildOf(reticle.transform) || transform.IsChildOf(reticle.transform))
        {
            Debug.LogError("ARPlacementController: Assign AR managers, camera, environment, marker and button. " +
                "Keep the controller and reticle outside the environment; check footprint and distance settings.", this);
            if (placeTrainingButton != null) placeTrainingButton.interactable = false;
            if (reticle != null && !transform.IsChildOf(reticle.transform)) reticle.SetActive(false);
            enabled = false;
            return;
        }

        // Allocates a native Unity object; field initializers also run during serialization.
        previewProperties = new MaterialPropertyBlock();
        environment.SetActive(false);
        reticle.SetActive(false);
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        // Only move/tint the marker supplied in the Inspector. Do not create geometry or components.
        markerRenderers = reticle.GetComponentsInChildren<Renderer>(true);
        if (markerRenderers.Length == 0)
            Debug.LogWarning("[AR Placement] Reticle has no renderer. Add a small marker mesh as a child in Unity.", this);
        BuildCorners();
        initialized = true;
        SetButton(false, "Scan floor");
    }

    private void OnEnable()
    {
        if (!initialized) return;
        // Existing Inspector event remains supported; avoid registering it twice.
        bool wired = false;
        for (int i = 0; i < placeTrainingButton.onClick.GetPersistentEventCount(); i++)
            wired |= placeTrainingButton.onClick.GetPersistentTarget(i) == this &&
                     placeTrainingButton.onClick.GetPersistentMethodName(i) == nameof(PlaceTraining);
        if (!wired) placeTrainingButton.onClick.AddListener(PlaceTraining);
    }

    private void BuildCorners()
    {
        // Preserve the environment's authored scale instead of resizing the content.
        Vector3 scale = environment.transform.lossyScale;
        Vector2 half = footprintSize * 0.5f;
        localCorners[0] = new Vector3((footprintCenter.x - half.x) * scale.x, 0f, (footprintCenter.y - half.y) * scale.z);
        localCorners[1] = new Vector3((footprintCenter.x - half.x) * scale.x, 0f, (footprintCenter.y + half.y) * scale.z);
        localCorners[2] = new Vector3((footprintCenter.x + half.x) * scale.x, 0f, (footprintCenter.y + half.y) * scale.z);
        localCorners[3] = new Vector3((footprintCenter.x + half.x) * scale.x, 0f, (footprintCenter.y - half.y) * scale.z);
    }

    private bool Ready() => !applicationPaused && ARSession.state == ARSessionState.SessionTracking &&
        raycastManager.isActiveAndEnabled && planeManager.isActiveAndEnabled &&
        anchorManager.isActiveAndEnabled && anchorManager.subsystem != null && anchorManager.subsystem.running;

    private void Update()
    {
        if (isPlaced)
        {
            if (hidePlaneVisualsAfterPlacement) HidePlaneVisuals();
            return;
        }
        if (!isPlacing && Time.unscaledTime >= errorUntil) UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        hasValidPlacement = false;
        candidatePlane = null;
        reticle.SetActive(false);
        if (!Ready())
        {
            SetButton(false, "Waiting for AR tracking and anchor support.");
            return;
        }

        Vector2 center = arCamera.pixelRect.center;
        if (!raycastManager.Raycast(center, hits, TrackableType.PlaneWithinPolygon))
        {
            SetButton(false, "Aim the centre of the screen at a horizontal surface.");
            return;
        }
        string rejection = "No tracked upward-facing horizontal surface at the screen centre.";
        foreach (ARRaycastHit hit in hits)
        {
            ARPlane plane = planeManager.GetPlane(hit.trackableId);
            if (!UsablePlane(plane)) continue;
            float distance = Vector3.Distance(arCamera.transform.position, hit.pose.position);
            if (distance < minDistance || distance > maxDistance)
            {
                rejection = $"Move the marker between {minDistance:0.##} and {maxDistance:0.##} metres from the camera.";
                break;
            }

            Vector3 forward = Vector3.ProjectOnPlane(hit.pose.position - arCamera.transform.position, plane.normal);
            if (forward.sqrMagnitude < 0.0001f) continue;
            placementPose = new Pose(hit.pose.position,
                Quaternion.LookRotation(forward.normalized, plane.normal) * Quaternion.Euler(0f, orientationOffset, 0f));
            hasValidPlacement = ValidCandidate(ref plane, placementPose);
            candidatePlane = plane;
            reticle.transform.SetPositionAndRotation(placementPose.position + plane.normal * surfaceOffset, placementPose.rotation);
            Color color = hasValidPlacement ? new Color(0.1f, 1f, 0.35f) : new Color(1f, 0.3f, 0.1f);
            previewProperties.SetColor("_BaseColor", color);
            previewProperties.SetColor("_Color", color);
            foreach (Renderer markerRenderer in markerRenderers)
                if (markerRenderer != null) markerRenderer.SetPropertyBlock(previewProperties);
            reticle.SetActive(true);
            SetButton(hasValidPlacement, hasValidPlacement ? "Ready. Press Place Training to place at the marker." : "Scan more floor to fit the configured footprint.");
            // Do not look through a nearer unsuitable floor/table to place on one behind it.
            return;
        }
        SetButton(false, rejection);
    }

    private static bool UsablePlane(ARPlane plane) => plane != null && plane.isActiveAndEnabled &&
        plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null &&
        plane.alignment == PlaneAlignment.HorizontalUp;

    private bool FitsPlane(ARPlane plane, Pose pose)
    {
        var polygon = plane.boundary;
        if (!polygon.IsCreated || polygon.Length < 3) return false;
        boundary.Clear();
        for (int i = 0; i < polygon.Length; i++) boundary.Add(polygon[i]);
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = plane.transform.InverseTransformPoint(pose.position + pose.rotation * localCorners[i]);
            planeCorners[i] = new Vector2(local.x, local.z);
        }
        return PlacementFootprintUtility.Fits(boundary, planeCorners);
    }

    private bool ValidCandidate(ref ARPlane plane, Pose pose)
    {
        // Plane merging is normal during scanning; use the replacement rather than failing silently.
        while (plane != null && plane.subsumedBy != null) plane = plane.subsumedBy;
        if (!Ready() || !UsablePlane(plane)) return false;
        float distance = Vector3.Distance(arCamera.transform.position, pose.position);
        if (distance < minDistance || distance > maxDistance) return false;
        if (requireFullFootprint) return FitsPlane(plane, pose);
        var polygon = plane.boundary;
        if (!polygon.IsCreated || polygon.Length < 3) return false;
        boundary.Clear();
        for (int i = 0; i < polygon.Length; i++) boundary.Add(polygon[i]);
        Vector3 local = plane.transform.InverseTransformPoint(pose.position);
        return PlacementFootprintUtility.ContainsPoint(boundary, new Vector2(local.x, local.z));
    }

    public async void PlaceTraining()
    {
        if (!initialized || !isActiveAndEnabled || isPlaced || isPlacing) return;
        Debug.Log("[AR Placement] PlaceTraining requested.", this);
        // Validate the displayed marker. Do not perform a second raycast that moves the target on click.
        ARPlane plane = candidatePlane;
        Pose pose = placementPose;
        if (!hasValidPlacement || !ValidCandidate(ref plane, pose))
        {
            ReportFailure("The displayed marker is no longer valid. Aim at a tracked surface and retry.");
            return;
        }
        int version = ++requestVersion;
        isPlacing = true;
        SetButton(false, "Placing...");
        ARAnchor anchor = null;
        try
        {
            // Prefer a plane attachment where supported; otherwise create a world anchor.
            if (anchorManager.subsystem.subsystemDescriptor.supportsTrackableAttachments)
            {
                try { anchor = anchorManager.AttachAnchor(plane, pose); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[AR Placement] Plane attachment failed; trying a world anchor. {exception.Message}", this);
                }
            }
            if (anchor == null)
            {
                var result = await anchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess()) anchor = result.value;
                else if (this != null && version == requestVersion)
                {
                    ReportFailure($"Anchor creation failed ({result.status}). Scan the surface and retry.");
                    return;
                }
            }
            if (this == null || version != requestVersion || !isActiveAndEnabled)
            {
                RemoveUnusedAnchor(anchor);
                return;
            }
            if (anchor == null || !ValidCandidate(ref plane, new Pose(anchor.transform.position, anchor.transform.rotation)))
            {
                RemoveUnusedAnchor(anchor);
                ReportFailure("Tracking or surface validity changed while creating the anchor. Aim and retry.");
                return;
            }

            Vector3 worldScale = environment.transform.lossyScale;
            environment.transform.SetParent(anchor.transform, false);
            environment.transform.localPosition = Vector3.zero;
            environment.transform.localRotation = Quaternion.identity;
            Vector3 anchorScale = anchor.transform.lossyScale;
            environment.transform.localScale = new Vector3(worldScale.x / anchorScale.x,
                worldScale.y / anchorScale.y, worldScale.z / anchorScale.z);
            isPlaced = true;
            hasValidPlacement = false;
            environment.SetActive(true);
            SetButton(false, "Training placed.");
            Debug.Log($"[AR Placement] Activated {environment.name} at {environment.transform.position}.", this);
            reticle.SetActive(false);
            placeTrainingButton.gameObject.SetActive(false);
            if (hidePlaneVisualsAfterPlacement) HidePlaneVisuals();
        }
        catch (Exception exception)
        {
            if (!isPlaced) RemoveUnusedAnchor(anchor);
            if (this != null && version == requestVersion)
            {
                ReportFailure($"Placement failed: {exception.Message}");
                Debug.LogException(exception, this);
            }
        }
        finally
        {
            if (this != null && version == requestVersion) isPlacing = false;
        }
    }

    private void RemoveUnusedAnchor(ARAnchor anchor)
    {
        if (anchor == null) return;
        if (anchorManager != null && anchorManager.isActiveAndEnabled && anchorManager.TryRemoveAnchor(anchor)) return;
        Destroy(anchor.gameObject);
    }

    private void HidePlaneVisuals()
    {
        // Keep tracking/managers active so anchor updates continue.
        foreach (ARPlane plane in planeManager.trackables)
        {
            plane.GetComponentsInChildren(true, planeRenderers);
            foreach (Renderer planeRenderer in planeRenderers)
                planeRenderer.enabled = false;
        }
    }

    private void SetButton(bool interactable, string label)
    {
        placeTrainingButton.interactable = interactable;
        // Preserve the user's button caption. Status goes only to an explicitly assigned text element.
        if (lastStatus == label) return;
        lastStatus = label;
        if (statusText != null) statusText.text = label;
    }

    private void ReportFailure(string message)
    {
        hasValidPlacement = false;
        reticle.SetActive(false);
        errorUntil = Time.unscaledTime + 3f;
        SetButton(false, message);
        Debug.LogWarning($"[AR Placement] {message}", this);
    }

    private void OnDisable()
    {
        InvalidatePreview();
        if (placeTrainingButton != null)
            placeTrainingButton.onClick.RemoveListener(PlaceTraining);
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused) InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        ++requestVersion; // Invalidate outstanding async placement completions.
        isPlacing = false;
        hasValidPlacement = false;
        if (reticle != null) reticle.SetActive(false);
        if (placeTrainingButton != null) placeTrainingButton.interactable = false;
    }
}
