#include <windows.h>
#include <roapi.h>
#include <windows.foundation.h>
#include <windows.graphics.effects.h>
#include <windows.graphics.effects.interop.h>
#include <windows.graphics.interop.h>
#include <initguid.h>
#include <d2d1.h>
#include <d2d1_1.h>
#include <d3d11.h>
#include <windows.ui.composition.interop.h>
#include <d2d1effects.h>
#include <wrl.h>
#include <wrl/wrappers/corewrappers.h>
#include <vector>
#include <cmath>

using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;
using namespace ABI::Windows::Graphics::Effects;
using namespace ABI::Windows::Foundation;

// Optional custom content layer for storage boxes. The Dock's existing effect
// and geometry exports retain their behavior.
extern "C" __declspec(dllexport) HRESULT __stdcall CreateGlassGraphicsDevice(IUnknown* compositor,IUnknown** output) {
    if(!compositor||!output) return E_POINTER;
    ComPtr<ID3D11Device> d3d;ComPtr<ID3D11DeviceContext> context;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&d3d,nullptr,&context);
    if(FAILED(hr)) hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&d3d,nullptr,&context);
    if(FAILED(hr)) return hr;
    ComPtr<IDXGIDevice> dxgi;hr=d3d.As(&dxgi);if(FAILED(hr)) return hr;
    ComPtr<ID2D1Device> device;hr=D2D1CreateDevice(dxgi.Get(),nullptr,&device);if(FAILED(hr)) return hr;
    ComPtr<ABI::Windows::UI::Composition::ICompositorInterop> interop;hr=compositor->QueryInterface(IID_PPV_ARGS(&interop));if(FAILED(hr)) return hr;
    ComPtr<ABI::Windows::UI::Composition::ICompositionGraphicsDevice> graphics;
    hr=interop->CreateGraphicsDevice(device.Get(),&graphics);if(FAILED(hr)) return hr;
    return graphics.CopyTo(output);
}
extern "C" __declspec(dllexport) HRESULT __stdcall PaintGlassContent(IUnknown* surface,int width,int height,const BYTE* pixels,int stride) {
    if(!surface||!pixels||width<1||height<1||stride<width*4) return E_INVALIDARG;
    ComPtr<ABI::Windows::UI::Composition::ICompositionDrawingSurfaceInterop> interop;
    HRESULT hr=surface->QueryInterface(IID_PPV_ARGS(&interop));if(FAILED(hr)) return hr;
    ComPtr<ID2D1DeviceContext> context;POINT offset;
    hr=interop->BeginDraw(nullptr,IID_PPV_ARGS(&context),&offset);if(FAILED(hr)) return hr;
    context->SetTransform(D2D1::Matrix3x2F::Translation((FLOAT)offset.x,(FLOAT)offset.y));
    context->Clear(D2D1::ColorF(0,0));
    ComPtr<ID2D1Bitmap> bitmap;
    hr=context->CreateBitmap(D2D1::SizeU(width,height),pixels,stride,D2D1::BitmapProperties(D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED)),&bitmap);
    if(SUCCEEDED(hr)) context->DrawBitmap(bitmap.Get(),D2D1::RectF(0,0,(FLOAT)width,(FLOAT)height));
    HRESULT end=interop->EndDraw();return FAILED(hr)?hr:end;
}

// Keep the IInspectable effect graph native: .NET Framework's CCW is not
// reliable for returning boxed WinRT values during compositor callbacks.
class GlassEffect final : public RuntimeClass<RuntimeClassFlags<WinRtClassicComMix>,
    IGraphicsEffect, IGraphicsEffectSource, IGraphicsEffectD2D1Interop> {
    InspectableClass(L"UDo.GlassEffect", BaseTrust);
public:
    GUID id{};
    ComPtr<IGraphicsEffectSource> source;
    std::vector<ComPtr<IInspectable>> properties;
    IFACEMETHODIMP get_Name(HSTRING* value) override { return WindowsCreateString(L"",0,value); }
    IFACEMETHODIMP put_Name(HSTRING) override { return S_OK; }
    IFACEMETHODIMP GetEffectId(GUID* value) override { *value=id; return S_OK; }
    IFACEMETHODIMP GetNamedPropertyMapping(LPCWSTR,UINT*,GRAPHICS_EFFECT_PROPERTY_MAPPING*) override { return E_INVALIDARG; }
    IFACEMETHODIMP GetPropertyCount(UINT* value) override { *value=static_cast<UINT>(properties.size()); return S_OK; }
    IFACEMETHODIMP GetProperty(UINT index,IPropertyValue** value) override {
        if(index>=properties.size()) return E_INVALIDARG;
        return properties[index].CopyTo(value);
    }
    IFACEMETHODIMP GetSource(UINT index,IGraphicsEffectSource** value) override {
        return index==0?source.CopyTo(value):E_INVALIDARG;
    }
    IFACEMETHODIMP GetSourceCount(UINT* value) override { *value=1; return S_OK; }
};

