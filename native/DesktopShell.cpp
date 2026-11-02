#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <exdisp.h>
#include <shlwapi.h>
#include <commctrl.h>
#include <oleacc.h>
#include <set>
#include <string>
#include <commoncontrols.h>

// Supported Shell interfaces, no Explorer memory access or injected code.
static HRESULT DesktopView(IFolderView2 **view) {
    IShellWindows *windows=nullptr; IDispatch *dispatch=nullptr;
    IServiceProvider *provider=nullptr; IShellBrowser *browser=nullptr; IShellView *shell=nullptr;
    HRESULT hr=CoCreateInstance(CLSID_ShellWindows,nullptr,CLSCTX_LOCAL_SERVER,IID_PPV_ARGS(&windows));
    VARIANT location,empty; VariantInit(&location); VariantInit(&empty);
    location.vt=VT_I4; location.lVal=CSIDL_DESKTOP; long hwnd=0;
    if(SUCCEEDED(hr)) hr=windows->FindWindowSW(&location,&empty,SWC_DESKTOP,&hwnd,SWFO_NEEDDISPATCH,&dispatch);
    if(SUCCEEDED(hr) && dispatch) hr=dispatch->QueryInterface(IID_PPV_ARGS(&provider)); else hr=E_FAIL;
    if(SUCCEEDED(hr)) hr=provider->QueryService(SID_STopLevelBrowser,IID_PPV_ARGS(&browser));
    if(SUCCEEDED(hr)) hr=browser->QueryActiveShellView(&shell);
    if(SUCCEEDED(hr)) hr=shell->QueryInterface(IID_PPV_ARGS(view));
    if(shell) shell->Release(); if(browser) browser->Release(); if(provider) provider->Release();
    if(dispatch) dispatch->Release(); if(windows) windows->Release(); return hr;
}
// Delegate the background context menu to Explorer itself. Its DefView supplies
// View/Sort/Refresh/Paste and the installed extensions; an in-process folder
// IContextMenu alone omits those view commands. This is an asynchronous message,
// not a simulated click on a possibly hidden file icon.
extern "C" __declspec(dllexport) HRESULT PartitionDesktopContextMenu(int x,int y,HANDLE cancel) {
    IFolderView2 *view=nullptr;IShellView *shell=nullptr;HWND host=nullptr;
    HRESULT hr=DesktopView(&view);
    if(SUCCEEDED(hr)) hr=view->QueryInterface(IID_PPV_ARGS(&shell));
    if(SUCCEEDED(hr)) hr=shell->GetWindow(&host);
    if(SUCCEEDED(hr)&&!(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0)) {
        if(!PostMessageW(host,WM_CONTEXTMENU,reinterpret_cast<WPARAM>(host),MAKELPARAM(x,y))) hr=HRESULT_FROM_WIN32(GetLastError());
    }
    if(shell) shell->Release();if(view) view->Release();return hr;
}
extern "C" __declspec(dllexport) HRESULT ZoneDesktopHost(HWND *hwnd) {
    IFolderView2 *view=nullptr; HRESULT hr=DesktopView(&view);
    if(SUCCEEDED(hr)) { IShellView *shell=nullptr; hr=view->QueryInterface(IID_PPV_ARGS(&shell));
        if(SUCCEEDED(hr)) {hr=shell->GetWindow(hwnd); shell->Release();} view->Release(); } return hr;
}
// Read-only desktop appearance. Called on the icon STA, never while painting.
extern "C" __declspec(dllexport) HRESULT DesktopVisualMetrics(int *side,int *dpi,DWORD *textColor) {
    IFolderView2 *view=nullptr;HRESULT hr=DesktopView(&view);if(FAILED(hr)) return hr;
    FOLDERVIEWMODE mode;hr=view->GetViewModeAndIconSize(&mode,side);
    IShellView *shell=nullptr;HWND host=nullptr;
    if(SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&shell)))) {shell->GetWindow(&host);shell->Release();}
    *dpi=host?GetDpiForWindow(host):96;
    HWND list=host?FindWindowExW(host,nullptr,L"SysListView32",nullptr):nullptr;
    DWORD_PTR color=0xffffff;
    if(list) SendMessageTimeoutW(list,LVM_GETTEXTCOLOR,0,0,SMTO_ABORTIFHUNG,100,&color);
    *textColor=static_cast<DWORD>(color);view->Release();return hr;
}
extern "C" __declspec(dllexport) HRESULT DesktopItemVisual(LPCWSTR path,int side,HICON *icon,LPWSTR name,int nameCount) {
    *icon=nullptr;if(nameCount>0) name[0]=0;
    IShellItem *item=nullptr;HRESULT hr=SHCreateItemFromParsingName(path,nullptr,IID_PPV_ARGS(&item));
    if(SUCCEEDED(hr)) {
        PWSTR label=nullptr;if(SUCCEEDED(item->GetDisplayName(SIGDN_NORMALDISPLAY,&label))) {wcsncpy_s(name,nameCount,label,_TRUNCATE);CoTaskMemFree(label);}
        // Shortcut overlays come from the system image list. For ordinary files,
        // ask the same Shell thumbnail provider used by Explorer first.
        IShellItemImageFactory *factory=nullptr;HBITMAP bitmap=nullptr;
        BOOL iconsOnly=FALSE;DWORD bytes=sizeof(iconsOnly);
        RegGetValueW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",L"IconsOnly",RRF_RT_REG_DWORD,nullptr,&iconsOnly,&bytes);
        if(!iconsOnly&&lstrcmpiW(PathFindExtensionW(path),L".lnk")!=0&&SUCCEEDED(item->QueryInterface(IID_PPV_ARGS(&factory)))) {
            SIZE size={side,side};if(SUCCEEDED(factory->GetImage(size,SIIGBF_THUMBNAILONLY,&bitmap))) {
                BITMAP description={};GetObject(bitmap,sizeof(description),&description);
                HBITMAP mask=CreateBitmap(description.bmWidth,description.bmHeight,1,1,nullptr);
                ICONINFO info={};info.fIcon=TRUE;info.hbmColor=bitmap;info.hbmMask=mask;*icon=CreateIconIndirect(&info);
                DeleteObject(mask);DeleteObject(bitmap);
            }
            factory->Release();
        }
        item->Release();
    }
    if(!*icon) {
        SHFILEINFOW info={};
        if(!SHGetFileInfoW(path,0,&info,sizeof(info),SHGFI_SYSICONINDEX|SHGFI_ICON|SHGFI_OVERLAYINDEX|SHGFI_ADDOVERLAYS)) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        if(info.hIcon) DestroyIcon(info.hIcon);
        // SHIL_JUMBO pads applications without a 256 px resource into a 256 px
        // canvas. Shrinking that canvas makes the visible icon tiny on DPI displays.
        IImageList *images=nullptr;int list=side<=16?SHIL_SMALL:side<=32?SHIL_LARGE:side<=128?SHIL_EXTRALARGE:SHIL_JUMBO;
        hr=SHGetImageList(list,IID_PPV_ARGS(&images));
        if(SUCCEEDED(hr)) {hr=images->GetIcon(info.iIcon&0x00ffffff,ILD_TRANSPARENT|INDEXTOOVERLAYMASK((info.iIcon>>24)&0xff),icon);images->Release();}
    }
    return *icon?S_OK:hr;
}
extern "C" __declspec(dllexport) HRESULT ZoneFolderFlags(BOOL write,DWORD *flags) {
    IFolderView2 *view=nullptr; HRESULT hr=DesktopView(&view);
    if(SUCCEEDED(hr)) {hr=write?view->SetCurrentFolderFlags(FWF_AUTOARRANGE|FWF_SNAPTOGRID,*flags):view->GetCurrentFolderFlags(flags); view->Release();} return hr;
}
extern "C" __declspec(dllexport) HRESULT ZonePositionPath(LPCWSTR path,POINT *point,BOOL write) {
    IFolderView2 *view=nullptr; IShellFolder *folder=nullptr; HRESULT hr=DesktopView(&view);
    if(FAILED(hr)) return hr;
    hr=view->GetFolder(IID_PPV_ARGS(&folder)); int count=0;
    if(SUCCEEDED(hr)) hr=view->ItemCount(SVGIO_ALLVIEW,&count);
    HRESULT result=HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    for(int i=0; SUCCEEDED(hr) && i<count; ++i) {
        PITEMID_CHILD pidl=nullptr; if(FAILED(view->Item(i,&pidl))) continue;
        STRRET name; WCHAR buffer[32768];
        if(SUCCEEDED(folder->GetDisplayNameOf(pidl,SHGDN_FORPARSING,&name)) &&
           SUCCEEDED(StrRetToBufW(&name,pidl,buffer,32768)) && lstrcmpiW(buffer,path)==0) {
            PCUITEMID_CHILD items[]={pidl};
            result=write?view->SelectAndPositionItems(1,items,point,SVSI_POSITIONITEM):view->GetItemPosition(pidl,point);
            CoTaskMemFree(pidl); break;
        }
        CoTaskMemFree(pidl);
    }
    if(folder) folder->Release(); view->Release(); return FAILED(hr)?hr:result;
}
extern "C" __declspec(dllexport) HRESULT ZonePositionBatch(int requested,LPCWSTR *paths,POINT *points,BOOL *placed) {
    for(int n=0;n<requested;n++) placed[n]=FALSE;
    IFolderView2 *view=nullptr; IShellFolder *folder=nullptr; HRESULT hr=DesktopView(&view);
    if(FAILED(hr)) return hr;
    hr=view->GetFolder(IID_PPV_ARGS(&folder)); int count=0;
    if(SUCCEEDED(hr)) hr=view->ItemCount(SVGIO_ALLVIEW,&count);
    for(int i=0;SUCCEEDED(hr)&&i<count;i++) {
        PITEMID_CHILD pidl=nullptr; if(FAILED(view->Item(i,&pidl))) continue;
        STRRET name; WCHAR buffer[32768];
        if(SUCCEEDED(folder->GetDisplayNameOf(pidl,SHGDN_FORPARSING,&name))&&SUCCEEDED(StrRetToBufW(&name,pidl,buffer,32768))) {
            for(int n=0;n<requested;n++) if(!placed[n]&&lstrcmpiW(buffer,paths[n])==0) {
                PCUITEMID_CHILD items[]={pidl};placed[n]=SUCCEEDED(view->SelectAndPositionItems(1,items,points+n,SVSI_POSITIONITEM));break;
            }
        }
        CoTaskMemFree(pidl);
    }
    if(folder) folder->Release();view->Release();return hr;
}
extern "C" __declspec(dllexport) HRESULT ZoneDesktopPoints(POINT *points,int capacity,int *written) {
    IFolderView2 *view=nullptr;HRESULT hr=DesktopView(&view);*written=0;
    if(FAILED(hr)) return hr;int count=0;hr=view->ItemCount(SVGIO_ALLVIEW,&count);
    if(capacity==0) *written=count;
    for(int i=0;SUCCEEDED(hr)&&i<count&&*written<capacity;i++) {
        PITEMID_CHILD pidl=nullptr;if(FAILED(view->Item(i,&pidl))) continue;
        if(SUCCEEDED(view->GetItemPosition(pidl,points+*written))) ++*written;
        CoTaskMemFree(pidl);
    }
    view->Release();return hr;
}

