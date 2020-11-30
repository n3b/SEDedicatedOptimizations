using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using HarmonyLib;
using n3bOptimizations.Patch.GasTank;
using n3bOptimizations.Replication.Inventory;
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

        static readonly Logger Log = LogManager.GetCurrentClassLogger();
        Persistent<PluginConfig> _config;
        PluginControl _control;
        public PluginConfig Config => _config?.Data;

        public UserControl GetControl()
        {
            return _control ?? (_control = new PluginControl(this));
        }

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
                    if (obj.Inject(harmony)) Info($"{t.Name} applied");
                    else Info($"{t.Name} skipped");
#if !DEBUG
                }
                catch (Exception e)
                {
                    Error($"Unable to apply {t}", e);
                }
#endif
            }

            torch.GameStateChanged += GameStateChanged;
            var manager = torch.Managers.GetManager<TorchSessionManager>();
            if (manager != null) manager.SessionStateChanged += SessionStateChanged;
        }

        public override void Update()
        {
            base.Update();
            GasTankPatch.Update();
            InventoryReplicableUpdate.Update();
        }

        void GameStateChanged(MySandboxGame game, TorchGameState state)
        {
            if (state != TorchGameState.Created) return;
            // not necessary after 1.196
            // API.Register();
        }

        void SessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            if (newState != TorchSessionState.Unloading) return;
            // todo
        }

        void SetupConfig()
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
                Directory.CreateDirectory(StoragePath);
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

        public static void Info(string str)
        {
            Log.Info($"n3bOptimizations: {str}");
        }

        public static void Warn(string str)
        {
            Log.Warn($"n3bOptimizations: {str}");
        }

        public static void Error(string str, Exception e = null)
        {
            if (e != null) Log.Error(e, $"n3bOptimizations: {str}");
            else Log.Error(e);
        }
    }
}