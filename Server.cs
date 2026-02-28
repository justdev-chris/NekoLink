using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Threading;

class NekoLinkServer
{
    static void Main()
    {
        TcpListener server = new TcpListener(System.Net.IPAddress.Any, 5900);
        server.Start();
        
        TcpClient client = server.AcceptTcpClient();
        NetworkStream stream = client.GetStream();
        
        var jpegCodec = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 70L); // 70% quality = smaller/faster
        
        while (true)
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
                }
            }
            Thread.Sleep(33); // ~30fps
        }
    }
    
    static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid)
                return codec;
        return null;
    }
}
