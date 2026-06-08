# 更新日志

## 1.2.6 (2026-06-08)

- 🎨[优化]-重新收敛根目录 `logo.svg`、`logo.png`、`logo.ico`，改为更简洁的 Socket 总线与中心端口图形，提升任务栏、浏览器标签和 NuGet 小图标辨识度。
- 🔨[优化]-调整 `pack.bat` 的 NuGet 包输出目录为 `artifacts\packages`，与常见 .NET 开源仓库产物目录保持一致。

## 1.2.5 (2026-06-08)

- 🎨[优化]-重新设计根目录 `logo.svg`、`logo.png`、`logo.ico`，以简洁的 C# / TCP Socket 连接和中心事件总线节点表达集成事件总线定位，并保持小尺寸图标可辨识。

## 1.2.4 (2026-06-08)

- 🔨[优化]-新增根目录 `pack.bat` 一键打包脚本，统一还原、Release 构建与 NuGet 包输出流程，并自动兼容本机兄弟仓库的 `CodeWF.NetWeaver` 本地包源。

## 1.2.3 (2026-06-08)

- 🔨[优化]-补齐根目录 logo.svg、logo.png、logo.ico 三件套，子工程通过 MSBuild Link 引用根 logo，避免维护多份图标副本。
- 🔨[优化]-统一目标框架：NuGet 包项目支持 `net8.0;net10.0`，Demo、App、测试与内部应用项目升级到 `net11.0` / `net11.0-windows`。
- 🔨[优化]-将 `CodeWF.NetWrapper` 依赖更新到 `2.1.2.3`，使 Socket 包的 `net8.0` 资产可以正常还原。
- 🔨[优化]-保留运行时帮助、Markdown 示例、内置备忘录和业务设计文档，仅收敛仓库级重复文档入口。

## 1.2.2 (2026-06-08)

- 统一版本号维护入口，只在仓库根目录 `Directory.Build.props` 中定义 `<Version>`。
- 清理英文/双语文档入口，后续仅维护简体中文文档。
- 完善 NuGet 发布配置，补充 Source Link、符号包和标签格式规范。
## 2026-06-08 仓库规范整理

- 统一文档维护入口：每个仓库只保留根目录 `README.md` 和根目录 `UpdateLog.md`，清理重复日志、英文文档和语言切换入口。
- 统一版本维护入口：包版本只在仓库根目录 `Directory.Build.props` 的 `<Version>` 节点维护，移除散落的程序集版本配置。
- 不再维护 `global.json`，SDK 选择交给本机或 CI 环境；NuGet 包和应用的目标框架在项目文件中明确声明。
- 统一 NuGet 包文档入口：包 README 统一引用仓库根 `README.md`，更新日志统一引用仓库根 `UpdateLog.md`。
