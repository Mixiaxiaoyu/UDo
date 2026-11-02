using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

// Aggregate only. Never retain key order, text, timestamps of individual presses,
// foreground app or a replayable event log. Hook work is O(1), without disk I/O.
internal sealed class KeyStatistics : IDisposable {
    internal const int Capacity=512;
    const uint Magic=0x314B444C; // LDK1, sparse daily counters + SHA-256
    readonly string root;
    readonly Func<DateTime> clock;
    readonly bool[] down=new bool[Capacity];
    readonly Dictionary<string,long[]> pending=new Dictionary<string,long[]>();
    readonly Dictionary<string,long[]> cache=new Dictionary<string,long[]>();
    readonly HashSet<string> recovered=new HashSet<string>();
    DateTime nextFlush;
    internal bool Enabled {get;private set;}
    internal string Error="";
    internal string Today {get {return clock().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}}
    internal KeyStatistics(string directory,Func<DateTime> now=null) {root=directory;clock=now??(()=>DateTime.Now);nextFlush=clock().AddSeconds(10);}
    internal void Configure(bool enabled) {Enabled=enabled;Array.Clear(down,0,down.Length);if(!enabled) Flush();}
    internal void Observe(int key,bool pressed,bool injected) {
        if(!Enabled || injected || key<=0 || key>=Capacity) return;
        if(!pressed) {down[key]=false;return;}
        if(down[key]) return;
        down[key]=true;
        string date=Today; long[] values;
        if(!pending.TryGetValue(date,out values)) {values=new long[Capacity];pending[date]=values;}
        if(values[key]<Int64.MaxValue) values[key]++;
    }
    internal static string ValidateDate(string text) {
        DateTime date;
        if(!DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) throw new ArgumentException("日期格式应为 YYYY-MM-DD");
        return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    }
    internal string FileFor(string date) {date=ValidateDate(date);return Path.Combine(root,date.Substring(0,4),date.Substring(5,2),date+".bin");}
    long[] Load(string date) {
        long[] values;
        if(cache.TryGetValue(date,out values)) return values;
        string path=FileFor(date);
        if(!File.Exists(path) && !File.Exists(path+".bak")) values=new long[Capacity];
        else {
            try {values=Read(path,date);}
            catch {
                try {values=Read(path+".bak",date);recovered.Add(date);}
                catch {throw new IOException("按键统计文件无法读取，已保留原文件："+date);}
            }
        }
        // History lookup never loads the entire archive. Retain at most four days.
        if(cache.Count>=4) cache.Remove(cache.Keys.First());
        cache[date]=values;return values;
    }
    internal long[] Counts(string date) {
        date=ValidateDate(date);
        var result=(long[])Load(date).Clone();long[] additions;
        if(pending.TryGetValue(date,out additions)) for(int i=0;i<Capacity;i++) result[i]=checked(result[i]+additions[i]);
        return result;
    }
    internal long TotalToday() {
        long total=0;
        foreach(long value in Counts(Today)) total=checked(total+value);
        return total;
    }
    internal object Snapshot(string date) {
        try {
            var counts=new Dictionary<string,long>();long total=0;
            var values=Counts(date);
            for(int i=1;i<Capacity;i++) if(values[i]>0) {counts[i.ToString(CultureInfo.InvariantCulture)]=values[i];total=checked(total+values[i]);}
            return new {date=date,total=total,counts=counts,enabled=Enabled,error=Error};
        } catch(Exception e) {Error=e.Message;return new {date=date,total=0,counts=new Dictionary<string,long>(),enabled=Enabled,error=Error};}
    }
    internal void Tick() {if(clock()>=nextFlush) {nextFlush=clock().AddSeconds(10);Flush();}}
    internal void Flush() {
        foreach(string date in pending.Keys.ToArray()) {
            try {
                long[] values=Counts(date);
                string path=FileFor(date);Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] payload;
                using(var memory=new MemoryStream()) using(var writer=new BinaryWriter(memory)) {
                    writer.Write(Magic);writer.Write(DateTime.ParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture).Ticks);
                    writer.Write((ushort)values.Count(x=>x>0));
                    for(int i=0;i<Capacity;i++) if(values[i]>0) {writer.Write((ushort)i);writer.Write(values[i]);}
                    writer.Flush();payload=memory.ToArray();
                }
                using(var output=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)) {
                    output.Write(payload,0,payload.Length);
                    using(var hash=SHA256.Create()) {var digest=hash.ComputeHash(payload);output.Write(digest,0,digest.Length);}
                    output.Flush(true);
                }
                if(File.Exists(path)) File.Replace(path+".tmp",path,recovered.Contains(date)?null:path+".bak");
                else File.Move(path+".tmp",path);
                recovered.Remove(date);cache[date]=values;pending.Remove(date);Error="";
            } catch(Exception e) {Error="统计尚未保存，将重试："+e.Message;}
        }
        while(cache.Count>4) cache.Remove(cache.Keys.First());
    }
    static long[] Read(string path,string date) {
        byte[] bytes=File.ReadAllBytes(path);
        if(bytes.Length<46 || bytes.Length>46+Capacity*10) throw new InvalidDataException();
        int payload=bytes.Length-32;
        using(var hash=SHA256.Create()) if(!hash.ComputeHash(bytes,0,payload).SequenceEqual(bytes.Skip(payload))) throw new InvalidDataException();
        var values=new long[Capacity];
        using(var reader=new BinaryReader(new MemoryStream(bytes,0,payload))) {
            if(reader.ReadUInt32()!=Magic || reader.ReadInt64()!=DateTime.ParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture).Ticks) throw new InvalidDataException();
            int length=reader.ReadUInt16();if(length>Capacity || payload!=14+length*10) throw new InvalidDataException();
            for(int i=0;i<length;i++) {int key=reader.ReadUInt16();long count=reader.ReadInt64();if(key<=0 || key>=Capacity || count<=0 || values[key]!=0) throw new InvalidDataException();values[key]=count;}
        }
        return values;
    }
    public void Dispose() {Enabled=false;Flush();}
}
