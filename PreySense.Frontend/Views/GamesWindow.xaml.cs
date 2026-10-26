using System.Windows;

namespace PreySense.Frontend.Views
{
    public partial class GamesWindow : Window
    {
        public GamesWindow()
        {
            InitializeComponent();
            MaxHeight = System.Math.Max(300, SystemParameters.WorkArea.Height - 32);
        }
    }
}
