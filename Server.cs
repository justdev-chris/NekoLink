using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Threading;

class NekoLinkServer : Form
{
    static TcpListener server;
    static NotifyIcon trayIcon;
    static ContextMenuStrip trayMenu;
    static bool running = true;
    static StreamWriter log;
    
    [STAThread]
    static void Main()
    {
        // Force create log file immediately
        try
        {
            log = new StreamWriter("server_debug.txt", true);
            log.WriteLine($"{DateTime.Now:HH:mm:ss} - SERVER INITIALIZING");
            log.Flush();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create log: {ex.Message}");
            return;
        }
        
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        
        // Hide console
        var handle = GetConsoleWindow();
        ShowWindow(handle, 0);
        
        Log("Server starting...");
        
        // Try UPnP port forwarding
        SetupUPnP();
        
        // Show local IPs
        string ips = "Local IPs:\n";
        foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ips += ip + "\n";
        Log(ips);
        
        // Start server in background
        Thread serverThread = new Thread(RunServer);
        serverThread.Start();
        
        // Setup tray
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show IPs", null, (s, e) => ShowIPs());
        trayMenu.Items.Add("Show Public IP", null, (s, e) => ShowPublicIP());
        trayMenu.Items.Add("Forward Ports (UPnP)", null, (s, e) => SetupUPnP());
        trayMenu.Items.Add("Show Debug", null, (s, e) => ShowDebug());
        trayMenu.Items.Add("Exit", null, (s, e) => { 
            Log("Exit clicked");
            running = false; 
            trayIcon.Visible = false;
            Application.Exit(); 
            Environment.Exit(0);
        });
        
        trayIcon = new NotifyIcon();
        trayIcon.Text = "NekoLink Server";
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.ShowBalloonTip(1000, "NekoLink", "Server running in background", ToolTipIcon.Info);
        
        Application.ApplicationExit += (s, e) => {
            Log("Application exiting");
            running = false;
            trayIcon?.Dispose();
            log?.Close();
        };
        
        Log("Server initialized");
        Application.Run();
    }
    
    static void SetupUPnP()
    {
        try
        {
            string localIP = GetLocalIP();
            string upnpc = Path.Combine(Application.StartupPath, "upnpc.exe");
            
            if (File.Exists(upnpc))
            {
                Log("Found upnpc.exe, attempting port forwarding...");
                
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = upnpc;
                process.StartInfo.Arguments = $"-a {localIP} 5900 5900 TCP";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (output.Contains("success") || output.Contains("Added"))
                    Log("Port 5900 forwarded successfully");
                else
                    Log("Port 5900 forwarding failed - may need manual setup");
                
                process.StartInfo.Arguments = $"-a {localIP} 5901 5901 TCP";
                process.Start();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (output.Contains("success") || output.Contains("Added"))
                    Log("Port 5901 forwarded successfully");
                
                // Get public IP
                string publicIP = GetPublicIP();
                if (!string.IsNullOrEmpty(publicIP))
                    Log($"Public IP: {publicIP}");
            }
            else
            {
                Log("upnpc.exe not found in application folder");
                Log("Download from: https://github.com/kaklakariada/portmapper/releases");
            }
        }
        catch (Exception ex)
        {
            Log($"UPnP error: {ex.Message}");
        }
    }
    
