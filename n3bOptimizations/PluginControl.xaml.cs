using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace n3bOptimizations
{
    public partial class PluginControl : UserControl
    {
        private Plugin Plugin { get; }

        public PluginControl()
        {
            InitializeComponent();
        }

        public PluginControl(Plugin plugin) : this()
        {
            Plugin = plugin;
            DataContext = plugin.Config;
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            BindingOperations.GetBindingExpression(GasTankEnabled, CheckBox.IsCheckedProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(GasTankThreshold1, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(GasTankThreshold2, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(GasTankInterval, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(GasTankBatches, TextBox.TextProperty).UpdateTarget();

            BindingOperations.GetBindingExpression(InventoryEnabled, CheckBox.IsCheckedProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(InventoryInterval, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(InventoryBatches, TextBox.TextProperty).UpdateTarget();
            BindingOperations.GetBindingExpression(InventoryPreventSharing, CheckBox.IsCheckedProperty).UpdateTarget();

            BindingOperations.GetBindingExpression(ProductionBlockEnabled, CheckBox.IsCheckedProperty).UpdateTarget();

            BindingOperations.GetBindingExpression(ConveyorCachingEnabled, CheckBox.IsCheckedProperty).UpdateTarget();

            BindingOperations.GetBindingExpression(SafeZoneCachingEnabled, CheckBox.IsCheckedProperty).UpdateTarget();

            Plugin.Save();
        }
    }
}