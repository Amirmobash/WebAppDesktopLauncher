using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WebAppDesktopLauncher
{
    public partial class MainWindow : Window
    {
        private sealed class LauncherConfig
        {
            public string AppUrl { get; set; } = "https://example.com";
            public int MaxWaitSeconds { get; set; } = 300;
            public int PollSeconds { get; set; } = 4;
            public int RequestTimeoutSeconds { get; set; } = 5;

            public string WindowTitle { get; set; } = "WebApp Desktop Client";
            public int Width { get; set; } = 1200;
            public int Height { get; set; } = 800;
            public int MinWidth { get; set; } = 800;
            public int MinHeight { get; set; } = 600;

            public bool DisableDevTools { get; set; } = true;
            public bool DisableContextMenus { get; set; } = true;
            public bool DisableStatusBar { get; set; } = true;

            // Optional: HTTP Basic Auth (z. B. Render Passwortschutz)
            public string BasicAuthUser { get; set; } = "";
            public string BasicAuthPassword { get; set; } = "";

            // Optional: Formular-Login (z. B. https://www.aikiw.com/login)
            public string AutoLoginUser { get; set; } = "Amir";
            public string AutoLoginPassword { get; set; } = "Amir";
        }

        private LauncherConfig _cfg = new LauncherConfig();
        private bool _loginAttempted = false;
        private CancellationTokenSource? _cts;

        // مهم: readonly نباشه تا بتونیم با Proxy Handler بسازیم
        private HttpClient? _http;

        // Logging
        private readonly object _logLock = new object();
        private string _logFilePath = "";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, __) =>
            {
                try { _cts?.Cancel(); } catch { }
                try { _http?.Dispose(); } catch { }
            };
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            _logFilePath = PrepareLogFile();
            Log("=== Launcher started ===");

            // Konfiguration laden
            _cfg = LoadConfig();
            Log($"Config: AppUrl={_cfg.AppUrl}, MaxWait={_cfg.MaxWaitSeconds}s, Poll={_cfg.PollSeconds}s, Timeout={_cfg.RequestTimeoutSeconds}s");

            Title = _cfg.WindowTitle;
            Width = _cfg.Width;
            Height = _cfg.Height;
            MinWidth = _cfg.MinWidth;
            MinHeight = _cfg.MinHeight;

            // Proxy-aware HttpClient (برای شبکه‌های شرکتی)
            _http = CreateProxyAwareHttpClient(_cfg.RequestTimeoutSeconds);

            try
            {
                // WebView2 initialisieren
                await Browser.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                Log("EnsureCoreWebView2Async FAILED: " + ex);
                Browser.NavigateToString("<html><body style='font-family:system-ui;padding:20px;'>" +
                                         "<h2>WebView2 konnte nicht initialisiert werden</h2>" +
                                         "<p>Bitte installieren/aktualisieren Sie Microsoft Edge WebView2 Runtime.</p>" +
                                         "</body></html>");
                return;
            }

            // Optional: HTTP Basic Auth automatisch beantworten (falls konfiguriert)
            Browser.CoreWebView2.BasicAuthenticationRequested += (_, eArgs) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_cfg.BasicAuthUser) &&
                        !string.IsNullOrWhiteSpace(_cfg.BasicAuthPassword))
                    {
                        eArgs.Response.UserName = _cfg.BasicAuthUser;
                        eArgs.Response.Password = _cfg.BasicAuthPassword;
                    }
                }
                catch
                {
                    // Falls etwas schiefgeht: Standard-Dialog von WebView2 zulassen
                }
            };

            // Falls eine /login Seite angezeigt wird: Zugangsdaten automatisch eintragen
            Browser.NavigationCompleted += async (_, navArgs) =>
            {
                try
                {
                    Log($"NavigationCompleted: IsSuccess={navArgs.IsSuccess}, WebErrorStatus={navArgs.WebErrorStatus}, Url={Browser.Source}");
                }
                catch { }

                await AutoLoginIfNeededAsync();
            };

            // Optional: mehr "native-like"
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = !_cfg.DisableDevTools;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = !_cfg.DisableContextMenus;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = !_cfg.DisableStatusBar;

            // Ladebildschirm sofort anzeigen
            Browser.NavigateToString(ReadAsset("loading.html"));

            // Backend warten + laden (im Hintergrund)
            _ = Task.Run(() => WaitAndLoadAsync(_cts.Token));
        }

        private static HttpClient CreateProxyAwareHttpClient(int timeoutSeconds)
        {
            // HttpClientHandler در net8 پشت‌صحنه از SocketsHttpHandler استفاده می‌کنه.
            // این تنظیمات کمک می‌کنن تو شبکه‌های شرکتی (Proxy/PAC/Auth) HttpClient مثل سیستم عمل کنه.
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = WebRequest.GetSystemWebProxy(),
                UseDefaultCredentials = true,
                Credentials = CredentialCache.DefaultCredentials,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                PreAuthenticate = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
            };

            // اختیاری: بعضی شبکه‌ها روی User-Agent حساسن
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WebAppDesktopLauncher/1.0");
            }
            catch { }

            return client;
        }

        private LauncherConfig LoadConfig()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(baseDir, "appsettings.json");

                if (!File.Exists(path))
                    return new LauncherConfig();

                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<LauncherConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return cfg ?? new LauncherConfig();
            }
            catch
            {
                // Wenn Konfig kaputt ist: Default nehmen
                return new LauncherConfig();
            }
        }

        private string ReadAsset(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, "Assets", fileName);

            if (!File.Exists(path))
            {
                return "<html lang='de'><body style='font-family:system-ui;background:#020617;color:#e5e7eb;padding:24px;'>"
                     + "<h2>Datei fehlt</h2>"
                     + $"<p>Die Datei <b>{fileName}</b> wurde nicht gefunden.</p>"
                     + "</body></html>";
            }

            return File.ReadAllText(path);
        }

        private HttpRequestMessage CreateRequestWithOptionalBasicAuth(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(_cfg.BasicAuthUser) &&
                !string.IsNullOrWhiteSpace(_cfg.BasicAuthPassword))
            {
                var raw = _cfg.BasicAuthUser + ":" + _cfg.BasicAuthPassword;
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
            }

            return req;
        }

        private async Task AutoLoginIfNeededAsync()
        {
            try
            {
                if (_loginAttempted) return;

                var url = Browser.Source?.ToString() ?? "";
                if (!url.Contains("/login", StringComparison.OrdinalIgnoreCase)) return;

                if (string.IsNullOrWhiteSpace(_cfg.AutoLoginUser) || string.IsNullOrWhiteSpace(_cfg.AutoLoginPassword))
                    return;

                _loginAttempted = true;

                var userJson = JsonSerializer.Serialize(_cfg.AutoLoginUser);
                var passJson = JsonSerializer.Serialize(_cfg.AutoLoginPassword);

                var js = $@"
(() => {{
  const USER = {userJson};
  const PASS = {passJson};

  const inputs = Array.from(document.querySelectorAll('input'));
  const guessUser = (el) => {{
    const s = ((el.name||'') + ' ' + (el.id||'') + ' ' + (el.placeholder||'')).toLowerCase();
    const t = (el.type||'').toLowerCase();
    return /(user|username|login|email|benutzer)/.test(s) || t === 'text' || t === 'email';
  }};

  const userInput = inputs.find(i => guessUser(i)) || document.querySelector('input[type=text], input[type=email], input:not([type])');
  const passInput = inputs.find(i => (i.type||'').toLowerCase() === 'password') || document.querySelector('input[type=password]');

  if (userInput) userInput.value = USER;
  if (passInput) passInput.value = PASS;

  const form = (passInput && passInput.form) || (userInput && userInput.form) || document.querySelector('form');
  if (form) {{
    form.submit();
  }} else {{
    const btn = document.querySelector('button[type=submit], input[type=submit], button');
    if (btn) btn.click();
  }}
}})();
";
                await Browser.CoreWebView2.ExecuteScriptAsync(js);

                // allow retry later if needed
                _ = Task.Run(async () =>
                {
                    await Task.Delay(4000);
                    _loginAttempted = false;
                });
            }
            catch (Exception ex)
            {
                Log("AutoLoginIfNeededAsync FAILED: " + ex.Message);
            }
        }

        private async Task WaitAndLoadAsync(CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            var appUrl = _cfg.AppUrl;

            int consecutiveFailures = 0;
            bool directNavigateFallbackTriggered = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_http == null) throw new InvalidOperationException("HttpClient not initialized.");

                    using var req = CreateRequestWithOptionalBasicAuth(appUrl);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                    consecutiveFailures = 0;

                    // Wie in Python: alles außer 5xx gilt als "bereit"
                    var code = (int)resp.StatusCode;
                    Log($"Poll OK: {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    if (code < 500)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            Log("Navigate (ready): " + appUrl);
                            Browser.CoreWebView2.Navigate(appUrl);
                        });

                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    Log($"Poll FAILED (#{consecutiveFailures}): {ex.GetType().Name}: {ex.Message}");

                    // ✅ فیکس عملی برای شرکت‌ها:
                    // اگر HttpClient چند بار پشت سرهم fail شد، احتمالاً مشکل Proxy/Auth هست
                    // ولی WebView2 (مثل مرورگر) می‌تونه لود کنه، پس یکبار مستقیم navigate می‌کنیم.
                    if (!directNavigateFallbackTriggered && consecutiveFailures >= 3)
                    {
                        directNavigateFallbackTriggered = true;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            Log("Direct navigate fallback triggered: " + appUrl);
                            Browser.CoreWebView2.Navigate(appUrl);
                        });

                        // نکته: اینجا return نمی‌کنیم تا اگر واقعاً سرور آماده نبود،
                        // همچنان پولینگ ادامه پیدا کنه و وقتی ready شد دوباره navigate بشه.
                        // (در عمل برای شرکت‌ها معمولاً همین یکبار هم کافیست)
                    }
                }

                if ((DateTimeOffset.UtcNow - start).TotalSeconds > _cfg.MaxWaitSeconds)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Log("Timeout reached. Showing error.html");
                        Browser.NavigateToString(ReadAsset("error.html"));
                    });
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _cfg.PollSeconds)), ct);
                }
                catch
                {
                    return;
                }
            }
        }

        private string PrepareLogFile()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WebAppDesktopLauncher",
                    "Logs"
                );
                Directory.CreateDirectory(dir);

                // یک فایل ثابت + ساده (اگر خواستی می‌تونیم تاریخ‌دارش کنیم)
                return Path.Combine(dir, "launcher.log");
            }
            catch
            {
                return "";
            }
        }

        private void Log(string msg)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logFilePath)) return;

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(_logFilePath, line);
                }
            }
            catch
            {
                // never crash due to logging
            }
        }
    }
}
