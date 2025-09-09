using UnityEngine;
using UnityEngine.InputSystem;

public class TouchInteractions : MonoBehaviour
{
    [Tooltip("Specifies a distance that may not be passed by press and release to evaluate to a tap")]
    public float clickThresholdMillimeters = 2.0f;
    [Tooltip("Specifies a distance that must be passed to be considered a swipe")]
    public float swipeThresholdMillimeters = 10.0f;
    
    // Animations
    public Animator animator;
    private int _animationIndex = 0;
    private int _lastAnimationIndex = 0;
    private float _animationTransitionTime = 1.0f;
    
    // Cameras
    public CameraControl cameraControl;
    
    private InputAction point;
    private InputAction click;
    private Vector2 start;
    private Vector2 current;
    private bool tracking;
    private bool hasStart;
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }
    public void OnEnable()
    {
        point = InputSystem.actions.FindAction("UI/Point");
        point.Enable();
        point.performed += OnPointPerformed;
        click = InputSystem.actions.FindAction("UI/Click");
        click.Enable();
        click.performed += OnClickPerformed;
    }
    private void OnPointPerformed(InputAction.CallbackContext ctx)
    {
        if (tracking && !hasStart)
        {
            start = ctx.ReadValue<Vector2>();
            hasStart = true;
        }
        current = ctx.ReadValue<Vector2>();
    }
    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        var value = ctx.ReadValue<float>();
        if (!tracking && value != 0)
            BeginTracking(ctx);
        else if (tracking && value == 0)
            EndTracking(ctx);
    }
    private void BeginTracking(InputAction.CallbackContext ctx)
    {
        start = Vector2.negativeInfinity;
        tracking = true;
        hasStart = false;
    }
    private void EndTracking(InputAction.CallbackContext ctx)
    {
        if (!hasStart)
        {
            OnTap();
        }
        else
        {
            var d = current - start;
            var distanceInPixels = d.magnitude;
            var distanceInMillimeters = PixelsToMillimeters(distanceInPixels);
            if (distanceInMillimeters < clickThresholdMillimeters)
                OnTap();
            else if (distanceInMillimeters >= swipeThresholdMillimeters)
                OnSwipe(VectorToDirection(d));
        }
        tracking = false;
    }
    private Direction VectorToDirection(Vector2 d)
    {
        var angle = Vector2.SignedAngle(Vector2.right, d);
        if (angle >= -45 && angle < 45)
            return Direction.Right;
        if (angle >= 45 && angle < 135)
            return Direction.Up;
        if (angle >= 135 && angle < 225)
            return Direction.Left;
        return Direction.Down;
    }
    private static float PixelsToMillimeters(float pixels)
    {
        return (pixels * 25.4f) / Screen.dpi;
    }
    private void OnTap()
    {
        // Camera update
        cameraControl.UpdateCamera();
    }
    private void OnSwipe(Direction direction)
    {
        if (direction == Direction.Right)
        {
            _animationIndex = (_animationIndex + 1) % 3;
            if (_animationIndex == 0)
                _animationIndex = 1;
        }
        else if (direction == Direction.Left)
        {
            _animationIndex = (_animationIndex - 1) % 3;
            if (_animationIndex <= 0)
            {
                _animationIndex = 2;
            }
        }
    }

    void Update()
    {
        // Anmation Update
        if (_animationIndex == 1 && _lastAnimationIndex != _animationIndex)
        {
            animator.CrossFadeInFixedTime("PosesAnim", _animationTransitionTime);
        }
        else if (_animationIndex == 2 && _lastAnimationIndex != _animationIndex)
        {
            animator.CrossFadeInFixedTime("Walk", _animationTransitionTime);
        }
        _lastAnimationIndex = _animationIndex;
        
    }
}