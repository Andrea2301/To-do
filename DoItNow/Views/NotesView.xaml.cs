using DoItNow.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoItNow.Views
{
    public partial class NotesView : UserControl
    {
        public NotesView()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.S || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            e.Handled = true;
            if (DataContext is NotesViewModel vm)
                await vm.SaveNoteAsync();
        }
    }
}
