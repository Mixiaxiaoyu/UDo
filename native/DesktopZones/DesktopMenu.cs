using System;
using System.IO;
using Microsoft.Win32;

namespace UDo.DesktopZones {
    internal static class DesktopMenu {
        const string Key="Software\\Classes\\DesktopBackground\\Shell\\UDo.DesktopZone";
        internal static void Install(string session) {
            InstallPartition(session);
            using(var existing=Registry.CurrentUser.OpenSubKey(Key)) {
                if(existing!=null && !String.Equals(existing.GetValue("UDoSession") as string,session,StringComparison.OrdinalIgnoreCase)) {
                    // Never overwrite a menu owned by another running instance.
                    string owner=existing.GetValue("UDoSession") as string;
                    if(String.IsNullOrEmpty(owner)) return;
                    string state=Path.Combine(owner,"state.json");
                    if(File.Exists(state)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(state)<TimeSpan.FromSeconds(10)) return;
                }
            }
            using(var key=Registry.CurrentUser.CreateSubKey(Key)) {
                key.SetValue("","新建收纳盒子");key.SetValue("Icon",System.Windows.Forms.Application.ExecutablePath);key.SetValue("UDoSession",session);
                using(var command=key.CreateSubKey("command")) command.SetValue("","\""+System.Windows.Forms.Application.ExecutablePath+"\" --new-zone \""+session+"\"");
            }
        }
        internal static void Remove(string session) {
            RemovePartition(session);
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(Key)) {if(key==null || !String.Equals(key.GetValue("UDoSession") as string,session,StringComparison.OrdinalIgnoreCase)) return;}
                Registry.CurrentUser.DeleteSubKeyTree(Key,false);
            } catch { }
        }
        const string PartitionKey="Software\\Classes\\DesktopBackground\\Shell\\UDo.DesktopPartition";
        static void InstallPartition(string session) {
            using(var existing=Registry.CurrentUser.OpenSubKey(PartitionKey)) {
                if(existing!=null&&!String.Equals(existing.GetValue("UDoSession") as string,session,StringComparison.OrdinalIgnoreCase)) {
                    string owner=existing.GetValue("UDoSession") as string;
                    if(String.IsNullOrEmpty(owner)) return;
                    string state=Path.Combine(owner,"state.json");if(File.Exists(state)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(state)<TimeSpan.FromSeconds(10)) return;
                }
            }
            using(var key=Registry.CurrentUser.CreateSubKey(PartitionKey)) {
                key.SetValue("","新建无感分区");key.SetValue("Icon",System.Windows.Forms.Application.ExecutablePath);key.SetValue("UDoSession",session);
                using(var command=key.CreateSubKey("command")) command.SetValue("","\""+System.Windows.Forms.Application.ExecutablePath+"\" --new-partition \""+session+"\"");
            }
        }
        static void RemovePartition(string session) {
            try {using(var key=Registry.CurrentUser.OpenSubKey(PartitionKey)) {if(key==null||!String.Equals(key.GetValue("UDoSession") as string,session,StringComparison.OrdinalIgnoreCase)) return;}Registry.CurrentUser.DeleteSubKeyTree(PartitionKey,false);} catch { }
        }
    }
}
