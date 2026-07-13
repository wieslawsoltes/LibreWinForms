// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Design;

/// <summary>
///  Provides the narrow ToolStrip keyboard state and navigation contract needed by portable designer hosts.
/// </summary>
public interface IPortableToolStripKeyboardHandlingService
{
    /// <summary>
    ///  Gets whether a ToolStrip template node currently owns keyboard input.
    /// </summary>
    bool TemplateNodeActive { get; }

    /// <summary>
    ///  Moves the current ToolStrip designer selection vertically.
    /// </summary>
    /// <param name="down"><see langword="true"/> to move down; <see langword="false"/> to move up.</param>
    void ProcessUpDown(bool down);
}
