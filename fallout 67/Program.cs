using System;
using System.Windows.Forms;

namespace fallover_67
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var lobby = new LobbyForm();
            if (lobby.ShowDialog() != DialogResult.OK) return;

            if (lobby.IsMultiplayer)
            {
                // Multiplayer: world is initialised with the shared seed from the server
                GameEngine.InitializeWorld(lobby.SelectedCountry, lobby.GameSeed);
                Application.Run(new ControlPanelForm(lobby.MpClient!, lobby.MpPlayers!));
            }
            else
            {
                // Singleplayer: normal flow
                GameEngine.InitializeWorld(lobby.SelectedCountry);
                Application.Run(new ControlPanelForm());
            }
        }
    }
}
