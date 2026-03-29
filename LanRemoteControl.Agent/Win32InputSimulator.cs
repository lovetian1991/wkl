using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>
/// Input simulator using Windows SendInput API.
/// Processes commands on a dedicated high-priority thread with a ConcurrentQueue for FIFO ordering.
/// </summary>
public sealed class Win32InputSimulator : IInputSimulator, IDisposable
{
    private readonly ConcurrentQueue<Action> _commandQueue = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly Thread _inputThread;
    private volatile bool _disposed;

    public Win32InputSimulator()
    {
        _inputThread = new Thread(ProcessLoop)
        {
            Name = "InputSimulator",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        _inputThread.Start();
    }

    public void ExecuteCommand(InputCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _commandQueue.Enqueue(() => DispatchCommand(command));
        _signal.Set();
    }

    public void SimulateMouseMove(int x, int y)
    {
        int screenWidth = InputNativeMethods.GetSystemMetrics(InputNativeMethods.SM_CXSCREEN);
        int screenHeight = InputNativeMethods.GetSystemMetrics(InputNativeMethods.SM_CYSCREEN);

        // Normalize to 0-65535 range for MOUSEEVENTF_ABSOLUTE
        int normalizedX = (int)((x * 65535.0) / (screenWidth - 1));
        int normalizedY = (int)((y * 65535.0) / (screenHeight - 1));

        var input = CreateMouseInput(
            normalizedX,
            normalizedY,
            InputNativeMethods.MOUSEEVENTF_MOVE | InputNativeMethods.MOUSEEVENTF_ABSOLUTE,
            0);

        SendSingleInput(input);
    }

    public void SimulateMouseClick(MouseButton button, ClickType clickType)
    {
        var (downFlag, upFlag) = button switch
        {
            MouseButton.Left => (InputNativeMethods.MOUSEEVENTF_LEFTDOWN, InputNativeMethods.MOUSEEVENTF_LEFTUP),
            MouseButton.Right => (InputNativeMethods.MOUSEEVENTF_RIGHTDOWN, InputNativeMethods.MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (InputNativeMethods.MOUSEEVENTF_MIDDLEDOWN, InputNativeMethods.MOUSEEVENTF_MIDDLEUP),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        int clickCount = clickType == ClickType.Double ? 2 : 1;

        for (int i = 0; i < clickCount; i++)
        {
            var downInput = CreateMouseInput(0, 0, downFlag, 0);
            var upInput = CreateMouseInput(0, 0, upFlag, 0);
            SendSingleInput(downInput);
            SendSingleInput(upInput);
        }
    }

    public void SimulateMouseScroll(int delta)
    {
        var input = CreateMouseInput(0, 0, InputNativeMethods.MOUSEEVENTF_WHEEL, delta);
        SendSingleInput(input);
    }

    public void SimulateKeyDown(ushort virtualKeyCode)
    {
        var input = CreateKeyboardInput(virtualKeyCode, InputNativeMethods.KEYEVENTF_KEYDOWN);
        SendSingleInput(input);
    }

    public void SimulateKeyUp(ushort virtualKeyCode)
    {
        var input = CreateKeyboardInput(virtualKeyCode, InputNativeMethods.KEYEVENTF_KEYUP);
        SendSingleInput(input);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signal.Set(); // Wake the thread so it can exit
        _inputThread.Join(TimeSpan.FromSeconds(2));
        _signal.Dispose();
    }

    // ─── Private helpers ────────────────────────────────────────────────

    private void DispatchCommand(InputCommand command)
    {
        switch (command.Type)
        {
            case InputType.MouseMove:
                SimulateMouseMove(command.X, command.Y);
                break;
            case InputType.MouseClick:
                SimulateMouseClick(command.Button, command.ClickType);
                break;
            case InputType.MouseScroll:
                SimulateMouseScroll(command.ScrollDelta);
                break;
            case InputType.KeyPress:
                if (command.IsKeyDown)
                    SimulateKeyDown(command.VirtualKeyCode);
                else
                    SimulateKeyUp(command.VirtualKeyCode);
                break;
        }
    }

    private void ProcessLoop()
    {
        while (!_disposed)
        {
            _signal.Wait();
            _signal.Reset();

            while (_commandQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch
                {
                    // SendInput failure: log warning and continue (design spec error handling)
                }
            }
        }
    }

    private static InputNativeMethods.INPUT CreateMouseInput(int dx, int dy, uint flags, int mouseData)
    {
        return new InputNativeMethods.INPUT
        {
            type = InputNativeMethods.INPUT_MOUSE,
            u = new InputNativeMethods.INPUT_UNION
            {
                mi = new InputNativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };
    }

    private static InputNativeMethods.INPUT CreateKeyboardInput(ushort virtualKeyCode, uint flags)
    {
        return new InputNativeMethods.INPUT
        {
            type = InputNativeMethods.INPUT_KEYBOARD,
            u = new InputNativeMethods.INPUT_UNION
            {
                ki = new InputNativeMethods.KEYBDINPUT
                {
                    wVk = virtualKeyCode,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };
    }

    private static void SendSingleInput(InputNativeMethods.INPUT input)
    {
        var inputs = new[] { input };
        InputNativeMethods.SendInput(1, inputs, Marshal.SizeOf<InputNativeMethods.INPUT>());
    }
}
