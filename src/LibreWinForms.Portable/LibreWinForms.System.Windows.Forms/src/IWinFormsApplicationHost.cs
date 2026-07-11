namespace System.Windows.Forms;

public interface IWinFormsApplicationHost
{
    void Run(Form mainForm);

    DialogResult ShowDialog(Form form, IWin32Window? owner);

    void ExitThread();
}

public interface IWinFormsTimerHost
{
    IDisposable RegisterTimer(int intervalMilliseconds, Action callback);
}

/// <summary>
/// Optional typed UI-thread dispatcher capability implemented by application hosts.
/// </summary>
/// <remarks>
/// The portable Forms layer owns delegate invocation and asynchronous-result state;
/// hosts only marshal strongly typed callbacks to their UI thread.
/// </remarks>
public interface IWinFormsDispatcherHost
{
    bool CheckAccess();

    void BeginInvoke(Action callback);

    void Invoke(Action callback);
}

/// <summary>
/// Optional typed drag/drop capability implemented by application hosts that can
/// run a native or portable UI drag session for a hosted Forms control.
/// </summary>
public interface IWinFormsDragDropHost
{
    DragDropEffects DoDragDrop(
        Control source,
        IDataObject data,
        DragDropEffects allowedEffects);
}

/// <summary>
/// Optional typed coordinate conversion capability for controls hosted inside a
/// native or portable top-level surface.
/// </summary>
public interface IWinFormsCoordinateHost
{
    bool TryPointToScreen(
        Control control,
        System.Drawing.Point point,
        out System.Drawing.Point screenPoint);

    bool TryPointToClient(
        Control control,
        System.Drawing.Point point,
        out System.Drawing.Point clientPoint);
}

/// <summary>
/// Optional typed extension implemented by application hosts that can end a
/// modal loop after a form publishes a non-none <see cref="Form.DialogResult"/>.
/// </summary>
public interface IWinFormsModalDialogHost
{
    void RequestDialogCompletion(Form form);
}

/// <summary>
/// Typed dialog-key entry point used by portable input hosts.
/// </summary>
public interface IWinFormsDialogKeyProcessor
{
    bool TryProcessDialogKey(Keys keyData, Control? focusedControl);
}
