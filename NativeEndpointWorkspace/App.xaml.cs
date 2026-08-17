using System;
using System.Windows;
using System.Windows.Threading;
using NativeEndpointWorkspace.Services;

namespace NativeEndpointWorkspace
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            RuntimeLogService.Shared.StartSession();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            RuntimeLogService.Shared.Dispose();
            base.OnExit(e);
        }

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            RuntimeLogService.Shared.Error("UNHANDLED_DISPATCHER_EXCEPTION", e.Exception);
            // Do not hide the exception; normal WPF failure behavior remains intact.
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            RuntimeLogService.Shared.Error("UNHANDLED_DOMAIN_EXCEPTION", e.ExceptionObject as Exception);
        }
    }
}
