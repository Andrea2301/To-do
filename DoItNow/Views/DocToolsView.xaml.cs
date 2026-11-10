using System;
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

        private void FileDrop_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void FileDrop_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    if (DataContext is DocToolsViewModel vm)
                    {
                        vm.LoadFile(files[0]);
                    }
                }
            }
        }
    }
}
