@echo off
title SandcastleMR Auto Push
color 0E

echo ==========================================
echo   SandcastleMR - 一键提交并推送
echo ==========================================
echo.

cd /d D:\unityproject\SandcastleMR

if %errorlevel% neq 0 (
    echo [错误] 找不到目录 D:\unityproject\SandcastleMR
    pause
    exit /b 1
)

echo [信息] 当前目录: %cd%
echo.

:: 添加所有改动
git add .

:: 提交（自动生成时间戳消息）
set msg=Update from Unity %date% %time:~0,8%
git commit -m "%msg%"

if %errorlevel% neq 0 (
    echo.
    echo [信息] 没有新改动需要提交。
    pause
    exit /b 0
)

:: 推送
echo.
echo [信息] 正在推送到 GitHub...
git push origin main

if %errorlevel% equ 0 (
    echo.
    echo [成功] 已推送! AI 那边可以看到你的改动了。
) else (
    echo.
    echo [警告] 推送失败，请检查网络。
)

echo.
pause
