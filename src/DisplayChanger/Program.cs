namespace DisplayChanger;

internal static class Program
{
    private const string MutexName = @"Local\DisplayChanger.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Another copy is already running in the tray; exit silently.
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new TrayAppContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"DisplayChanger hit an unexpected error and will close.\n\n{ex}",
                "DisplayChanger",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
