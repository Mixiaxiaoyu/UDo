using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UDo.DesktopZones {
    internal interface IDesktopAdapter {
        IntPtr Host {get;}
        bool Position(string path,Point screenPoint);
        bool TryGetPosition(string path,out Point point);
        byte[] CaptureVisibility();
        void HideItems(string[] paths,byte[] original);
        void RestoreVisibility(byte[] original);
        ICollection<string> PositionMany(IDictionary<string,Point> positions);
        Point[] Occupied();
        string Import(string path);
        void EnableLayout();
        void RestoreLayout();
    }
    internal sealed class DesktopAdapter : IDesktopAdapter {
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int BoxCaptureClip(byte[] bytes,uint capacity,out uint length);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int BoxRestoreClip(byte[] bytes,uint length);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int BoxClipItems(int count,[MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr)] string[] paths,byte[] original,uint length);
        public byte[] CaptureVisibility() {uint length;Marshal.ThrowExceptionForHR(BoxCaptureClip(null,0,out length));byte[] bytes=new byte[length];if(length>0) Marshal.ThrowExceptionForHR(BoxCaptureClip(bytes,length,out length));return bytes;}
        public void HideItems(string[] paths,byte[] original) {Marshal.ThrowExceptionForHR(BoxClipItems(paths.Length,paths,original,(uint)original.Length));}
        public void RestoreVisibility(byte[] original) {Marshal.ThrowExceptionForHR(BoxRestoreClip(original,(uint)original.Length));}
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int ZoneDesktopHost(out IntPtr hwnd);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int ZonePositionPath(string path,ref Point point,bool write);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int ZoneFolderFlags(bool write,ref uint flags);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int ZonePositionBatch(int count,[MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr)] string[] paths,[In] Point[] points,[Out] int[] placed);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int ZoneDesktopPoints([Out] Point[] points,int capacity,out int written);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd,ref Point point);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
        [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child,IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        readonly string journal;
        internal DesktopAdapter(string journal) {this.journal=journal;}
        public IntPtr Host {get {IntPtr hwnd; Marshal.ThrowExceptionForHR(ZoneDesktopHost(out hwnd)); return hwnd;}}
        public void EnableLayout() {
            uint flags=0; Marshal.ThrowExceptionForHR(ZoneFolderFlags(false,ref flags));
            // Record before changing Explorer layout, including independent crash recovery.
            if(!File.Exists(journal)) {Directory.CreateDirectory(Path.GetDirectoryName(journal)); File.WriteAllText(journal,(flags&0x5).ToString());}
            flags=0; Marshal.ThrowExceptionForHR(ZoneFolderFlags(true,ref flags));
        }
        public void RestoreLayout() {Restore(journal);}
        internal static void Restore(string journal) {
            if(!File.Exists(journal)) return;
            try {uint flags=UInt32.Parse(File.ReadAllText(journal)); Marshal.ThrowExceptionForHR(ZoneFolderFlags(true,ref flags)); File.Delete(journal);} catch { }
        }
        public bool Position(string path,Point screenPoint) {
            // Shell view positions use its client coordinates, including negative-origin monitors.
            IntPtr host=Host; ScreenToClient(host,ref screenPoint);
            return ZonePositionPath(path,ref screenPoint,true)>=0;
        }
        public ICollection<string> PositionMany(IDictionary<string,Point> positions) {
            if(positions.Count==0) return new string[0];
            string[] paths=positions.Keys.ToArray();Point[] points=paths.Select(p=>positions[p]).ToArray();int[] placed=new int[paths.Length];IntPtr host=Host;
            for(int i=0;i<points.Length;i++) ScreenToClient(host,ref points[i]);
            Marshal.ThrowExceptionForHR(ZonePositionBatch(paths.Length,paths,points,placed));
            return paths.Where((p,i)=>placed[i]!=0).ToArray();
        }
        internal uint ReadFlags() {uint flags=0;Marshal.ThrowExceptionForHR(ZoneFolderFlags(false,ref flags));return flags;}
        public Point[] Occupied() {
            int count;Marshal.ThrowExceptionForHR(ZoneDesktopPoints(null,0,out count));
            var points=new Point[Math.Min(32768,count+32)];Marshal.ThrowExceptionForHR(ZoneDesktopPoints(points,points.Length,out count));
            IntPtr host=Host;for(int i=0;i<count;i++) ClientToScreen(host,ref points[i]);return points.Take(count).ToArray();
        }
        public bool TryGetPosition(string path,out Point point) {
            point=Point.Empty;if(ZonePositionPath(path,ref point,false)<0) return false;
            ClientToScreen(Host,ref point);return true;
        }
        public string Import(string path) {
            path=Path.GetFullPath(path);
            if(!File.Exists(path)&&!Directory.Exists(path)) throw new FileNotFoundException("文件已移动或不存在",path);
            string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string common=Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string directory=Path.GetDirectoryName(path);
            if(String.Equals(directory,desktop,StringComparison.OrdinalIgnoreCase)||String.Equals(directory,common,StringComparison.OrdinalIgnoreCase)) return path;
            // External drops add a desktop shortcut; never move the user's source file.
            string stem=Path.GetFileNameWithoutExtension(path), target=Path.Combine(desktop,stem+".lnk"); int suffix=2;
            while(File.Exists(target)) target=Path.Combine(desktop,stem+" ("+(suffix++)+").lnk");
            object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); object shortcut=null;
            try {
                shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{target});
                shortcut.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{path});
                shortcut.GetType().InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null); return target;
            } finally {if(shortcut!=null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);}
        }
    }
}
