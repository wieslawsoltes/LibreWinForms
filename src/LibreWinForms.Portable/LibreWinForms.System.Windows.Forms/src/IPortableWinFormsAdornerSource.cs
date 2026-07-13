namespace System.Windows.Forms;

/// <summary>
/// Provides a typed, reflection-free bridge for transient WinForms designer adornments that
/// must be painted after the hosted control's child tree.
/// </summary>
public interface IPortableWinFormsAdornerSource
{
    bool SupportsPortableAdornments { get; }

    long PortableAdornerVersion { get; }

    void PaintPortableAdornments(PaintEventArgs e);
}