// One COM enumeration for the reference catalog; called only by a managed STA worker.
typedef void (__cdecl *PartitionItemCallback)(LPCWSTR path,int x,int y,int left,int top,int width,int height);
extern "C" __declspec(dllexport) HRESULT PartitionDesktopItems(PartitionItemCallback callback) {
    IFolderView2 *view=nullptr;IShellFolder *folder=nullptr;IShellView *shell=nullptr;
    HRESULT hr=DesktopView(&view);if(FAILED(hr)) return hr;
    HWND host=nullptr;hr=view->QueryInterface(IID_PPV_ARGS(&shell));
    if(SUCCEEDED(hr)) hr=shell->GetWindow(&host);
    if(SUCCEEDED(hr)) hr=view->GetFolder(IID_PPV_ARGS(&folder));
    IAccessible *accessible=nullptr;
    HWND list=host?FindWindowExW(host,nullptr,L"SysListView32",nullptr):nullptr;
    if(list) AccessibleObjectFromWindow(list,OBJID_CLIENT,IID_PPV_ARGS(&accessible));
    int count=0;if(SUCCEEDED(hr)) hr=view->ItemCount(SVGIO_ALLVIEW,&count);
    for(int i=0;SUCCEEDED(hr)&&i<count;i++) {
        PITEMID_CHILD pidl=nullptr;if(FAILED(view->Item(i,&pidl))) continue;
        STRRET name;WCHAR buffer[32768];POINT point={};
        if(SUCCEEDED(folder->GetDisplayNameOf(pidl,SHGDN_FORPARSING,&name))&&SUCCEEDED(StrRetToBufW(&name,pidl,buffer,32768))&&SUCCEEDED(view->GetItemPosition(pidl,&point))) {
            ClientToScreen(host,&point);long x=point.x,y=point.y,w=75,h=100;
            if(accessible) {VARIANT child;VariantInit(&child);child.vt=VT_I4;child.lVal=i+1;accessible->accLocation(&x,&y,&w,&h,child);}
            callback(buffer,point.x,point.y,x,y,w,h);
        }
        CoTaskMemFree(pidl);
    }
    if(accessible) accessible->Release();if(shell) shell->Release();if(folder) folder->Release();view->Release();return hr;
}

