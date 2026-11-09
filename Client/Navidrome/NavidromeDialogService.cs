namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

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
        Task<bool> ConfirmAsync(string messageKey);
    }

    public sealed class NavidromeDialogService : INavidromeDialogService
    {
        public async Task<bool> ConfirmAsync(string messageKey)
        {
            var dialog = new MessageDialog(NavidromeText.Get(messageKey));
            dialog.Commands.Add(new UICommand(NavidromeText.Get("Delete"), null, true));
            dialog.Commands.Add(new UICommand(NavidromeText.Get("Cancel"), null, false));
            dialog.DefaultCommandIndex = dialog.CancelCommandIndex = 1;
            return (bool)(await dialog.ShowAsync()).Id;
        }

        public Task<bool> FormAsync(FrameworkElement anchor, string titleKey, IList<FormField> fields, Func<string> validate = null)
        {
            var completion = new TaskCompletionSource<bool>();
            var panel = new StackPanel { Width = 280 };
            panel.Children.Add(new TextBlock { Text = NavidromeText.Get(titleKey), FontSize = 24, TextWrapping = TextWrapping.Wrap });
            var readers = new List<Action>();
            foreach (var field in fields)
            {
                panel.Children.Add(new TextBlock { Text = NavidromeText.Get(field.LabelKey), Margin = new Thickness(0, 16, 0, 4), TextWrapping = TextWrapping.Wrap });
                if (field.Kind == FormFieldKind.Boolean)
                {
                    var control = new ToggleSwitch { IsOn = field.Value is bool && (bool)field.Value };
                    readers.Add(() => field.Value = control.IsOn); panel.Children.Add(control);
                }
                else if (field.Kind == FormFieldKind.Choice)
                {
                    var control = new ComboBox { ItemsSource = field.Choices, DisplayMemberPath = field.DisplayMemberPath ?? string.Empty,
                        HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = field.Value };
                    readers.Add(() => field.Value = control.SelectedItem); panel.Children.Add(control);
                }
                else if (field.Kind == FormFieldKind.Date)
                {
                    var control = new DatePicker { Date = field.Value is DateTimeOffset ? (DateTimeOffset)field.Value : DateTimeOffset.Now.AddDays(7),
                        HorizontalAlignment = HorizontalAlignment.Stretch };
                    var original = control.Date;
                    readers.Add(() => field.Value = control.Date.Date == original.Date ? field.Value : control.Date); panel.Children.Add(control);
                }
                else
                {
                    var control = new TextBox { Text = field.Value as string ?? string.Empty, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
                    readers.Add(() => field.Value = control.Text); panel.Children.Add(control);
                }
            }
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(error);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            var save = new Button { Content = NavidromeText.Get("Save"), MinHeight = 40 };
            var cancel = new Button { Content = NavidromeText.Get("Cancel"), MinHeight = 40, Margin = new Thickness(12, 0, 0, 0) };
            commands.Children.Add(save); commands.Children.Add(cancel); panel.Children.Add(commands);
            var flyout = new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, Window.Current.Bounds.Height - 100) } };
            save.Click += (sender, args) =>
            {
                foreach (var read in readers) read();
                error.Text = validate == null ? string.Empty : validate() ?? string.Empty;
                if (!string.IsNullOrEmpty(error.Text)) return;
                completion.TrySetResult(true); flyout.Hide();
            };
            cancel.Click += (sender, args) => flyout.Hide();
            flyout.Closed += (sender, args) => completion.TrySetResult(false);
            flyout.ShowAt(anchor ?? (FrameworkElement)Window.Current.Content);
            return completion.Task;
        }
    }
}
