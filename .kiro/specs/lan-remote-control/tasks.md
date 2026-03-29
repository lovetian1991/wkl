# 实现计划：局域网远程控制工具

## 概述

按照依赖关系从底层基础设施逐步构建到完整功能。先建立共享数据模型和网络协议层，再分别实现被控端（Agent）和主控端（Controller）核心组件，最后集成联调。技术栈：.NET 10、WPF、DXGI Desktop Duplication、TurboJPEG、SendInput API。

## 任务

- [x] 1. 搭建解决方案结构和共享数据模型
  - [x] 1.1 创建解决方案和项目结构
    - 创建 `LanRemoteControl.sln` 解决方案
    - 创建 `LanRemoteControl.Shared` 类库项目（共享数据模型和协议）
    - 创建 `LanRemoteControl.Agent` 控制台/服务项目（被控端）
    - 创建 `LanRemoteControl.Controller` WPF 项目（主控端）
    - 创建 `LanRemoteControl.Tests` xUnit 测试项目，引用 FsCheck 和 FsCheck.Xunit
    - 所有项目目标框架 `net10.0-windows`，Agent 和 Controller 引用 Shared 项目
    - _需求: 全部_

  - [x] 1.2 实现核心枚举和数据结构
    - 在 Shared 项目中创建 `MessageType` 枚举（DiscoveryRequest, DiscoveryResponse, SessionRequest, SessionResponse, SessionClose, SessionCloseAck, Heartbeat, FrameData, InputCommand）
    - 创建 `InputType`、`MouseButton`、`ClickType` 枚举
    - 创建 `InputCommand`、`FrameHeader`、`EncodedFrame`、`CapturedFrame`、`DecodedFrame`、`Resolution` 数据结构
    - 创建 `DiscoveryResponsePayload`、`SessionRequestPayload`、`SessionResponsePayload`、`DiscoveredAgent` 数据结构
    - _需求: 8.1, 8.2, 8.3_

  - [x] 1.3 实现二进制协议序列化/反序列化
    - 实现消息帧的读写：1 字节 MessageType + 4 字节 PayloadLength (LE) + 变长 Payload
    - 实现 `FrameHeader` 的 25 字节固定布局二进制序列化（使用 `BinaryPrimitives`）
    - 实现 `InputCommand` 的二进制序列化/反序列化
    - 实现会话管理消息（SessionRequest/Response 等）的 JSON 序列化（`System.Text.Json`）
    - _需求: 8.1, 8.2_

  - [ ]* 1.4 编写协议序列化属性测试和单元测试
    - 单元测试：验证各消息类型的二进制序列化/反序列化往返正确性
    - 单元测试：验证 FrameHeader 25 字节布局正确性
    - 单元测试：验证 JSON 序列化的会话管理消息往返正确性
    - _需求: 8.1, 8.2_

- [x] 2. 实现被控端核心组件——桌面捕获与帧编码
  - [x] 2.1 实现桌面捕获器（DesktopCapturer）
    - 创建 `IDesktopCapturer` 接口和基于 DXGI Desktop Duplication API 的实现类
    - 使用 `IDXGIOutputDuplication` 获取桌面帧，帧数据保持在 GPU 内存中
    - 实现 `CaptureNextFrame(int timeoutMs)` 方法，默认帧间隔 33ms（30fps）
    - 实现 `CurrentResolution` 属性和 `ReduceResolution(float scaleFactor)` 方法
    - 实现 DXGI 资源初始化失败重试逻辑（最多 5 次，间隔 1 秒）
    - 实现连续超时 10 次后重新初始化 DXGI 资源的恢复逻辑
    - 实现 `IDisposable`，确保 DXGI 资源正确释放
    - _需求: 3.1, 3.2, 3.4, 3.7, 3.8, 3.9_

  - [x] 2.2 实现帧编码器（FrameEncoder）
    - 创建 `IFrameEncoder` 接口和基于 TurboJPEG（libjpeg-turbo）的实现类
    - 实现 `Encode(CapturedFrame frame)` 方法，默认 JPEG 质量 70
    - 实现 `Quality` 属性，支持动态调整（范围 30-100）
    - 使用 `ArrayPool<byte>.Shared` 复用编码缓冲区，减少 GC 压力
    - _需求: 8.3, 8.5_

  - [x] 2.3 实现自适应帧率/分辨率控制器
    - 创建自适应控制器，监控当前帧率
    - 当帧率低于 20fps 时，输出缩放因子 0.75 触发分辨率降低
    - 当帧率恢复到 20fps 以上时，恢复原始分辨率
    - 帧率波动控制在 ±5fps 范围内
    - _需求: 3.5, 3.6_

  - [ ]* 2.4 编写属性测试：低帧率触发自适应分辨率降低
    - **Property 3: 低帧率触发自适应分辨率降低**
    - 生成随机帧率值（0-60），验证帧率 < 20fps 时输出缩放因子 < 1.0，帧率 ≥ 20fps 时不触发调整
    - **验证: 需求 3.6**

  - [ ]* 2.5 编写属性测试：帧压缩有效性
    - **Property 11: 帧压缩有效性**
    - 生成随机像素数据帧（宽度 > 0，高度 > 0），验证编码后数据大小严格小于原始帧数据大小
    - **验证: 需求 8.3**

  - [ ]* 2.6 编写属性测试：帧编解码往返一致性
    - **Property 12: 帧编解码往返一致性**
    - 生成随机像素数据帧，编码再解码后验证 PSNR ≥ 30dB 且尺寸一致
    - **验证: 需求 8.5**

