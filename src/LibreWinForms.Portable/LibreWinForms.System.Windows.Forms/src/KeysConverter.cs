using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// Converts portable WinForms key values to and from the invariant shortcut
/// strings used by menus, add-ins, and designers.
/// </summary>
public class KeysConverter : TypeConverter, IComparer
{
    private static readonly Dictionary<string, Keys> s_namedKeys =
        new Dictionary<string, Keys>(StringComparer.Ordinal)
        {
            ["(none)"] = Keys.None,
            [nameof(Keys.None)] = Keys.None,
            ["Ctrl"] = Keys.Control,
            [nameof(Keys.Control)] = Keys.Control,
            [nameof(Keys.Alt)] = Keys.Alt,
            [nameof(Keys.Shift)] = Keys.Shift,
            ["Enter"] = Keys.Return,
            [nameof(Keys.Return)] = Keys.Return,
            [nameof(Keys.End)] = Keys.End,
            ["PageDown"] = Keys.PageDown,
            ["Next"] = Keys.PageDown,
            [nameof(Keys.Insert)] = Keys.Insert,
            [nameof(Keys.Home)] = Keys.Home,
            [nameof(Keys.Delete)] = Keys.Delete,
            ["PageUp"] = Keys.PageUp,
            [nameof(Keys.Back)] = Keys.Back,
            [nameof(Keys.Backspace)] = Keys.Back
        };

    private static readonly Dictionary<Keys, string> s_displayNames =
        new Dictionary<Keys, string>
        {
            [Keys.None] = "(none)",
            [Keys.Return] = "Enter",
            [Keys.End] = "End",
            [Keys.PageDown] = "PageDown",
            [Keys.Insert] = "Insert",
            [Keys.Home] = "Home",
            [Keys.Delete] = "Delete",
            [Keys.PageUp] = "PageUp",
            [Keys.Back] = "Backspace",
            [Keys.D0] = "0",
            [Keys.D1] = "1",
            [Keys.D2] = "2",
            [Keys.D3] = "3",
            [Keys.D4] = "4",
            [Keys.D5] = "5",
            [Keys.D6] = "6",
            [Keys.D7] = "7",
            [Keys.D8] = "8",
            [Keys.D9] = "9"
        };

    private StandardValuesCollection? _standardValues;

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string)
            || sourceType == typeof(Enum[])
            || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            || destinationType == typeof(Enum[])
            || base.CanConvertTo(context, destinationType);
    }

    public int Compare(object? x, object? y)
    {
        return string.Compare(
            ConvertToInvariantString(x),
            ConvertToInvariantString(y),
            StringComparison.Ordinal);
    }

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            Keys result = Keys.None;
            bool foundKeyCode = false;
            foreach (string token in text.Split('+', StringSplitOptions.TrimEntries))
            {
                if (!TryParseToken(token, out Keys current))
                {
                    throw new ArgumentException($"'{token}' is not a valid value for Keys.", nameof(value));
                }

                if ((current & Keys.KeyCode) != Keys.None)
                {
                    if (foundKeyCode)
                    {
                        throw new FormatException("Key combinations may contain only one non-modifier key.");
                    }

                    foundKeyCode = true;
                }

                result |= current;
            }

            return result;
        }

        if (value is Enum[] values)
        {
            long result = 0;
            foreach (Enum item in values)
            {
                result |= Convert.ToInt64(item, CultureInfo.InvariantCulture);
            }

            return Enum.ToObject(typeof(Keys), result);
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is not Keys keys)
        {
            if (value is int numericValue)
            {
                keys = (Keys)numericValue;
            }
            else
            {
                return base.ConvertTo(context, culture, value, destinationType);
            }
        }

        if (destinationType == typeof(string))
        {
            return FormatKeys(keys);
        }

        if (destinationType == typeof(Enum[]))
        {
            return ToEnumArray(keys);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return _standardValues ??= new StandardValuesCollection(
            new object[]
            {
                Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4,
                Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
                Keys.Alt, Keys.Back, Keys.Control, Keys.Delete, Keys.End,
                Keys.Return, Keys.F1, Keys.F10, Keys.F11, Keys.F12,
                Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6,
                Keys.F7, Keys.F8, Keys.F9, Keys.Home, Keys.Insert,
                Keys.PageDown, Keys.PageUp, Keys.Shift
            });
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    private static bool TryParseToken(string token, out Keys key)
    {
        if (s_namedKeys.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            key = (Keys)((int)Keys.D0 + (token[0] - '0'));
            return true;
        }

        return Enum.TryParse(token, ignoreCase: false, out key);
    }

    private static string FormatKeys(Keys keys)
    {
        if (keys == Keys.None)
        {
            return "(none)";
        }

        var terms = new List<string>(4);
        Keys modifiers = keys & Keys.Modifiers;
        if ((modifiers & Keys.Control) == Keys.Control)
        {
            terms.Add("Ctrl");
        }

        if ((modifiers & Keys.Alt) == Keys.Alt)
        {
            terms.Add("Alt");
        }

        if ((modifiers & Keys.Shift) == Keys.Shift)
        {
            terms.Add("Shift");
        }

        Keys keyCode = keys & Keys.KeyCode;
        if (keyCode != Keys.None)
        {
            if (s_displayNames.TryGetValue(keyCode, out string? displayName))
            {
                terms.Add(displayName);
            }
            else if (Enum.IsDefined(keyCode))
            {
                terms.Add(Enum.GetName(keyCode)!);
            }
        }

        return string.Join('+', terms);
    }

    private static Enum[] ToEnumArray(Keys keys)
    {
        if (keys == Keys.None)
        {
            return new Enum[] { Keys.None };
        }

        var terms = new List<Enum>(4);
        Keys modifiers = keys & Keys.Modifiers;
        if ((modifiers & Keys.Control) == Keys.Control)
        {
            terms.Add(Keys.Control);
        }

        if ((modifiers & Keys.Alt) == Keys.Alt)
        {
            terms.Add(Keys.Alt);
        }

        if ((modifiers & Keys.Shift) == Keys.Shift)
        {
            terms.Add(Keys.Shift);
        }

        Keys keyCode = keys & Keys.KeyCode;
        if (keyCode != Keys.None && Enum.IsDefined(keyCode))
        {
            terms.Add(keyCode);
        }

        return terms.ToArray();
    }
}
