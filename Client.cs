using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Timer = System.Windows.Forms.Timer;

class NekoLinkClient
{
    static TcpClient controlClient;
    static NetworkStream controlStream;
    static bool keyboardLocked = false;
    static Form form;
    static Panel controlPanel;
    static Label statusLabel;
    static Button lockBtn;
    static Button unlockBtn;
    static Process ffmpegProcess;
    static StreamWriter log;
    static TextBox debugBox;
    
    [STAThread]
    static void Main(string[] args)
    {
        // Setup logging
        log = new StreamWriter("client_debug.txt", true);
        Log("Client starting...");
        
        string serverIp = args.Length > 0 ? args[0] : "";
        
        if (string.IsNullOrEmpty(serverIp))
        {
            serverIp = Microsoft.VisualBasic.Interaction.InputBox("Enter server IP:", "NekoLink", "192.168.1.", 500, 500);
            if (string.IsNullOrEmpty(serverIp)) return;
        }
        
        Log($"Server IP: {serverIp}");
        
        try
        {
            // Connect control channel
            Log("Connecting to control port 5901...");
            controlClient = new TcpClient();
            controlClient.Connect(serverIp, 5901);
            controlStream = controlClient.GetStream();
            Log("Control connected!");
        }
        catch (Exception ex)
        {
            Log($"Control connection failed: {ex.Message}");
            MessageBox.Show($"Could not connect to control server: {ex.Message}");
            return;
        }
        
        // Start ffmpeg video
        Log("Starting ffmpeg video...");
        StartVideo(serverIp);
        
        // Create GUI
        CreateWindow();
        
        Application.Run(form);
    }
    
