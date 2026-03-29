# 需求文档

## 简介

本功能实现一个局域网远程控制工具，包含主控端和被控端两个组件。被控端作为 Windows 服务运行，提供桌面画面捕获和输入模拟能力；主控端提供图形界面，可实时查看并远程操控被控端桌面。两端通过局域网进行通信，支持设备发现、连接管理和实时桌面流传输。

## 术语表

- **主控端（Controller）**：具有 UI 界面的客户端程序，用于查看和操控被控端桌面
- **被控端（Agent）**：安装在目标 Windows 机器上的服务程序（Windows 服务名称为 "wkl"），负责捕获桌面画面并接收远程输入指令
- **桌面帧（Desktop_Frame）**：被控端捕获的一帧桌面画面数据
- **输入指令（Input_Command）**：主控端发送的鼠标或键盘操作指令
- **会话（Session）**：主控端与被控端之间建立的一次远程控制连接
- **服务管理器（Service_Manager）**：Windows 服务控制管理器（SCM），负责管理被控端服务的生命周期
- **设备发现（Device_Discovery）**：在局域网内自动发现可用被控端的机制

## 需求

### 需求 1：被控端服务安装与运行

**用户故事：** 作为系统管理员，我希望被控端能以 "wkl" 作为服务名称安装为 Windows 服务并运行，以便在系统启动时自动运行且无需用户登录，同时对被控端本地用户完全不可见。

#### 验收标准

1. THE Agent SHALL 支持通过命令行以 "wkl" 作为服务名称安装为 Windows 系统服务
2. THE Agent SHALL 支持通过命令行从 Windows 系统服务中卸载 "wkl" 服务
3. WHEN 操作系统启动完成后，THE Service_Manager SHALL 自动启动 "wkl" 服务
4. WHILE Agent 以服务模式运行时，THE Agent SHALL 在无用户登录的情况下保持 "wkl" 服务运行
5. WHILE Agent 以服务模式运行时，THE Agent SHALL 不在 Windows 任务栏、系统托盘区域显示任何图标或窗口
6. THE Agent 的服务进程名称 SHALL 采用与系统常见服务相似的命名方式（如 "wkl"），避免使用包含 "remote"、"control"、"spy" 等敏感关键词的进程名
7. WHILE Agent 以服务模式运行时，THE Agent SHALL 不弹出任何系统通知、对话框或用户提示

### 需求 2：被控端崩溃自动恢复

**用户故事：** 作为系统管理员，我希望被控端在崩溃后能自动重启，以确保远程控制服务的高可用性。

#### 验收标准

1. IF "wkl" 服务进程异常退出，THEN THE Service_Manager SHALL 在 5 秒内自动重启 "wkl" 服务
2. IF "wkl" 服务连续崩溃超过 3 次，THEN THE Agent SHALL 将崩溃信息写入系统事件日志
3. WHEN "wkl" 服务从崩溃中恢复后，THE Agent SHALL 恢复到可接受连接的就绪状态

### 需求 3：被控端桌面捕获

**用户故事：** 作为主控端用户，我希望能实时看到被控端的桌面画面，同时捕获过程对被控端本地用户完全透明、无任何可感知的异常。

#### 验收标准

1. WHILE 一个 Session 处于活跃状态时，THE Agent SHALL 持续捕获桌面画面并生成 Desktop_Frame
2. THE Agent SHALL 以不低于每秒 30 帧的速率捕获 Desktop_Frame，以确保画面流畅稳定
3. WHILE Agent 捕获桌面画面时，THE Agent SHALL 不干扰被控端本地用户的正常操作
4. THE Agent SHALL 捕获完整的桌面画面，包括所有可见窗口和任务栏
5. WHILE 一个 Session 处于活跃状态时，THE Agent SHALL 保持帧率波动不超过 ±5 帧/秒，以确保画面传输稳定无卡顿
6. IF 系统资源不足导致帧率下降至 20 帧/秒以下，THEN THE Agent SHALL 自动降低捕获分辨率以维持稳定帧率
7. WHILE Agent 捕获桌面画面时，THE Agent SHALL 不产生任何可见的视觉提示，包括但不限于屏幕闪烁、画面卡顿、光标形状变化或屏幕边框高亮
8. WHILE Agent 捕获桌面画面时，THE Agent SHALL 不在被控端屏幕上叠加任何水印、指示器或状态提示
9. WHILE Agent 捕获桌面画面时，THE Agent 的 CPU 占用率 SHALL 不超过单核 5%，以避免被控端本地用户感知到性能下降

### 需求 4：被控端输入模拟

**用户故事：** 作为主控端用户，我希望能远程控制被控端的鼠标和键盘，同时输入模拟过程对被控端本地用户完全透明，不产生任何可感知的异常现象。

#### 验收标准