#define CHECK_HR(call) do { HRESULT result=(call); if(FAILED(result)) return result; } while(0)
extern "C" __declspec(dllexport) HRESULT __stdcall CreateGlassEffect(
    float sigma,float opacity,float tint,float edge,IUnknown* input,IGraphicsEffect** output) {
    if(!input || !output) return E_POINTER;
    *output=nullptr;
    ComPtr<IPropertyValueStatics> values;
    CHECK_HR(RoGetActivationFactory(HStringReference(RuntimeClass_Windows_Foundation_PropertyValue).Get(),IID_PPV_ARGS(&values)));
    auto blur=Make<GlassEffect>();
    blur->id=CLSID_D2D1GaussianBlur;
    CHECK_HR(input->QueryInterface(IID_PPV_ARGS(&blur->source)));
    blur->properties.resize(3);
    CHECK_HR(values->CreateSingle(sigma,&blur->properties[0]));
    CHECK_HR(values->CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_QUALITY,&blur->properties[1]));
    CHECK_HR(values->CreateUInt32(D2D1_BORDER_MODE_HARD,&blur->properties[2]));
    auto color=Make<GlassEffect>();
    color->id=CLSID_D2D1ColorMatrix;
    CHECK_HR(blur.As(&color->source));
    color->properties.resize(3);
    float k=1-opacity,add=tint*opacity+edge;
    float matrix[]={k,0,0,0, 0,k,0,0, 0,0,k,0, 0,0,0,1, add,add,add,0};
    CHECK_HR(values->CreateSingleArray(20,matrix,&color->properties[0]));
    CHECK_HR(values->CreateUInt32(D2D1_COLORMATRIX_ALPHA_MODE_PREMULTIPLIED,&color->properties[1]));
    CHECK_HR(values->CreateBoolean(true,&color->properties[2]));
    return color.CopyTo(output);
}

// Shift the live host backdrop at the glass rim. Unlike a wallpaper bitmap,
// this also follows video wallpapers and windows moving behind the Dock.
extern "C" __declspec(dllexport) HRESULT __stdcall CreateGlassRefractionEffect(
    float shiftX,float shiftY,IUnknown* input,IGraphicsEffect** output) {
    if(!input || !output) return E_POINTER;
    *output=nullptr;
    ComPtr<IPropertyValueStatics> values;
    CHECK_HR(RoGetActivationFactory(HStringReference(RuntimeClass_Windows_Foundation_PropertyValue).Get(),IID_PPV_ARGS(&values)));
    auto blur=Make<GlassEffect>();
    blur->id=CLSID_D2D1GaussianBlur;
    CHECK_HR(input->QueryInterface(IID_PPV_ARGS(&blur->source)));
    blur->properties.resize(3);
    CHECK_HR(values->CreateSingle(0.5f,&blur->properties[0]));
    CHECK_HR(values->CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_QUALITY,&blur->properties[1]));
    CHECK_HR(values->CreateUInt32(D2D1_BORDER_MODE_HARD,&blur->properties[2]));
    auto transform=Make<GlassEffect>();
    transform->id=CLSID_D2D12DAffineTransform;
    CHECK_HR(blur.As(&transform->source));
    transform->properties.resize(4);
    CHECK_HR(values->CreateUInt32(D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_LINEAR,&transform->properties[0]));
    CHECK_HR(values->CreateUInt32(D2D1_BORDER_MODE_HARD,&transform->properties[1]));
    float matrix[]={1,0,0,1,shiftX,shiftY};
    CHECK_HR(values->CreateSingleArray(6,matrix,&transform->properties[2]));
    CHECK_HR(values->CreateSingle(0,&transform->properties[3]));
    return transform.CopyTo(output);
}

