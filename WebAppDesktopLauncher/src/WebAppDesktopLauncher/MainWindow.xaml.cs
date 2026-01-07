using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
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
        }

        private LauncherConfig _cfg = new LauncherConfig();
        private CancellationTokenSource? _cts;

        private readonly HttpClient _http = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, __) => _cts?.Cancel();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            // Konfiguration laden
            _cfg = LoadConfig();

            Title = _cfg.WindowTitle;
            Width = _cfg.Width;
            Height = _cfg.Height;
            MinWidth = _cfg.MinWidth;
            MinHeight = _cfg.MinHeight;

            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _cfg.RequestTimeoutSeconds));

            // WebView2 initialisieren
            await Browser.EnsureCoreWebView2Async();

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


            // Optional: mehr "native-like"
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = !_cfg.DisableDevTools;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = !_cfg.DisableContextMenus;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = !_cfg.DisableStatusBar;

            // Ladebildschirm sofort anzeigen
            Browser.NavigateToString(ReadAsset("loading.html"));

            // Backend warten + laden (im Hintergrund)
            _ = Task.Run(() => WaitAndLoadAsync(_cts.Token));
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
                // Fallback: minimaler Text in Deutsch
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

        private async Task WaitAndLoadAsync(CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            var appUrl = _cfg.AppUrl;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var req = CreateRequestWithOptionalBasicAuth(appUrl);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                    // Wie in Python: alles außer 5xx gilt als "bereit"
                    if ((int)resp.StatusCode < 500)
                    {
                        await Dispatcher.InvokeAsync(() => Browser.CoreWebView2.Navigate(appUrl));
                        return;
                    }
                }
                catch
                {
                    // Ignorieren: Server ist noch nicht bereit
                }

                if ((DateTimeOffset.UtcNow - start).TotalSeconds > _cfg.MaxWaitSeconds)
                {
                    await Dispatcher.InvokeAsync(() => Browser.NavigateToString(ReadAsset("error.html")));
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
    }
}
