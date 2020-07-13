using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NLog;
using Sandbox;
using Sandbox.ModAPI;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Session;
using Torch.Session;

namespace n3b.TorchOptimizationsPlugin
{
    public class Plugin : TorchPluginBase
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public static readonly AutoResetEvent productionTick = new AutoResetEvent(false);

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            var harmony = new Harmony("n3b.TorchOptimizationsPlugin");
            GasTankThrottle.Inject(harmony);
            torch.GameStateChanged += GameStateChanged;
            var manager = torch.Managers.GetManager<TorchSessionManager>();
            if (manager != null) manager.SessionStateChanged += SessionStateChanged;
        }

        public override void Update()
        {
            base.Update();
            GasTankThrottle.Update(MyAPIGateway.Session.ElapsedPlayTime.Ticks);
        }

        void GameStateChanged(MySandboxGame game, TorchGameState state)
        {
            if (state == TorchGameState.Creating)
            {
                // init
            }
        }

        void SessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            if (newState == TorchSessionState.Unloading)
            {
                // cleanup
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
                getType = Expression.GetActionType;
            }
            else
            {
                getType = Expression.GetFuncType;
                types = types.Concat(new[] { methodInfo.ReturnType });
            }

            return methodInfo.IsStatic ? Delegate.CreateDelegate(getType(types.ToArray()), methodInfo) : Delegate.CreateDelegate(getType(types.ToArray()), target, methodInfo.Name);
        }
    }
}