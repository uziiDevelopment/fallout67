using System;
using System.Windows.Forms;
using Velopack;

namespace fallover_67
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            VelopackApp.Build().Run();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Load persistent player profile
            ProfileManager.Load();

            while (true)
            {
                var lobby = new LobbyForm();
                if (lobby.ShowDialog() != DialogResult.OK) break;

                // Record match start in profile
                ProfileManager.RecordGameStart(lobby.SelectedCountry, lobby.IsMultiplayer);

                ControlPanelForm mainForm;

                if (lobby.IsMultiplayer)
                {
                    GameEngine.InitializeWorld(lobby.SelectedCountry, lobby.GameSeed);
                    mainForm = new ControlPanelForm(lobby.MpClient!, lobby.MpPlayers!, lobby.ServerUrl, lobby.MinigamesEnabled);
                }
                else
                {
                    GameEngine.InitializeWorld(lobby.SelectedCountry);
                    mainForm = new ControlPanelForm(lobby.ServerUrl, lobby.MinigamesEnabled);
                }

                Application.Run(mainForm);
            }
        }
    }
}
