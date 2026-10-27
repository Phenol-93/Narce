# 开发与发布

## 环境

使用 Windows x64 和 .NET 10 SDK。global.json 允许同一 .NET 10 版本系列中的 SDK 特性版本前滚；依赖及内容哈希记录在各项目的 packages.lock.json 中。

Nacre 使用原生 WPF 和 MVVM。Core 不依赖 WPF；运行时的第三方密码学依赖为 Konscious.Security.Cryptography.Argon2 及其 Blake2 依赖。

## 验证

```powershell
dotnet build Nacre.sln -c Release
dotnet test Nacre.sln -c Release
dotnet list Nacre.sln package --vulnerable --include-transitive
```

核心测试使用虚构凭据和 RFC 9106 的公开 Argon2id 测试向量，覆盖认证加密、错误密码、篡改检测、读写、备份恢复、主密码修改、密码长度边界和 dotenv 解析。

桌面测试需要可用的 Windows 桌面会话，会创建临时测试保险库，短暂打开窗口并操作剪贴板。截图输出到 artifacts/UiScreenshots；请勿在真实个人保险库上执行测试自动化。

## 发布

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -Portable
```

脚本读取 Directory.Build.props 中的版本，输出到 artifacts/releases，分别生成标准和便携 ZIP 及 .sha256 校验文件。输出目录已存在时拒绝覆盖，避免误处理用户数据。

两种包包含相同的 Nacre.exe；便携包额外包含 portable.flag。面向用户分发 ZIP 与校验文件即可，生成的程序、测试产物和个人数据不应加入源码仓库。

发布配置采用 win-x64、自包含单文件，禁用裁剪。等价的基本命令：

```powershell
dotnet publish src/Nacre/Nacre.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=WinX64 -o artifacts/manual-publish
```

单文件发布会按 .NET 的机制提取原生运行库到运行时缓存；保险库明文不会写入该缓存。程序不要求管理员权限。

## 验证发布 EXE

将下面的路径替换成实际发布版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-release.ps1 -ExePath artifacts/releases/Nacre-1.0.2-win-x64/Nacre.exe
```

脚本在 artifacts/ReleaseSmoke 的独立目录中使用虚构的六字符主密码，验证首次创建、便携位置、保存、退出、重启解锁、复制和锁定清理。DOTNET_ROOT 指向不存在的路径，用于检查自包含启动；不会操作标准模式的个人保险库。

图标为本项目原创，可用 scripts/create-icon.ps1 重新生成。更新 SDK 或运行时依赖时，应一并检查 licenses 中的第三方许可证。

## 修改约定

项目约定见源码根目录的 AGENTS.md，安全格式与边界见 [SECURITY.md](../SECURITY.md)。改动应包含必要的验证，避免提交保险库、真实 .env、密钥、转储、个人路径或包含敏感数据的截图。
