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
            public int RequestTimeoutSeconds { get; set; } = 8;

            public string WindowTitle { get; set; } = "WebApp Desktop Client";
            public int Width { get; set; } = 1200;
            public int Height { get; set; } = 800;
            public int MinWidth { get; set; } = 800;
            public int MinHeight { get; set; } = 600;

            public bool DisableDevTools { get; set; } = true;
            public bool DisableContextMenus { get; set; } = true;
            public bool DisableStatusBar { get; set; } = true;

            // Optional: HTTP Basic Auth (z. B. Passwortschutz)
            // Hinweis: Besser NICHT hardcoden – in appsettings.json konfigurieren.
            public string BasicAuthUser { get; set; } = "";
            public string BasicAuthPassword { get; set; } = "";

            // Optional: Formular-Login (z. B. /login)
            // Hinweis: Besser NICHT hardcoden – in appsettings.json konfigurieren.
            public string AutoLoginUser { get; set; } = "";
            public string AutoLoginPassword { get; set; } = "";
        }

        private LauncherConfig _cfg = new LauncherConfig();
        private bool _loginAttempted = false;
        private bool _appShown = false;

        private CancellationTokenSource? _cts;
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
            Log("=== Launcher gestartet ===");

            // Konfiguration laden
            _cfg = LoadConfig();

            Title = _cfg.WindowTitle;
            Width = _cfg.Width;
            Height = _cfg.Height;
            MinWidth = _cfg.MinWidth;
            MinHeight = _cfg.MinHeight;

            // Wichtig: WebView verstecken, damit der Benutzer die Login-Seite nie sieht.
            Browser.Visibility = Visibility.Hidden;
            ShowOverlay("Bitte warten…");

            // Proxy-aware HttpClient (für Unternehmensnetzwerke/Proxy/PAC)
            _http = CreateProxyAwareHttpClient(_cfg.RequestTimeoutSeconds);

            try
            {
                // WebView2 initialisieren
                await Browser.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                Log("EnsureCoreWebView2Async FEHLER: " + ex);
                ShowOverlay("WebView2 Runtime fehlt oder ist fehlerhaft.");
                return;
            }

            // WebView2 Einstellungen (mehr „Desktop-App“-Feeling)
            try
            {
                var s = Browser.CoreWebView2.Settings;
                s.AreDevToolsEnabled = !_cfg.DisableDevTools;
                s.AreDefaultContextMenusEnabled = !_cfg.DisableContextMenus;
                s.IsStatusBarEnabled = !_cfg.DisableStatusBar;

                s.IsZoomControlEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsBuiltInErrorPageEnabled = false;
            }
            catch { }

            // Optional: Farbschema (wenn vom SDK unterstützt)
            try
            {
                Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
            }
            catch { }

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
                    // Falls etwas schiefgeht: Standarddialog zulassen
                }
            };

            // DOWNLOAD: Benutzer soll den Dateinamen eingeben.
            // Speicherort ist IMMER Desktop. (Kein Ordnerwechsel durch Benutzer)
            // WICHTIG: Kein SaveFileDialog → robust & zuverlässig.
            Browser.CoreWebView2.DownloadStarting += (_, eArgs) =>
            {
                try
                {
                    // Vorschlag: Dateiname aus dem default ResultFilePath holen (kompatibel auch ohne SuggestedFileName)
                    var defaultPath = eArgs.ResultFilePath; // z.B. ...\Downloads\report.pdf
                    var suggestedName = Path.GetFileName(defaultPath);

                    if (string.IsNullOrWhiteSpace(suggestedName))
                        suggestedName = "download";

                    suggestedName = SanitizeFileName(suggestedName);

                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                    // Dialog muss im UI-Thread geöffnet werden
                    Dispatcher.Invoke(() =>
                    {
                        var dlg = new FileNameDialog(suggestedName)
                        {
                            Owner = this
                        };

                        var ok = dlg.ShowDialog() == true;

                        if (!ok)
                        {
                            // Benutzer hat abgebrochen → Download abbrechen
                            eArgs.Cancel = true;
                            eArgs.Handled = true;
                            Log("Download vom Benutzer abgebrochen.");
                            return;
                        }

                        // Benutzer gibt nur Dateinamen ein, wir setzen den Desktop-Pfad.
                        var fileNameOnly = SanitizeFileName(dlg.FileNameResult);

                        // Falls Benutzer keine Endung eingibt: die vorhandene Endung vom Vorschlag übernehmen
                        fileNameOnly = EnsureExtension(fileNameOnly, suggestedName);

                        var target = GetUniquePath(Path.Combine(desktop, fileNameOnly));

                        eArgs.ResultFilePath = target;
                        eArgs.Handled = true;

                        Log("Download-Ziel (Desktop fix): " + target);
                    });
                }
                catch (Exception ex)
                {
                    Log("DownloadStarting FEHLER: " + ex.Message);
                    // Fallback: Standardverhalten
                    eArgs.Handled = false;
                }
            };

            // Overlay Logik: Login-Seite darf nie sichtbar werden
            Browser.CoreWebView2.NavigationStarting += (_, navArgs) =>
            {
                try
                {
                    var url = navArgs.Uri ?? "";
                    if (!_appShown)
                    {
                        ShowOverlay(IsLoginUrl(url) ? "Anmeldung wird durchgeführt…" : "Komponenten werden geladen…");
                    }
                    else
                    {
                        // Wenn Session abläuft und wieder Login kommt: erneut verstecken
                        if (IsLoginUrl(url))
                        {
                            Browser.Visibility = Visibility.Hidden;
                            ShowOverlay("Anmeldung wird durchgeführt…");
                        }
                    }
                }
                catch { }
            };

            Browser.NavigationCompleted += async (_, navArgs) =>
            {
                try
                {
                    Log($"NavigationCompleted: IsSuccess={navArgs.IsSuccess}, WebErrorStatus={navArgs.WebErrorStatus}, Url={Browser.Source}");

                    // Falls Login-Seite: Auto-Login im Hintergrund
                    await AutoLoginIfNeededAsync();

                    var url = Browser.Source?.ToString() ?? "";

                    // Sobald wir NICHT mehr auf Login sind, UI freigeben
                    if (navArgs.IsSuccess && !IsLoginUrl(url))
                    {
                        _appShown = true;
                        Browser.Visibility = Visibility.Visible;
                        HideOverlay();
                    }
                    else
                    {
                        ShowOverlay(IsLoginUrl(url) ? "Anmeldung wird durchgeführt…" : "Bitte warten…");
                    }
                }
                catch { }
            };

            // Backend warten + laden
            _ = Task.Run(() => WaitAndLoadAsync(_cts.Token));
        }

        // ========================= Kernlogik =========================

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
                    if (_http == null) throw new InvalidOperationException("HttpClient ist nicht initialisiert.");

                    using var req = CreateRequestWithOptionalBasicAuth(appUrl);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                    consecutiveFailures = 0;

                    var code = (int)resp.StatusCode;
                    Log($"Poll OK: {code} {resp.ReasonPhrase}");

                    // Alles außer 5xx gilt als „bereit“
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
                    Log($"Poll FEHLER (#{consecutiveFailures}): {ex.GetType().Name}: {ex.Message}");

                    // Unternehmensnetzwerke: HttpClient kann wegen Proxy/Auth scheitern, WebView2 kann aber oft laden.
                    // Nach ein paar Fehlern einmal direkt mit WebView2 navigieren (Overlay bleibt aktiv).
                    if (!directNavigateFallbackTriggered && consecutiveFailures >= 3)
                    {
                        directNavigateFallbackTriggered = true;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            Log("Direkter Navigate-Fallback: " + appUrl);
                            Browser.CoreWebView2.Navigate(appUrl);
                        });
                    }
                }

                if ((DateTimeOffset.UtcNow - start).TotalSeconds > _cfg.MaxWaitSeconds)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Log("Timeout erreicht. Zeige Overlay-Fehler.");
                        ShowOverlay("Zeitüberschreitung. Bitte Internet/Proxy prüfen.");
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

        private async Task AutoLoginIfNeededAsync()
        {
            try
            {
                if (_loginAttempted) return;

                var url = Browser.Source?.ToString() ?? "";
                if (!IsLoginUrl(url)) return;

                if (string.IsNullOrWhiteSpace(_cfg.AutoLoginUser) ||
                    string.IsNullOrWhiteSpace(_cfg.AutoLoginPassword))
                    return;

                _loginAttempted = true;

                var userJson = JsonSerializer.Serialize(_cfg.AutoLoginUser);
                var passJson = JsonSerializer.Serialize(_cfg.AutoLoginPassword);

                // Hinweis: Der Benutzer sieht die Login-Seite nicht, weil Overlay aktiv ist.
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

                // Optional: erneuter Versuch später möglich
                _ = Task.Run(async () =>
                {
                    await Task.Delay(4000);
                    _loginAttempted = false;
                });
            }
            catch (Exception ex)
            {
                Log("AutoLogin FEHLER: " + ex.Message);
            }
        }

        // ========================= Hilfsmethoden =========================

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
                return new LauncherConfig();
            }
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

        private static HttpClient CreateProxyAwareHttpClient(int timeoutSeconds)
        {
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

            try { client.DefaultRequestHeaders.UserAgent.ParseAdd("WebAppDesktopLauncher/1.0"); } catch { }
            return client;
        }

        private static bool IsLoginUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = url.ToLowerInvariant();

            // Bei Bedarf anpassen, falls Login-URL anders ist (z.B. /account/login)
            return url.Contains("/login") || url.Contains("/auth") || url.Contains("signin") || url.Contains("sign-in");
        }

        private void ShowOverlay(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                try { OverlayText.Text = msg; } catch { }
                BlockingOverlay.Visibility = Visibility.Visible;
            });
        }

        private void HideOverlay()
        {
            Dispatcher.Invoke(() => BlockingOverlay.Visibility = Visibility.Collapsed);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        // Wenn Benutzer keine Endung eintippt: Endung vom Vorschlag übernehmen.
        private static string EnsureExtension(string fileNameOnly, string suggestedName)
        {
            try
            {
                var ext = Path.GetExtension(fileNameOnly);
                if (!string.IsNullOrWhiteSpace(ext))
                    return fileNameOnly;

                var suggestedExt = Path.GetExtension(suggestedName);
                if (!string.IsNullOrWhiteSpace(suggestedExt))
                    return fileNameOnly + suggestedExt;
            }
            catch { }

            return fileNameOnly;
        }

        // Falls Datei bereits existiert, eindeutigen Namen erzeugen.
        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path) ?? "";
            var file = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            for (int i = 1; i < 10000; i++)
            {
                var p = Path.Combine(dir, $"{file} ({i}){ext}");
                if (!File.Exists(p)) return p;
            }
            return path;
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
            catch { }
        }
    }
}
