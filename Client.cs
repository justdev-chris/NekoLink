using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.VisualStyles;

class NekoLinkClient
{
    static TcpClient controlClient;
    static NetworkStream controlStream;
    static bool keyboardLocked = false;
    static Form form;
    static PictureBox pictureBox;
    
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Write("Enter server IP: ");
            string ip = Console.ReadLine();
            args = new[] { ip };
        }
        
        string serverIp = args[0];
        Console.WriteLine($"Connecting to {serverIp}...");
        
        try
        {
            // Connect control channel
            controlClient = new TcpClient();
            controlClient.Connect(serverIp, 5901);
            controlStream = controlClient.GetStream();
            Console.WriteLine("Control channel connected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect control channel: {ex.Message}");
            return;
        }
        
        // Start video (ffmpeg window)
        try
        {
            string ffmpeg = "ffmpeg.exe";
            Process process = new Process();
            process.StartInfo.FileName = ffmpeg;
            process.StartInfo.Arguments = $"-i udp://{serverIp}:5900?pkt_size=1316 -f sdl \"NekoLink - {serverIp}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            Console.WriteLine("Video player started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start video: {ex.Message}");
            Console.WriteLine("Make sure ffmpeg.exe is in the same folder");
        }
        
        // Create control overlay window
        CreateOverlay();
        
        Application.Run(form);
    }
    
    static void CreateOverlay()
    {
        form = new Form();
        form.Text = "NekoLink Control";
        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        form.Size = new System.Drawing.Size(300, 120);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.TopMost = true;
        form.KeyPreview = true;
        
        Label lbl = new Label();
        lbl.Text = "NekoLink Active\nClick to lock\nRight Ctrl to unlock";
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lbl.Font = new Font("Arial", 10, FontStyle.Bold);
        form.Controls.Add(lbl);
        
        form.Click += (s, e) => {
            keyboardLocked = true;
            lbl.BackColor = System.Drawing.Color.LightGreen;
            lbl.Text = "🔒 LOCKED\nRight Ctrl to unlock";
        };
        
        form.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.RControlKey)
            {
                keyboardLocked = false;
                lbl.BackColor = SystemColors.Control;
                lbl.Text = "NekoLink Active\nClick to lock\nRight Ctrl to unlock";
                Console.WriteLine("Unlocked");
            }
            
            if (keyboardLocked && !e.Control)
            {
                SendKey((byte)e.KeyCode, true);
                Console.WriteLine($"Key down: {e.KeyCode}");
            }
        };
        
        form.KeyUp += (s, e) => {
            if (keyboardLocked && !e.Control)
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
        catch (Exception ex)
        {
            Console.WriteLine($"Send error: {ex.Message}");
        }
    }
    
    static void SendKey(byte key, bool down)
    {
        SendCommand($"KEY,{key},{down}");
    }
}
