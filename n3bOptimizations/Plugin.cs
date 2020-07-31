using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using HarmonyLib;
using n3b.SEMultiplayer;
using n3bOptimizations.Patch.GasTank;
using NLog;
using Sandbox;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;

namespace n3bOptimizations
{
    public class Plugin : TorchPluginBase, IWpfPlugin
    {
        public static PluginConfig StaticConfig;

        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private Persistent<PluginConfig> _config;
        public PluginConfig Config => _config?.Data;
        private PluginControl _control;
        public UserControl GetControl() => _control ?? (_control = new PluginControl(this));

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            SetupConfig();
            var harmony = new Harmony("n3b.TorchOptimizationsPlugin");
            var i = typeof(IPatch);
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetInterfaces().Contains(i)))
            {
#if !DEBUG
                try
                {
#endif
                var obj = (IPatch) Activator.CreateInstance(t);
                if (obj.Inject(harmony)) Log.Info($"{t.Name} applied");
                else Log.Info($"{t.Name} skipped");
#if !DEBUG
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Unable to apply {t}");
                }
#endif
            }


            new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.ApplicationIdle, (s, e) => { Console.WriteLine(">>>>>>>>> timer fired"); },
                Dispatcher.CurrentDispatcher);

            torch.GameStateChanged += GameStateChanged;
            var manager = torch.Managers.GetManager<TorchSessionManager>();
            if (manager != null) manager.SessionStateChanged += SessionStateChanged;
        }

        public override void Update()
        {
            base.Update();
            GasTankPatch.Update();
        }

        void GameStateChanged(MySandboxGame game, TorchGameState state)
        {
            if (state != TorchGameState.Created) return;
            API.Register();
        }

        void SessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            if (newState != TorchSessionState.Unloading) return;
            // todo
        }

        private void SetupConfig()
        {
            var configFile = Path.Combine(StoragePath, "n3bOptimizations.cfg");

            try
            {
                _config = Persistent<PluginConfig>.Load(configFile);
            }
            catch (Exception e)
            {
                Log.Warn(e);
            }

            if (_config?.Data == null)
            {
                _config = new Persistent<PluginConfig>(configFile, new PluginConfig());
                _config.Save();
            }

            StaticConfig = _config.Data;
        }

        public void Save()
        {
            try
            {
                _config.Save();
                Log.Info("Configuration Saved.");
            }
            catch (IOException e)
            {
                Log.Warn(e, "Configuration failed to save");
            }
        }
    }
}