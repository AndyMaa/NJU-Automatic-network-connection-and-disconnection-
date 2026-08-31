#!/bin/bash
cd "$(dirname "$0")"

if ! command -v python3 >/dev/null 2>&1; then
  osascript -e 'display dialog "没有检测到 Python 3。\n\n请先安装 Python 3（可从 python.org 下载），然后再打开这个程序。" buttons {"好"} default button "好" with icon caution'
  exit 1
fi

if ! python3 -c "import tkinter" >/dev/null 2>&1; then
  osascript -e 'display dialog "当前 Python 缺少 tkinter 组件。\n\n如果使用 Homebrew 安装的 Python，请先执行：brew install python-tk" buttons {"好"} default button "好" with icon caution'
  exit 1
fi

exec python3 nju_wlan_helper.py
