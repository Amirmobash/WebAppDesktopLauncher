# WebAppDesktopLauncher


## 1️⃣ README.md


````markdown
# WebApp Desktop Launcher

A lightweight **Windows desktop client** for any existing web application.

Instead of opening a browser and manually navigating to a URL, end users can simply run a single `.exe` file.  
The client shows a modern **German loading screen**, waits until the backend (e.g. Render, custom server, etc.) is reachable, and then loads the full web UI inside a native desktop window.

> Author: **Amir Mobasheraghdam**

---

## ✨ Features

- 🖥️ **Native-like desktop app** for Windows (no browser chrome, no address bar)
- 🌐 **Connects to any existing web backend** (configurable URL)
- ⏳ **Custom German loading screen** while the server starts (supports slow/cold starts such as Render)
- 🚨 **Friendly error screen** if the server is not reachable within a configurable timeout
- 📦 Single-file `.exe` build using **PyInstaller**
- ✅ No Python required on the end-user’s machine – everything is bundled

---

## 🧱 Architecture Overview

This project is only the **desktop client**.

The backend (web app) is assumed to already exist and be deployed somewhere (e.g. Render, your own server, etc.) and be accessible via a normal HTTP/HTTPS URL.

The desktop client:

- Is written in **Python**
- Uses **pywebview** to embed a minimal browser into a native window
- Uses **requests** to poll the backend until it is available
- Shows:
  - A modern German loading screen while waiting
  - The real web UI once the backend responds
  - A German error screen if the backend never becomes reachable

---

## 🛠 Tech Stack

