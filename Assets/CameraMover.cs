using UnityEngine;

public class CameraMover : MonoBehaviour
{
    [Header("Movement Settings")]
    public float movementSpeed = 10f;
    public float sprintMultiplier = 2f;

    [Header("Mouse Look Settings")]
    public float mouseSensitivity = 100f;
    public bool invertYAxis = false;
    public bool lockCursor = true;

    private float xRotation = 0f;

    void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        HandleMovement();
        HandleMouseLook();
    }

    void HandleMovement()
    {
        // Get input axes
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");
        float verticalQeInput = Input.GetKey(KeyCode.E) ? 1f : Input.GetKey(KeyCode.Q) ? -1f : 0f;

        // Calculate movement direction
        Vector3 movementDirection = transform.forward * verticalInput +
                                  transform.right * horizontalInput +
                                  Vector3.up * verticalQeInput;

        // Normalize vector to prevent faster diagonal movement
        if (movementDirection.magnitude > 1f)
        {
            movementDirection.Normalize();
        }

        // Apply sprint multiplier if Left Shift is held down
        float currentSpeed = movementSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= sprintMultiplier;
        }

        // Move the camera
        transform.Translate(movementDirection * currentSpeed * Time.deltaTime, Space.World);
    }

    void HandleMouseLook()
    {
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Invert Y axis if needed
        if (invertYAxis)
        {
            mouseY *= -1f;
        }

        // Calculate vertical rotation (look up/down)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f); // Prevent over-rotation

        // Apply rotations
        transform.localRotation = Quaternion.Euler(xRotation, transform.localEulerAngles.y, 0f);
        transform.Rotate(Vector3.up * mouseX); // Horizontal rotation
    }
}