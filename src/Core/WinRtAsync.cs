using System;
using System.Threading;
using Windows.Foundation;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Espera síncrona de operaciones asíncronas de WinRT. Sustituye a AsTask(): las extensiones de
    /// System.Runtime.WindowsRuntime.dll están compiladas contra el Windows.winmd unificado del SDK, que no coincide
    /// con los metadatos por partes de System32\WinMetadata que usa este proyecto. Llamar solo desde hilos en segundo plano.
    /// </summary>
    public static class WinRtAsync
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public static T Wait<T>(IAsyncOperation<T> operation)
        {
            return Wait(operation, Timeout);
        }

        /// <summary>
        /// Igual, con un tiempo máximo propio (el dictado de voz dura lo que hable el usuario).
        /// </summary>
        public static T Wait<T>(IAsyncOperation<T> operation, TimeSpan timeout)
        {
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                // Si la operación ya terminó, WinRT invoca el manejador en cuanto se asigna
                operation.Completed = (op, status) => done.Set();
                if (!done.WaitOne(timeout)) throw new TimeoutException("La operación de Windows no respondió a tiempo");
            }

            if (operation.Status == AsyncStatus.Error) throw operation.ErrorCode;
            if (operation.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
            return operation.GetResults();
        }
    }
}
