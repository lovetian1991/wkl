using System.Windows;
using System.Windows.Input;
using LanRemoteControl.Shared;
using InputType = LanRemoteControl.Shared.InputType;
using MouseButton = LanRemoteControl.Shared.MouseButton;

namespace LanRemoteControl.Controller;

/// <summary>
/// 输入采集器实现。
/// 捕获 WPF UIElement 上的鼠标和键盘事件，转换为 InputCommand 并通过事件输出。
/// 鼠标移动事件节流至 60Hz（16.67ms 间隔）。
/// </summary>
public sealed class InputCollector : IInputCollector
{
    /// <summary>鼠标移动最小间隔（约 60Hz）</summary>
    private static readonly long ThrottleIntervalTicks = TimeSpan.FromMilliseconds(16.67).Ticks;

    private UIElement? _target;
    private int _remoteWidth;
    private int _remoteHeight;
    private long _lastMouseMoveTicks;

    public event Action<InputCommand>? OnInputCaptured;

    public void Attach(UIElement target, int remoteWidth, int remoteHeight)
    {
        Detach();

        _target = target;
        _remoteWidth = remoteWidth;
        _remoteHeight = remoteHeight;
        _lastMouseMoveTicks = 0;

        _target.MouseMove += OnMouseMove;
        _target.MouseDown += OnMouseDown;
        _target.MouseUp += OnMouseUp;
        _target.MouseWheel += OnMouseWheel;
        _target.KeyDown += OnKeyDown;
        _target.KeyUp += OnKeyUp;

        // Ensure the element can receive keyboard focus
        _target.Focusable = true;
    }

    public void Detach()
    {
        if (_target is null)
            return;

        _target.MouseMove -= OnMouseMove;
        _target.MouseDown -= OnMouseDown;
        _target.MouseUp -= OnMouseUp;
        _target.MouseWheel -= OnMouseWheel;
        _target.KeyDown -= OnKeyDown;
        _target.KeyUp -= OnKeyUp;

        _target = null;
    }

    private (int remoteX, int remoteY) MapCoordinates(Point position)
    {
        if (_target is null)
            return (0, 0);

        var controlWidth = ((FrameworkElement)_target).ActualWidth;
        var controlHeight = ((FrameworkElement)_target).ActualHeight;

        if (controlWidth <= 0 || controlHeight <= 0)
            return (0, 0);

        int remoteX = (int)(position.X / controlWidth * _remoteWidth);
        int remoteY = (int)(position.Y / controlHeight * _remoteHeight);

        // Clamp to valid range
        remoteX = Math.Clamp(remoteX, 0, _remoteWidth - 1);
        remoteY = Math.Clamp(remoteY, 0, _remoteHeight - 1);

        return (remoteX, remoteY);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // Throttle mouse move events to ~60Hz
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastMouseMoveTicks < ThrottleIntervalTicks)
            return;
        _lastMouseMoveTicks = now;

        var pos = e.GetPosition(_target);
        var (rx, ry) = MapCoordinates(pos);

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.MouseMove,
            X = rx,
            Y = ry
        });
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_target);
        var (rx, ry) = MapCoordinates(pos);
        var button = MapMouseButton(e.ChangedButton);

        var clickType = e.ClickCount >= 2 ? ClickType.Double : ClickType.Single;

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.MouseClick,
            X = rx,
            Y = ry,
            Button = button,
            ClickType = clickType,
            IsKeyDown = true
        });

        // Capture mouse to receive events even when cursor leaves the element
        _target?.CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_target);
        var (rx, ry) = MapCoordinates(pos);
        var button = MapMouseButton(e.ChangedButton);

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.MouseClick,
            X = rx,
            Y = ry,
            Button = button,
            ClickType = ClickType.Single,
            IsKeyDown = false
        });

        _target?.ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(_target);
        var (rx, ry) = MapCoordinates(pos);

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.MouseScroll,
            X = rx,
            Y = ry,
            ScrollDelta = e.Delta
        });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.KeyPress,
            VirtualKeyCode = (ushort)vk,
            IsKeyDown = true
        });

        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);

        OnInputCaptured?.Invoke(new InputCommand
        {
            Type = InputType.KeyPress,
            VirtualKeyCode = (ushort)vk,
            IsKeyDown = false
        });

        e.Handled = true;
    }

    private static MouseButton MapMouseButton(System.Windows.Input.MouseButton wpfButton) =>
        wpfButton switch
        {
            System.Windows.Input.MouseButton.Left => MouseButton.Left,
            System.Windows.Input.MouseButton.Right => MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => MouseButton.Middle,
            _ => MouseButton.Left
        };
}
