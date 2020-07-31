using System;
using System.Linq;
using System.Reflection;

namespace n3bOptimizations.Util
{
    public static class Extensions
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
}