- [x] 3. 实现被控端核心组件——输入模拟
  - [x] 3.1 实现输入模拟器（InputSimulator）
    - 创建 `IInputSimulator` 接口和基于 Windows SendInput API 的实现类
    - 通过 P/Invoke 调用 `SendInput`，定义 `INPUT`、`MOUSEINPUT`、`KEYBDINPUT` 结构体
    - 实现鼠标操作：移动（`MOUSEEVENTF_ABSOLUTE`）、单击、双击、右键、滚轮
    - 实现键盘操作：按下（KeyDown）、释放（KeyUp）
    - 输入执行在专用高优先级线程上，确保 16ms 内响应
    - 按接收顺序逐一执行输入指令，保持顺序一致性
    - _需求: 4.1, 4.2, 4.3, 4.4, 4.8_

  - [ ]* 3.2 编写属性测试：InputCommand 到 SendInput 结构转换正确性
    - **Property 4: 输入指令到 SendInput 结构转换正确性**
    - 生成随机 InputCommand（各类型），验证生成的 SendInput 结构体字段正确
    - **验证: 需求 4.1, 4.2**

  - [ ]* 3.3 编写属性测试：输入指令执行顺序保持
    - **Property 5: 输入指令执行顺序保持**
    - 生成随机 InputCommand 序列，验证执行顺序与接收顺序一致
    - **验证: 需求 4.4**

- [x] 4. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 5. 实现被控端网络层——通信服务端与设备发现
  - [x] 5.1 实现会话管理器（SessionManager）
    - 创建 `ISessionManager` 接口和实现类
    - 实现 `ActiveSession` 属性、`TryAcceptSession` 和 `EndSession` 方法
    - 确保同一时间仅允许一个活跃会话，拒绝并发请求
    - _需求: 7.5_

  - [ ]* 5.2 编写属性测试：单活跃会话约束
    - **Property 10: 单活跃会话约束**
    - 生成随机数量（N ≥ 2）的并发会话请求，验证仅 1 个被接受，其余被拒绝
    - **验证: 需求 7.5**

  - [x] 5.3 实现通信服务端（CommunicationServer）
    - 创建 `ICommunicationServer` 接口和基于原生 TCP Socket 的实现类
    - 实现 TCP 监听、客户端连接/断开事件
    - 实现 `SessionContext` 管理，集成 `SessionManager` 进行会话准入控制
    - 实现 `SendFrameAsync` 方法，按二进制协议格式发送帧数据
    - 实现接收 InputCommand 消息并分发到 InputSimulator
    - 实现会话握手流程：接收 SessionRequest → 通过 SessionManager 判断 → 回复 SessionResponse
    - 实现 TCP 连接异常断开时的会话资源清理
    - _需求: 7.1, 7.2, 7.5, 8.1, 8.2, 8.6_

  - [x] 5.4 实现设备发现响应器（DiscoveryResponder）
    - 创建 `IDiscoveryResponder` 接口和实现类
    - 监听 UDP 端口 19620
    - 收到 DiscoveryRequest 后回复包含主机名和 TCP 服务端口的 DiscoveryResponse
    - _需求: 6.1_

  - [ ]* 5.5 编写属性测试：设备发现请求-响应正确性
    - **Property 7: 设备发现请求-响应正确性**
    - 生成随机主机名和端口配置，验证响应包含非空主机名和有效端口号（1-65535）
    - **验证: 需求 6.1**