    static string GetLocalIP()
    {
        foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && 
                !ip.ToString().StartsWith("169.254") &&
                !ip.ToString().StartsWith("127."))
                return ip.ToString();
        return "192.168.1.1";
    }
    
    static string GetPublicIP()
    {
        try
        {
            using (var webClient = new System.Net.WebClient())
            {
                string ip = webClient.DownloadString("http://api.ipify.org").Trim();
                return ip;
            }
        }
        catch
        {
            return null;
        }
    }
    
    static void RunServer()
    {
        try
        {
            server = new TcpListener(System.Net.IPAddress.Any, 5900);
            server.Start();
            Log($"Server started on port 5900");
            
            while (running)
            {
                Log("Waiting for client...");
                var client = server.AcceptTcpClient();
                Log($"Client connected: {client.Client.RemoteEndPoint}");
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (Exception ex)
        {
            Log($"Server error: {ex}");
        }
    }
    
    static void HandleClient(object obj)
    {
        var client = (TcpClient)obj;
        var stream = client.GetStream();
        
        var jpegCodec = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 70L);
        
        int frameCount = 0;
        DateTime lastLog = DateTime.Now;
        
        while (client.Connected && running)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                    
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, jpegCodec, encoderParams);
                        byte[] data = ms.ToArray();
                        
                        stream.Write(BitConverter.GetBytes(data.Length), 0, 4);
                        stream.Write(data, 0, data.Length);
                        
                        frameCount++;
                        if ((DateTime.Now - lastLog).TotalSeconds >= 10)
                        {
                            Log($"Streaming at ~{frameCount/10}fps");
                            frameCount = 0;
                            lastLog = DateTime.Now;
                        }
                    }
                }
                
                // Handle commands
                try
                {
                    if (stream.DataAvailable)
                    {
                        byte[] cmdBuffer = new byte[1024];
                        int read = stream.Read(cmdBuffer, 0, cmdBuffer.Length);
                        if (read > 0)
                        {
                            string cmd = System.Text.Encoding.ASCII.GetString(cmdBuffer, 0, read);
                            string[] parts = cmd.Split(',');
                            
                            if (parts[0] == "MOUSE" && parts.Length >= 3)
                            {
                                if (int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
                                {
                                    Cursor.Position = new Point(x, y);
                                }
                            }
                            else if (parts[0] == "CLICKDOWN" && parts.Length >= 4)
                            {
                                if (int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
                                {
                                    Cursor.Position = new Point(x, y);
                                    if (parts[3].Contains("Left"))
                                        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
                                    else if (parts[3].Contains("Right"))
                                        mouse_event(0x08, 0, 0, 0, UIntPtr.Zero);
                                }
                            }
                            else if (parts[0] == "CLICKUP" && parts.Length >= 2)
                            {
                                if (parts[1].Contains("Left"))
                                    mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
                                else if (parts[1].Contains("Right"))
                                    mouse_event(0x10, 0, 0, 0, UIntPtr.Zero);
                            }
                            else if (parts[0] == "KEY" && parts.Length >= 3)
                            {
                                if (byte.TryParse(parts[1], out byte key))
                                {
                                    uint flags = parts[2] == "True" ? 0u : 2u;
                                    keybd_event(key, 0, flags, UIntPtr.Zero);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Command error: {ex.Message}");
                }
                
                Thread.Sleep(33);
            }
            catch (Exception ex)
            {
                Log($"Client error: {ex.Message}");
                break;
            }
        }
        
        Log("Client disconnected");
        client.Close();
    }
    
    static void ShowIPs()
    {
        string ips = "Local IPs:\n";
        foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ips += ip + "\n";
        
        string publicIP = GetPublicIP();
        if (!string.IsNullOrEmpty(publicIP))
            ips += $"\nPublic IP: {publicIP}";
        else
            ips += "\nCould not get public IP";
            
        MessageBox.Show(ips, "NekoLink");
    }
    
    static void ShowPublicIP()
    {
        string publicIP = GetPublicIP();
        if (!string.IsNullOrEmpty(publicIP))
            MessageBox.Show($"Public IP: {publicIP}\n\nGive this to remote clients\nMake sure ports 5900/5901 are forwarded", "NekoLink");
        else
            MessageBox.Show("Could not get public IP", "NekoLink");
    }
    
    static void ShowDebug()
    {
        try
        {
            if (File.Exists("server_debug.txt"))
            {
                string content = File.ReadAllText("server_debug.txt");
                if (content.Length > 5000)
                    content = content.Substring(content.Length - 5000);
                MessageBox.Show(content, "Server Debug");
            }
            else
            {
                MessageBox.Show("No debug file yet", "Server Debug");
            }
        }
        catch { }
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
    
    static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid)
                return codec;
        return null;
    }
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}