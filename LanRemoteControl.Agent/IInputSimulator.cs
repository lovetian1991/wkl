using LanRemoteControl.Shared;

namespace LanRemoteControl.Agent;

/// <summary>输入模拟器接口</summary>
public interface IInputSimulator
{
    void SimulateMouseMove(int x, int y);
    void SimulateMouseClick(MouseButton button, ClickType clickType);
    void SimulateMouseScroll(int delta);
    void SimulateKeyDown(ushort virtualKeyCode);
    void SimulateKeyUp(ushort virtualKeyCode);
    void ExecuteCommand(InputCommand command);
}
