using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace n3bOptimizations
{
    public partial class PluginControl : UserControl
    {
        private Plugin Plugin { get; }
        
        public PluginControl() {
            InitializeComponent();
        }
        
        public PluginControl(Plugin plugin): this() {
            Plugin = plugin;
            DataContext = plugin.Config;
        }
        
        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            BindingOperations.GetBindingExpression(Threshold1, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(Threshold2, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(PerTicks, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(Batches, TextBox.TextProperty).UpdateTarget();
            Plugin.Save();
        }
    }
}