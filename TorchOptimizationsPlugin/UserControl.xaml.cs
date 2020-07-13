using System.Windows;
using System.Windows.Controls;

namespace n3b.TorchOptimizationsPlugin
{
    public partial class PluginControl : UserControl
    {
        private Plugin Plugin { get; }
        
        public PluginControl(Plugin plugin) {
            Plugin = plugin;
            DataContext = plugin.Config;
        }
        
        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
        }
    }
}