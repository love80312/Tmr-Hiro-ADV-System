using System.Windows.Controls;
using TmrHiroStudio.Core;

namespace TmrHiroStudio
{
    public partial class MainWindow
    {
        private void ScriptExportEncoding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            ComboBoxItem item = combo == null ? null : combo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                ScriptTextCodec.SelectedExportEncoding = item.Tag.ToString();
        }

        private void ScriptExportTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            ComboBoxItem item = combo == null ? null : combo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                ScriptTextCodec.SelectedExportTransform = item.Tag.ToString();
        }
    }
}
