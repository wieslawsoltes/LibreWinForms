namespace System.Windows.Forms;

/// <summary>
/// Provides a typed lifecycle notification for portable controls that own resources tied to
/// their current presentation host.
/// </summary>
/// <remarks>
/// Controls should acquire host-owned drawing or platform resources when attached and release
/// them when detached. The callbacks can run more than once across unload/reload cycles.
/// </remarks>
public interface IPortableWinFormsHostLifecycle
{
    void OnPortableHostAttached();

    void OnPortableHostDetached();
}
