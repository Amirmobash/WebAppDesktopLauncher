using System.Windows;

namespace WebAppDesktopLauncher
{
    public partial class FileNameDialog : Window
    {
        public string FileNameResult { get; private set; } = "";

        public FileNameDialog(string suggestedName)
        {
            InitializeComponent();
            FileNameBox.Text = suggestedName ?? "";
            FileNameBox.SelectAll();
            FileNameBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            FileNameResult = FileNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(FileNameResult))
            {
                MessageBox.Show(this, "File name cannot be empty.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
