
using UnityEngine;
using UnityEngine.InputSystem;

public class EditorXROriginMovement : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float fastSpeed = 4f;
    [SerializeField] private float verticalSpeed = 1.5f;

    void Update()
    {
        if (!Application.isEditor || !Application.isPlaying)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float speed = keyboard.leftShiftKey.isPressed
            ? fastSpeed : moveSpeed;

        Vector3 forward = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.forward, Vector3.up).normalized
            : transform.forward;

        Vector3 right = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.right, Vector3.up).normalized
            : transform.right;

        Vector3 movement = Vector3.zero;

        if (keyboard.wKey.isPressed) movement += forward;
        if (keyboard.sKey.isPressed) movement -= forward;
        if (keyboard.dKey.isPressed) movement += right;
        if (keyboard.aKey.isPressed) movement -= right;

        if (keyboard.eKey.isPressed) movement += Vector3.up;
        if (keyboard.qKey.isPressed) movement -= Vector3.up;

        float vertical = new Vector3(
            movement.x, 0, movement.z
        ).magnitude;

        Vector3 horizontal = new Vector3(
            movement.x, 0, movement.z
        );

        if (horizontal.sqrMagnitude > 1f)
            horizontal.Normalize();

        transform.position +=
            horizontal * speed * Time.deltaTime;

        transform.position +=
            Vector3.up * movement.y * verticalSpeed * Time.deltaTime;
    }
}