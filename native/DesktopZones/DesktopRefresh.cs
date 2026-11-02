using System;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;

namespace UDo.DesktopZones {
    // Explorer COM/MSAA may stall. Never run it on the window/input thread.
    // One STA worker owns the visibility lease and coalesces the latest membership.
    internal sealed class DesktopRefresh : IDisposable {
        readonly IDesktopAdapter desktop;readonly string journal;
        readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly Thread thread;
        string[] paths=new string[0];bool requested=true;volatile bool stopping;
        IntPtr host;string error="";
        internal IntPtr Host {get {lock(gate) return host;}}
        internal string Error {get {lock(gate) return error;}}
        internal int Passes;
        internal DesktopRefresh(IDesktopAdapter desktop,string journal,string[] items) {
            this.desktop=desktop;this.journal=journal;paths=items;
            thread=new Thread(Run) {IsBackground=true,Name="UDo desktop visibility"};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();
        }
        internal void Request(string[] items) {
            lock(gate) {if(paths.SequenceEqual(items,StringComparer.OrdinalIgnoreCase)) return;paths=items;requested=true;}
            wake.Set();
        }
        internal void Invalidate() {lock(gate) requested=true;wake.Set();}
        void Run() {
            int hr=CoInitializeEx(IntPtr.Zero,2);BoxIconLease lease=null;
            try {
                lease=new BoxIconLease(journal,desktop);
                DateTime next=DateTime.MinValue,last=DateTime.MinValue,fullRefresh=DateTime.MinValue;
                while(!stopping) {
                    bool run,changed;string[] snapshot;
                    lock(gate) {changed=requested;run=changed||DateTime.UtcNow>=next;requested=false;snapshot=paths;}
                    if(run) {
                        int delay=(int)(150-(DateTime.UtcNow-last).TotalMilliseconds);
                        if(delay>0) Thread.Sleep(delay);
                        if(stopping) break;
                        // Pick up any membership changes arriving during the debounce.
                        lock(gate) {snapshot=paths;changed|=requested;requested=false;}
                        try {
                            IntPtr current=desktop.Host;
                            if(changed||current!=Host||DateTime.UtcNow>=fullRefresh) {
                                lease.Apply(snapshot);fullRefresh=DateTime.UtcNow.AddSeconds(30);
                                Interlocked.Increment(ref Passes);
                            }
                            lock(gate) {host=current;error="";}
                        } catch(Exception e) {lock(gate) error=e.Message;}
                        last=DateTime.UtcNow;next=last.AddSeconds(5);
                    }
                    wake.WaitOne(Math.Max(1,(int)Math.Min(5000,(next-DateTime.UtcNow).TotalMilliseconds)));
                }
            } catch(Exception e) {lock(gate) error=e.Message;}
            finally {if(lease!=null) lease.Restore();if(hr>=0) CoUninitialize();}
        }
        public void Dispose() {
            stopping=true;wake.Set();
            // If Explorer hangs during shutdown, the independent process guard
            // retains the journal and restores it after this helper exits.
            if(thread.Join(2500)) wake.Dispose();
        }
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint mode);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
    }
}
