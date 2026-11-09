# XY 轮盘 · SOLIDWORKS

当前源码版本 **1.0.1327.326**。包含可配置的轮盘、属性水印、工序识别与编辑、一键爆炸。轮盘支持不同文档场景、组合录制、快捷键、用户宏、原生命令及第三方命令绑定。

## 从源码构建

准备 Windows x64、.NET 8 SDK、.NET Framework 4.8，以及本机 SOLIDWORKS 安装所带的四个互操作程序集。在源码根目录运行以下命令。

    powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-source.ps1 -RunTests

多版本或自定义安装目录可通过 **-SolidWorksInteropDirectory** 指定。DLL 输出路径、安装模板用法和依赖列表见 [构建说明](BUILD_SOURCE.md)。

[1.0.1327.326 源码发行](https://github.com/zhao960728-crypto/xy-sw-wheel/releases/tag/v1.0.1327.326) 已通过 1,440 项自动测试。该版本尚待 SOLIDWORKS 宿主验收。功能范围及旧配置的处理见 [版本说明](docs/source-cleanup-1.0.1327.326.md)。

## 许可证

XY 项目原创部分采用 [MIT](LICENSE)，第三方内容保留原有权利与适用条款。SOLIDWORKS 互操作程序集由本机安装提供，按其原许可使用。

AutoCAD 和中望 CAD 版另见 [小阳 CAD 轮盘](https://github.com/zhao960728-crypto/xy-cad-wheel)。
