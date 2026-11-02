using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace UDo.DesktopPartitions {
    // Opt-in, bounded diagnostics. No disk I/O on paint/input callbacks.
    internal static class PerformanceProbe {
        internal static bool Enabled=Environment.GetEnvironmentVariable("UDO_PERF")=="1";
        static readonly object gate=new object();
        static readonly Dictionary<string,long> counters=new Dictionary<string,long>();
        static readonly Dictionary<string,Queue<double>> samples=new Dictionary<string,Queue<double>>();
        internal static long Start() {return Enabled?Stopwatch.GetTimestamp():0;}
        internal static void Count(string name,long value=1) {
            if(!Enabled) return;lock(gate) {long old;counters.TryGetValue(name,out old);counters[name]=old+value;}
        }
        internal static void Peak(string name,long value) {
            if(!Enabled) return;lock(gate) {long old;counters.TryGetValue(name,out old);counters[name]=Math.Max(old,value);}
        }
        internal static void End(string name,long start) {
            if(!Enabled||start==0) return;
            double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            lock(gate) {Queue<double> queue;if(!samples.TryGetValue(name,out queue)) samples[name]=queue=new Queue<double>();if(queue.Count==2048) queue.Dequeue();queue.Enqueue(ms);}
        }
        internal static void Export(string path) {
            if(!Enabled) return;
            var summary=new Dictionary<string,object>();
            lock(gate) {
                foreach(var pair in counters) summary[pair.Key]=pair.Value;
                foreach(var pair in samples) {var values=pair.Value.ToArray();Array.Sort(values);summary[pair.Key]=new {count=values.Length,p50=values[(values.Length-1)/2],p95=values[(int)((values.Length-1)*.95)],p99=values[(int)((values.Length-1)*.99)],max=values[values.Length-1]};}
            }
            using(var p=Process.GetCurrentProcess()) {p.Refresh();summary["working_set_mb"]=p.WorkingSet64/1048576.0;summary["private_bytes_mb"]=p.PrivateMemorySize64/1048576.0;summary["handle_count"]=p.HandleCount;}
            File.WriteAllText(path,PartitionStore.Json().Serialize(summary));
        }
    }
}
