using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Launcher {
    sealed class Splash : Form {
        [StructLayout(LayoutKind.Sequential)] struct Point32 {public int X,Y;public Point32(int x,int y) {X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)] struct Size32 {public int Width,Height;public Size32(int width,int height) {Width=width;Height=height;}}
        [StructLayout(LayoutKind.Sequential)] struct Blend {public byte Operation,Flags,Alpha,Format;}
        [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader {
            public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;
            public int XPelsPerMeter,YPelsPerMeter;public uint ColorsUsed,ColorsImportant;
        }
        [StructLayout(LayoutKind.Sequential)] struct BitmapInfo {public BitmapInfoHeader Header;public uint Color;}
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destinationDc,ref Point32 destination,ref Size32 size,IntPtr sourceDc,ref Point32 source,uint key,ref Blend blend,uint flags);
        readonly Process process;
        readonly string marker;
        readonly string iconPath;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        IntPtr memoryDc,bitmap,oldBitmap;
        int side;
        bool finishing;
        internal Splash(Process process,string marker,string iconPath) {
            this.process=process;this.marker=marker;this.iconPath=iconPath;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            AutoScaleMode=AutoScaleMode.None;
            ClientSize=new Size(104,104);
            StartPosition=FormStartPosition.Manual;
            var bounds=Screen.PrimaryScreen.Bounds;
            Location=new Point(bounds.Left+(bounds.Width-Width)/2,bounds.Top+(bounds.Height-Height)/2);
            timer.Interval=16;
            timer.Tick+=delegate {
                if(!finishing && (File.Exists(marker)||process.HasExited)) finishing=true;
                byte next=(byte)(finishing?Math.Max(0,alpha-41):Math.Min(255,alpha+31));
                SetAlpha(next);
                if(finishing && alpha==0) Close();
            };
            Shown+=delegate {
                side=Math.Max(ClientSize.Width,ClientSize.Height);
                ClientSize=new Size(side,side);
                var screen=Screen.PrimaryScreen.Bounds;
                Location=new Point(screen.Left+(screen.Width-Width)/2,screen.Top+(screen.Height-Height)/2);
                PreparePixels(this.iconPath);SetAlpha(0);timer.Start();
            };
        }
        byte alpha;
        protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x80000;return p;}}
        void PreparePixels(string path) {
            using(var source=Image.FromFile(path))
            using(var pixels=new Bitmap(side,side,PixelFormat.Format32bppPArgb)) {
                using(var graphics=Graphics.FromImage(pixels)) {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source,new Rectangle(0,0,side,side));
                }
                var info=new BitmapInfo {Header=new BitmapInfoHeader {Size=40,Width=side,Height=-side,Planes=1,Bits=32}};
                IntPtr bits;
                bitmap=CreateDIBSection(IntPtr.Zero,ref info,0,out bits,IntPtr.Zero,0);
                if(bitmap==IntPtr.Zero||bits==IntPtr.Zero) throw new InvalidOperationException("无法准备启动 Logo 图像");
                var data=pixels.LockBits(new Rectangle(0,0,side,side),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try {
                    var bytes=new byte[side*side*4];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);Marshal.Copy(bytes,0,bits,bytes.Length);
                } finally {pixels.UnlockBits(data);}
                memoryDc=CreateCompatibleDC(IntPtr.Zero);
                if(memoryDc==IntPtr.Zero) throw new InvalidOperationException("无法准备启动 Logo 画布");
                oldBitmap=SelectObject(memoryDc,bitmap);
            }
        }
        void SetAlpha(byte value) {
            alpha=value;
            if(!IsHandleCreated||memoryDc==IntPtr.Zero) return;
            var point=new Point32(Left,Top);var size=new Size32(side,side);var origin=new Point32(0,0);
            var blend=new Blend {Operation=0,Flags=0,Alpha=value,Format=1};
            if(!UpdateLayeredWindow(Handle,IntPtr.Zero,ref point,ref size,memoryDc,ref origin,0,ref blend,2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override void Dispose(bool disposing) {
            if(disposing) {
                timer.Dispose();
                if(memoryDc!=IntPtr.Zero) {SelectObject(memoryDc,oldBitmap);DeleteDC(memoryDc);memoryDc=IntPtr.Zero;}
                if(bitmap!=IntPtr.Zero) {DeleteObject(bitmap);bitmap=IntPtr.Zero;}
            }
            base.Dispose(disposing);
        }
    }
    [STAThread] static void Main() {
        bool first;
        using(var mutex=new Mutex(true,"Local\\LumaDock.Launcher.v1",out first)) {
            if(!first) return;
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string config=Path.Combine(root,"godot-path.txt");
            string engine=File.Exists(config)?File.ReadAllText(config).Trim():"";
            if(!File.Exists(engine)) {
                using(var picker=new OpenFileDialog {Title="选择 Godot 4 Windows 可执行文件",Filter="Godot 引擎|*.exe"}) {
                    if(picker.ShowDialog()!=DialogResult.OK) return;
                    engine=picker.FileName; File.WriteAllText(config,engine);
                }
            }
            try {
                string marker=Path.Combine(Path.GetTempPath(),"udo-launch-"+Guid.NewGuid().ToString("N")+".ready");
                string arguments="--path \""+root.TrimEnd('\\')+"\" -- --launch-ready=\""+marker+"\"";
                using(var process=Process.Start(new ProcessStartInfo(engine,arguments) {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
                    using(var splash=new Splash(process,marker,Path.Combine(root,"assets","udo.png"))) Application.Run(splash);
                    process.WaitForExit();
                }
                if(File.Exists(marker)) File.Delete(marker);
            } catch(Exception e) { MessageBox.Show(e.Message,"有兜 · UDo 启动失败"); }
        }
    }
}
