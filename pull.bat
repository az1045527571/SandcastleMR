@echo off
title SandcastleMR Auto Pull
color 0A

echo ==========================================
echo   SandcastleMR - 一键拉取最新代码
echo ==========================================
echo.

cd /d D:\unityproject\SandcastleMR

if %errorlevel% neq 0 (
    echo [错误] 找不到目录 D:\unityproject\SandcastleMR
    pause
    exit /b 1
)

echo [信息] 当前目录: %cd%
echo [信息] 正在拉取最新代码...
echo.

git pull origin main

if %errorlevel% equ 0 (
    echo.
    echo [成功] 代码已更新! 回到 Unity 等待自动导入即可。
) else (
    echo.
    echo [警告] 拉取失败，请检查网络或 Git 配置。
)

echo.
pause
