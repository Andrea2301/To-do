using System.Windows.Controls;
using System.Windows.Input;
using DoItNow.ViewModels;

namespace DoItNow.Views
{
    public partial class AiChatView : UserControl
    {
        public AiChatView()
        {
            InitializeComponent();
        }

        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                if (DataContext is AiChatViewModel vm)
                {
                    _ = vm.SendMessageAsync();
                }
            }
        }
    }
}
