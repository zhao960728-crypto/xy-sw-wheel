# XY轮盘源码构建

环境：Windows x64、.NET SDK 8、.NET Framework 4.8，以及已安装 SOLIDWORKS 的四个互操作程序集。首次构建需要访问 NuGet 恢复项目原有依赖。

从解压后的源码根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-source.ps1 -RunTests
```

脚本只读检测 64/32 位注册表中的 SOLIDWORKS 安装目录，不安装或注册插件。多版本或非标准安装可明确指定目录：

```powershell
.\tools\build-source.ps1 -SolidWorksInteropDirectory 'D:\SOLIDWORKS' -RunTests
```

目录内须有 `SolidWorks.Interop.sldworks.dll`、`SolidWorks.Interop.swconst.dll`、`SolidWorks.Interop.swcommands.dll` 和 `SolidWorks.Interop.swpublished.dll`。也可设置进程环境变量 `SOLIDWORKS_INTEROP_DIR`，或在 `dotnet build/test` 时传入 `-p:SolidWorksInteropDirectory="目录"`。项目文件中的引用统一使用该属性；旧开发环境路径仅为存在时的兼容候选。

主 DLL 输出到 `src/XY.SolidWorksPlugin/bin/x64/Release/net48/`，测试结果默认写入 `artifacts/test-results/`。单元测试会短暂显示独立轮盘/设置测试窗口，不打开或修改用户模型。真实 SOLIDWORKS 宿主验证是独立步骤，本次源码清理未执行宿主验收。历史混合功能宿主测试工具已从源码交付移除。

重新打包：

```powershell
.\tools\package-source.ps1
```

该脚本收集源码、资源、测试及构建脚本，创建文件清单，逐文件核对 ZIP 内的长度和 SHA256，再交付到 `dist/`。同版本已存在时须用 `-Force`，原包在新包完整校验前保留。包外同时生成 SHA256 和校验 JSON；包内不带编译文件、SOLIDWORKS 程序集、用户数据、日志或历史产物。

安装入口是源代码模板。若需安装，应先构建、准备匹配版本的 `插件文件` 目录并完成宿主验收。本源码包没有自动安装或修改当前加载项。
