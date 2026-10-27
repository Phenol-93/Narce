# Nacre 项目约定

- 使用 C# / .NET 10 / 原生 WPF / MVVM，默认中文；保持运行时完全离线。
- Core 只包含模型、认证加密、文件存储、dotenv 和分类标签逻辑；UI 放在 Nacre。
- 不增加云服务、遥测、自动更新、托盘、自启动、AI 或全局热键。
- 所有业务数据和设置只允许写入经过 AES-256-GCM 认证加密的载荷；使用现有 Argon2id 库，禁止自制加密算法或降低 KDF 参数。
- 修改文件格式时必须明确版本与兼容策略；禁止悄悄创建新保险库覆盖旧数据。
- 保存先写加密临时文件、Flush、解密校验，再原子替换并保留加密旧文件。
- 主密码仅通过 PasswordBox.SecurePassword 转换，派生后清零可控字节数组；不要打印异常详情、密码、凭据、正文或导入内容。
- 锁定必须清空编辑器、列表、对话框、派生密钥与可控敏感引用；必须测试异步导入与闲置锁定之间的竞态。
- 分类和标签跨模块共享，不能加入厂商预设。所有设置和按钮必须有实际功能。
- PowerShell 5.1 读取 UTF-8 源码使用显式 UTF-8 或 File.ReadAllText；禁止按系统 ANSI 读写，避免中文损坏。
- 修改后按范围运行测试；交付前运行 dotnet build Nacre.sln -c Release、dotnet test Nacre.sln -c Release 和 scripts/publish.ps1。WPF 集成测试需要 Windows 桌面会话，会短暂打开测试窗口并操作虚构内容的剪贴板。
- 不提交 bin、obj、artifacts、保险库、真实 .env、密码、日志、转储或临时明文。依赖锁文件应提交。
- 不需要默认委派子代理；GitHub 地址与签名证书未配置时明确说明，不得编造。
