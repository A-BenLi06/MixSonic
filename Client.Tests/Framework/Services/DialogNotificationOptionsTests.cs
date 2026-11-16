namespace Client.Tests.Framework.Services
{
    using FluentAssertions;
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
    using Mixdrome.Framework.Services;

    [TestClass]
    public class DialogNotificationOptionsTests
    {
        #region Fields

        private DialogNotificationOptions _subject;

        #endregion

        #region Public Methods and Operators

        [TestMethod]
        public void CtorShouldInitializePossibleActions()
        {
            _subject.PossibleActions.Should().NotBeNull();
        }

        [TestMethod]
        public void PossibleActionsAfterInitShouldReturnADeafaultAction()
        {
            _subject.PossibleActions.Count.Should().Be(1);
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _subject = new DialogNotificationOptions();
        }

        #endregion
    }
}