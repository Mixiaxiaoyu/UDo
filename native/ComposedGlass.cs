using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.Foundation;
using Windows.Graphics.Effects;

// Clip the backdrop VISUAL itself. An HWND region does not clip the legacy
// accent backdrop on Windows 10, which leaves rectangular corner artifacts.
internal sealed class ComposedGlass : IDisposable {
    static object queueController;
    readonly Compositor compositor;
    CompositionPathGeometry outline;
    CompositionGeometricClip bodyClip;
    readonly SpriteVisual surface;
    readonly ContainerVisual root;
    readonly SpriteVisual border;
    ContainerVisual refractionLayer;
    SpriteVisual[] refractionBands;
    InsetClip[] refractionBandClips;
    CompositionGeometricClip refractionClip;
    CompositionEffectBrush[] refractionBrushes;
    CompositionEffectFactory[] refractionFactories;
    CompositionPathGeometry borderOutline;
    CompositionGeometricClip borderClip;
    Vector2 previousSize;
    float previousRadius,previousStroke;
    CompositionEffectBrush bodyBrush, borderBrush;
    CompositionEffectFactory bodyFactory, borderFactory;
    readonly CompositionBackdropBrush backdrop;
    string appearance;
    float appearanceScale;
    readonly CompositionTarget target;
    CompositionGraphicsDevice graphics;
    CompositionDrawingSurface contentSurface;
    CompositionSurfaceBrush contentBrush;
    SpriteVisual contentVisual;
    System.Drawing.Size contentSize;
    readonly bool contentOnly;
    InsetClip viewportClip;
    ShapeVisual selectionVisual;
    CompositionRectangleGeometry selectionGeometry;
    CompositionSpriteShape selectionShape;
    CompositionColorBrush selectionFill,selectionStroke;
    sealed class EditorShape : IDisposable {
        internal CompositionRectangleGeometry Geometry;internal CompositionSpriteShape Shape;
        internal System.Drawing.Rectangle Bounds;internal int Style=-1;
        public void Dispose() {Shape.Dispose();Geometry.Dispose();}
    }
    ShapeVisual editorVisual;
    readonly System.Collections.Generic.List<EditorShape> editorShapes=new System.Collections.Generic.List<EditorShape>();
    CompositionColorBrush editorFill,editorBlue,editorWhite,editorGuide;
    // A reusable pool for canvas rectangles, handles and alignment guides.
    // Only changed geometry is sent to the compositor; no monitor-sized bitmap upload.
    internal void SetEditorShapes(System.Collections.Generic.IList<System.Drawing.Rectangle> rectangles,System.Collections.Generic.IList<int> styles) {
        if(editorVisual==null) {
            editorVisual=compositor.CreateShapeVisual();root.Children.InsertAtTop(editorVisual);
            editorFill=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(18,13,111,255));
            editorBlue=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255,62,145,255));
            editorWhite=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255,255,255,255));
            editorGuide=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255,255,85,180));
        }
        editorVisual.Size=root.Size;
        while(editorShapes.Count<rectangles.Count) {
            var geometry=compositor.CreateRectangleGeometry();var shape=compositor.CreateSpriteShape(geometry);
            editorShapes.Add(new EditorShape {Geometry=geometry,Shape=shape});editorVisual.Shapes.Add(shape);
        }
        for(int i=0;i<editorShapes.Count;i++) {
            var item=editorShapes[i];var rect=i<rectangles.Count?rectangles[i]:System.Drawing.Rectangle.Empty;
            if(rect!=item.Bounds) {item.Bounds=rect;item.Geometry.Offset=new Vector2(rect.X,rect.Y);item.Geometry.Size=new Vector2(Math.Max(0,rect.Width),Math.Max(0,rect.Height));}
            int style=i<styles.Count?styles[i]:0;
            if(item.Style!=style) {
                item.Style=style;item.Shape.FillBrush=style==2?editorWhite:style==3?editorGuide:editorFill;
                item.Shape.StrokeBrush=style==3?editorGuide:editorBlue;item.Shape.StrokeThickness=style==1?2:1;
            }
        }
    }

    internal ComposedGlass(IntPtr window,bool contentOnly=false) {
        this.contentOnly=contentOnly;
        if(queueController==null) {
            var options=new QueueOptions {size=12,thread=2,apartment=2};
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options,out queueController));
        }
        compositor=new Compositor();
        IntPtr pointer;
        ((IDesktopTarget)(object)compositor).CreateDesktopWindowTarget(window,true,out pointer);
        try {target=(CompositionTarget)Marshal.GetObjectForIUnknown(pointer);}
        finally {Marshal.Release(pointer);}
        root=compositor.CreateContainerVisual();
        target.Root=root;
        if(contentOnly) {viewportClip=compositor.CreateInsetClip();root.Clip=viewportClip;}
        else {
            surface=compositor.CreateSpriteVisual();backdrop=compositor.CreateHostBackdropBrush();border=compositor.CreateSpriteVisual();
            root.Children.InsertAtBottom(surface);root.Children.InsertAtTop(border);SetAppearance("light",1);
        }
    }
    internal void Resize(int width,int height,int radius,string theme,float scale) {
        if(contentOnly) {root.Size=new Vector2(width,height);if(selectionVisual!=null) selectionVisual.Size=root.Size;return;}
        if(appearance!=theme || appearanceScale!=scale) SetAppearance(theme,scale);
        var size=new Vector2(width,height);
        root.Size=size; surface.Size=size; border.Size=size;
        if(size!=previousSize || radius!=previousRadius || scale!=previousStroke || (theme=="glass"&&refractionClip==null)) {
            IntPtr path;
            Marshal.ThrowExceptionForHR(CreateGlassBorder(width,height,radius,scale,out path));
            CompositionPathGeometry next;
            try {next=compositor.CreatePathGeometry(new CompositionPath((Windows.Graphics.IGeometrySource2D)Marshal.GetObjectForIUnknown(path)));}
            finally {Marshal.Release(path);}
            var clip=compositor.CreateGeometricClip(next); border.Clip=clip;
            Marshal.ThrowExceptionForHR(CreateGlassBorder(width,height,radius,0,out path));
            CompositionPathGeometry nextBody;
            try {nextBody=compositor.CreatePathGeometry(new CompositionPath((Windows.Graphics.IGeometrySource2D)Marshal.GetObjectForIUnknown(path)));}
            finally {Marshal.Release(path);}
            var nextBodyClip=compositor.CreateGeometricClip(nextBody);surface.Clip=nextBodyClip;
            if(refractionLayer!=null) {
                var nextRefractionClip=compositor.CreateGeometricClip(nextBody);
                refractionLayer.Clip=nextRefractionClip;
                if(refractionClip!=null) refractionClip.Dispose();
                refractionClip=nextRefractionClip;
            }
            if(bodyClip!=null) {bodyClip.Dispose();outline.Dispose();}
            bodyClip=nextBodyClip;outline=nextBody;
            if(borderClip!=null) {borderClip.Dispose();borderOutline.Dispose();}
            borderClip=clip;borderOutline=next;
            previousSize=size;previousRadius=radius;previousStroke=scale;
        }
        if(refractionLayer!=null) {
            refractionLayer.Size=size;
            // The entire shelf behaves as a shallow lens. The strongest bend
            // sits at the rim and tapers toward the middle of the glass.
            float[] edges={0,height*.08f,height*.18f,height*.32f,height*.5f};
            for(int band=0;band<4;band++) for(int end=0;end<2;end++) {
                int index=band*2+end;
                refractionBands[index].Size=size;
                var clip=refractionBandClips[index];
                clip.TopInset=end==0?edges[band]:Math.Max(0,height-edges[band+1]);
                clip.BottomInset=end==0?Math.Max(0,height-edges[band+1]):edges[band];
            }
        }
    }
    internal void SetMaterialOpacity(float value) {
        if(contentOnly) return;
        surface.Opacity=Math.Max(0,Math.Min(1,value));border.Opacity=surface.Opacity;
        if(refractionLayer!=null) refractionLayer.Opacity=surface.Opacity;
    }
    // Retained GPU geometry: dragging a marquee never repaints/uploads the monitor bitmap.
    internal void SetSelection(System.Drawing.Rectangle rectangle) {
        if(selectionVisual==null) {
            if(rectangle.IsEmpty) return;
            selectionGeometry=compositor.CreateRectangleGeometry();selectionShape=compositor.CreateSpriteShape(selectionGeometry);
            selectionFill=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(35,13,111,255));
            selectionStroke=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255,13,111,255));
            selectionShape.FillBrush=selectionFill;selectionShape.StrokeBrush=selectionStroke;selectionShape.StrokeThickness=2;
            selectionShape.StrokeDashArray.Add(3);selectionShape.StrokeDashArray.Add(1);
            selectionVisual=compositor.CreateShapeVisual();selectionVisual.Size=root.Size;selectionVisual.Shapes.Add(selectionShape);root.Children.InsertAtTop(selectionVisual);
        }
        selectionVisual.IsVisible=rectangle.Width>0&&rectangle.Height>0;
        if(selectionVisual.IsVisible) {selectionGeometry.Offset=new Vector2(rectangle.X,rectangle.Y);selectionGeometry.Size=new Vector2(rectangle.Width,rectangle.Height);}
    }
    internal void SetContent(System.Drawing.Bitmap bitmap,int width=0,int height=0) {
        var logical=new System.Drawing.Size(width>0?width:bitmap.Width,height>0?height:bitmap.Height);
        if(graphics==null) {
            IntPtr input=Marshal.GetIUnknownForObject(compositor),output;
            try {Marshal.ThrowExceptionForHR(CreateGlassGraphicsDevice(input,out output));}
            finally {Marshal.Release(input);}
            try {graphics=(CompositionGraphicsDevice)Marshal.GetObjectForIUnknown(output);} finally {Marshal.Release(output);}
            contentVisual=compositor.CreateSpriteVisual();root.Children.InsertAtTop(contentVisual);
        }
        // Bucket allocations while resizing. The root clip uses the exact HWND size.
        var allocated=contentOnly?new System.Drawing.Size((logical.Width+127)/128*128,(logical.Height+127)/128*128):logical;
        if(contentSurface==null||contentSize!=allocated) {
            if(contentBrush!=null) contentBrush.Dispose();if(contentSurface!=null) contentSurface.Dispose();
            contentSurface=graphics.CreateDrawingSurface(new Windows.Foundation.Size(allocated.Width,allocated.Height),Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,Windows.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            contentBrush=compositor.CreateSurfaceBrush(contentSurface);contentVisual.Brush=contentBrush;
            contentVisual.Size=new Vector2(allocated.Width,allocated.Height);contentSize=allocated;
        }
        var pixels=bitmap.LockBits(new System.Drawing.Rectangle(System.Drawing.Point.Empty,logical),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        IntPtr pointer=Marshal.GetIUnknownForObject(contentSurface);
        try {Marshal.ThrowExceptionForHR(PaintGlassContent(pointer,logical.Width,logical.Height,pixels.Scan0,pixels.Stride));}
        finally {Marshal.Release(pointer);bitmap.UnlockBits(pixels);}
    }
    // Figma 495:4648 / 495:4688. LINEAR_DODGE adds to the tinted
    // backdrop; a normal dark source-over stroke produces the wrong black rim.
    void SetAppearance(string theme,float scale) {
        bool dark=theme=="dark",glass=theme=="glass";
        float sigma=(dark?15f:glass?4f:67.9570465f)*scale;
        float opacity=dark?.39f:glass?0f:.36f;
        float tint=dark?.2916666567f:glass?1f:246f/255f;
        float edge=glass?0f:26f/255f*(dark?1f:.46f);
        var nextBody=MakeBrush(sigma,opacity,tint,0);
        var nextBorder=MakeBrush(sigma,opacity,tint,edge);
        surface.Brush=nextBody.Item2; border.Brush=nextBorder.Item2;
        if(bodyBrush!=null) {bodyBrush.Dispose();borderBrush.Dispose();bodyFactory.Dispose();borderFactory.Dispose();}
        bodyFactory=nextBody.Item1; bodyBrush=nextBody.Item2;
        borderFactory=nextBorder.Item1; borderBrush=nextBorder.Item2;
        if(glass) SetRefraction(scale);
        else if(refractionLayer!=null) refractionLayer.IsVisible=false;
        appearance=theme; appearanceScale=scale;
    }
    void SetRefraction(float scale) {
        if(refractionLayer==null) {
            refractionLayer=compositor.CreateContainerVisual();
            root.Children.InsertAbove(refractionLayer,surface);
            refractionBands=new SpriteVisual[8];refractionBandClips=new InsetClip[8];
            for(int i=0;i<refractionBands.Length;i++) {
                var visual=compositor.CreateSpriteVisual();var clip=compositor.CreateInsetClip();
                visual.Clip=clip;refractionBands[i]=visual;refractionBandClips[i]=clip;
                refractionLayer.Children.InsertAtTop(visual);
            }
        }
        var brushes=new CompositionEffectBrush[refractionBands.Length];var factories=new CompositionEffectFactory[refractionBands.Length];
        float[] shifts={10*scale,6*scale,3*scale,1.5f*scale};
        float[] strengths={.95f,.86f,.72f,.55f};
        for(int band=0;band<4;band++) for(int end=0;end<2;end++) {
            int index=band*2+end;
            var pair=MakeRefractionBrush(end==0?-shifts[band]:shifts[band]);
            factories[index]=pair.Item1;brushes[index]=pair.Item2;
            refractionBands[index].Brush=pair.Item2;
            refractionBands[index].Opacity=strengths[band];
        }
        if(refractionBrushes!=null) for(int i=0;i<refractionBrushes.Length;i++) {refractionBrushes[i].Dispose();refractionFactories[i].Dispose();}
        refractionBrushes=brushes;refractionFactories=factories;
        refractionLayer.IsVisible=true;
    }
    Tuple<CompositionEffectFactory,CompositionEffectBrush> MakeRefractionBrush(float shiftY) {
        var parameter=new CompositionEffectSourceParameter("Backdrop");
        IntPtr input=Marshal.GetIUnknownForObject(parameter),output;
        CompositionEffectFactory factory;
        try {
            Marshal.ThrowExceptionForHR(CreateGlassRefractionEffect(0,shiftY,input,out output));
            try {factory=compositor.CreateEffectFactory((IGraphicsEffect)Marshal.GetObjectForIUnknown(output));}
            finally {Marshal.Release(output);}
        } finally {Marshal.Release(input);}
        var brush=factory.CreateBrush();brush.SetSourceParameter("Backdrop",backdrop);
        return Tuple.Create(factory,brush);
    }
    Tuple<CompositionEffectFactory,CompositionEffectBrush> MakeBrush(float sigma,float opacity,float tint,float edge) {
        var parameter=new CompositionEffectSourceParameter("Backdrop");
        IntPtr input=Marshal.GetIUnknownForObject(parameter), output;
        CompositionEffectFactory factory;
        try {
            Marshal.ThrowExceptionForHR(CreateGlassEffect(sigma,opacity,tint,edge,input,out output));
            try {factory=compositor.CreateEffectFactory((IGraphicsEffect)Marshal.GetObjectForIUnknown(output));}
            finally {Marshal.Release(output);}
        } finally {Marshal.Release(input);}
        var brush=factory.CreateBrush(); brush.SetSourceParameter("Backdrop",backdrop);
        return Tuple.Create(factory,brush);
    }
    public void Dispose() {
        target.Root=null;
        if(editorVisual!=null) {editorVisual.Dispose();foreach(var item in editorShapes) item.Dispose();editorFill.Dispose();editorBlue.Dispose();editorWhite.Dispose();editorGuide.Dispose();}
        if(selectionVisual!=null) {selectionVisual.Dispose();selectionShape.Dispose();selectionGeometry.Dispose();selectionFill.Dispose();selectionStroke.Dispose();}
        if(contentVisual!=null) {contentVisual.Dispose();contentBrush.Dispose();contentSurface.Dispose();graphics.Dispose();}
        if(bodyBrush!=null) {bodyBrush.Dispose();borderBrush.Dispose();bodyFactory.Dispose();borderFactory.Dispose();}
        if(refractionBrushes!=null) for(int i=0;i<refractionBrushes.Length;i++) {refractionBrushes[i].Dispose();refractionFactories[i].Dispose();}
        if(refractionLayer!=null) {if(refractionClip!=null) refractionClip.Dispose();for(int i=0;i<refractionBands.Length;i++) {refractionBandClips[i].Dispose();refractionBands[i].Dispose();}refractionLayer.Dispose();}
        if(borderClip!=null) {borderClip.Dispose();borderOutline.Dispose();}
        target.Dispose(); root.Dispose(); if(border!=null) border.Dispose();
        if(bodyClip!=null) {bodyClip.Dispose();outline.Dispose();}
        if(surface!=null) surface.Dispose();if(backdrop!=null) backdrop.Dispose();if(viewportClip!=null) viewportClip.Dispose();compositor.Dispose();
    }
    [DllImport("GlassEffects.dll")] static extern int CreateGlassEffect(float sigma,float opacity,float tint,float edge,IntPtr input,out IntPtr effect);
    [DllImport("GlassEffects.dll")] static extern int CreateGlassRefractionEffect(float shiftX,float shiftY,IntPtr input,out IntPtr effect);
    [DllImport("GlassEffects.dll")] static extern int CreateGlassGraphicsDevice(IntPtr compositor,out IntPtr device);
    [DllImport("GlassEffects.dll")] static extern int PaintGlassContent(IntPtr surface,int width,int height,IntPtr pixels,int stride);
    [DllImport("GlassEffects.dll")] static extern int CreateGlassBorder(float width,float height,float radius,float stroke,out IntPtr geometry);
    [StructLayout(LayoutKind.Sequential)] struct QueueOptions {public int size,thread,apartment;}
    [DllImport("coremessaging.dll")] static extern int CreateDispatcherQueueController(QueueOptions options,[MarshalAs(UnmanagedType.IUnknown)] out object controller);
    [ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopTarget {void CreateDesktopWindowTarget(IntPtr window,[MarshalAs(UnmanagedType.Bool)] bool topmost,out IntPtr target);}
}
