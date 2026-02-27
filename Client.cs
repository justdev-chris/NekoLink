using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Threading;

class NekoLinkClient
{
    static TcpClient controlClient;
    static NetworkStream controlStream;
    static bool keyboardLocked = false;
    static Form form;
    static PictureBox pictureBox; // Placeholder, video shows in separate window
    
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Write("Enter server IP: ");
            string ip = Console.ReadLine();
            args = new[] { ip };
        }
        
        string serverIp = args[0];
        
        // Connect control channel
        controlClient = new TcpClient();
        controlClient.Connect(serverIp, 5901);
        controlStream = controlClient.GetStream();
        
        // Start video (ffmpeg window)
        string ffmpeg = "ffmpeg.exe";
        Process process = new Process();
        process.StartInfo.FileName = ffmpeg;
        process.StartInfo.Arguments = $"-i udp://{serverIp}:5900?pkt_size=1316 -f sdl NekoLink";
        process.StartInfo.UseShellExecute = false;
        process.Start();
        
        // Create control overlay window
        CreateOverlay();
        
        Application.Run(form);
    }
    
    static void CreateOverlay()
    {
        form = new Form();
        form.Text = "NekoLink Control";
        form.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        form.Size = new Size(300, 100);
        form.TopMost = true;
        
        Label lbl = new Label();
        lbl.Text = "NekoLink Active\nClick to lock, Right Ctrl to unlock";
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        form.Controls.Add(lbl);
        
        form.Click += (s, e) => {
            keyboardLocked = true;
            lbl.BackColor = Color.LightGreen;
            lbl.Text = "LOCKED - Right Ctrl to unlock";
        };
        
        form.KeyPreview = true;
        form.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.RControlKey)
            {
                keyboardLocked = false;
                lbl.BackColor = SystemColors.Control;
                lbl.Text = "NekoLink Active\nClick to lock, Right Ctrl to unlock";
            }
            
            if (keyboardLocked)
            {
                SendKey((byte)e.KeyCode, true);
            }
        };
        
        form.KeyUp += (s, e) => {
            if (keyboardLocked)
            {
                SendKey((byte)e.KeyCode, false);
            }
        };
    }
    
    static void SendCommand(string cmd)
    {
        try
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes(cmd);
            controlStream.Write(data, 0, data.Length);
        }
        catch { }
    }
    
    static void SendKey(byte key, bool down)
    {
        SendCommand($"KEY,{key},{down}");
    }
}
