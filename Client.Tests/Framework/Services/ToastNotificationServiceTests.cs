namespace Client.Tests.Framework.Services
{
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
    using Mixdrome.Framework.Services;

    [TestClass]
    public class ToastNotificationServiceTests
    {
        private ToastNotificationService _subject;

        [TestInitialize]
        public void Setup()
        {
            _subject = new ToastNotificationService();
        }
    }
}