using System.Windows;

namespace WebAppDesktopLauncher
{
    public partial class FileNameDialog : Window
    {
        // Enthält den vom Benutzer eingegebenen Dateinamen (ohne Pfad).
        public string FileNameResult { get; private set; } = "";

        public FileNameDialog(string suggestedName)
        {
            InitializeComponent();

            // Vorschlag setzen und direkt markieren, damit der Benutzer schnell überschreiben kann.
            FileNameBox.Text = suggestedName ?? "";
            FileNameBox.SelectAll();
            FileNameBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = (FileNameBox.Text ?? "").Trim();

            // Einfacher Check: Dateiname darf nicht leer sein.
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this,
                    "Der Dateiname darf nicht leer sein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            FileNameResult = name;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
