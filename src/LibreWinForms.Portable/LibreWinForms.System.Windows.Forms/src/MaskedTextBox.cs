using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

public enum InsertKeyMode
{
    Default = 0,
    Insert = 1,
    Overwrite = 2
}

[Flags]
public enum MaskFormat
{
    ExcludePromptAndLiterals = 0,
    IncludePrompt = 1,
    IncludeLiterals = 2,
    IncludePromptAndLiterals = IncludePrompt | IncludeLiterals
}

public sealed class MaskInputRejectedEventArgs : EventArgs
{
    public MaskInputRejectedEventArgs(int position, MaskedTextResultHint rejectionHint)
    {
        Position = position;
        RejectionHint = rejectionHint;
    }

    public int Position { get; }

    public MaskedTextResultHint RejectionHint { get; }
}

public delegate void MaskInputRejectedEventHandler(object? sender, MaskInputRejectedEventArgs e);

public sealed class TypeValidationEventArgs : EventArgs
{
    public TypeValidationEventArgs(Type? validatingType, bool isValidInput, object? returnValue, string? message)
    {
        ValidatingType = validatingType;
        IsValidInput = isValidInput;
        ReturnValue = returnValue;
        Message = message;
    }

    public bool Cancel { get; set; }

    public bool IsValidInput { get; }

    public string? Message { get; }

    public object? ReturnValue { get; }

    public Type? ValidatingType { get; }
}

public delegate void TypeValidationEventHandler(object? sender, TypeValidationEventArgs e);

public class MaskedTextBox : TextBoxBase
{
    private MaskedTextProvider _provider;
    private string _mask;
    private MaskFormat _textMaskFormat = MaskFormat.IncludeLiterals;
    private bool _synchronizingText;

    public MaskedTextBox()
        : this(string.Empty)
    {
    }

    public MaskedTextBox(string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        _mask = mask;
        _provider = CreateProvider(mask);
        SynchronizeText();
    }

    public MaskedTextBox(MaskedTextProvider maskedTextProvider)
    {
        ArgumentNullException.ThrowIfNull(maskedTextProvider);
        _provider = (MaskedTextProvider)maskedTextProvider.Clone();
        _mask = _provider.Mask;
        SynchronizeText();
    }

    public event MaskInputRejectedEventHandler? MaskInputRejected;

    public event TypeValidationEventHandler? TypeValidationCompleted;

    [DefaultValue("")]
    public string Mask
    {
        get => _mask;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.Equals(_mask, value, StringComparison.Ordinal))
            {
                return;
            }

            string input = Text;
            _mask = value;
            RecreateProvider(input);
        }
    }

    [DefaultValue('_')]
    public char PromptChar
    {
        get => _provider.PromptChar;
        set
        {
            if (value == _provider.PromptChar)
            {
                return;
            }

            string input = Text;
            _provider.PromptChar = value;
            SetProviderText(input);
        }
    }

    [DefaultValue(typeof(MaskFormat), nameof(MaskFormat.IncludeLiterals))]
    public MaskFormat TextMaskFormat
    {
        get => _textMaskFormat;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(MaskFormat));
            }

            if (_textMaskFormat != value)
            {
                _textMaskFormat = value;
                SynchronizeText();
            }
        }
    }

    public MaskFormat CutCopyMaskFormat { get; set; } = MaskFormat.IncludeLiterals;

    public bool AsciiOnly
    {
        get => _provider.AsciiOnly;
        set => RecreateProvider(Text, asciiOnly: value);
    }

    public bool BeepOnError { get; set; }

    public bool HidePromptOnLeave { get; set; }

    public InsertKeyMode InsertKeyMode { get; set; }

    public bool IsOverwriteMode => InsertKeyMode == InsertKeyMode.Overwrite;

    public bool MaskCompleted => string.IsNullOrEmpty(_mask) || _provider.MaskCompleted;

    public bool MaskFull => string.IsNullOrEmpty(_mask) || _provider.MaskFull;

    public bool RejectInputOnFirstFailure { get; set; }

    public bool ResetOnPrompt
    {
        get => _provider.ResetOnPrompt;
        set => _provider.ResetOnPrompt = value;
    }

    public bool ResetOnSpace
    {
        get => _provider.ResetOnSpace;
        set => _provider.ResetOnSpace = value;
    }

    public bool SkipLiterals
    {
        get => _provider.SkipLiterals;
        set => _provider.SkipLiterals = value;
    }

    public HorizontalAlignment TextAlign { get; set; }

    public Type? ValidatingType { get; set; }

    public object? ValidateText()
    {
        if (ValidatingType is null)
        {
            return null;
        }

        object? result = null;
        string? message = null;
        bool valid;
        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(ValidatingType);
            result = converter.ConvertFromString(null, CultureInfo.CurrentCulture, Text);
            valid = true;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            valid = false;
            message = exception.Message;
        }

        var args = new TypeValidationEventArgs(ValidatingType, valid, result, message);
        TypeValidationCompleted?.Invoke(this, args);
        return valid && !args.Cancel ? result : null;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!_synchronizingText)
        {
            SetProviderText(base.Text);
        }

        base.OnTextChanged(e);
    }

    private MaskedTextProvider CreateProvider(string mask, bool? asciiOnly = null)
    {
        string effectiveMask = string.IsNullOrEmpty(mask) ? "&" : mask;
        return new MaskedTextProvider(
            effectiveMask,
            CultureInfo.CurrentCulture,
            allowPromptAsInput: true,
            promptChar: '_',
            passwordChar: '\0',
            asciiOnly ?? false);
    }

    private void RecreateProvider(string input, bool? asciiOnly = null)
    {
        char prompt = _provider.PromptChar;
        bool resolvedAsciiOnly = asciiOnly ?? _provider.AsciiOnly;
        _provider = CreateProvider(_mask, resolvedAsciiOnly);
        _provider.PromptChar = prompt;
        SetProviderText(input);
    }

    private void SetProviderText(string input)
    {
        if (string.IsNullOrEmpty(_mask))
        {
            SynchronizeText(input);
            return;
        }

        _provider.Clear();
        if (!_provider.Set(input, out int testPosition, out MaskedTextResultHint resultHint))
        {
            MaskInputRejected?.Invoke(this, new MaskInputRejectedEventArgs(testPosition, resultHint));
        }

        SynchronizeText();
    }

    private void SynchronizeText() => SynchronizeText(
        string.IsNullOrEmpty(_mask)
            ? base.Text
            : _provider.ToString(
                ignorePasswordChar: false,
                includePrompt: (_textMaskFormat & MaskFormat.IncludePrompt) != 0,
                includeLiterals: (_textMaskFormat & MaskFormat.IncludeLiterals) != 0,
                startPosition: 0,
                length: _provider.Length));

    private void SynchronizeText(string value)
    {
        if (string.Equals(base.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _synchronizingText = true;
        try
        {
            base.Text = value;
        }
        finally
        {
            _synchronizingText = false;
        }
    }
}
