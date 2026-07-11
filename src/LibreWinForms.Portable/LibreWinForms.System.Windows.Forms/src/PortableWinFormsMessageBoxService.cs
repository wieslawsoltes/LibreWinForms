using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

internal static class PortableWinFormsMessageBoxService
{
    private static readonly MessageBoxServiceRegistrar s_registrar = new();
    private static IDisposable? s_registrarRegistration;
    private static Func<PortableMessageBoxRequest, string?>? s_show;

    [ModuleInitializer]
    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterMessageBoxService(s_registrar);
    }

    internal static DialogResult Show(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxOptions options,
        DialogResult fallbackResult)
    {
        RegisterPortableInteropService();

        Func<PortableMessageBoxRequest, string?>? show = Volatile.Read(ref s_show);
        if (show is null)
        {
            return fallbackResult;
        }

        var request = new PortableMessageBoxRequest(
            owner,
            text,
            caption,
            GetButtonName(buttons),
            GetIconName(icon),
            GetResultName(fallbackResult),
            options.ToString(),
            GetResultName(fallbackResult));
        return ConvertResult(fallbackResult, show(request));
    }

    private static DialogResult ConvertResult(DialogResult fallbackResult, string? resultName)
    {
        return resultName switch
        {
            null => fallbackResult,
            nameof(DialogResult.None) => DialogResult.None,
            nameof(DialogResult.OK) => DialogResult.OK,
            nameof(DialogResult.Cancel) => DialogResult.Cancel,
            nameof(DialogResult.Abort) => DialogResult.Abort,
            nameof(DialogResult.Retry) => DialogResult.Retry,
            nameof(DialogResult.Ignore) => DialogResult.Ignore,
            nameof(DialogResult.Yes) => DialogResult.Yes,
            nameof(DialogResult.No) => DialogResult.No,
            nameof(DialogResult.TryAgain) => DialogResult.TryAgain,
            nameof(DialogResult.Continue) => DialogResult.Continue,
            _ => throw new InvalidOperationException(
                $"Portable message box handler returned an invalid result '{resultName}'.")
        };
    }

    private static string GetButtonName(MessageBoxButtons buttons)
    {
        return buttons switch
        {
            MessageBoxButtons.OK => nameof(MessageBoxButtons.OK),
            MessageBoxButtons.OKCancel => nameof(MessageBoxButtons.OKCancel),
            MessageBoxButtons.AbortRetryIgnore => nameof(MessageBoxButtons.AbortRetryIgnore),
            MessageBoxButtons.YesNoCancel => nameof(MessageBoxButtons.YesNoCancel),
            MessageBoxButtons.YesNo => nameof(MessageBoxButtons.YesNo),
            MessageBoxButtons.RetryCancel => nameof(MessageBoxButtons.RetryCancel),
            MessageBoxButtons.CancelTryContinue => nameof(MessageBoxButtons.CancelTryContinue),
            _ => throw new InvalidOperationException($"Unsupported message box button set '{buttons}'.")
        };
    }

    private static string GetIconName(MessageBoxIcon icon)
    {
        return icon switch
        {
            MessageBoxIcon.None => nameof(MessageBoxIcon.None),
            MessageBoxIcon.Hand => nameof(MessageBoxIcon.Error),
            MessageBoxIcon.Question => nameof(MessageBoxIcon.Question),
            MessageBoxIcon.Exclamation => nameof(MessageBoxIcon.Warning),
            MessageBoxIcon.Asterisk => nameof(MessageBoxIcon.Information),
            _ => throw new InvalidOperationException($"Unsupported message box icon '{icon}'.")
        };
    }

    private static string GetResultName(DialogResult result)
    {
        return result switch
        {
            DialogResult.None => nameof(DialogResult.None),
            DialogResult.OK => nameof(DialogResult.OK),
            DialogResult.Cancel => nameof(DialogResult.Cancel),
            DialogResult.Abort => nameof(DialogResult.Abort),
            DialogResult.Retry => nameof(DialogResult.Retry),
            DialogResult.Ignore => nameof(DialogResult.Ignore),
            DialogResult.Yes => nameof(DialogResult.Yes),
            DialogResult.No => nameof(DialogResult.No),
            DialogResult.TryAgain => nameof(DialogResult.TryAgain),
            DialogResult.Continue => nameof(DialogResult.Continue),
            _ => throw new InvalidOperationException($"Unsupported dialog result '{result}'.")
        };
    }

    private static Registration Register(Func<PortableMessageBoxRequest, string?> show)
    {
        ArgumentNullException.ThrowIfNull(show);

        Volatile.Write(ref s_show, show);
        return new Registration(show);
    }

    private static void Clear()
    {
        Volatile.Write(ref s_show, null);
    }

    private sealed class MessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.WinForms;

        public IDisposable Register(Func<PortableMessageBoxRequest, string?> show)
        {
            return PortableWinFormsMessageBoxService.Register(show);
        }

        public void Clear()
        {
            PortableWinFormsMessageBoxService.Clear();
        }
    }

    private sealed class Registration : IDisposable
    {
        private Func<PortableMessageBoxRequest, string?>? _show;

        public Registration(Func<PortableMessageBoxRequest, string?> show)
        {
            _show = show;
        }

        public void Dispose()
        {
            Func<PortableMessageBoxRequest, string?>? show = _show;
            if (show is null)
            {
                return;
            }

            _show = null;
            if (ReferenceEquals(Volatile.Read(ref s_show), show))
            {
                Volatile.Write(ref s_show, null);
            }
        }
    }
}