- [x] 6. 实现被控端自适应带宽控制与敏感词过滤
  - [x] 6.1 实现自适应带宽/压缩质量控制器
    - 监控网络传输延迟和带宽
    - 当带宽低于阈值时，降低 JPEG 编码质量（最低至 30）
    - 当带宽恢复时，恢复默认质量（70）
    - _需求: 8.7, 8.8_

  - [ ]* 6.2 编写属性测试：低带宽触发自适应压缩质量调整
    - **Property 13: 低带宽触发自适应压缩质量调整**
    - 生成随机带宽值，验证低带宽时质量降低，带宽恢复时质量恢复
    - **验证: 需求 8.8**

  - [x] 6.3 实现敏感词过滤工具类
    - 实现进程名称/服务名称敏感词检测函数
    - 实现日志消息敏感短语过滤函数
    - 敏感词列表：remote, control, spy, monitor, capture, keylog, remote control, desktop capture, screen spy, input simulation
    - _需求: 1.6, 9.5_

  - [ ]* 6.4 编写属性测试：进程名称不含敏感关键词
    - **Property 1: 进程名称不含敏感关键词**
    - 生成随机字符串，验证敏感词检测函数正确识别
    - **验证: 需求 1.6**

  - [ ]* 6.5 编写属性测试：日志条目不含敏感关键词
    - **Property 14: 日志条目不含敏感关键词**
    - 生成随机日志消息，验证敏感词过滤函数正确工作
    - **验证: 需求 9.5**

- [x] 7. 组装被控端服务宿主（Agent ServiceHost）
  - [x] 7.1 实现 Agent 服务宿主
    - 创建 `AgentService : BackgroundService`，在 `ExecuteAsync` 中编排所有被控端组件
    - 启动流程：初始化 DesktopCapturer → 启动 CommunicationServer → 启动 DiscoveryResponder
    - 会话活跃时：捕获帧 → 编码 → 发送帧数据；接收 InputCommand → 执行输入模拟
    - 使用 `CancellationToken` 实现优雅停止
    - 使用 `Microsoft.Extensions.Hosting.WindowsServices` 注册为 Windows 服务
    - _需求: 1.3, 1.4, 1.5, 1.7, 3.1_

  - [x] 7.2 实现服务安装/卸载命令行
    - 支持 `--install` 参数：以 "wkl" 为服务名称安装 Windows 服务，启动类型设为自动
    - 支持 `--uninstall` 参数：卸载 "wkl" 服务
    - 配置 SCM 恢复策略：前 3 次失败 5 秒内重启
    - _需求: 1.1, 1.2, 2.1_

  - [x] 7.3 实现崩溃计数与事件日志记录
    - 连续崩溃超过 3 次时写入 Windows 事件日志（使用敏感词过滤后的消息）
    - 崩溃恢复后恢复到可接受连接的就绪状态
    - _需求: 2.2, 2.3, 9.5_

  - [ ]* 7.4 编写属性测试：连续崩溃超阈值触发日志记录
    - **Property 2: 连续崩溃超阈值触发日志记录**
    - 生成随机崩溃计数（0-100），验证 N > 3 时生成日志条目，N ≤ 3 时不生成
    - **验证: 需求 2.2**

  - [ ]* 7.5 编写单元测试：服务安装/卸载和恢复配置
    - 验证 `--install` 和 `--uninstall` 命令行参数解析正确
    - 验证 SCM 恢复策略配置为 5 秒重启
    - 验证崩溃恢复后进入可接受连接的就绪状态
    - _需求: 1.1, 1.2, 2.1, 2.3_

- [x] 8. 检查点 - 确保被控端所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 9. 实现主控端核心组件
  - [x] 9.1 实现帧解码器（FrameDecoder）
    - 创建 `IFrameDecoder` 接口和基于 TurboJPEG 的实现类
    - 实现 `Decode(EncodedFrame encoded)` 方法，返回 `DecodedFrame`
    - 解码失败时跳过损坏帧，保持上一个有效帧
    - _需求: 8.4, 8.5_

  - [x] 9.2 实现通信客户端（CommunicationClient）
    - 创建 `ICommunicationClient` 接口和基于 TCP Socket 的实现类
    - 实现 `ConnectAsync`、`DisconnectAsync`、`SendInputCommandAsync` 方法
    - 实现帧数据接收：按二进制协议解析 FrameHeader + JPEG 数据，触发 `OnFrameReceived` 事件
    - 实现连接断开检测，触发 `OnDisconnected` 事件
    - 实现会话握手：发送 SessionRequest → 接收 SessionResponse
    - _需求: 7.1, 7.2, 7.3, 7.4, 8.1, 8.4, 8.6_

  - [x] 9.3 实现设备发现客户端（DiscoveryClient）
    - 创建 `IDiscoveryClient` 接口和实现类
    - 实现 `ScanAsync` 方法：发送 UDP 广播到端口 19620，收集响应
    - 返回 `List<DiscoveredAgent>`（主机名、IP 地址、端口）
    - _需求: 6.2, 6.3_

  - [x] 9.4 实现输入采集器（InputCollector）
    - 创建 `IInputCollector` 接口和实现类
    - 捕获桌面画面显示区域的鼠标事件（移动、点击、滚轮）和键盘事件（按下、释放）
    - 实现坐标映射：将 UI 控件坐标转换为被控端桌面坐标
    - 鼠标移动事件采集频率不低于 60Hz
    - 通过 `OnInputCaptured` 事件输出 `InputCommand`
    - _需求: 4.5, 5.3_

  - [ ]* 9.5 编写属性测试：UI 事件到 InputCommand 转换正确性
    - **Property 6: UI 事件到 InputCommand 转换正确性**
    - 生成随机鼠标/键盘事件和视口尺寸，验证坐标映射和类型转换正确
    - **验证: 需求 5.3**

  - [ ]* 9.6 编写属性测试：IP 地址格式验证
    - **Property 9: IP 地址格式验证**
    - 生成随机字符串（含有效和无效 IPv4 地址），验证验证器仅接受有效格式
    - **验证: 需求 6.4**

