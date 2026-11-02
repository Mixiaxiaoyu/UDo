using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace UDo.DesktopPartitions {
    // Shell extensions may be slow. They never run on the pointer/UI thread or
    // behind catalog scans. Only one menu request may be outstanding.
    internal sealed class DesktopMenuService : IDisposable {
        readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly ManualResetEvent cancel=new ManualResetEvent(false);
        readonly Queue<Action> completed=new Queue<Action>();readonly Func<Point,int> show;
        Thread worker;Action pending;bool busy;volatile bool stopping;
        internal DesktopMenuService(Func<Point,int> show=null) {this.show=show??ShowNative;}
        int ShowNative(Point at) {return PartitionDesktopContextMenu(at.X,at.Y,cancel.SafeWaitHandle.DangerousGetHandle());}
        internal bool Open(Point at,Action<int,string> finish) {
            lock(gate) {
                if(stopping||busy) return false;
                busy=true;pending=delegate {
                    int result=0;string error="";
                    try {result=show(at);Marshal.ThrowExceptionForHR(result);}catch(Exception e) {error=e.Message;}
                    lock(gate) if(!stopping) completed.Enqueue(delegate {busy=false;finish(result,error);});
                };
                if(worker==null) {worker=new Thread(Run) {IsBackground=true,Name="UDo desktop menu"};worker.SetApartmentState(ApartmentState.STA);worker.Start();}
                wake.Set();return true;
            }
        }
        internal void Pump() {Action action=null;lock(gate) if(completed.Count>0) action=completed.Dequeue();if(action!=null) action();}
        void Run() {
            int hr=OleInitialize(IntPtr.Zero);
            try {while(!stopping) {Action action;lock(gate) {action=pending;pending=null;}if(action!=null&&!stopping) action();else if(!stopping) wake.WaitOne();}}
            finally {if(hr>=0) OleUninitialize();wake.Dispose();cancel.Dispose();}
        }
        public void Dispose() {
            lock(gate) {if(stopping) return;stopping=true;pending=null;completed.Clear();cancel.Set();wake.Set();if(worker==null) {wake.Dispose();cancel.Dispose();return;}}
            worker.Join(100);
        }
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int PartitionDesktopContextMenu(int x,int y,IntPtr cancel);
        [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")] static extern void OleUninitialize();
    }
}
