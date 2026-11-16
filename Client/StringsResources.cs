namespace Mixdrome
{
    using Mixdrome.Playback;

    public class StringResources
    {
        private static readonly PlaybackViewModelStrings PlaybackViewModelStringsInstance = new PlaybackViewModelStrings();

        public PlaybackViewModelStrings PlaybackViewModelStrings
        {
            get
            {
                return PlaybackViewModelStringsInstance;
            }
        }
    }
}