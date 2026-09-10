// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public static partial class Clipboard
{
    private static DataObject? GetPortableDataObject()
    {
        ILibreDataTransfer? transfer = LibrePlatform.Current.Clipboard.GetData();
        if (transfer is null)
        {
            return null;
        }

        if (transfer is PortableClipboardDataTransfer owned)
        {
            return owned.DataObject;
        }

        DataObject dataObject = new();
        foreach (string format in transfer.Formats)
        {
#pragma warning disable WFDEV005 // The typed platform boundary preserves the public IDataObject contract.
            object? value = transfer.GetData(format, autoConvert: false);
#pragma warning restore WFDEV005
            if (value is not null)
            {
                dataObject.SetData(format, autoConvert: false, value);
            }
        }

        return dataObject;
    }

    private sealed class PortableClipboardDataTransfer(DataObject dataObject) : ILibreDataTransfer
    {
        internal DataObject DataObject { get; } = dataObject;

        public IReadOnlyList<string> Formats => DataObject.GetFormats(autoConvert: false);

        public bool Contains(string format, bool autoConvert)
            => DataObject.GetDataPresent(format, autoConvert);

#pragma warning disable WFDEV005 // The typed platform boundary preserves arbitrary IDataObject payloads.
        public object? GetData(string format, bool autoConvert)
            => DataObject.GetData(format, autoConvert);
#pragma warning restore WFDEV005
    }
}
#endif