- **Language:** Python (recommended: 3.11, 64-bit)
- **UI Container:** [pywebview](https://pywebview.flowrl.com/)
- **HTTP Client:** `requests`
- **Bundler:** [PyInstaller](https://pyinstaller.org/)
- **OS Target:** Windows 64-bit

---

## 📂 Project Structure

Example layout:

```text
WebAppDesktopLauncher/
├─ main.py             # Main entry point: loading screen + webview logic
├─ README.md           # Project documentation
├─ requirements.txt    # Python dependencies
└─ .gitignore          # Git ignore rules
````

`requirements.txt`:

```text
pywebview
pyinstaller
requests
```

---

## ⚙️ Configuration

In `main.py`, configure the URL of your backend:

```python
APP_URL = "https://your-backend-or-render-app-url.example.com"
```

You can point this to:

* A Render deployment
* Any HTTPS web application
* A local server (e.g. `http://127.0.0.1:5000`) during development

There is also a timeout for how long the client will wait for the backend to respond before showing an error page:

```python
MAX_WAIT_SECONDS = 300  # e.g. 5 minutes
```

---

## 🚀 Getting Started (Development)

### 1. Clone the repository

```bash
git clone https://github.com/<your-username>/WebAppDesktopLauncher.git
cd WebAppDesktopLauncher
```

### 2. Install Python

Install **Python 3.11 (64-bit)** from the official Python website.

You can list installed versions on Windows:

```bash
py -0p
```

### 3. Create and activate a virtual environment (recommended)

```bash
py -3.11 -m venv .venv
.\.venv\Scripts\activate
```

You should now see something like:

```text
(.venv) C:\path\to\WebAppDesktopLauncher>
```

### 4. Install dependencies

Using `requirements.txt`:

```bash
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

(or manually)

```bash
python -m pip install pywebview pyinstaller requests
```

### 5. Run the app in development

```bash
python main.py
```

What happens:

1. A native window opens.
2. A **German loading screen** is shown.
3. In the background, the client periodically sends `GET` requests to `APP_URL`.
4. Once the backend responds (with any non-5xx HTTP status), the real web application is loaded inside the window.
5. If the backend does not become reachable within `MAX_WAIT_SECONDS`, an error page is shown instead of crashing.

---

## 🧩 Main Logic Overview (`main.py`)

The main concepts:

1. **LOADING_HTML**
   Embedded German loading page shown immediately at startup.

2. **ERROR_HTML**
   Embedded German error page shown if no connection can be established within the timeout.

3. **wait_and_load(window)**

   * Runs in a background thread.
   * Repeatedly calls `requests.get(APP_URL, timeout=5)`.
   * If the status code is not a 5xx error, it assumes the backend is ready.
   * Then it calls `window.load_url(APP_URL)` to load the real app.
   * If `MAX_WAIT_SECONDS` is exceeded, it calls `window.load_html(ERROR_HTML)`.

4. **main()**

   * Creates a pywebview window with the loading HTML:

     ```python
     window = webview.create_window(
         title=WINDOW_TITLE,
         html=LOADING_HTML,
         width=WINDOW_WIDTH,
         height=WINDOW_HEIGHT,
         resizable=True,
         fullscreen=False,
         min_size=(800, 600)
     )
     ```
   * Starts the `wait_and_load` thread.
   * Starts the pywebview event loop.

---

## 📦 Building the Windows EXE

Once everything works, you can build a portable `.exe` for distribution.

From the project root (with the virtual environment activated):

```bash
python -m PyInstaller --noconsole --onefile --name WebAppLauncher main.py
```

* `--onefile` → bundle everything into a single executable
* `--noconsole` → no console window
* `--name WebAppLauncher` → output name will be `WebAppLauncher.exe`

After a successful build, the executable will be in:

```text
dist/WebAppLauncher.exe
```

You can copy this file to any **64-bit Windows** machine and run it without installing Python.

---

## 💡 Usage for End Users

1. Download `WebAppLauncher.exe`.
2. Double-click to run.
3. The app will:

   * Show a German loading screen indicating that the application is starting.
   * Automatically connect to your configured backend URL.
   * Display the full web UI once it is ready.

If the backend or the internet connection is down, a German error page is displayed with hints for the user.

On first run, Windows SmartScreen may warn about an unknown publisher. Users can click
**“More info” → “Run anyway”** to proceed.

---

## 🔧 Troubleshooting

### Only the error page is shown

Check:

1. Is `APP_URL` correct?
2. Can you open `APP_URL` in a normal browser from the same machine?
3. Is your backend or Render service online?
4. Increase `MAX_WAIT_SECONDS` for slow cold starts.

### `ModuleNotFoundError: No module named 'webview'`

Make sure dependencies are installed inside the active virtual environment:

```bash
.\.venv\Scripts\activate
python -m pip install pywebview requests pyinstaller
```

---

## 📜 License

Example MIT license:

```text
MIT License

Copyright (c) 2025 Amir Mobasheraghdam

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction...
```

(You can replace this with any license you prefer.)

---

## 👤 Author

**Amir Mobasheraghdam**

* Design & implementation of the desktop client
* Integration concept for existing web backends (e.g. Render, custom servers)
* Custom loading & error UX for online applications

````

---

## 2️⃣ main.py (نسخه‌ی جدید بدون اسم قبلی)

فایل `main.py` رو این‌طوری بذار:

```python
import threading
import time

import requests
import webview

# =========================
# Core configuration
# =========================

# URL of the backend web application (Render, custom server, etc.)
APP_URL = "https://your-backend-or-render-app-url.example.com"

WINDOW_TITLE = "WebApp Desktop Client"
WINDOW_WIDTH = 1200
WINDOW_HEIGHT = 800
MAX_WAIT_SECONDS = 300  # max time to wait for backend (e.g. 5 minutes)


# =========================
# German loading screen HTML
# =========================

LOADING_HTML = r"""
<!DOCTYPE html>
<html lang="de">
<head>
    <meta charset="UTF-8">
    <title>Anwendung wird gestartet…</title>
    <style>
        html, body {
            margin: 0;
            padding: 0;
            height: 100%;
            font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            background: radial-gradient(circle at top, #1f2933 0, #020617 60%);
            color: #e5e7eb;
            overflow: hidden;
        }

        .center {
            height: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
        }

        .card {
            background: rgba(15, 23, 42, 0.92);
            border-radius: 20px;
            padding: 32px 40px;
            box-shadow:
                0 20px 40px rgba(0, 0, 0, 0.65),
                0 0 0 1px rgba(148, 163, 184, 0.2);
            max-width: 480px;
            width: 100%;
            position: relative;
            overflow: hidden;
        }

        .card::before {
            content: "";
            position: absolute;
            inset: -80px;
            background:
                radial-gradient(circle at top left, rgba(56, 189, 248, 0.18), transparent 55%),
                radial-gradient(circle at bottom right, rgba(129, 140, 248, 0.18), transparent 55%);
            opacity: 0.8;
            z-index: -1;
        }

        .logo-circle {
            width: 48px;
            height: 48px;
            border-radius: 999px;
            border: 2px solid rgba(148, 163, 184, 0.6);
            display: inline-flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 16px;
            position: relative;
        }

        .logo-circle::after {
            content: "";
            position: absolute;
            inset: -6px;
            border-radius: inherit;
            border: 1px solid rgba(56, 189, 248, 0.4);
            opacity: 0.7;
        }

        .logo-circle span {
            font-weight: 700;
            font-size: 18px;
            letter-spacing: 0.08em;
            text-transform: uppercase;
            color: #e5e7eb;
        }

        h1 {
            margin: 0 0 8px 0;
            font-size: 22px;
            letter-spacing: 0.03em;
        }

        .subtitle {
            margin: 0 0 24px 0;
            font-size: 14px;
            color: #9ca3af;
        }

        .loader {
            width: 52px;
            height: 52px;
            border-radius: 999px;
            border: 3px solid rgba(148, 163, 184, 0.35);
            border-top-color: #38bdf8;
            animation: spin 0.9s linear infinite;
            margin-bottom: 12px;
        }

        @keyframes spin {
            to { transform: rotate(360deg); }
        }

        .status {
            font-size: 14px;
            color: #e5e7eb;
            margin-bottom: 4px;
        }

        .hint {
            font-size: 12px;
            color: #9ca3af;
        }

        .steps {
            margin-top: 18px;
            font-size: 12px;
            color: #9ca3af;
            list-style: none;
            padding: 0;
        }

        .steps li {
            display: flex;
            align-items: center;
            margin-bottom: 6px;
        }

        .steps li span {
            width: 6px;
            height: 6px;
            border-radius: 999px;
            background: rgba(148, 163, 184, 0.7);
            margin-right: 8px;
        }

        .footer {
            margin-top: 18px;
            font-size: 11px;
            color: #6b7280;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .pill {
            border-radius: 999px;
            border: 1px solid rgba(148, 163, 184, 0.7);
            padding: 3px 10px;
            font-size: 10px;
            text-transform: uppercase;
            letter-spacing: 0.08em;
        }
    </style>
</head>
<body>
<div class="center">
    <div class="card">
        <div class="logo-circle">
            <span>WA</span>
        </div>
        <h1>WebApp Desktop Client</h1>
        <p class="subtitle">Die Anwendung wird vorbereitet. Einen kleinen Moment bitte.</p>

        <div class="loader"></div>
        <div class="status">Verbindung zum Server wird aufgebaut…</div>
        <div class="hint">Bitte schließen Sie dieses Fenster nicht.</div>

        <ul class="steps">
            <li><span></span>Backend wird gestartet</li>
            <li><span></span>Oberfläche wird geladen</li>
            <li><span></span>Funktionen werden initialisiert</li>
        </ul>

        <div class="footer">
            <div class="pill">Initialisierung</div>
            <div>v1.0 · Online-Client</div>
        </div>
    </div>
</div>
</body>
</html>
"""


# =========================
# German error page HTML
# =========================

ERROR_HTML = r"""
<!DOCTYPE html>
<html lang="de">
<head>
    <meta charset="UTF-8">
    <title>Keine Verbindung</title>
    <style>
        html, body {
            margin: 0;
            padding: 0;
            height: 100%;
            font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            background: #020617;
            color: #e5e7eb;
        }
        .center {
            height: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .box {
            background: #020617;
            border-radius: 18px;
            padding: 28px 32px;
            border: 1px solid rgba(248, 113, 113, 0.45);
            box-shadow: 0 18px 35px rgba(0, 0, 0, 0.8);
            max-width: 420px;
        }
        h1 {
            margin: 0 0 10px 0;
            font-size: 20px;
        }
        p {
            margin: 0 0 8px 0;
            font-size: 14px;
            color: #d1d5db;
        }
        ul {
            margin: 10px 0 0 18px;
            font-size: 13px;
            color: #9ca3af;
        }
    </style>
</head>
<body>
<div class="center">
    <div class="box">
        <h1>Keine Verbindung zum Server</h1>
        <p>Die Anwendung konnte innerhalb der erwarteten Zeit keine Verbindung herstellen.</p>
        <p>Bitte prüfen Sie:</p>
        <ul>
            <li>Ihre Internetverbindung</li>
            <li>Ob der Server-Dienst online ist</li>
        </ul>
        <p>Starten Sie das Programm anschließend erneut.</p>
    </div>
</div>
</body>
</html>
"""


# =========================
# Backend waiting logic
# =========================

def wait_and_load(window):
    """Run in background: waits for backend to become available, then loads the real URL."""
    start = time.time()

    while True:
        try:
            resp = requests.get(APP_URL, timeout=5)
            # Any non-5xx status code is treated as "backend is up"
            if resp.status_code < 500:
                break
        except Exception:
            # Ignore errors (backend not ready yet) and retry
            pass

        # If timeout exceeded, show error page and stop
        if time.time() - start > MAX_WAIT_SECONDS:
            window.load_html(ERROR_HTML)
            return

        # Wait a few seconds before trying again
        time.sleep(4)

    # When ready, load the actual web application
    window.load_url(APP_URL)


def main():
    # Start with the loading HTML, so the user sees an immediate UI
    window = webview.create_window(
        title=WINDOW_TITLE,
        html=LOADING_HTML,
        width=WINDOW_WIDTH,
        height=WINDOW_HEIGHT,
        resizable=True,
        fullscreen=False,
        min_size=(800, 600)
    )

    # Background thread to wait for backend and then load it
    t = threading.Thread(target=wait_and_load, args=(window,), daemon=True)
    t.start()

    webview.start()


if __name__ == "__main__":
    main()
````

فقط **یادت نره `APP_URL` رو با آدرس واقعی backend خودت عوض کنی** (Render یا هرچی).

---

## 3️⃣ requirements.txt

یه فایل `requirements.txt` بساز:

```text
pywebview
pyinstaller
requests
```

---

## 4️⃣ .gitignore

برای تمیز بودن repo، `.gitignore` این‌طوری خوبه:

```gitignore
# Python venv
.venv/
env/
venv/

# PyInstaller output
build/
dist/
*.spec

# General Python
__pycache__/
*.py[cod]
*.pyo

# OS junk
.DS_Store
Thumbs.db
