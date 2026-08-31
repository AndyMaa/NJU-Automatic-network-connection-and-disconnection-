#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import json
import os
import queue
import subprocess
import threading
import time
import urllib.request
import urllib.error
import tkinter as tk
from tkinter import messagebox, scrolledtext


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(BASE_DIR, "config.json")

DEFAULTS = {
    "username": "",
    "password": "",
    "idleTimeoutSeconds": 300,
    "pollSeconds": 5,
    "infoUrl": "https://p.nju.edu.cn/api/portal/v1/getinfo",
    "loginUrl": "https://p.nju.edu.cn/api/portal/v1/login",
    "logoutUrl": "https://p.nju.edu.cn/api/portal/v1/logout",
}


def load_config():
    if not os.path.exists(CONFIG_PATH):
        cfg = dict(DEFAULTS)
        save_config(cfg)
        return cfg
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            cfg = json.load(f)
        for key, value in DEFAULTS.items():
            cfg.setdefault(key, value)
        return cfg
    except Exception:
        return dict(DEFAULTS)


def save_config(cfg):
    try:
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        return True
    except Exception:
        return False


def get_idle_seconds():
    try:
        output = subprocess.check_output(
            ["ioreg", "-c", "IOHIDSystem"], stderr=subprocess.DEVNULL
        )
        text = output.decode("utf-8", "ignore")
        for line in text.splitlines():
            if "HIDIdleTime" in line:
                parts = line.split("=")
                if len(parts) >= 2:
                    value = parts[-1].strip().split()[0]
                    return int(value) // 1000000000
    except Exception:
        pass
    return 0


