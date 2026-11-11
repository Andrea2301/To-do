using System.Windows;
using System.Windows.Controls;
using DoItNow.ViewModels;

namespace DoItNow.Views
{
    public partial class DocToolsView : UserControl
    {
        public DocToolsView()
        {
            InitializeComponent();
        }

        private static bool HasFiles(DragEventArgs e)
            => e.Data.GetDataPresent(DataFormats.FileDrop);

        private void FileDrop_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void FileDrop_Drop(object sender, DragEventArgs e)
        {
            if (!HasFiles(e)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 } && DataContext is DocToolsViewModel vm)
                vm.LoadFile(files[0]);
        }

        private void Convert_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Convert_Drop(object sender, DragEventArgs e)
        {
            if (!HasFiles(e)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 } && DataContext is DocToolsViewModel vm)
                vm.AddFiles(files);
        }
    }
}
