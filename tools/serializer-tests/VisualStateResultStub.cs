namespace Common.Results
{
    using System.Threading.Tasks;

    /// <summary>Desktop test stub: original uses Windows.UI.Xaml to drive visual states.</summary>
    public class VisualStateResult : ResultBase
    {
        public VisualStateResult(string stateName, bool useTransitions = true)
        {
        }

        public override Task Execute()
        {
            return Task.FromResult(true);
        }

        protected override Task ExecuteCore()
        {
            return Task.FromResult(true);
        }
    }
}
