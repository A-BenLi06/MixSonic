namespace Mixdrome.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Windows.UI.Xaml;

    public enum FormFieldKind { Text, Boolean, Choice, Date }
    public sealed class FormField
    {
        public string LabelKey { get; set; }
        public FormFieldKind Kind { get; set; }
        public object Value { get; set; }
        public IEnumerable<object> Choices { get; set; }
        public string DisplayMemberPath { get; set; }
    }

    public interface INavidromeDialogService
    {
        Task<bool> FormAsync(FrameworkElement anchor, string titleKey, IList<FormField> fields, Func<string> validate = null);
        Task<bool> ConfirmAsync(string messageKey, string confirmKey = "Delete");
    }
}
