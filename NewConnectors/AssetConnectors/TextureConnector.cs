#region

using System;
using System.Collections;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityFrooxEngineRunner;
using Cubemap = UnityEngine.Cubemap;
using Object = UnityEngine.Object;
using Texture2D = SharpDX.Direct3D11.Texture2D;
using Texture3D = UnityEngine.Texture3D;
using TextureFormat = Elements.Assets.TextureFormat;
using TextureWrapMode = FrooxEngine.TextureWrapMode;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class TextureConnector :
    AssetConnector,
    ITexture2DConnector,
    ICubemapConnector,
    IUnityTextureProvider, ITexture3DConnector
{
    public const int TIMESLICE_RESOLUTION = 65536;
    private int _anisoLevel;
    private ShaderResourceView _dx11Resource;

    private Texture2D _dx11Tex;

    private TextureFilterMode _filterMode;
    private int _lastLoadedMip;
    private float _mipmapBias;
    private AssetIntegrated _onPropertiesSet;
    private ColorProfile? _targetProfile;
    private bool _texturePropertiesDirty;
    private int _totalMips;
    private Texture3D _unityTexture3D;
    private TextureWrapMode _wrapU;
    private TextureWrapMode _wrapV;
    private TextureWrapMode _wrapW;


    public UnityEngine.Texture2D UnityTexture2D { get; private set; }

    public Cubemap UnityCubemap { get; private set; }

    public void SetCubemapFormat(
        int size,
        int mips,
        TextureFormat format,
        ColorProfile profile,
        AssetIntegrated onDone)
    {
        SetTextureFormat(new TextureFormatData
        {
            Type = TextureType.Cubemap,
            Width = size,
            Height = size,
            Mips = mips,
            Format = format,
            OnDone = onDone,
            Profile = profile,
            Depth = 1
        });
    }

    public void SetCubemapData(BitmapCube data, int startMipLevel, AssetIntegrated onSet)
    {
        SetTextureData(new TextureUploadData
        {
            BitmapCube = data,
            StartMip = startMipLevel,
            OnDone = onSet
        });
    }

    public void SetCubemapProperties(
        TextureFilterMode filterMode,
        int anisoLevel,
        float mipmapBias,
        AssetIntegrated onSet)
    {
        _texturePropertiesDirty = true;
        _filterMode = filterMode;
        _anisoLevel = anisoLevel;
        _mipmapBias = mipmapBias;
        _onPropertiesSet = onSet;
        if (onSet == null)
            return;
        UnityAssetIntegrator.EnqueueProcessing(UpdateTextureProperties, Asset.HighPriorityIntegration);
    }

    public void SetTexture2DFormat(
        int width,
        int height,
        int mips,
        TextureFormat format,
        ColorProfile profile,
        AssetIntegrated onDone)
    {
        SetTextureFormat(new TextureFormatData
        {
            Type = TextureType.Texture2D,
            Width = width,
            Height = height,
            Mips = mips,
            Format = format,
            OnDone = onDone,
            Profile = profile,
            Depth = 1
        });
    }


    public void SetTexture2DData(Bitmap2D data, int startMipLevel, TextureUploadHint hint, AssetIntegrated onSet)
    {
        SetTextureData(new TextureUploadData
        {
            Bitmap2D = data,
            StartMip = startMipLevel,
            Hint = hint,
            OnDone = onSet
        });
    }

    public void SetTexture2DProperties(
        TextureFilterMode filterMode,
        int anisoLevel,
        TextureWrapMode wrapU,
        TextureWrapMode wrapV,
        float mipmapBias,
        AssetIntegrated onSet)
    {
        _texturePropertiesDirty = true;
        _filterMode = filterMode;
        _anisoLevel = anisoLevel;
        _wrapU = wrapU;
        _wrapV = wrapV;
        _mipmapBias = mipmapBias;
        _onPropertiesSet = onSet;
        if (onSet == null)
            return;
        UnityAssetIntegrator.EnqueueProcessing(UpdateTextureProperties, Asset.HighPriorityIntegration);
    }

    public override void Unload()
    {
        UnityAssetIntegrator.EnqueueProcessing(Destroy, true);
    }

    public void SetTexture3DFormat(int width, int height, int depth, int mipmaps, TextureFormat format,
        ColorProfile profile, AssetIntegrated onDone)
    {
        SetTextureFormat(new TextureFormatData
        {
            Type = TextureType.Texture3D,
            Width = width,
            Height = height,
            Depth = depth,
            Mips = mipmaps,
            Format = format,
            OnDone = onDone,
            Profile = profile
        });
    }

    public void SetTexture3DData(Bitmap3D data, Texture3DUploadHint hint, AssetIntegrated onSet)
    {
        SetTextureData(new TextureUploadData
        {
            Bitmap3D = data,
            StartMip = 0,
            OnDone = onSet,
            hint3D = hint
        });
    }

    public void SetTexture3DProperties(TextureFilterMode filterMode, TextureWrapMode wrapU, TextureWrapMode wrapV,
        TextureWrapMode wrapW, AssetIntegrated onSet)
    {
        _texturePropertiesDirty = true;
        _filterMode = filterMode;
        _wrapU = wrapU;
        _wrapV = wrapV;
        _wrapW = wrapW;
        _onPropertiesSet = onSet;
        if (onSet != null)
            UnityAssetIntegrator.EnqueueProcessing(UpdateTextureProperties, Asset.HighPriorityIntegration);
    }

    Texture IUnityTextureProvider.UnityTexture => (Texture)(UnityTexture2D ?? (object)UnityCubemap ?? _unityTexture3D);

    private void SetTextureFormatUnity(TextureFormatData format)
    {
        var environmentInstanceChanged = false;
        if (format.Type == TextureType.Texture2D)
        {
            var textureFormat = format.Format.ToUnity();
            if (UnityTexture2D == null || UnityTexture2D.width != format.Width ||
                UnityTexture2D.height != format.Height || UnityTexture2D.format != textureFormat ||
                UnityTexture2D.mipmapCount > 1 != format.Mips > 1)
            {
                Destroy();
                UnityTexture2D = new UnityEngine.Texture2D(format.Width, format.Height, textureFormat, format.Mips > 1);
                environmentInstanceChanged = true;
            }
        }
        else if (format.Type == TextureType.Texture3D)
        {
            var profile = format.Profile;
            var graphicsFormat = format.Format.ToUnityExperimental(ref profile);
            if (profile != format.Profile) _targetProfile = profile; //new ColorProfile?(profile);
            if (_unityTexture3D == null || _unityTexture3D.width != format.Width ||
                _unityTexture3D.height != format.Height || _unityTexture3D.depth != format.Depth ||
                _unityTexture3D.graphicsFormat != graphicsFormat ||
                _unityTexture3D.mipmapCount > 1 != format.Mips > 1 || profile != _targetProfile)
            {
                Destroy();
                _targetProfile = profile; //new ColorProfile?(profile);
                _unityTexture3D = new Texture3D(format.Width, format.Height, format.Depth, graphicsFormat,
                    TextureCreationFlags.None);
                environmentInstanceChanged = true;
                //ColorProfile colorProfile = profile;
                //ColorProfile? targetProfile = this._targetProfile;
                //if (colorProfile == targetProfile.GetValueOrDefault() & targetProfile != null)
                //{
                //goto IL_199;
                //}
            }
        }

        AssignTextureProperties();
        format.OnDone(environmentInstanceChanged);
    }

    private void SetTextureFormat(TextureFormatData format)
    {
        if (format.Type == TextureType.Texture3D)
        {
            SetFormatUnity();
            return;
        }

        switch (UnityAssetIntegrator.GraphicsDeviceType)
        {
            case GraphicsDeviceType.Direct3D11:
                UnityAssetIntegrator.EnqueueRenderThreadProcessing(SetTextureFormatDX11Native(format));
                return;
            /*
         case GraphicsDeviceType.OpenGLES2:
         case GraphicsDeviceType.OpenGLES3:
         case GraphicsDeviceType.OpenGLCore:
                UnityAssetIntegrator.EnqueueRenderThreadProcessing(this.SetTextureFormatOpenGLNative(format));
             return;
                */
        }

        SetFormatUnity();

        void SetFormatUnity()
        {
            UnityAssetIntegrator.EnqueueProcessing(delegate { SetTextureFormatUnity(format); }, true);
        }
    }

    private void SetTextureData(TextureUploadData data)
    {
        if (data.Bitmap3D != null)
        {
            //UnityAssetIntegrator.EnqueueRenderThreadProcessing(() => UploadTextureDataUnity(data));
            EnqueueUnityUpload();
            return;
        }

        switch (UnityAssetIntegrator.GraphicsDeviceType)
        {
            case GraphicsDeviceType.Direct3D11:
                data.Format.ToDX11(out var convertToFormat, data.Bitmap.Profile, Engine.SystemInfo);
                if (convertToFormat != data.Format)
                {
                    UniLog.Warning(
                        $"Converting texture format from {data.Format} to {convertToFormat}. Texture: {data.Bitmap}. Asset: {Asset}");
                    data.ConvertTo(convertToFormat);
                }

                UnityAssetIntegrator.EnqueueRenderThreadProcessing(UploadTextureDataDX11Native(data));
                return;
            /*
         case GraphicsDeviceType.OpenGLES2:
         case GraphicsDeviceType.OpenGLES3:
         case GraphicsDeviceType.OpenGLCore:
            {
              Helper.OpenGL_TextureFormat openGL_TextureFormat = data.Format.ToOpenGL(data.Bitmap.Profile, base.Engine.SystemInfo);
              if (openGL_TextureFormat.sourceFormat != data.Format)
              {
                  UniLog.Warning($"Converting texture format from {data.Format} to {openGL_TextureFormat}. Texture: {data.Bitmap}. Asset: {Asset}");
                 data.ConvertTo(openGL_TextureFormat.sourceFormat);
             }
              base.UnityAssetIntegrator.EnqueueRenderThreadProcessing(UploadTextureDataOpenGLNative(data));
              return;
            }
          */
        }

        EnqueueUnityUpload();

        void EnqueueUnityUpload()
        {
            UnityAssetIntegrator.EnqueueProcessing(delegate { UploadTextureDataUnity(data); },
                Asset.HighPriorityIntegration);
        }
    }

    private void UploadTextureDataUnity(TextureUploadData data)
    {
        if (data.Bitmap2D != null)
        {
            var @int = new int2(UnityTexture2D.width, UnityTexture2D.height);
            var num = 0;
            for (var i = 0; i < data.StartMip; i++)
            {
                var int2 = Bitmap2DBase.AlignSize(in @int, data.Format);
                num += int2.x * int2.y;
                @int = @int / 2;
                @int = MathX.Max(in @int, 1);
            }

            num = (int)MathX.BitsToBytes(num * data.Format.GetBitsPerPixel());
            var rawTextureData = UnityTexture2D.GetRawTextureData<byte>();
            var bitmap2D = data.Bitmap2D;
            byte[] array;
            if ((array = bitmap2D != null ? bitmap2D.RawData : null) == null)
            {
                var bitmapCube = data.BitmapCube;
                array = bitmapCube != null ? bitmapCube.RawData : null;
            }

            var array2 = array;
            for (var j = 0; j < array2.Length; j++) rawTextureData[j + num] = array2[j];
        }
        else if (data.Bitmap3D != null)
        {
            //ColorProfile profile = data.Bitmap3D.Profile;
            //ColorProfile? targetProfile = _targetProfile;
            if (_targetProfile.HasValue && data.Bitmap3D.Profile != _targetProfile)
                data.Bitmap3D.ConvertToProfile(_targetProfile.Value);
            _unityTexture3D.SetPixelData(data.Bitmap3D.RawData, data.StartMip);
        }

        if (data.StartMip == 0)
        {
            var unityTexture2D = UnityTexture2D;
            if (unityTexture2D != null) unityTexture2D.Apply(false, !data.Hint.readable);
            var unityCubemap = UnityCubemap;
            if (unityCubemap != null) unityCubemap.Apply(false, !data.Hint.readable);
            var unityTexture3D = _unityTexture3D;
            if (unityTexture3D != null) unityTexture3D.Apply(false, !data.hint3D.readable);
            Engine.TextureUpdated();
        }

        data.OnDone(false);
    }

    private IEnumerator DestroyDX11(ShaderResourceView resource, Texture2D tex)
    {
        if (resource != null) resource.Dispose();
        if (tex != null) tex.Dispose();
        yield break;
    }

    private void Destroy()
    {
        if ((bool)UnityTexture2D) Object.DestroyImmediate(UnityTexture2D, true);
        if ((bool)UnityCubemap) Object.DestroyImmediate(UnityCubemap, true);
        if ((bool)_unityTexture3D) Object.DestroyImmediate(_unityTexture3D, true);
        if (_dx11Resource != null)
        {
            UnityAssetIntegrator.EnqueueRenderThreadProcessing(DestroyDX11(_dx11Resource, _dx11Tex));
            _dx11Tex = null;
            _dx11Resource = null;
        }

        UnityTexture2D = null;
        UnityCubemap = null;
        UnityTexture2D = null;
        _targetProfile = null;
    }

    private void AssignTextureProperties()
    {
        if (!_texturePropertiesDirty)
            return;
        _texturePropertiesDirty = false;
        if (UnityTexture2D != null)
        {
            if (_filterMode == TextureFilterMode.Anisotropic)
            {
                UnityTexture2D.filterMode = FilterMode.Trilinear;
                UnityTexture2D.anisoLevel = _anisoLevel;
            }
            else
            {
                UnityTexture2D.filterMode = _filterMode.ToUnity();
                UnityTexture2D.anisoLevel = 0;
            }

            UnityTexture2D.wrapModeU = _wrapU.ToUnity();
            UnityTexture2D.wrapModeV = _wrapV.ToUnity();
            UnityTexture2D.mipMapBias = _mipmapBias;
        }

        if (UnityCubemap != null)
        {
            if (_filterMode == TextureFilterMode.Anisotropic)
            {
                UnityCubemap.filterMode = FilterMode.Trilinear;
                UnityCubemap.anisoLevel = _anisoLevel;
            }
            else
            {
                UnityCubemap.filterMode = _filterMode.ToUnity();
                UnityCubemap.anisoLevel = 0;
            }

            UnityCubemap.mipMapBias = _mipmapBias;
        }

        if (_unityTexture3D != null)
        {
            if (_filterMode == TextureFilterMode.Anisotropic)
            {
                _unityTexture3D.filterMode = FilterMode.Trilinear;
                _unityTexture3D.anisoLevel = _anisoLevel;
            }
            else
            {
                _unityTexture3D.filterMode = _filterMode.ToUnity();
                _unityTexture3D.anisoLevel = 0;
            }

            _unityTexture3D.mipMapBias = _mipmapBias;
            _unityTexture3D.wrapModeU = _wrapU.ToUnity();
            _unityTexture3D.wrapModeV = _wrapV.ToUnity();
            _unityTexture3D.wrapModeW = _wrapW.ToUnity();
        }
    }

    private void UpdateTextureProperties()
    {
        AssignTextureProperties();
        var onPropertiesSet = _onPropertiesSet;
        _onPropertiesSet = null;
        onPropertiesSet(false);
    }

    private void GenerateUnityTextureFromDX11(TextureFormatData format)
    {
        switch (format.Type)
        {
            case TextureType.Texture2D:
                UnityTexture2D = UnityEngine.Texture2D.CreateExternalTexture(format.Width, format.Height,
                    format.Format.ToUnity(), format.Mips > 1, false, _dx11Resource.NativePointer);
                break;
            case TextureType.Cubemap:
                UnityCubemap = Cubemap.CreateExternalTexture(format.Width, format.Format.ToUnity(),
                    format.Mips > 1, _dx11Resource.NativePointer);
                break;
        }

        AssignTextureProperties();
        format.OnDone(true);
    }

    private IEnumerator SetTextureFormatDX11Native(TextureFormatData format)
    {
        var format2 = format.Format.ToDX11(out _, format.Profile, Engine.SystemInfo);
        var description = _dx11Tex?.Description ?? default(Texture2DDescription);
        var flag = false;
        if (_dx11Tex == null
            || description.Width != format.Width
            || description.Height != format.Height
            || description.ArraySize != format.ArraySize
            || description.Format != format2
            || description.MipLevels != format.Mips)
        {
            if (_dx11Tex != null)
            {
                var oldUnityTex = UnityTexture2D;
                var oldUnityCube = UnityCubemap;
                var oldDX11tex = _dx11Tex;
                var oldDX11res = _dx11Resource;
                var oldOnDone = format.OnDone;
                format.OnDone = delegate
                {
                    if (oldUnityTex) Object.DestroyImmediate(oldUnityTex);
                    if (oldUnityCube) Object.DestroyImmediate(oldUnityCube);
                    oldDX11res?.Dispose();
                    oldDX11tex?.Dispose();
                    oldOnDone(true);
                };
            }

            description.Width = format.Width;
            description.Height = format.Height;
            description.MipLevels = format.Mips;
            description.ArraySize = format.ArraySize;
            description.Format = format2;
            description.SampleDescription.Count = 1;
            description.Usage = ResourceUsage.Default;
            description.BindFlags = BindFlags.ShaderResource;
            description.CpuAccessFlags = CpuAccessFlags.None;
            description.OptionFlags = format.Type == TextureType.Texture2D
                ? ResourceOptionFlags.ResourceClamp
                : ResourceOptionFlags.TextureCube | ResourceOptionFlags.ResourceClamp;
            var description2 = default(ShaderResourceViewDescription);
            description2.Format = description.Format;
            description2.Dimension = format.Type == TextureType.Texture2D
                ? ShaderResourceViewDimension.Texture2D
                : ShaderResourceViewDimension.TextureCube;
            switch (format.Type)
            {
                case TextureType.Texture2D:
                    description2.Texture2D.MipLevels = format.Mips;
                    description2.Texture2D.MostDetailedMip = 0;
                    break;
                case TextureType.Cubemap:
                    description2.TextureCube.MipLevels = format.Mips;
                    description2.TextureCube.MostDetailedMip = 0;
                    break;
            }

            try
            {
                _dx11Tex = new Texture2D(UnityAssetIntegrator._dx11device, description);
                _dx11Resource = new ShaderResourceView(UnityAssetIntegrator._dx11device, _dx11Tex, description2);
                _totalMips = format.Mips;
            }
            catch (Exception ex)
            {
                UniLog.Error(
                    $"Exception creating texture: Width: {description.Width}, Height: {description.Height}, Mips: {description.MipLevels}, format: {format2}.");
                throw ex;
            }

            _lastLoadedMip = format.Mips;
            flag = true;
        }

        if (flag)
            UnityAssetIntegrator.EnqueueProcessing(delegate { GenerateUnityTextureFromDX11(format); },
                true);
        else if (_texturePropertiesDirty)
            UnityAssetIntegrator.EnqueueProcessing(delegate
            {
                AssignTextureProperties();
                format.OnDone(false);
            }, true);
        else
            format.OnDone(false);

        yield break;
    }

    private IEnumerator UploadTextureDataDX11Native(TextureUploadData data)
    {
        var elements = data.ElementCount;
        var hint = data.Hint;
        var bitmap = data.Bitmap;
        var faceSize = data.FaceSize;
        var format = data.Format;
        var totalMipMaps = _totalMips;
        var width = hint.region?.width ?? faceSize.x;
        var height = hint.region?.height ?? faceSize.y;
        var startX = hint.region?.x ?? 0;
        var startY = hint.region?.y ?? 0;
        var blockSize = format.BlockSize();
        var bitsPerPixel = format.GetBitsPerPixel();
        if (width > 0 || height > 0)
        {
            for (var mip = 0; mip < bitmap.MipMapLevels; mip++)
            {
                for (var face = 0; face < elements; face++)
                {
                    var levelSize = data.MipMapSize(mip);
                    var targetMip = data.StartMip + mip;
                    width = MathX.Min(width, levelSize.x - startX);
                    height = MathX.Min(height, levelSize.y - startY);
                    var mipSize = Bitmap2DBase.AlignSize(in levelSize, data.Format);
                    var size2 = new int2(width, height);
                    size2 = Bitmap2DBase.AlignSize(in size2, data.Format);
                    var rowGranularity3 = 65536 / width;
                    rowGranularity3 -= rowGranularity3 % 4;
                    rowGranularity3 = MathX.Max(4, rowGranularity3);
                    var row = 0;
                    var rowPitch = (int)(MathX.BitsToBytes(mipSize.x * bitsPerPixel) * blockSize.y);
                    while (row < height)
                    {
                        if (row > 0) yield return null;

                        ResourceRegion? resourceRegion = new ResourceRegion(startX, startY + row, 0, startX + size2.x,
                            MathX.Min(startY + row + rowGranularity3, startY + size2.y), 1);
                        if (resourceRegion.Value.Left == 0 && resourceRegion.Value.Top == 0 &&
                            resourceRegion.Value.Right == mipSize.x && resourceRegion.Value.Bottom == mipSize.y)
                            resourceRegion = null;

                        var num = startY + row;
                        if (data.Bitmap2D != null) num = levelSize.y - num - 1;

                        var num2 = (int)MathX.BitsToBytes(data.PixelStart(startX, num, mip, face) * bitsPerPixel);
                        UnityAssetIntegrator._dx11device.ImmediateContext.UpdateSubresource(ref bitmap.RawData[num2],
                            _dx11Tex, targetMip + face * totalMipMaps, rowPitch, 0, resourceRegion);
                        row += rowGranularity3;
                        Engine.TextureSliceUpdated();
                    }
                }

                width /= 2;
                height /= 2;
                startX /= 2;
                startY /= 2;
                width = MathX.Max(width, 1);
                height = MathX.Max(height, 1);
            }

            _lastLoadedMip = MathX.Min(_lastLoadedMip, data.StartMip);
            _dx11Tex.Device.ImmediateContext.SetMinimumLod(_dx11Tex, _lastLoadedMip);
        }

        Engine.TextureUpdated();
        data.OnDone(false);
    }

    private enum TextureType
    {
        Texture2D,
        Cubemap,
        Texture3D
    }

    private class TextureFormatData
    {
        public int Depth;
        public TextureFormat Format;
        public int Height;
        public int Mips;
        public AssetIntegrated OnDone;
        public ColorProfile Profile;
        public TextureType Type;
        public int Width;

        public int ArraySize
        {
            get
            {
                return Type switch
                {
                    TextureType.Texture2D => 1,
                    TextureType.Cubemap => 6,
                    TextureType.Texture3D => Depth,
                    _ => throw new Exception("Invalid texture type: " + Type)
                };
            }
        }
    }

    private class TextureUploadData
    {
        public Bitmap2D Bitmap2D;

        public Bitmap3D Bitmap3D;

        public BitmapCube BitmapCube;

        public TextureUploadHint Hint;

        public Texture3DUploadHint hint3D;

        public AssetIntegrated OnDone;

        public int StartMip;

        public Bitmap Bitmap
        {
            get
            {
                Bitmap result;
                if ((result = Bitmap2D) == null) result = BitmapCube != null ? BitmapCube : Bitmap3D;
                return result;
            }
        }

        public TextureFormat Format => Bitmap.Format;


        public int2 FaceSize
        {
            get
            {
                var bitmap2D = Bitmap2D;
                if (bitmap2D != null) return bitmap2D.Size;
                var bitmapCube = BitmapCube;
                if (bitmapCube == null) return int2.Zero;
                return bitmapCube.Size;
            }
        }

        public int ElementCount
        {
            get
            {
                if (Bitmap2D != null) return 1;
                if (BitmapCube != null) return 6;
                if (Bitmap3D != null) return Bitmap3D.Size.z;
                throw new Exception("Invalid state, must have either Bitmap2D, BitmapCUBE or Bitmap3D");
            }
        }

        public int2 MipMapSize(int mip)
        {
            var bitmap2D = Bitmap2D;
            if (bitmap2D != null) return bitmap2D.MipMapSize(mip);
            var bitmapCube = BitmapCube;
            if (bitmapCube == null) return int2.Zero;
            return bitmapCube.MipMapSize(mip);
        }

        public int PixelStart(int x, int y, int mip, int face)
        {
            var bitmap2D = Bitmap2D;
            if (bitmap2D == null) return BitmapCube.PixelStart(x, y, (BitmapCube.Face)face, mip);
            return bitmap2D.PixelStart(x, y, mip);
        }

        public void ConvertTo(TextureFormat format)
        {
            if (Bitmap2D != null) Bitmap2D = Bitmap2D.ConvertTo(format);
            if (BitmapCube != null) BitmapCube = BitmapCube.ConvertTo(format);
        }
    }
}