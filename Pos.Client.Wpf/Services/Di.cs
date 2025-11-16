// Pos.Client.Wpf/Services/Di.cs
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Services
{
    public static class Di
    {
        public static T? Get<T>() where T : class
            => App.Services?.GetService<T>();
    }
}
