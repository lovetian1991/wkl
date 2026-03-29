namespace LanRemoteControl.Shared;

/// <summary>输入指令类型</summary>
public enum InputType : byte
{
    MouseMove   = 0x01,
    MouseClick  = 0x02,
    MouseScroll = 0x03,
    KeyPress    = 0x04,
}