- [x] 10. 实现主控端 WPF 界面
  - [x] 10.1 实现主窗口布局（MainWindow）
    - 创建 WPF 主窗口，包含：设备列表面板、桌面画面显示区域、工具栏（连接/断开按钮、IP 输入框）、状态栏（连接状态、帧率、延迟）
    - 实现 IP 地址输入验证（IPv4 格式校验），无效时显示错误提示
    - _需求: 5.1, 5.4, 6.4_

  - [x] 10.2 实现桌面画面渲染
    - 使用 `WriteableBitmap` 渲染解码后的帧到显示区域
    - 集成 FrameDecoder，接收帧数据后解码并更新 WriteableBitmap
    - 在 UI 线程通过 Dispatcher 更新画面
    - _需求: 5.2, 8.4_

  - [x] 10.3 实现设备发现 UI 交互
    - 扫描按钮触发 DiscoveryClient.ScanAsync
    - 将发现的 Agent 列表绑定到设备列表面板，显示主机名和 IP 地址
    - 双击设备列表项或点击连接按钮发起连接
    - _需求: 6.2, 6.3_

  - [ ]* 10.4 编写属性测试：发现结果显示完整性
    - **Property 8: 发现结果显示完整性**
    - 生成随机 DiscoveredAgent 列表，验证 UI 渲染后列表项数量与输入一致，且包含主机名和 IP
    - **验证: 需求 6.3**

  - [x] 10.5 实现连接/断开与会话管理 UI 逻辑
    - 连接按钮：调用 CommunicationClient.ConnectAsync，成功后开始接收帧和发送输入
    - 断开按钮：调用 CommunicationClient.DisconnectAsync，清理资源
    - 连接断开时在状态栏显示提示信息
    - 会话被拒绝时显示 "被控端已有活跃连接" 提示
    - 窗口关闭时断开会话并释放所有资源
    - _需求: 5.4, 5.5, 7.1, 7.3, 7.4_

  - [x] 10.6 集成输入采集器到桌面画面显示区域
    - 将 InputCollector 附加到桌面画面显示控件
    - InputCollector 捕获的 InputCommand 通过 CommunicationClient 发送到 Agent
    - _需求: 5.3, 4.5_

- [x] 11. 检查点 - 确保主控端所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [ ] 12. 端到端集成与最终验证
  - [-] 12.1 集成被控端完整管线
    - 验证 Agent 启动后完整管线工作：DesktopCapturer → FrameEncoder → CommunicationServer 发送帧
    - 验证 CommunicationServer 接收 InputCommand → InputSimulator 执行
    - 验证自适应帧率控制器和带宽控制器协同工作
    - _需求: 3.1, 4.1, 8.2, 8.7_

  - [ ] 12.2 集成主控端完整管线
    - 验证 Controller 完整管线：CommunicationClient 接收帧 → FrameDecoder → WriteableBitmap 渲染
    - 验证 InputCollector → CommunicationClient 发送 InputCommand
    - 验证设备发现 → 连接 → 会话建立 → 帧显示 → 输入控制完整流程
    - _需求: 5.2, 5.3, 6.2, 7.1, 7.2_

  - [ ]* 12.3 编写集成测试
    - 使用 loopback TCP 连接测试完整会话生命周期（建立 → 帧传输 → 输入传输 → 断开）
    - 使用 loopback UDP 测试设备发现流程
    - 使用 mock DXGI 接口测试捕获-编码-传输-解码管线
    - _需求: 6.1, 7.1, 7.2, 7.3, 7.4, 7.5, 8.1, 8.2_

- [ ] 13. 最终检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## 备注

- 标记 `*` 的任务为可选任务，可跳过以加速 MVP 开发
- 每个任务引用了对应的需求编号，确保可追溯性
- 检查点任务用于阶段性验证，确保增量开发的正确性
- 属性测试使用 FsCheck 框架，验证设计文档中定义的正确性属性
- 单元测试和属性测试互补：单元测试捕获具体 bug，属性测试验证通用正确性
