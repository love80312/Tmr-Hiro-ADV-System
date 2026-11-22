using System.Windows.Controls;
using TmrHiroStudio.Core;

namespace TmrHiroStudio
{
    public partial class MainWindow
    {
        private void ScriptExportEncoding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.SelectedItem is ComboBoxItem item &&
                item.Tag != null)
            {
                ScriptTextCodec.SelectedExportEncoding = item.Tag.ToString();
            }
        }

        private void ScriptExportTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.SelectedItem is ComboBoxItem item &&
                item.Tag != null)
            {
                ScriptTextCodec.SelectedExportTransform = item.Tag.ToString();
            }
        }
    }
}