def http_json(url, data=None, timeout=10):
    headers = {
        "Accept": "application/json",
        "Referer": "https://p.nju.edu.cn/",
        "User-Agent": "Mozilla/5.0 (NJU-Network-Helper)",
    }
    body = None
    method = "GET"
    if data is not None:
        method = "POST"
        body = json.dumps(data).encode("utf-8")
        headers["Content-Type"] = "application/json"

    request = urllib.request.Request(url, data=body, headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def login_request(cfg):
    """发送登录请求，返回解析后的 JSON（即使 HTTP 返回错误也尝试读取）。"""
    headers = {
        "Accept": "application/json",
        "Referer": "https://p.nju.edu.cn/",
        "User-Agent": "Mozilla/5.0 (NJU-Network-Helper)",
        "Content-Type": "application/json",
    }
    payload = {
        "username": cfg.get("username", ""),
        "password": cfg.get("password", ""),
    }
    body = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        cfg["loginUrl"], data=body, headers=headers, method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        try:
            return json.loads(exc.read().decode("utf-8"))
        except Exception:
            return None
    except Exception:
        return None


def get_status(cfg):
    try:
        root = http_json(cfg["infoUrl"])
        results = root.get("results") or {}
        total = int(results.get("total", 0))
        online = root.get("reply_code") == 0 and total > 0
        rows = results.get("rows") or []
        row = rows[0] if rows else {}
        return {
            "online": online,
            "fullname": str(row.get("fullname", "")),
            "username": str(row.get("username", "")),
            "balance": str(row.get("balance", "")),
            "service_name": str(row.get("service_name", "")),
            "error": "",
        }
    except Exception as exc:
        return {
            "online": False,
            "fullname": "",
            "username": "",
            "balance": "",
            "service_name": "",
            "error": str(exc),
        }


class App:
    PRIMARY = "#6a005f"
    PRIMARY_LIGHT = "#f3e5f1"
    BG = "#f5f7fa"
    CARD = "#ffffff"
    TEXT = "#32373c"
    SUCCESS = "#2e7d32"
    DANGER = "#c0392b"

    def __init__(self):
        self.root = tk.Tk()
        self.root.title("NJU-WLAN 自动连断网")
        self.root.geometry("660x800")
        self.root.configure(bg=self.BG)

        self.cfg = load_config()
        self.q = queue.Queue()
        self.busy = False
        self.auto_running = False
        self.auto_thread = None
        self.all_buttons = []
        self.dock_mode = False
        self.docked = False
        self.normal_geometry = None
        self.win_width = 0
        self.win_height = 0
        self.tab = None

        self.username_var = tk.StringVar(value=self.cfg.get("username", ""))
        self.password_var = tk.StringVar(value=self.cfg.get("password", ""))
        timeout_minutes = max(1, int(self.cfg.get("idleTimeoutSeconds", 300)) // 60)
        self.timeout_var = tk.StringVar(value=str(timeout_minutes))

        self.build_ui()
        self.update_auto_buttons()
        self.root.after(100, self.poll_queue)
        self.root.after(150, self.check_dock)
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)
        self.refresh_status()

    def build_ui(self):
        header = tk.Frame(self.root, bg=self.PRIMARY, height=64)
        header.pack(fill="x")
        header.pack_propagate(False)

        tk.Label(
            header,
            text="NJU-WLAN 网络助手",
            bg=self.PRIMARY,
            fg="white",
            font=("PingFang SC", 16, "bold"),
        ).pack(side="left", padx=20)

        tk.Label(
            header,
            text="空闲自动断网 · 操作自动联网",
            bg=self.PRIMARY,
            fg=self.PRIMARY_LIGHT,
            font=("PingFang SC", 10),
        ).pack(side="right", padx=20)

        self.build_status_card()
        self.build_settings_card()
        self.build_action_card()
        self.build_log_card()

    def build_status_card(self):
        card = tk.Frame(self.root, bg=self.CARD)
        card.pack(fill="x", padx=20, pady=(14, 6))

        tk.Label(
            card,
            text="当前状态",
            bg=self.CARD,
            fg=self.TEXT,
            font=("PingFang SC", 12, "bold"),
        ).grid(row=0, column=0, sticky="w", padx=16, pady=(12, 2))

        self.status_dot = tk.Label(
            card,
            text="●",
            bg=self.CARD,
            fg="#666666",
            font=("PingFang SC", 12),
        )
        self.status_dot.grid(row=0, column=1, sticky="w", padx=(8, 0), pady=(12, 2))

        self.status_label = tk.Label(
            card,
            text="检测中...",
            bg=self.CARD,
            fg="#666666",
            font=("PingFang SC", 12, "bold"),
        )
        self.status_label.grid(row=0, column=2, sticky="w", padx=(2, 8), pady=(12, 2))

        self.account_label = tk.Label(
            card,
            text="账号：",
            bg=self.CARD,
            fg=self.TEXT,
            font=("PingFang SC", 10),
            justify="left",
            anchor="w",
            wraplength=570,
        )
        self.account_label.grid(row=1, column=0, columnspan=3, sticky="w", padx=16, pady=(4, 0))

        self.idle_label = tk.Label(
            card,
            text="空闲时间：0 秒",
            bg=self.CARD,
            fg=self.TEXT,
            font=("PingFang SC", 10),
        )
        self.idle_label.grid(row=2, column=0, columnspan=3, sticky="w", padx=16, pady=(4, 12))

    def build_settings_card(self):
        card = tk.Frame(self.root, bg=self.CARD)
        card.pack(fill="x", padx=20, pady=6)

        tk.Label(
            card,
            text="账号与超时设置",
            bg=self.CARD,
            fg=self.TEXT,
            font=("PingFang SC", 12, "bold"),
        ).grid(row=0, column=0, columnspan=2, sticky="w", padx=16, pady=(12, 8))

        tk.Label(card, text="账号：", bg=self.CARD, fg=self.TEXT).grid(
            row=1, column=0, sticky="w", padx=16, pady=4
        )
        username_entry = tk.Entry(
            card,
            textvariable=self.username_var,
            font=("PingFang SC", 11),
            relief="solid",
            borderwidth=1,
        )
        username_entry.grid(row=1, column=1, sticky="we", padx=(4, 16), pady=4)

        tk.Label(card, text="密码：", bg=self.CARD, fg=self.TEXT).grid(
            row=2, column=0, sticky="w", padx=16, pady=4
        )
        password_entry = tk.Entry(
            card,
            textvariable=self.password_var,
            show="*",
            font=("PingFang SC", 11),
            relief="solid",
            borderwidth=1,
        )
        password_entry.grid(row=2, column=1, sticky="we", padx=(4, 16), pady=4)

        tk.Label(card, text="超时时间：", bg=self.CARD, fg=self.TEXT).grid(
            row=3, column=0, sticky="w", padx=16, pady=4
        )
        timeout_frame = tk.Frame(card, bg=self.CARD)
        timeout_frame.grid(row=3, column=1, sticky="w", padx=(4, 16), pady=4)
        tk.Entry(
            timeout_frame,
            textvariable=self.timeout_var,
            width=6,
            font=("PingFang SC", 11),
            relief="solid",
            borderwidth=1,
        ).pack(side="left")
        tk.Label(timeout_frame, text=" 分钟", bg=self.CARD, fg=self.TEXT).pack(side="left")

        save_frame = tk.Frame(card, bg=self.CARD)
        save_frame.grid(row=1, column=2, rowspan=3, sticky="n", padx=(10, 16), pady=(2, 10))
        self.make_button(save_frame, "保存设置", self.PRIMARY, "white", self.save_settings).pack(fill="x", pady=4)
        self.make_button(save_frame, "保存并登录", self.SUCCESS, "white", self.save_and_login).pack(fill="x", pady=4)

        card.columnconfigure(1, weight=1)

    def build_action_card(self):
        card = tk.Frame(self.root, bg=self.BG)
        card.pack(fill="x", padx=20, pady=12)

        buttons = [
            ("立即联网", self.PRIMARY, self.connect_action_start),
            ("立即断网", self.DANGER, self.disconnect_action_start),
            ("启动自动模式", self.PRIMARY, self.start_auto),
            ("停止自动模式", self.DANGER, self.stop_auto),
            ("查看状态", self.PRIMARY, self.status_action_start),
            ("贴边隐藏", self.PRIMARY, self.toggle_dock),
            ("退出程序", "#5a6672", self.on_close),
        ]

        for index, (text, color, command) in enumerate(buttons):
            row, col = divmod(index, 4)
            button = self.make_button(card, text, color, "white", command)
            button.grid(row=row, column=col, padx=6, pady=6, sticky="nsew")
            if text == "启动自动模式":
                self.btn_start_auto = button
            elif text == "停止自动模式":
                self.btn_stop_auto = button
            elif text == "贴边隐藏":
                self.btn_dock = button

        for col in range(4):
            card.columnconfigure(col, weight=1)

    def build_log_card(self):
        card = tk.Frame(self.root, bg=self.CARD)
        card.pack(fill="both", expand=True, padx=20, pady=(2, 20))

        tk.Label(
            card,
            text="运行日志",
            bg=self.CARD,
            fg=self.TEXT,
            font=("PingFang SC", 11, "bold"),
        ).pack(anchor="w", padx=12, pady=(8, 2))

        self.log_text = scrolledtext.ScrolledText(
            card,
            height=10,
            bg=self.CARD,
            fg=self.TEXT,
            font=("Menlo", 10),
            relief="flat",
            borderwidth=0,
        )
        self.log_text.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def make_button(self, parent, text, bg, fg, command):
        button = tk.Button(
            parent,
            text=text,
            bg=bg,
            fg=fg,
            activebackground=bg,
            activeforeground=fg,
            relief="flat",
            borderwidth=0,
            highlightthickness=0,
            padx=10,
            pady=6,
            font=("PingFang SC", 10, "bold"),
            command=command,
        )
        self.all_buttons.append(button)
        return button

    def poll_queue(self):
        try:
            while True:
                item = self.q.get_nowait()
                self.handle_queue_item(item)
        except queue.Empty:
            pass
        self.root.after(100, self.poll_queue)

    def handle_queue_item(self, item):
        kind, value = item
        if kind == "log":
            self.log_text.insert("end", value + "\n")
            self.log_text.see("end")
        elif kind == "status":
            self.update_status_ui(value)
        elif kind == "idle":
            self.idle_label.config(text=f"空闲时间：{value} 秒")
        elif kind == "busy":
            self.set_busy(value)
        elif kind == "prompt":
            messagebox.showwarning("提示", value)

    def queue_put(self, kind, value):
        self.q.put((kind, value))

    def log(self, message):
        self.queue_put("log", f"[{time.strftime('%H:%M:%S')}] {message}")

    def set_busy(self, busy):
        self.busy = busy
        if busy:
            for button in self.all_buttons:
                button.config(state="disabled")
        else:
            for button in self.all_buttons:
                button.config(state="normal")
            self.update_auto_buttons()

    def update_auto_buttons(self):
        if self.auto_running:
            self.btn_start_auto.config(state="disabled")
            self.btn_stop_auto.config(state="normal")
        else:
            self.btn_start_auto.config(state="normal")
            self.btn_stop_auto.config(state="disabled")

    def update_status_ui(self, status):
        if status.get("online"):
            color = self.SUCCESS
            text = "在线"
        elif status.get("error"):
            color = "#e67e22"
            text = "未知"
        else:
            color = self.DANGER
            text = "离线"
        self.status_label.config(text=text, fg=color)
        self.status_dot.config(fg=color)

        if status.get("online"):
            text = "当前在线账号：" + (status.get("username") or "未知")
            if status.get("fullname"):
                text += "    姓名：" + status.get("fullname")
            if status.get("service_name"):
                text += "    套餐：" + status.get("service_name")
            if status.get("balance"):
                text += "    余额：" + status.get("balance")
        else:
            text = "账号：" + self.cfg.get("username", "")
            if status.get("error"):
                text += "    " + status.get("error")

        self.account_label.config(text=text)
        self.idle_label.config(text=f"空闲时间：{get_idle_seconds()} 秒")

    def run_action(self, action, log_message):
        if self.busy:
            return
        self.set_busy(True)
        self.log(log_message)
        threading.Thread(target=self.action_wrapper, args=(action,), daemon=True).start()

    def action_wrapper(self, action):
        try:
            action()
        except Exception as exc:
            self.log("操作出错：" + str(exc))
        finally:
            self.queue_put("busy", False)

    def refresh_status(self):
        threading.Thread(target=self.status_worker, daemon=True).start()

    def status_worker(self):
        status = get_status(self.cfg)
        self.queue_put("status", status)
        self.queue_put("idle", get_idle_seconds())

    def connect_action_start(self):
        self.run_action(lambda: self.connect_action(False), "正在执行一键联网...")

    def disconnect_action_start(self):
        self.run_action(self.disconnect_action, "正在执行一键断网...")

    def status_action_start(self):
        self.run_action(self.status_action, "正在查询状态...")

    def connect_action(self, force_relogin=False):
        success, message = self.connect_with_result(force_relogin)
        if success:
            self.log("联网成功。")
        else:
            self.log("联网失败。")
            if message:
                self.queue_put("prompt", "联网失败：" + message)
        self.queue_put("status", get_status(self.cfg))

    def disconnect_action(self):
        if self.disconnect_core():
            self.log("断网成功。")
        else:
            self.log("断网失败，请稍后重试。")
        self.queue_put("status", get_status(self.cfg))

    def status_action(self):
        status = get_status(self.cfg)
        self.queue_put("status", status)
        if status.get("online"):
            self.log("当前在线。")
        elif status.get("error"):
            self.log("当前状态未知：" + status.get("error"))
        else:
            self.log("当前离线。")

    def save_settings_from_ui(self):
        username = self.username_var.get().strip()
        password = self.password_var.get()
        try:
            minutes = int(self.timeout_var.get().strip())
        except ValueError:
            minutes = 5
        minutes = max(1, min(600, minutes))
        self.timeout_var.set(str(minutes))
        self.cfg["username"] = username
        self.cfg["password"] = password
        self.cfg["idleTimeoutSeconds"] = minutes * 60
        return save_config(self.cfg)

    def save_settings(self):
        if not self.validate_settings():
            return
        if not self.save_settings_from_ui():
            self.show_save_error()
            return
        self.log("设置已保存。")
        self.refresh_status()

    def save_and_login(self):
        if not self.validate_settings():
            return
        if not self.save_settings_from_ui():
            self.show_save_error()
            return
        self.run_action(lambda: self.connect_action(True), "正在使用新账号登录...")

    def show_save_error(self):
        messagebox.showerror(
            "保存失败",
            "无法写入配置文件。\n\n请确认本程序所在文件夹有写入权限，"
            "或把程序放到自己的文稿、下载等可写目录。",
        )

    def validate_settings(self):
        if not self.username_var.get().strip() or not self.password_var.get():
            messagebox.showwarning("提示", "请输入账号和密码。")
            return False
        return True

    def connect_with_result(self, force_relogin=False):
        """返回 (success, message)。message 非空表示账密错误等明确失败。"""
        status = get_status(self.cfg)
        if status.get("online") and not force_relogin:
            return True, ""

        if status.get("online") and force_relogin:
            try:
                http_json(self.cfg["logoutUrl"], data={})
            except Exception:
                pass
            for _ in range(5):
                time.sleep(1)
                if not get_status(self.cfg).get("online"):
                    break

        result = login_request(self.cfg)
        if result is not None:
            code = result.get("reply_code")
            if code is not None and int(code) > 0:
                message = str(result.get("reply_msg", "") or "")
                if not message:
                    message = "账号或密码错误，请检查后重试。"
                return False, message

        for _ in range(8):
            time.sleep(2)
            if get_status(self.cfg).get("online"):
                return True, ""
        return False, ""

    def connect_core(self):
        success, _ = self.connect_with_result()
        return success

    def disconnect_core(self):
        status = get_status(self.cfg)
        if not status.get("online"):
            return True

        try:
            http_json(self.cfg["logoutUrl"], data={})
        except Exception:
            return False

        for _ in range(8):
            time.sleep(2)
            if not get_status(self.cfg).get("online"):
                return True
        return False

    def start_auto(self):
        if self.auto_running:
            self.log("自动模式已经在运行。")
            return
        if not self.cfg.get("username") or not self.cfg.get("password"):
            messagebox.showwarning("提示", "请先填写并保存账号密码。")
            return
        self.auto_running = True
        self.update_auto_buttons()
        self.log("自动模式已启动。")
        self.auto_thread = threading.Thread(target=self.auto_loop, daemon=True)
        self.auto_thread.start()

    def stop_auto(self):
        if not self.auto_running:
            self.log("自动模式未运行。")
            return
        self.auto_running = False
        self.update_auto_buttons()
        self.log("正在停止自动模式...")

    def auto_loop(self):
        next_connect_at = 0
        next_disconnect_at = 0

        while self.auto_running:
            idle_seconds = get_idle_seconds()
            status = get_status(self.cfg)
            timeout_seconds = int(self.cfg.get("idleTimeoutSeconds", 300))
            should_be_online = idle_seconds < timeout_seconds

            if should_be_online and not status.get("online"):
                now = time.time()
                if now >= next_connect_at:
                    self.log(f"检测到操作（空闲 {idle_seconds} 秒），正在联网...")
                    success, message = self.connect_with_result()
                    if success:
                        next_connect_at = 0
                    elif message:
                        next_connect_at = float("inf")
                        self.log(f"自动联网失败：{message}（已停止重试，请检查账号密码）。")
                    else:
                        next_connect_at = now + 20
                        self.log("联网失败，稍后重试。")
            elif not should_be_online and status.get("online"):
                now = time.time()
                if now >= next_disconnect_at:
                    self.log(f"已空闲 {idle_seconds} 秒，正在断网...")
                    if self.disconnect_core():
                        next_disconnect_at = 0
                    else:
                        next_disconnect_at = now + 30
                        self.log("断网失败，稍后重试。")

            self.queue_put("status", status)
            self.queue_put("idle", idle_seconds)

            poll_seconds = max(1, min(int(self.cfg.get("pollSeconds", 5)), 5))
            for _ in range(poll_seconds):
                if not self.auto_running:
                    break
                time.sleep(1)

        self.log("自动模式线程已退出。")

    def toggle_dock(self):
        self.dock_mode = not self.dock_mode
        if self.dock_mode:
            self.btn_dock.config(text="取消贴边")
            self.normal_geometry = self.root.geometry()
            self.win_width = self.root.winfo_width()
            self.win_height = self.root.winfo_height()
            self.dock_to_edge()
            self.log("已贴边隐藏：把鼠标移到屏幕右边缘的小标签即可弹出。")
        else:
            self.btn_dock.config(text="贴边隐藏")
            self.restore_normal()
            self.log("已退出贴边隐藏。")

    def ensure_tab(self):
        if self.tab is not None:
            return
        self.tab = tk.Toplevel(self.root)
        self.tab.overrideredirect(True)
        self.tab.configure(bg=self.PRIMARY)
        self.tab.withdraw()

    def dock_to_edge(self):
        if self.docked:
            return
        self.ensure_tab()
        screen_w = self.root.winfo_screenwidth()
        screen_h = self.root.winfo_screenheight()
        tab_w, tab_h = 12, 48
        x = screen_w - tab_w
        y = max(28, int(screen_h * 0.38))
        self.tab.geometry(f"{tab_w}x{tab_h}+{x}+{y}")
        self.tab.deiconify()
        self.tab.lift()
        self.root.withdraw()
        self.docked = True

    def slide_out(self):
        if not self.docked:
            return
        if self.tab is not None:
            self.tab.withdraw()
        screen_w = self.root.winfo_screenwidth()
        screen_h = self.root.winfo_screenheight()
        w = self.win_width or 700
        h = self.win_height or 800
        x = screen_w - w
        y = max(28, (screen_h - h) // 2)
        self.root.geometry(f"+{x}+{y}")
        self.root.deiconify()
        self.root.lift()
        self.docked = False

    def restore_normal(self):
        self.docked = False
        if self.tab is not None:
            self.tab.withdraw()
        if self.normal_geometry:
            self.root.geometry(self.normal_geometry)
        self.root.deiconify()
        self.root.lift()

    def check_dock(self):
        if self.dock_mode:
            px = self.root.winfo_pointerx()
            py = self.root.winfo_pointery()
            if self.docked:
                if self.tab is not None and self.tab.winfo_viewable():
                    tx = self.tab.winfo_rootx()
                    ty = self.tab.winfo_rooty()
                    tw = self.tab.winfo_width()
                    th = self.tab.winfo_height()
                    if tx <= px < tx + tw and ty <= py < ty + th:
                        self.slide_out()
            else:
                rx = self.root.winfo_rootx()
                ry = self.root.winfo_rooty()
                w = self.root.winfo_width()
                h = self.root.winfo_height()
                if not (rx <= px < rx + w and ry <= py < ry + h):
                    self.dock_to_edge()
        self.root.after(150, self.check_dock)

    def on_close(self):
        self.auto_running = False
        if self.tab is not None:
            try:
                self.tab.destroy()
            except Exception:
                pass
        self.root.destroy()


if __name__ == "__main__":
    App().root.mainloop()
