@echo off
chcp 65001 >nul
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
  echo [错误] 未找到 .NET Framework 4.0 编译器：%FW%\csc.exe
  echo 请在已安装 .NET Framework 的 Windows 上运行本脚本。
  pause
  exit /b 1
)
"%FW%\csc.exe" /nologo /optimize+ /codepage:65001 /target:winexe /out:"NJU-WLAN网络助手.exe" /win32icon:"app.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%\System.Web.Extensions.dll" "NJU_Network_Assistant.cs"
if %errorlevel% neq 0 (
  echo [错误] 编译失败，请查看上面的错误信息。
  pause
  exit /b 1
)
echo [完成] 已生成 NJU-WLAN网络助手.exe
endlocal
