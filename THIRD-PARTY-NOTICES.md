# 第三方组件

应用自身采用 MIT License（见 LICENSE）。

| 组件 | 版本 | 许可证 | 来源 |
| --- | --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT | https://github.com/kmaragon/Konscious.Security.Cryptography |
| Konscious.Security.Cryptography.Blake2（传递依赖） | 1.1.1 | MIT | 同上 |
| Microsoft .NET / Windows Desktop Runtime（自包含分发） | 随当前 .NET 10 SDK 的补丁版本 | MIT 及其第三方声明 | https://github.com/dotnet/runtime 、https://github.com/dotnet/wpf |

Konscious 的版权与完整 MIT 条文见 licenses/Konscious-MIT.txt。自包含包附带 .NET 与 WPF 的 LICENSE 和 THIRD-PARTY-NOTICES，见 licenses。

开发/测试还使用 Microsoft.NET.Test.Sdk、xUnit、xunit.runner.visualstudio 和 coverlet.collector；仅测试项目引用，不随应用分发。精确依赖和内容哈希由各项目 packages.lock.json 记录。

RFC 9106 的公开 Argon2id 测试向量仅用于密码学验证，其来源与适用条款见 https://www.rfc-editor.org/rfc/rfc9106.html 。
