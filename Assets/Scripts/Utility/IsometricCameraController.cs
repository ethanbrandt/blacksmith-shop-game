using UnityEngine;

public class IsometricCameraController : MonoBehaviour
{
    [Header("Initial Rotation")]
    public Vector3 fixedRotation = new Vector3(13.206f, 51.511f, 1.494f);

    [Header("Follow Target")]
    public Transform target;

    [Header("Smoothing")]
    public float smoothing = 30f;

    [Header("Orbit (Right Mouse Drag)")]
    public float orbitSensitivity = 0.3f;
    public float minPitch = 5f;
    public float maxPitch = 60f;

    [Header("Zoom")]
    public float zoomSpeed = 2f;
    public float minZoom = 3f;
    public float maxZoom = 15f;
    public float defaultZoom = 5f;

    [Header("Pixel-Perfect")]
    public bool usePixelSnap = true;
    public PixelRendererFeature pixelRendererFeature;

    private static readonly int PixelPanOffsetId = Shader.PropertyToID("_PixelPanOffset");

    private Camera _cam;
    private Vector3 _truePosition;
    private Vector3 _targetPosition;
    private float _targetZoom;
    private float _trueOrthographicSize;
    private float _currentYRotation;
    private float _targetYRotation;
    private float _currentXRotation;
    private float _targetXRotation;

    private void Awake()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
        {
            Debug.LogError("[IsometricCamera] No Camera found in CameraPivot children!");
            return;
        }

        if (!_cam.orthographic)
        {
            Debug.LogWarning("[IsometricCamera] Camera is not Orthographic. Forcing adjustment.");
            _cam.orthographic = true;
        }

        _currentYRotation = fixedRotation.y;
        _targetYRotation = fixedRotation.y;
        _currentXRotation = fixedRotation.x;
        _targetXRotation = fixedRotation.x;

        transform.rotation = Quaternion.Euler(_currentXRotation, _currentYRotation, fixedRotation.z);

        if (target != null)
        {
            _truePosition = target.position;
        }
        else
        {
            _truePosition = transform.position;
        }

        _targetPosition = _truePosition;
        _targetZoom = defaultZoom;
        _trueOrthographicSize = defaultZoom;
        _cam.orthographicSize = defaultZoom;

        Debug.Log("[IsometricCamera] Camera Pivot initialized successfully.");
    }

    private void LateUpdate()
    {
        if (_cam == null) return;
        
        // I don't need these for the current prototype, so they're disabled
        //HandleOrbitInput();
        //HandleZoomInput();
        
        HandleFollowTarget();
        ApplyTransformations();
    }

    private void HandleOrbitInput()
    {
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            _targetYRotation += mouseX * orbitSensitivity * 10f;
            _targetXRotation -= mouseY * orbitSensitivity * 10f;

            _targetXRotation = Mathf.Clamp(_targetXRotation, minPitch, maxPitch);
        }

        _currentYRotation = _targetYRotation;
        _currentXRotation = _targetXRotation;
    }

    private void HandleZoomInput()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            _targetZoom -= scroll * zoomSpeed;
            _targetZoom = Mathf.Clamp(_targetZoom, minZoom, maxZoom);
        }
    }

    private void HandleFollowTarget()
    {
        if (target != null)
        {
            _targetPosition = target.position;
        }
    }

    private void GetInternalResolution(out int width, out int height)
    {
        if (pixelRendererFeature != null)
        {
            width = pixelRendererFeature.settings.width;
            height = pixelRendererFeature.settings.height;
            return;
        }

        width = 640;
        height = 360;
    }

    private void ApplyTransformations()
    {
        transform.rotation = Quaternion.Euler(_currentXRotation, _currentYRotation, fixedRotation.z);

        _truePosition = Vector3.Lerp(_truePosition, _targetPosition, smoothing * Time.deltaTime);
        _trueOrthographicSize = Mathf.Lerp(_trueOrthographicSize, _targetZoom, smoothing * Time.deltaTime);

        if (!usePixelSnap)
        {
            _cam.orthographicSize = _trueOrthographicSize;
            transform.position = _truePosition;
            Shader.SetGlobalVector(PixelPanOffsetId, Vector4.zero);
            return;
        }

        GetInternalResolution(out int internalWidth, out int internalHeight);

        // Snap zoom to ortho steps aligned with the internal pixel grid.
        float orthoSnapStep = 2f / internalHeight;
        float snappedOrtho = Mathf.Round(_trueOrthographicSize / orthoSnapStep) * orthoSnapStep;
        snappedOrtho = Mathf.Clamp(snappedOrtho, minZoom, maxZoom);
        _cam.orthographicSize = snappedOrtho;

        float worldPerPixelY = (snappedOrtho * 2f) / internalHeight;
        float worldPerPixelX = (snappedOrtho * 2f * _cam.aspect) / internalWidth;

        Vector3 camLocalOffset = _cam.transform.localPosition;
        Vector3 desiredCameraWorld = _truePosition + transform.rotation * camLocalOffset;

        Vector3 right = _cam.transform.right;
        Vector3 up = _cam.transform.up;

        float alongRight = Vector3.Dot(desiredCameraWorld, right);
        float alongUp = Vector3.Dot(desiredCameraWorld, up);

        float snappedRight = Mathf.Round(alongRight / worldPerPixelX) * worldPerPixelX;
        float snappedUp = Mathf.Round(alongUp / worldPerPixelY) * worldPerPixelY;

        float errorRight = alongRight - snappedRight;
        float errorUp = alongUp - snappedUp;

        Vector3 snappedCameraWorld = desiredCameraWorld - (errorRight * right + errorUp * up);
        transform.position = snappedCameraWorld - transform.rotation * camLocalOffset;

        float uvOffsetX = errorRight / (snappedOrtho * 2f * _cam.aspect);
        float uvOffsetY = errorUp / (snappedOrtho * 2f);

        Shader.SetGlobalVector(PixelPanOffsetId, new Vector4(uvOffsetX, uvOffsetY, 0, 0));
    }
}