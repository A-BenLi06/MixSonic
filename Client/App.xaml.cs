namespace Subsonic8
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Caliburn.Micro;
    using Client.Common;
    using Client.Common.Services;
    using global::Common;
    using MugenInjection;
    using SubEchoNest;
    using SubLastFm;
    using Subsonic8.Framework;
    using Subsonic8.Shell;
    using Windows.ApplicationModel;
    using Windows.ApplicationModel.Activation;
    using Windows.Storage;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public sealed partial class App
    {
        #region Fields

        private const string StartupLogFileName = "startup-crash.log";

        private const string StartupLogSettingsKey = "StartupCrashLog";

        private static readonly SemaphoreSlim StartupLogGate = new SemaphoreSlim(1, 1);

        private static readonly List<string> StartupLogLines = new List<string>();

        private CustomFrameAdapter _navigationService;

        private IShellViewModel _shellViewModel;

        #endregion

        #region Constructors and Destructors

        public App()
        {
            UnhandledException += OnApplicationUnhandledException;
            LogStartupEvent("App constructor");
            InitializeComponent();
        }

        #endregion

        #region Methods

        protected override void Configure()
        {
            LogStartupEvent("Configure start");
            Kernel.Load<CommonModule>();
            Kernel.Load<SubsonicCommonModule>();
            Kernel.Load<SubLastFmModule>();
            Kernel.Load<SubEchoNestModule>();
            Kernel.Load<ClientModule>();
            LogStartupEvent("Configure complete");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            LogStartupEvent("OnLaunched");
            try
            {
                StartApplication();
            }
            catch (Exception exception)
            {
                LogStartupException("OnLaunched failed", exception);
                throw;
            }
        }

        protected override void OnSearchActivated(SearchActivatedEventArgs args)
        {
            LogStartupEvent("OnSearchActivated");
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                try
                {
                    StartApplication();
                }
                catch (Exception exception)
                {
                    LogStartupException("OnSearchActivated failed", exception);
                    throw;
                }
            }

            _shellViewModel.SendSearchQueryMessage(args.QueryText);
        }

        protected override async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await Kernel.Get<Subsonic8.Navidrome.IPlaybackActivityService>().SuspendAsync();
                await SuspensionManager.SaveAsync();
            }
            finally { deferral.Complete(); }
        }

        private void BindShellViewModelToView(ShellView shellView)
        {
            _shellViewModel = Kernel.Get<IShellViewModel>();

            ViewModelBinder.Bind(_shellViewModel, shellView, null);
        }

        private ShellView GetShellView()
        {
            return (ShellView)RootFrame.Content;
        }

        private void RegisterNavigationService(Frame shellFrame, bool treatViewAsLoaded = false)
        {
            _navigationService = new CustomFrameAdapter(shellFrame, treatViewAsLoaded);
            Kernel.Bind<INavigationService>().ToConstant(_navigationService);
            Kernel.Bind<ICustomFrameAdapter>().ToConstant(_navigationService);
        }

        private void StartApplication()
        {
            LogStartupEvent("DisplayRootView start");
            DisplayRootView<ShellView>();
            LogStartupEvent("DisplayRootView complete");

            var shellView = GetShellView();

            RegisterNavigationService(shellView.ShellFrame);

            BindShellViewModelToView(shellView);
            LogStartupEvent("StartApplication complete");
        }

        private static void LogStartupEvent(string message)
        {
            WriteStartupLog(message, null);
        }

        private static void LogStartupException(string message, Exception exception)
        {
            WriteStartupLog(message, exception);
        }

        private static async void WriteStartupLog(string message, Exception exception)
        {
            try
            {
                var line = string.Format(
                    "{0:O} {1}{2}",
                    DateTimeOffset.Now,
                    message,
                    exception == null ? string.Empty : Environment.NewLine + exception);

                RememberStartupLogLine(line);

                await StartupLogGate.WaitAsync();
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                        StartupLogFileName,
                        CreationCollisionOption.OpenIfExists);

                    await FileIO.AppendTextAsync(file, line + Environment.NewLine);
                }
                finally
                {
                    StartupLogGate.Release();
                }
            }
            catch
            {
            }
        }

        private static void RememberStartupLogLine(string line)
        {
            lock (StartupLogLines)
            {
                StartupLogLines.Add(line);
                if (StartupLogLines.Count > 50)
                {
                    StartupLogLines.RemoveAt(0);
                }

                ApplicationData.Current.LocalSettings.Values[StartupLogSettingsKey] = string.Join(
                    Environment.NewLine,
                    StartupLogLines);
            }
        }

        private static void OnApplicationUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogStartupException("UnhandledException", e.Exception);
        }

        #endregion
    }
}
