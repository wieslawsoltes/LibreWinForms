namespace System.Windows.Forms;

/// <summary>
/// Provides a typed, reflection-free bridge from a portable WinForms control to a host-owned
/// <see cref="System.Drawing.Graphics"/> recording surface.
/// </summary>
public interface IPortableWinFormsPaintSource
{
    bool SupportsPortablePainting { get; }

    long PortablePaintVersion { get; }

    void PaintPortableBackground(PaintEventArgs e);

    void PaintPortable(PaintEventArgs e);
}
