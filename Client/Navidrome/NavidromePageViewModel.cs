namespace Subsonic8.Navidrome
{
    using System;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Services.Navidrome;
    using MugenInjection.Attributes;
    using Subsonic8.BottomBar;
    using Subsonic8.Framework.ViewModel;

    public abstract class NavidromePageViewModel : ViewModelBase
    {
        private bool _isLoading;
        private bool _isWorking;
        private string _error;
        protected int LoadVersion;
        protected bool PageActive;

        [Inject] public INavidromeSessionService Session { get; set; }
        [Inject] public IMediaQueueService MediaQueue { get; set; }
        [Inject] public INavidromeDialogService Dialogs { get; set; }
        [Inject] public IOfflineLibraryService Offline { get; set; }
        public bool IsLoading { get { return _isLoading; } protected set { _isLoading = value; NotifyOfPropertyChange(); Changed(); } }
        public bool IsWorking { get { return _isWorking; } private set { _isWorking = value; NotifyOfPropertyChange(); Changed(); } }
        public string Error { get { return _error; } protected set { _error = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(() => HasError); } }
        public bool HasError { get { return !string.IsNullOrEmpty(Error); } }
        public bool CanReload { get { return !IsWorking; } }

        protected override async void OnActivate()
        {
            base.OnActivate();
            PageActive = true;
            ErrorDialogViewModel.Hide();
            EventAggregator.PublishOnUIThread(new ChangeBottomBarMessage { BottomBarViewModel = null });
            Session.LibraryChanged += LibraryChanged;
            await Reload();
        }

        protected override void OnDeactivate(bool close)
        {
            PageActive = false;
            ++LoadVersion;
            Session.LibraryChanged -= LibraryChanged;
            base.OnDeactivate(close);
        }

        private async void LibraryChanged(object sender, EventArgs args) { if (PageActive) await Reload(); }
        public new abstract Task Reload();
        protected virtual void Changed() { NotifyOfPropertyChange(() => CanReload); }
        protected bool IsCurrent(int version, NavidromeApi api)
        { return PageActive && version == LoadVersion && api.AccountKey == Session.AccountKey; }
        protected void RequireAccount(NavidromeApi api)
        { if (api.AccountKey != Session.AccountKey) throw new OperationCanceledException(); }

        protected async Task RunOperation(Func<NavidromeApi, Task> operation)
        {
            await RunLocalOperation(async () =>
            {
                await Session.ConnectAsync();
                await operation(Session.CreateApi());
            });
        }

        protected async Task RunLocalOperation(Func<Task> operation)
        {
            if (IsWorking) return;
            IsWorking = true;
            Error = null;
            try
            {
                await operation();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Error = exception.Message; }
            finally { IsWorking = false; }
        }
    }
}
