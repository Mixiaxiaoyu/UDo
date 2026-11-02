using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

// Animation packets are transient. Statistics retain only per-day counters.
internal sealed class KeyVisualizer : IDisposable {
    delegate IntPtr KeyboardProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,KeyboardProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    readonly KeyboardProc callback;
    readonly int[] last=new int[256];
    readonly KeyStatistics statistics;
    IntPtr hook;
    UdpClient socket;
    string token;
    internal bool Enabled { get {return socket!=null && hook!=IntPtr.Zero;} }

    internal KeyVisualizer(KeyStatistics stats) {callback=OnKeyboard;statistics=stats;}
    internal void Configure(bool enabled,int port,string sessionToken) {
        if(socket!=null) {socket.Close();socket=null;}
        token=null;Array.Clear(last,0,last.Length);
        if(!enabled) {UpdateHook();return;}
        if(port<1024 || port>65535 || String.IsNullOrEmpty(sessionToken) || sessionToken.Length!=32) throw new ArgumentException("Invalid visualization endpoint");
        token=sessionToken;
        socket=new UdpClient();
        socket.Connect(IPAddress.Loopback,port);
        socket.Client.Blocking=false;
        UpdateHook();
    }
    internal void ConfigureStatistics(bool enabled) {statistics.Configure(enabled);UpdateHook();}
    void UpdateHook() {
        bool needed=socket!=null || statistics.Enabled;
        if(!needed && hook!=IntPtr.Zero) {UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
        if(needed && hook==IntPtr.Zero) {
            hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero) {int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error);}
        }
    }
    static bool Down(int key) {return (GetAsyncKeyState(key)&0x8000)!=0;}
    IntPtr OnKeyboard(int code,IntPtr message,IntPtr data) {
        if(code>=0 && (message.ToInt64()==0x100 || message.ToInt64()==0x104 || message.ToInt64()==0x101 || message.ToInt64()==0x105)) {
            int key=Marshal.ReadInt32(data), flags=Marshal.ReadInt32(data,8);
            int statisticsKey=key==13 && (flags&1)!=0 ? 269 : key; // keypad Enter
            statistics.Observe(statisticsKey,message.ToInt64()==0x100 || message.ToInt64()==0x104,(flags&0x10)!=0);
        }
        if(socket!=null && code>=0 && (message.ToInt64()==0x100 || message.ToInt64()==0x104)) {
            int key=Marshal.ReadInt32(data);
            if(((key>=65 && key<=90)||(key>=48 && key<=57)) && !Down(0x11) && !Down(0x12) && !Down(0x5B) && !Down(0x5C)) {
                int now=Environment.TickCount;
                // A held key can pulse, but cannot create an unbounded stream.
                if(last[key]==0 || unchecked(now-last[key])>=65) {
                    last[key]=now;
                    byte[] packet=Encoding.ASCII.GetBytes(token+":"+key.ToString());
                    try {socket.Send(packet,packet.Length);} catch(SocketException) {} catch(ObjectDisposedException) {}
                }
            }
        }
        // Never consume or modify input to the user's application.
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose() {
        if(hook!=IntPtr.Zero) {UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
        if(socket!=null) {socket.Close();socket=null;}
        token=null; Array.Clear(last,0,last.Length);
        statistics.Configure(false);
    }
}
