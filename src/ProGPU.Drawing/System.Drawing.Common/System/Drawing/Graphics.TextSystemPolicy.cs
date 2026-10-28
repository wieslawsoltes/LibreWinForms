using System.Drawing.Text;
using ProGPU.SystemDrawing;

namespace System.Drawing;

public partial class Graphics
{
    private Func<DrawingTextSystemSettings?>? _captureTextSystemSettings;
    private bool _capturingTextSystemSettings;
    private object? _textSystemSettingsIdentity;

    internal void BindTextSystemSettings(Func<DrawingTextSystemSettings?>? capture)
    {
        EnsureNotDisposed();
        _textSystemSettingsIdentity = new();
        _captureTextSystemSettings = capture;
    }

    internal DrawingGdiPlusTextPolicy CaptureGdiPlusTextPolicy()
    {
        EnsureNotDisposed();
        if (_capturingTextSystemSettings)
            throw new InvalidOperationException("A system text-policy observation cannot recursively capture itself.");
        TextRenderingHint hint = _textRenderingHint;
        int contrast = _textContrast;
        if (hint is not (TextRenderingHint.SystemDefault or TextRenderingHint.ClearTypeGridFit))
            return new(hint, contrast, null);
        if (_captureTextSystemSettings is not { } capture)
            return new(hint, contrast, DrawingTextSystemPolicy.CaptureCurrentSystemSettings());
        object? identity = _textSystemSettingsIdentity;
        DrawingTextSystemSettings? settings;
        _capturingTextSystemSettings = true;
        try { settings = capture(); }
        finally { _capturingTextSystemSettings = false; }
        EnsureNotDisposed();
        if (!ReferenceEquals(identity, _textSystemSettingsIdentity) ||
            !ReferenceEquals(capture, _captureTextSystemSettings) ||
            hint != _textRenderingHint || contrast != _textContrast)
            throw new InvalidOperationException("The source text policy changed during its system observation.");
        return new(hint, contrast, settings);
    }
}
