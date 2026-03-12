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

            var lobby = new LobbyForm();
            if (lobby.ShowDialog() != DialogResult.OK) return;

            if (lobby.IsMultiplayer)
            {
                GameEngine.InitializeWorld(lobby.SelectedCountry, lobby.GameSeed);
                Application.Run(new ControlPanelForm(lobby.MpClient!, lobby.MpPlayers!, lobby.ServerUrl, lobby.MinigamesEnabled));
            }
            else
            {
                GameEngine.InitializeWorld(lobby.SelectedCountry);
                Application.Run(new ControlPanelForm(lobby.ServerUrl, lobby.MinigamesEnabled));
            }
        }
    }
}
