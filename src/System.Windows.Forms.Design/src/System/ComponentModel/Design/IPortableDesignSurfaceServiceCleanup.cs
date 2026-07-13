// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.ComponentModel.Design;

/// <summary>
///  Provides explicit cleanup for the fixed service aliases owned by a portable design surface.
/// </summary>
public interface IPortableDesignSurfaceServiceCleanup
{
    /// <summary>
    ///  Removes all fixed service aliases that currently refer to the design surface's designer host.
    /// </summary>
    void RemoveDesignerHostServices();
}
