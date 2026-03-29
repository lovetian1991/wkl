namespace LanRemoteControl.Shared;

/// <summary>输入指令</summary>
public record InputCommand
{
    public InputType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public MouseButton Button { get; init; }
    public ClickType ClickType { get; init; }
    public int ScrollDelta { get; init; }
    public ushort VirtualKeyCode { get; init; }
    public bool IsKeyDown { get; init; }
}
