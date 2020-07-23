using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using HarmonyLib;
using n3b.SEMultiplayer;
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
        long channelId = 2171994463;

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
            GasTankThrottle.Init(harmony, Config);

            var i = typeof(IPatch);
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetInterfaces().Contains(i)))
            {
#if !DEBUG
                try
                {
#endif
                var obj = (IPatch) Activator.CreateInstance(t);
                obj.Inject(harmony);
#if !DEBUG
                    Log.Info($"{t.Name} applied");
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Unable to apply {t}");
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
            GasTankThrottle.Update();
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

    public static class PluginExtensions
    {
        public static Delegate CreateDelegate(this MethodInfo methodInfo, object target)
        {
            Func<Type[], Type> getType;
            var isAction = methodInfo.ReturnType == (typeof(void));
            var types = methodInfo.GetParameters().Select(p => p.ParameterType);

            if (isAction)
            {
                getType = System.Linq.Expressions.Expression.GetActionType;
            }
            else
            {
                getType = System.Linq.Expressions.Expression.GetFuncType;
                types = types.Concat(new[] {methodInfo.ReturnType});
            }

            return methodInfo.IsStatic ? Delegate.CreateDelegate(getType(types.ToArray()), methodInfo) : Delegate.CreateDelegate(getType(types.ToArray()), target, methodInfo.Name);
        }
    }

    public class PluginConfig : ViewModel
    {
        private int threshold1 = 6;
        private int threshold2 = 3;
        private int perTicks = 13;
        private int batches = 2;

        public int Threshold1
        {
            get => threshold1;
            set => SetValue(ref threshold1, Math.Max(Math.Min(value, 100), (int) Threshold2 + 1));
        }

        public int Threshold2
        {
            get => threshold2;
            set => SetValue(ref threshold2, Math.Max(Math.Min(value, Threshold1 - 1), 1));
        }

        public int PerTicks
        {
            get => perTicks;
            set => SetValue(ref perTicks, Math.Max(Math.Min(value, 60), 1));
        }

        public int Batches
        {
            get => batches;
            set => SetValue(ref batches, Math.Max(Math.Min(value, 5), 1));
        }
    }
}