1. WHEN Agent 收到鼠标类型的 Input_Command 时，THE Agent SHALL 在 16 毫秒内在被控端桌面上模拟对应的鼠标操作（移动、单击、双击、右键、滚轮）
2. WHEN Agent 收到键盘类型的 Input_Command 时，THE Agent SHALL 在被控端桌面上模拟对应的键盘操作（按键、释放、组合键）
3. WHILE Agent 执行远程 Input_Command 时，THE Agent SHALL 不阻塞被控端本地用户的输入操作
4. WHEN Controller 发送连续的鼠标移动 Input_Command 时，THE Agent SHALL 按接收顺序逐一执行，确保鼠标轨迹连贯流畅
5. THE Controller SHALL 以不低于每秒 60 次的频率采集并发送鼠标移动事件，以确保鼠标操控的流畅性
6. WHILE 被控端本地用户正在进行操作时，THE Agent SHALL 不因执行远程 Input_Command 而导致光标位置突然跳动
7. WHILE 被控端本地用户正在进行操作时，THE Agent SHALL 不因执行远程 Input_Command 而导致窗口焦点意外切换
8. WHILE Agent 执行远程 Input_Command 时，THE Agent SHALL 不触发被控端操作系统的 UAC 提示或其他安全对话框

### 需求 5：主控端用户界面

**用户故事：** 作为主控端用户，我希望有一个图形界面来管理远程控制连接，以便方便地查看和操控被控端。

#### 验收标准

1. THE Controller SHALL 提供一个图形用户界面窗口
2. THE Controller SHALL 在界面中显示被控端的实时桌面画面
3. THE Controller SHALL 支持用户通过界面上的操作（鼠标点击、键盘输入）向被控端发送 Input_Command
4. THE Controller SHALL 提供手动连接和断开被控端的功能按钮
5. WHEN 用户关闭 Controller 窗口时，THE Controller SHALL 断开当前 Session 并释放所有资源

### 需求 6：局域网设备发现

**用户故事：** 作为主控端用户，我希望能自动发现局域网内的被控端，以便快速建立连接而无需手动输入地址。

#### 验收标准

1. WHILE Agent 服务处于运行状态时，THE Agent SHALL 响应局域网内的 Device_Discovery 请求
2. THE Controller SHALL 提供扫描局域网内可用 Agent 的功能
3. WHEN Controller 发起 Device_Discovery 时，THE Controller SHALL 在界面中展示发现的 Agent 列表，包含设备名称和 IP 地址
4. THE Controller SHALL 支持用户手动输入 Agent 的 IP 地址进行连接

### 需求 7：会话管理

**用户故事：** 作为主控端用户，我希望能可靠地建立和管理与被控端的连接会话，以确保远程控制过程稳定。

#### 验收标准

1. WHEN Controller 请求连接某个 Agent 时，THE Controller SHALL 与该 Agent 建立一个 Session
2. WHEN Session 建立成功后，THE Controller SHALL 开始接收并显示 Desktop_Frame
3. IF 网络连接中断导致 Session 断开，THEN THE Controller SHALL 在界面中显示连接断开的提示信息
4. WHEN 用户点击断开按钮时，THE Controller SHALL 主动关闭当前 Session
5. THE Agent SHALL 同一时间仅允许一个活跃的 Session

### 需求 8：局域网通信协议

**用户故事：** 作为开发者，我希望主控端和被控端之间有明确的通信协议，以确保数据传输的可靠性和实时性。

#### 验收标准

1. THE Controller 和 Agent SHALL 通过局域网 TCP 连接传输控制指令和会话管理消息
2. THE Agent SHALL 通过局域网传输 Desktop_Frame 数据到 Controller
3. THE Agent SHALL 对 Desktop_Frame 进行压缩编码后再传输，以降低带宽占用
4. THE Controller SHALL 对接收到的 Desktop_Frame 进行解码并渲染显示
5. FOR ALL 有效的 Desktop_Frame，编码后解码 SHALL 产生与原始帧视觉等价的图像（往返一致性）
6. THE Controller 和 Agent 之间的 Input_Command 传输端到端延迟 SHALL 不超过 50 毫秒
7. WHILE Session 处于活跃状态时，THE Agent SHALL 维持 Desktop_Frame 的稳定传输，丢帧率不超过 1%
8. IF 网络带宽波动导致传输延迟增加，THEN THE Agent SHALL 动态调整 Desktop_Frame 的压缩率以维持传输稳定性

### 需求 9：被控端操作全程透明性

**用户故事：** 作为系统管理员，我希望被控端的所有远程控制操作对本地用户完全透明不可感知，以确保远程控制过程不影响被控端用户的正常使用体验。

#### 验收标准

1. WHILE Session 处于活跃状态时，THE Agent 的所有操作（桌面捕获、输入模拟、网络通信）SHALL 不导致被控端本地用户可感知的性能下降（CPU 占用增量不超过单核 5%，内存占用不超过 50MB）
2. WHILE Session 处于活跃状态时，THE Agent SHALL 不在被控端产生任何视觉异常，包括屏幕闪烁、光标跳动、窗口焦点切换、桌面重绘或分辨率变化
3. WHILE Session 处于活跃状态时，THE Agent SHALL 不触发被控端操作系统的任何安全提示、防火墙弹窗或用户授权请求
4. WHILE Agent 进行网络通信时，THE Agent SHALL 不导致被控端本地用户的网络体验出现可感知的延迟增加或带宽下降
5. THE Agent SHALL 不在 Windows 事件查看器中写入可被普通用户轻易发现的异常日志条目（崩溃恢复日志除外）
6. WHILE Session 处于活跃状态时，THE Agent SHALL 不修改被控端的系统设置、注册表项或桌面外观
