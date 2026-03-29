using LanRemoteControl.Shared;

namespace LanRemoteControl.Controller;

/// <summary>输入采集器接口，捕获用户在桌面画面显示区域的鼠标和键盘操作</summary>
public interface IInputCollector
{
    /// <summary>捕获到输入指令时触发</summary>
    event Action<InputCommand>? OnInputCaptured;

    /// <summary>附加到 WPF UIElement，开始捕获输入事件</summary>
    /// <param name="target">桌面画面显示区域控件</param>
    /// <param name="remoteWidth">被控端桌面宽度</param>
    /// <param name="remoteHeight">被控端桌面高度</param>
    void Attach(System.Windows.UIElement target, int remoteWidth, int remoteHeight);

    /// <summary>分离并移除所有事件处理器</summary>
    void Detach();
}
