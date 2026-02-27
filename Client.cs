using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;

class NekoLinkClient
{
    static TcpClient client;
    static NetworkStream stream;
    static Form form;
    static PictureBox pictureBox;
    static bool keyboardLocked = false;
    
    static void Main(string[] args)
    {
        client = new TcpClient();
        client.Connect(args[0], 5900);
        stream = client.GetStream();
        
        form = new Form();
        form.Text = "NekoLink - Remote Desktop";
        form.WindowState = FormWindowState.Maximized;
        form.KeyPreview = true;
        
        pictureBox = new PictureBox();
        pictureBox.Dock = DockStyle.Fill;
        pictureBox.MouseMove += (s, e) => { if(keyboardLocked) SendMouse(e.X, e.Y); };
        pictureBox.MouseClick += (s, e) => { if(keyboardLocked) SendClick(e.X, e.Y, e.Button.ToString()); };
        pictureBox.Click += (s, e) => { keyboardLocked = true; form.Text = "NekoLink - Keyboard LOCKED (Press Right Ctrl to unlock)"; };
        
        form.Controls.Add(pictureBox);
        
        // Global key handler for unlock
        form.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.RControlKey)
            {
                keyboardLocked = false;
                form.Text = "NekoLink - Remote Desktop (Unlocked)";
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
        
        System.Threading.Thread pool = new System.Threading.Thread(ReceiveScreen);
        pool.Start();
        
        Application.Run(form);
    }
    
    static void ReceiveScreen()
{
    int frameCount = 0;
    DateTime lastTime = DateTime.Now;
    
    while (true)
    {
        byte[] lenBytes = new byte[4];
        stream.Read(lenBytes, 0, 4);
        int len = BitConverter.ToInt32(lenBytes, 0);
        
        byte[] imgData = new byte[len];
        int total = 0;
        while (total < len)
            total += stream.Read(imgData, total, len - total);
        
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(imgData))
        {
            Image img = Image.FromStream(ms);
            pictureBox.Invoke((MethodInvoker)delegate { 
                pictureBox.Image = (Image)img.Clone(); 
                
                // FPS counter
                frameCount++;
                if ((DateTime.Now - lastTime).TotalSeconds >= 1)
                {
                    form.Text = $"NekoLink - {frameCount} FPS";
                    frameCount = 0;
                    lastTime = DateTime.Now;
                }
            });
        }
    }
}
    
    static void SendMouse(int x, int y)
    {
        string cmd = $"MOUSE,{x},{y}";
        stream.Write(System.Text.Encoding.ASCII.GetBytes(cmd), 0, cmd.Length);
    }
    
    static void SendClick(int x, int y, string button)
    {
        string cmd = $"CLICK,{x},{y},{button}";
        stream.Write(System.Text.Encoding.ASCII.GetBytes(cmd), 0, cmd.Length);
    }
    
    static void SendKey(byte key, bool down)
    {
        string cmd = $"KEY,{key},{down}";
        stream.Write(System.Text.Encoding.ASCII.GetBytes(cmd), 0, cmd.Length);
    }
}
