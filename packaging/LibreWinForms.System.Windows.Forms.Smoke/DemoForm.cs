namespace LibreWinForms.System.Windows.Forms.Smoke;

internal sealed class DemoForm : global::System.Windows.Forms.Form
{
    private readonly global::System.Windows.Forms.Design.AnchorEditor _anchorEditor = new();

    public DemoForm()
    {
        Text = "Canonical LibreWinForms package smoke";
        _ = _anchorEditor;
    }
}
