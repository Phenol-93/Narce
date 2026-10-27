using System.Windows.Controls;
using Nacre.Services;
namespace Nacre.Views;
public partial class RecordEditorView : UserControl
{
    public RecordEditorView() => InitializeComponent();
    private void InvalidDate(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        Dialogs.Error("日期格式无效，请通过日历选择，或清空日期以取消有效期。");
        var picker = (DatePicker)sender;
        picker.Text = picker.SelectedDate?.ToString("d") ?? "";
    }
}
