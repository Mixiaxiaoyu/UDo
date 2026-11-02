using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Explorer's classic notification toolbar (Windows 10, including overflow).
// Read only icon metadata; never inject code, change registrations or enumerate
// arbitrary background processes. Unknown layouts fail closed.
internal sealed class TrayApps : IDisposable {
    internal sealed class Entry {
        internal IntPtr Owner, Toolbar;
        internal uint Pid, IconId;
        internal int Index;
        internal string Path, Title;
        internal string Key {get {return Pid+":"+Owner.ToInt64()+":"+IconId;}}
    }
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,Native.EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] bytes,UIntPtr size,out UIntPtr read);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr process,out bool wow);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    volatile Entry[] snapshot=new Entry[0];
    volatile bool disposed;
    internal volatile string LastError="";
    int scanning;
    internal Entry[] Snapshot {get {return snapshot;}}

    internal void Refresh() {
        if(disposed || Interlocked.CompareExchange(ref scanning,1,0)!=0) return;
        ThreadPool.QueueUserWorkItem(delegate {
            try {var result=Scan(); if(!disposed) {snapshot=result; LastError="";}}
            catch(Exception e) {LastError=e.Message; /* Keep last good snapshot on transient Explorer failure. */ }
            finally {Interlocked.Exchange(ref scanning,0);}
        });
    }
    internal static Entry[] Scan() {
        var bars=new List<IntPtr>();
        Native.EnumWindows(delegate(IntPtr h,IntPtr ignored) {
            string cls=Native.Class(h);
            if(cls!="Shell_TrayWnd" && cls!="NotifyIconOverflowWindow" && cls!="Shell_NotifyIconOverflowWindow") return true;
            EnumChildWindows(h,delegate(IntPtr child,IntPtr unused) {
                if(Native.Class(child)!="ToolbarWindow32") return true;
                IntPtr ancestor=GetParent(child);
                for(int depth=0;ancestor!=IntPtr.Zero && depth<16;depth++,ancestor=GetParent(ancestor)) {
                    string parentClass=Native.Class(ancestor);
                    if(parentClass=="TrayNotifyWnd" || parentClass=="NotifyIconOverflowWindow" || parentClass=="Shell_NotifyIconOverflowWindow") {bars.Add(child);break;}
                }
                return true;
            },IntPtr.Zero);
            return true;
        },IntPtr.Zero);
        var result=new List<Entry>(); var seen=new HashSet<string>();
        foreach(IntPtr bar in bars) {
            using(var memory=new ToolbarMemory(bar)) {
                if(!memory.Valid) continue;
                int count=memory.Message(0x418,IntPtr.Zero,IntPtr.Zero).ToInt32();
                if(count<0 || count>256) continue;
                for(int i=0;i<count;i++) {
                    Entry entry=memory.ReadEntry(i);
                    if(entry!=null && seen.Add(entry.Key)) result.Add(entry);
                }
            }
        }
        return result.ToArray();
    }

    internal bool Activate(string key,uint expectedPid) {
        Entry selected=null;
        foreach(var item in snapshot) if(item.Key==key && item.Pid==expectedPid) {selected=item;break;}
        if(selected==null) return false;
        using(var memory=new ToolbarMemory(selected.Toolbar)) {
            if(!memory.Valid) return false;
            // Re-read the index: Explorer may reorder or remove icons between frames.
            var current=memory.ReadEntry(selected.Index);
            if(current==null || current.Key!=key || !String.Equals(current.Path,selected.Path,StringComparison.OrdinalIgnoreCase)) return false;
            AllowSetForegroundWindow(selected.Pid);
            if(memory.Message(0x41D,new IntPtr(selected.Index),memory.Buffer)==IntPtr.Zero) return false;
            byte[] rect=memory.Read(memory.Buffer,16); if(rect==null) return false;
            int x=(BitConverter.ToInt32(rect,0)+BitConverter.ToInt32(rect,8))/2;
            int y=(BitConverter.ToInt32(rect,4)+BitConverter.ToInt32(rect,12))/2;
            IntPtr point=new IntPtr(unchecked((y<<16)|(x&0xffff)));
            // Let Explorer translate mouse activation into the app's registered
            // callback version, rather than guessing legacy/v4 message layouts.
            memory.Message(0x200,IntPtr.Zero,point); // WM_MOUSEMOVE
            memory.Message(0x201,new IntPtr(1),point); // WM_LBUTTONDOWN
            memory.Message(0x202,IntPtr.Zero,point); // WM_LBUTTONUP
            memory.Message(0x203,new IntPtr(1),point); // WM_LBUTTONDBLCLK
            memory.Message(0x202,IntPtr.Zero,point); // WM_LBUTTONUP
            return true;
        }
    }
    public void Dispose() {disposed=true; snapshot=new Entry[0];}

    sealed class ToolbarMemory : IDisposable {
        internal readonly IntPtr Toolbar;
        IntPtr process;
        internal IntPtr Buffer;
        internal bool Valid {get {return process!=IntPtr.Zero && Buffer!=IntPtr.Zero;}}
        internal ToolbarMemory(IntPtr bar) {
            Toolbar=bar;
            uint pid; Native.GetWindowThreadProcessId(bar,out pid);
            string path=Native.Exe(pid);
            if(!String.Equals(path,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),StringComparison.OrdinalIgnoreCase)) return;
            process=Native.OpenProcess(0x1018,false,pid); // query + VM read/operation
            bool wow;
            if(process==IntPtr.Zero || !IsWow64Process(process,out wow) || wow || IntPtr.Size!=8) return;
            Buffer=VirtualAllocEx(process,IntPtr.Zero,new UIntPtr(4096),0x3000,4);
        }
        internal IntPtr Message(uint message,IntPtr w,IntPtr l) {
            IntPtr result;
            if(SendMessageTimeout(Toolbar,message,w,l,0x22,100,out result)==IntPtr.Zero) throw new InvalidOperationException("Tray toolbar unavailable");
            return result;
        }
        internal byte[] Read(IntPtr address,int count) {
            var data=new byte[count]; UIntPtr read;
            return ReadProcessMemory(process,address,data,new UIntPtr((uint)count),out read) && read.ToUInt64()==(ulong)count?data:null;
        }
        internal Entry ReadEntry(int index) {
            if(Message(0x417,new IntPtr(index),Buffer)==IntPtr.Zero) return null; // TB_GETBUTTON
            byte[] button=Read(Buffer,32); if(button==null) return null;
            var pointer=new IntPtr(BitConverter.ToInt64(button,16)); // x64 TBBUTTON.dwData
            if(pointer==IntPtr.Zero) return null;
            byte[] data=Read(pointer,32); if(data==null) return null;
            var owner=new IntPtr(BitConverter.ToInt64(data,0));
            uint id=BitConverter.ToUInt32(data,8), callback=BitConverter.ToUInt32(data,12), pid;
            if(!Native.IsWindow(owner) || callback<0x400 || callback>0xffff) return null;
            Native.GetWindowThreadProcessId(owner,out pid);
            string path=Native.Exe(pid); if(String.IsNullOrEmpty(path)) return null;
            // Shell-owned system indicators are not separate applications.
            if(String.Equals(path,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),StringComparison.OrdinalIgnoreCase)) return null;
            string title="";
            IntPtr textPointer=new IntPtr(BitConverter.ToInt64(button,24));
            if(textPointer.ToInt64()>65535) {
                byte[] text=Read(textPointer,256);
                if(text!=null) title=Encoding.Unicode.GetString(text).Split('\0')[0];
            }
            return new Entry {Owner=owner,Toolbar=Toolbar,Pid=pid,IconId=id,Index=index,Path=path,Title=title};
        }
        public void Dispose() {
            if(Buffer!=IntPtr.Zero) VirtualFreeEx(process,Buffer,UIntPtr.Zero,0x8000);
            if(process!=IntPtr.Zero) Native.CloseHandle(process);
            Buffer=process=IntPtr.Zero;
        }
    }
}
