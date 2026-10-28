using System;
using System.Collections.Generic;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Bus de eventos pub/sub desacoplado para comunicación modular sin dependencias directas.
    /// Compatible con C# 5 y .NET Framework / .NET Core.
    /// </summary>
    public class EventBus
    {
        private static readonly Lazy<EventBus> _instance = new Lazy<EventBus>(CreateInstance);

        private static EventBus CreateInstance()
        {
            return new EventBus();
        }

        public static EventBus Instance
        {
            get { return _instance.Value; }
        }

        private readonly Dictionary<string, List<Action<object>>> _subscriptions 
            = new Dictionary<string, List<Action<object>>>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        public void Subscribe(string eventName, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;

            lock (_lock)
            {
                List<Action<object>> handlers;
                if (!_subscriptions.TryGetValue(eventName, out handlers))
                {
                    handlers = new List<Action<object>>();
                    _subscriptions[eventName] = handlers;
                }
                handlers.Add(handler);
            }
        }

        public void Unsubscribe(string eventName, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;

            lock (_lock)
            {
                List<Action<object>> handlers;
                if (_subscriptions.TryGetValue(eventName, out handlers))
                {
                    handlers.Remove(handler);
                }
            }
        }

        public bool HasSubscribers(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return false;

            lock (_lock)
            {
                List<Action<object>> handlers;
                return _subscriptions.TryGetValue(eventName, out handlers) && handlers.Count > 0;
            }
        }

        public void Publish(string eventName, object payload = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            List<Action<object>> handlersCopy = null;

            lock (_lock)
            {
                List<Action<object>> handlers;
                if (_subscriptions.TryGetValue(eventName, out handlers))
                {
                    handlersCopy = new List<Action<object>>(handlers);
                }
            }

            if (handlersCopy != null)
            {
                foreach (Action<object> handler in handlersCopy)
                {
                    try
                    {
                        handler(payload);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[EventBus] Error en evento '{0}': {1}", eventName, ex);
                    }
                }
            }
        }
    }
}