// Hide only the owned icon rectangles by clipping the transparent icon view.
// Files, Shell items, icon order and positions are never mutated.
static HRESULT IconList(HWND *list) {
    HWND host=nullptr;HRESULT hr=ZoneDesktopHost(&host);
    if(SUCCEEDED(hr)) {*list=FindWindowExW(host,nullptr,L"SysListView32",nullptr);if(!*list) hr=E_FAIL;}return hr;
}
extern "C" __declspec(dllexport) HRESULT BoxCaptureClip(BYTE *bytes,DWORD capacity,DWORD *length) {
    HWND list;HRESULT hr=IconList(&list);if(FAILED(hr)) return hr;
    HRGN region=CreateRectRgn(0,0,0,0);int type=GetWindowRgn(list,region);
    *length=type==ERROR?0:GetRegionData(region,0,nullptr);
    if(bytes&&capacity>=*length&&*length) GetRegionData(region,capacity,reinterpret_cast<RGNDATA*>(bytes));
    DeleteObject(region);return S_OK;
}
extern "C" __declspec(dllexport) HRESULT BoxRestoreClip(BYTE *bytes,DWORD length) {
    HWND list;HRESULT hr=IconList(&list);if(FAILED(hr)) return hr;
    HRGN region=length?ExtCreateRegion(nullptr,length,reinterpret_cast<RGNDATA*>(bytes)):nullptr;
    if(length&&!region) return E_FAIL;
    if(!SetWindowRgn(list,region,TRUE)) {if(region) DeleteObject(region);return E_FAIL;}return S_OK;
}
extern "C" __declspec(dllexport) HRESULT BoxClipItems(int requested,LPCWSTR *paths,BYTE *original,DWORD length) {
    struct PathLess {bool operator()(const std::wstring& a,const std::wstring& b) const {return lstrcmpiW(a.c_str(),b.c_str())<0;}};
    std::set<std::wstring,PathLess> wanted;
    for(int n=0;n<requested;n++) wanted.insert(paths[n]);
    HWND list;HRESULT hr=IconList(&list);if(FAILED(hr)) return hr;
    RECT client;GetClientRect(list,&client);
    HRGN region=length?ExtCreateRegion(nullptr,length,reinterpret_cast<RGNDATA*>(original)):CreateRectRgnIndirect(&client);
    if(!region) return E_FAIL;
    IFolderView2 *view=nullptr;IShellFolder *folder=nullptr;IAccessible *accessible=nullptr;
    hr=DesktopView(&view);
    if(SUCCEEDED(hr)) hr=view->GetFolder(IID_PPV_ARGS(&folder));
    if(SUCCEEDED(hr)) hr=AccessibleObjectFromWindow(list,OBJID_CLIENT,IID_PPV_ARGS(&accessible));
    int count=0;if(SUCCEEDED(hr)) hr=view->ItemCount(SVGIO_ALLVIEW,&count);
    for(int i=0;SUCCEEDED(hr)&&i<count;i++) {
        PITEMID_CHILD pidl=nullptr;if(FAILED(view->Item(i,&pidl))) continue;
        STRRET name;WCHAR buffer[32768];
        if(SUCCEEDED(folder->GetDisplayNameOf(pidl,SHGDN_FORPARSING,&name))&&SUCCEEDED(StrRetToBufW(&name,pidl,buffer,32768))) {
            if(wanted.find(buffer)!=wanted.end()) {
                VARIANT child;VariantInit(&child);child.vt=VT_I4;child.lVal=i+1;
                long x=0,y=0,w=0,h=0;
                if(SUCCEEDED(accessible->accLocation(&x,&y,&w,&h,child))&&w>0&&h>0) {
                    POINT pt={x,y};ScreenToClient(list,&pt);
                    HRGN hole=CreateRectRgn(pt.x,pt.y,pt.x+w,pt.y+h);
                    CombineRgn(region,region,hole,RGN_DIFF);DeleteObject(hole);
                }
            }
        }
        CoTaskMemFree(pidl);
    }
    if(accessible) accessible->Release();if(folder) folder->Release();if(view) view->Release();
    if(SUCCEEDED(hr)) {
        HRGN previous=CreateRectRgn(0,0,0,0);
        bool unchanged=GetWindowRgn(list,previous)!=ERROR&&EqualRgn(previous,region);
        DeleteObject(previous);
        if(!unchanged) {if(SetWindowRgn(list,region,TRUE)) region=nullptr;else hr=E_FAIL;}
    }
    if(region) DeleteObject(region);return hr;
}
