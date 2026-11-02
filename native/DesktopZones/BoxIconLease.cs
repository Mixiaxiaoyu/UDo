using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UDo.DesktopZones {
    // Reversible view clipping, with a write-ahead journal for the independent guard.
    // No file, Shell item or icon position is changed.
    internal sealed class BoxIconLease {
        readonly string path;readonly IDesktopAdapter desktop;byte[] original;
        internal BoxIconLease(string path,IDesktopAdapter desktop) {
            this.path=path;this.desktop=desktop;Recover(path,desktop);
            if(File.Exists(path)) throw new IOException("桌面显示正在恢复，请稍后重试");
        }
        internal void Apply(IEnumerable<string> items) {
            string[] paths=items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(paths.Length==0) {Restore();return;}
            if(original==null) {
                original=desktop.CaptureVisibility();Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,Convert.ToBase64String(original));
            }
            else if(!File.Exists(path)) File.WriteAllText(path,Convert.ToBase64String(original));
            desktop.HideItems(paths,original);
        }
        internal void Restore() {
            if(original!=null && !File.Exists(path)) File.WriteAllText(path,Convert.ToBase64String(original));
            Recover(path,desktop);if(!File.Exists(path)) original=null;
        }
        internal static void Recover(string path,IDesktopAdapter desktop) {
            if(!File.Exists(path)) return;
            try {
                byte[] saved=Convert.FromBase64String(File.ReadAllText(path));
                desktop.RestoreVisibility(saved);
                // Keep the journal if Explorer has not actually accepted the
                // original region. The guard can then retry after we exit.
                if(desktop.CaptureVisibility().SequenceEqual(saved)) File.Delete(path);
            } catch { /* Preserve the journal for the independent guard. */ }
        }
    }
}
