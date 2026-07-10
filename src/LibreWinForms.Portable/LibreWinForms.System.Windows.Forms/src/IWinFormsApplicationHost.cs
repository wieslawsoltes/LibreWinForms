namespace System.Windows.Forms;

public interface IWinFormsApplicationHost
{
    void Run(Form mainForm);

    DialogResult ShowDialog(Form form, IWin32Window? owner);

    void ExitThread();
}