    static void StartVideo(string serverIp)
    {
        try
        {
            string ffmpeg = "ffmpeg.exe";
            
            // Check if ffmpeg exists
            if (!File.Exists(ffmpeg))
            {
                Log($"ERROR: {ffmpeg} not found in current directory!");
                MessageBox.Show("ffmpeg.exe not found! Put it in the same folder.");
                return;
            }
            
            Log($"Launching: {ffmpeg} -i udp://{serverIp}:5900...");
            
            ffmpegProcess = new Process();
            ffmpegProcess.StartInfo.FileName = ffmpeg;
            ffmpegProcess.StartInfo.Arguments = $"-i udp://{serverIp}:5900?pkt_size=1316 -f sdl \"NekoLink - {serverIp}\"";
            ffmpegProcess.StartInfo.UseShellExecute = false;
            ffmpegProcess.StartInfo.CreateNoWindow = false;
            ffmpegProcess.StartInfo.RedirectStandardError = true;
            ffmpegProcess.StartInfo.RedirectStandardOutput = true;
            
            ffmpegProcess.Start();
            Log($"ffmpeg started with PID: {ffmpegProcess.Id}");
            
            // Read ffmpeg output for debugging
            ffmpegProcess.BeginErrorReadLine();
            ffmpegProcess.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                    Log($"ffmpeg: {e.Data}");
            };
        }
        catch (Exception ex)
        {
            Log($"ffmpeg error: {ex.Message}");
            MessageBox.Show($"Failed to start ffmpeg: {ex.Message}");
        }
    }
    
    static void CreateWindow()
    {
        form = new Form();
        form.Text = "NekoLink Control";
        form.Size = new Size(400, 300);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.TopMost = true;
        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        form.KeyPreview = true;
        
        controlPanel = new Panel();
        controlPanel.Dock = DockStyle.Fill;
        controlPanel.Padding = new Padding(10);
        
        Label infoLabel = new Label();
        infoLabel.Text = "NekoLink Connected";
        infoLabel.Dock = DockStyle.Top;
        infoLabel.Height = 30;
        infoLabel.TextAlign = ContentAlignment.MiddleCenter;
        infoLabel.Font = new Font("Arial", 10, FontStyle.Bold);
        
        statusLabel = new Label();
        statusLabel.Text = "Status: Unlocked";
        statusLabel.Dock = DockStyle.Top;
        statusLabel.Height = 25;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        
        FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
        buttonPanel.Dock = DockStyle.Top;
        buttonPanel.Height = 40;
        buttonPanel.FlowDirection = FlowDirection.LeftToRight;
        buttonPanel.Padding = new Padding(5);
        
        lockBtn = new Button();
        lockBtn.Text = "🔒 Lock";
        lockBtn.Size = new Size(80, 30);
        lockBtn.Click += (s, e) => Lock();
        
        unlockBtn = new Button();
        unlockBtn.Text = "🔓 Unlock";
        unlockBtn.Size = new Size(80, 30);
        unlockBtn.Click += (s, e) => Unlock();
        
        Button fullscreenBtn = new Button();
        fullscreenBtn.Text = "Fullscreen";
        fullscreenBtn.Size = new Size(80, 30);
        fullscreenBtn.Click += (s, e) => {
            if (ffmpegProcess != null && !ffmpegProcess.HasExited)
            {
                SetForegroundWindow(ffmpegProcess.MainWindowHandle);
                SendKeys.SendWait("%{ENTER}");
            }
        };
        
        buttonPanel.Controls.Add(lockBtn);
        buttonPanel.Controls.Add(unlockBtn);
        buttonPanel.Controls.Add(fullscreenBtn);
        
        // Debug output box
        debugBox = new TextBox();
        debugBox.Dock = DockStyle.Fill;
        debugBox.Multiline = true;
        debugBox.ScrollBars = ScrollBars.Vertical;
        debugBox.ReadOnly = true;
        debugBox.Font = new Font("Consolas", 8);
        debugBox.Text = "Debug output:\r\n";
        
        // Add debug updates from log
        Timer debugTimer = new Timer();
        debugTimer.Interval = 500;
        debugTimer.Tick += (s, e) => {
            if (log != null)
            {
                log.Flush();
                try
                {
                    if (File.Exists("client_debug.txt"))
                    {
                        string lastLines = File.ReadAllText("client_debug.txt");
                        if (debugBox.Text.Length > 10000)
                            debugBox.Text = "Debug output:\r\n" + lastLines.Substring(Math.Max(0, lastLines.Length - 5000));
                        else
                            debugBox.Text = "Debug output:\r\n" + lastLines;
                        debugBox.SelectionStart = debugBox.Text.Length;
                        debugBox.ScrollToCaret();
                    }
                }
                catch { }
            }
        };
        debugTimer.Start();
        
        controlPanel.Controls.Add(debugBox);
        controlPanel.Controls.Add(buttonPanel);
        controlPanel.Controls.Add(statusLabel);
        controlPanel.Controls.Add(infoLabel);
        
        form.Controls.Add(controlPanel);
        
        // Keyboard events
        form.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.RControlKey)
            {
                Unlock();
            }
            
            if (keyboardLocked && !e.Control)
            {
                SendKey((byte)e.KeyCode, true);
                Log($"Sent key: {e.KeyCode}");
            }
        };
        
        form.KeyUp += (s, e) => {
            if (keyboardLocked)
            {
                SendKey((byte)e.KeyCode, false);
            }
        };
        
        form.FormClosing += (s, e) => {
            Log("Closing...");
            try { 
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                    ffmpegProcess.Kill(); 
            } catch { }
        };
    }
    
    static void Lock()
    {
        keyboardLocked = true;
        statusLabel.Text = "Status: 🔒 LOCKED";
        statusLabel.ForeColor = Color.Red;
        form.Text = "NekoLink [LOCKED]";
        Log("Locked");
    }
    
    static void Unlock()
    {
        keyboardLocked = false;
        statusLabel.Text = "Status: 🔓 Unlocked";
        statusLabel.ForeColor = Color.Green;
        form.Text = "NekoLink Control";
        Log("Unlocked");
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
            Log($"Send error: {ex.Message}");
        }
    }
    
    static void SendKey(byte key, bool down)
    {
        SendCommand($"KEY,{key},{down}");
    }
    
    static void Log(string message)
    {
        try
        {
            string logMsg = $"{DateTime.Now:HH:mm:ss} - {message}";
            Console.WriteLine(logMsg);
            if (log != null)
            {
                log.WriteLine(logMsg);
                log.Flush();
            }
        }
        catch { }
    }
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
}