class GlassGeometry final : public RuntimeClass<RuntimeClassFlags<WinRtClassicComMix>,
    ABI::Windows::Graphics::IGeometrySource2D, ABI::Windows::Graphics::IGeometrySource2DInterop> {
    InspectableClass(L"UDo.GlassGeometry",BaseTrust);
public:
    ComPtr<ID2D1Geometry> geometry;
    IFACEMETHODIMP GetGeometry(ID2D1Geometry** value) override { return geometry.CopyTo(value); }
    IFACEMETHODIMP TryGetGeometryUsingFactory(ID2D1Factory*,ID2D1Geometry** value) override { *value=nullptr;return S_OK; }
};

extern "C" __declspec(dllexport) HRESULT __stdcall CreateGlassBorder(float w,float h,float r,float stroke,
    ABI::Windows::Graphics::IGeometrySource2D** output) {
    ComPtr<ID2D1Factory> factory;
    CHECK_HR(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,factory.GetAddressOf()));
    // Figma's 60% smoothing: two tangent cubics around a circular arc.
    // The p=(1+s)*r construction is described in Figma's squircle article.
    float s=.6f,p=(1+s)*r;
    float theta=s*3.14159265358979323846f/4;
    float d=r*(1-std::cos(theta)),c=d/std::tan(theta);
    float arc=r*std::sqrt(2.f)*std::sin((1-s)*3.14159265358979323846f/4);
    float b=(p-arc-c-d)/3,a=2*b;
    ComPtr<ID2D1PathGeometry> outer;
    ComPtr<ID2D1GeometrySink> outline;
    CHECK_HR(factory->CreatePathGeometry(&outer)); CHECK_HR(outer->Open(&outline));
    outline->BeginFigure(D2D1::Point2F(w-p,0),D2D1_FIGURE_BEGIN_FILLED);
    for(int corner=0;corner<4;corner++) {
        auto point=[&](float x,float y) {
            switch(corner) {
            case 0:return D2D1::Point2F(w+x,y);
            case 1:return D2D1::Point2F(w-y,h+x);
            case 2:return D2D1::Point2F(-x,h-y);
            default:return D2D1::Point2F(y,-x);
            }
        };
        if(corner) outline->AddLine(point(-p,0));
        outline->AddBezier(D2D1::BezierSegment(point(-p+a,0),point(-p+a+b,0),point(-p+a+b+c,d)));
        outline->AddArc(D2D1::ArcSegment(point(-d,p-a-b-c),D2D1::SizeF(r,r),0,D2D1_SWEEP_DIRECTION_CLOCKWISE,D2D1_ARC_SIZE_SMALL));
        outline->AddBezier(D2D1::BezierSegment(point(0,p-a-b),point(0,p-a),point(0,p)));
    }
    outline->EndFigure(D2D1_FIGURE_END_CLOSED); CHECK_HR(outline->Close());
    auto source=Make<GlassGeometry>();
    if(stroke<=0) { CHECK_HR(outer.As(&source->geometry));return source.CopyTo(output); }
    ComPtr<ID2D1PathGeometry> band;
    ComPtr<ID2D1GeometrySink> bandSink;
    CHECK_HR(factory->CreatePathGeometry(&band)); CHECK_HR(band->Open(&bandSink));
    CHECK_HR(outer->Widen(2*stroke,nullptr,nullptr,bandSink.Get())); CHECK_HR(bandSink->Close());
    ComPtr<ID2D1PathGeometry> ring;
    ComPtr<ID2D1GeometrySink> sink;
    CHECK_HR(factory->CreatePathGeometry(&ring));
    CHECK_HR(ring->Open(&sink));
    CHECK_HR(outer->CombineWithGeometry(band.Get(),D2D1_COMBINE_MODE_INTERSECT,nullptr,sink.Get()));
    CHECK_HR(sink->Close());
    CHECK_HR(ring.As(&source->geometry));
    return source.CopyTo(output);
}
