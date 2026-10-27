# Nacre

一款轻量的 Windows 本地密钥与文本收藏工具，用于整理 API Key、开发凭据、Prompt 和短代码片段。

**Windows 10 / 11 x64 · C# / .NET 10 / WPF · MIT License**

数据保存在本地加密文件中，无需在线账户。应用完全离线运行，不收集遥测，不接入 AI 模型。

## 功能

- **开发凭据管理**：新增、编辑、删除、复制记录；支持多个自定义凭据字段，并可分别设置敏感或普通属性。
- **日常取用**：敏感字段默认隐藏，可临时显示或单独复制；Base URL、环境变量名也可分别复制。
- **自由整理**：自定义分类、标签、收藏、搜索、筛选和排序，不预设厂商或业务分类。
- **导入与配置**：预览并选择导入 .env 变量；按需选择字段，复制为 .env 或 JSON 配置片段。
- **文本收藏**：统一管理 Prompt 与代码/配置片段，支持多行编辑、搜索、收藏、正文复制和 txt/md 导入导出。
- **本地保护**：主密码解锁、Argon2id 密钥派生、AES-256-GCM 认证加密、手动与闲置锁定、剪贴板延时清理、加密备份和恢复。
- **简洁界面**：默认中文，支持浅色、深色和跟随系统，以及标准、便携两种存储模式。

每条凭据还可保存文档/后台链接、使用项目、有效期和备注。外部链接由系统默认浏览器打开。

## 下载与使用

在本仓库的 **Releases** 页面下载 Windows x64 ZIP，解压后运行 Nacre.exe。发布包包含 .NET 运行环境，无需另行安装 .NET。

1. 首次启动时创建主密码，至少 6 个字符；建议使用独有的长密码。
2. 在“密钥库”或“文本收藏”中点击“新增”，填写内容后保存。
3. 分类可直接输入；多个标签使用中英文逗号、分号或换行分隔，也可在“分类与标签”中统一管理。
4. 在“设置”中调整主题、锁定与剪贴板清理时间，或修改主密码、创建备份。

**忘记主密码无法恢复数据。** 所有修改需手动保存；默认闲置 5 分钟自动锁定，自动锁定会放弃未保存的编辑。敏感剪贴板默认 30 秒后尝试清理。

### 导入与导出

- .env 导入支持 UTF-8/BOM、LF/CRLF、export、空值、引号、多行引号和常用转义。导入前可逐项预览，重名变量另存为新记录，不覆盖已有记录；不会展开变量或执行命令。
- 复制配置时可勾选字段并调整输出名称；.env 输出名称需要符合环境变量命名规则。
- txt/md 导入导出保留文本正文。导出文件是**未加密的 UTF-8 明文**，请妥善保管。
- 单个导入文件最大 4 MiB，保险库解密载荷最大 32 MiB，适合开发凭据与短文本收藏。

## 数据位置与便携模式

| 模式 | 启用方式 | 数据位置 |
| --- | --- | --- |
| 标准模式 | 使用标准版，EXE 旁没有 portable.flag | %LOCALAPPDATA%\Nacre |
| 便携模式 | 使用便携版，或在 EXE 旁创建空文件 portable.flag | EXE 所在目录\Data |

两种模式使用同一份程序。便携目录不可写时会提示错误，不会自动更换存储位置。切换模式不会搬迁数据，请先创建加密备份，再通过恢复功能迁移。

## 备份与升级

在“设置”中选择“保存加密备份”，并将备份保存在独立的可靠介质上。恢复时需要确认覆盖并输入**该备份对应的主密码**；当前文件会先保留为带 before-restore 标记的文件。

保险库的上一次保存版本保存在 vault.nacre.previous。主文件损坏时，可在恢复对话框中选择该文件或手动备份。自动备份不能替代独立介质备份。

修改主密码后，旧备份仍需旧密码打开，请重新备份。升级标准版时保留原数据目录；升级便携版时保留原 Data 文件夹和 portable.flag，只替换程序文件。

## 从源码构建

需要 Windows 和 .NET 10 SDK。使用支持 .NET 10 的 Visual Studio 打开 Nacre.sln，或在 VS Code 中使用 C# 开发工具。

```powershell
dotnet restore Nacre.sln
dotnet build Nacre.sln -c Release
dotnet run --project src/Nacre/Nacre.csproj
dotnet test Nacre.sln -c Release
```

WPF 集成测试需要 Windows 桌面会话，会短暂打开测试窗口并使用虚构文本操作剪贴板。测试覆盖加解密、密码与篡改校验、存储与恢复、dotenv 解析及主要界面流程。

生成 Windows x64 自包含发布包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -Portable
```

产物位于 artifacts/releases，包含 ZIP 和 SHA-256 校验文件。更多开发与发布说明见 [开发文档](docs/DEVELOPMENT.md)。

## 安全说明

保险库对凭据、文本、分类、标签、备注和设置进行整体认证加密。解锁期间，明文必须存在于进程内存中；托管字符串、系统交换文件以及第三方剪贴板工具中的副本无法保证彻底擦除。

本项目尚未经过独立安全审计，不能防御已控制 Windows 会话的恶意软件、管理员、键盘记录器或屏幕录制。请配合系统账户保护、磁盘加密和可靠备份使用。完整格式和安全边界见 [SECURITY.md](SECURITY.md)。

当前验证环境为 Windows 11 x64，尚未完成 Windows 10 实机验证。发布程序未附带代码签名，Windows SmartScreen 可能提示未知发布者。

## 参与贡献

欢迎提交问题和改进建议。报告问题时请附上软件版本、Windows 版本和不含敏感信息的复现步骤；不要上传真实密钥、个人保险库或主密码。

项目结构：

- src/Nacre.Core：模型、认证加密、文件存储、备份恢复、dotenv 和分类标签。
- src/Nacre：WPF 界面、ViewModel、主题与 Windows 本地交互。
- tests：核心测试与 WPF 集成测试。
- scripts：发布、发布验证和图标生成脚本。
- licenses：随程序分发的第三方许可证。

## 许可证

采用 [MIT License](LICENSE)。第三方组件及许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
