namespace Mixdrome.Framework
{
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Mixdrome.Framework.Interfaces;

    public interface INotificationsHelper : IToastNotificationCapable, 
                                            ITileNotificationCapable, 
                                            IHandle<StartPlaybackMessage>
    {
    }
}