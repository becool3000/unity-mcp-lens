using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Provides an extension method to convert a <see cref="Texture2D"/> to a GIF byte array.
    /// This utility leverages the native FreeImage library that is bundled with the Unity Editor.
    /// Note: This class and its methods are only available and intended for use within the Unity Editor.
    /// </summary>
    static class FreeImageEncoder
    {
#if UNITY_EDITOR_WIN
        const string k_FreeImageDll = "FreeImage";
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        // On macOS and Linux, FreeImage is statically linked into the Editor executable.
        const string k_FreeImageDll = "__Internal";
#endif

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void OutputMessageFunction(FREE_IMAGE_FORMAT fif, IntPtr message);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_SetOutputMessage")]
        static extern void FreeImage_SetOutputMessage(OutputMessageFunction omf);

        static readonly OutputMessageFunction k_ErrorMessageHandler = FreeImageErrorHandler;

        static FreeImageEncoder()
        {
            FreeImage_SetOutputMessage(k_ErrorMessageHandler);
        }

        static void FreeImageErrorHandler(FREE_IMAGE_FORMAT fif, IntPtr message)
        {
            if (message != IntPtr.Zero)
            {
                var errorMessage = Marshal.PtrToStringAnsi(message);
                Debug.LogError($"[FreeImage Error] Format: {fif}, Message: {errorMessage}");
            }
        }

        enum FREE_IMAGE_FORMAT
        {
            FIF_BMP = 0,
            FIF_LBM = 5,
            FIF_TARGA = 17,
            FIF_TIFF = 18,
            FIF_PSD = 20,
            FIF_GIF = 25,
            FIF_HDR = 26,
            FIF_PICT = 33,
        }

        enum FREE_IMAGE_QUANTIZE
        {
            FIQ_WUQUANT = 0,
        }

        enum FREE_IMAGE_TYPE
        {
            FIT_UNKNOWN = 0,
            FIT_BITMAP = 1,
            FIT_UINT16 = 2,
            FIT_INT16 = 3,
            FIT_UINT32 = 4,
            FIT_INT32 = 5,
            FIT_FLOAT = 6,
            FIT_DOUBLE = 7,
            FIT_COMPLEX = 8,
            FIT_RGB16 = 9,
            FIT_RGBA16 = 10,
            FIT_RGBF = 11,
            FIT_RGBAF = 12,
        }

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_Allocate")]
        static extern IntPtr FreeImage_Allocate(int width, int height, int bpp, uint redMask, uint greenMask, uint blueMask);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_AllocateT")]
        static extern IntPtr FreeImage_AllocateT(FREE_IMAGE_TYPE type, int width, int height, int bpp, uint redMask, uint greenMask, uint blueMask);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_Unload")]
        static extern void FreeImage_Unload(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetScanLine")]
        static extern IntPtr FreeImage_GetScanLine(IntPtr dib, int scanline);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_OpenMemory")]
        static extern IntPtr FreeImage_OpenMemory(IntPtr data = default, uint sizeInBytes = 0);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_CloseMemory")]
        static extern void FreeImage_CloseMemory(IntPtr stream);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_SaveToMemory")]
        static extern bool FreeImage_SaveToMemory(FREE_IMAGE_FORMAT fif, IntPtr dib, IntPtr stream, int flags);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_AcquireMemory")]
        static extern bool FreeImage_AcquireMemory(IntPtr stream, out IntPtr data, out uint sizeInBytes);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_ColorQuantize")]
        static extern IntPtr FreeImage_ColorQuantize(IntPtr dib, FREE_IMAGE_QUANTIZE algorithm);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_SetTransparencyTable")]
        static extern void FreeImage_SetTransparencyTable(IntPtr dib, byte[] table, int count);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_ConvertToRGBF")]
        static extern IntPtr FreeImage_ConvertToRGBF(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetBPP")]
        static extern uint FreeImage_GetBPP(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetWidth")]
        static extern int FreeImage_GetWidth(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetHeight")]
        static extern int FreeImage_GetHeight(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetRedMask")]
        static extern uint FreeImage_GetRedMask(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetGreenMask")]
        static extern uint FreeImage_GetGreenMask(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetBlueMask")]
        static extern uint FreeImage_GetBlueMask(IntPtr dib);

        [StructLayout(LayoutKind.Sequential)]
        struct RGBQUAD
        {
            public byte rgbBlue;
            public byte rgbGreen;
            public byte rgbRed;
            public byte rgbReserved;
        }

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetPalette")]
        static extern IntPtr FreeImage_GetPalette(IntPtr dib);

        [DllImport(k_FreeImageDll, EntryPoint = "FreeImage_GetColorsUsed")]
        static extern uint FreeImage_GetColorsUsed(IntPtr dib);

        /// <summary>
        /// Creates a native FreeImage bitmap from a Unity Texture2D.
        /// </summary>
        /// <param name="tex">The source texture.</param>
        /// <param name="hasAlpha">Outputs whether the texture contains non-opaque pixels.</param>
        /// <returns>A handle to the created FreeImage bitmap (dib).</returns>
        static IntPtr CreateDibFromTexture(Texture2D tex, out bool hasAlpha)
        {
            hasAlpha = false;
            var width = tex.width;
            var height = tex.height;

            IntPtr dib;

            // the FreeImage instance we are interfacing with is expecting to receive pixel data in RGBA format.
            switch (tex.format)
            {
                case TextureFormat.ARGB32:
                {
                    // FreeImage.h defines FREEIMAGE_COLORORDER FREEIMAGE_COLORORDER_RGB
                    dib = FreeImage_Allocate(width, height, 32, 0x0000FF, 0x00FF00, 0xFF0000);
                    if (dib == IntPtr.Zero)
                    {
                        throw new Exception("FreeImage_Allocate failed for 32-bit bitmap.");
                    }

                    using var rawData = tex.GetRawTextureData<byte>();
                    var stride = width * 4;
                    var rowBuffer = new byte[stride]; // Small, temporary managed buffer for one row.

                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * stride;
                        for (var x = 0; x < stride; x += 4)
                        {
                            var i = srcOffset + x;
                            var alpha = rawData[i + 0];
                            rowBuffer[x + 0] = rawData[i + 1]; // R
                            rowBuffer[x + 1] = rawData[i + 2]; // G
                            rowBuffer[x + 2] = rawData[i + 3]; // B
                            rowBuffer[x + 3] = alpha; // A
                            if (alpha < 255)
                            {
                                hasAlpha = true;
                            }
                        }

                        Marshal.Copy(rowBuffer, 0, FreeImage_GetScanLine(dib, y), stride);
                    }

                    break;
                }

                case TextureFormat.BGRA32:
                {
                    // FreeImage.h defines FREEIMAGE_COLORORDER FREEIMAGE_COLORORDER_RGB
                    dib = FreeImage_Allocate(width, height, 32, 0x0000FF, 0x00FF00, 0xFF0000);
                    if (dib == IntPtr.Zero)
                    {
                        throw new Exception("FreeImage_Allocate failed for 32-bit bitmap.");
                    }

                    using var rawData = tex.GetRawTextureData<byte>();
                    var stride = width * 4;
                    var rowBuffer = new byte[stride];

                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * stride;
                        for (var x = 0; x < stride; x += 4)
                        {
                            var i = srcOffset + x;
                            var alpha = rawData[i + 3];
                            rowBuffer[x + 0] = rawData[i + 2]; // R
                            rowBuffer[x + 1] = rawData[i + 1]; // G
                            rowBuffer[x + 2] = rawData[i + 0]; // B
                            rowBuffer[x + 3] = alpha; // A
                            if (alpha < 255)
                            {
                                hasAlpha = true;
                            }
                        }

                        Marshal.Copy(rowBuffer, 0, FreeImage_GetScanLine(dib, y), stride);
                    }

                    break;
                }

                case TextureFormat.RGBA32:
                {
                    // FreeImage.h defines FREEIMAGE_COLORORDER FREEIMAGE_COLORORDER_RGB
                    dib = FreeImage_Allocate(width, height, 32, 0x0000FF, 0x00FF00, 0xFF0000);
                    if (dib == IntPtr.Zero)
                    {
                        throw new Exception("FreeImage_Allocate failed for 32-bit bitmap.");
                    }

                    using var rawData = tex.GetRawTextureData<byte>();
                    var stride = width * 4;

                    // We must copy from NativeArray to managed array row-by-row for Marshal.Copy
                    var rowBuffer = new byte[stride];

                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * stride;
                        var slice = new NativeSlice<byte>(rawData, srcOffset, stride);
                        slice.CopyTo(rowBuffer); // Efficient copy from native to managed buffer

                        var destPtr = FreeImage_GetScanLine(dib, y);
                        Marshal.Copy(rowBuffer, 0, destPtr, stride);
                    }

                    for (var i = 3; i < rawData.Length; i += 4)
                    {
                        if (rawData[i] < 255)
                        {
                            hasAlpha = true;
                            break;
                        }
                    }

                    break;
                }

                case TextureFormat.RGB24:
                {
                    // FreeImage.h defines FREEIMAGE_COLORORDER FREEIMAGE_COLORORDER_RGB
                    dib = FreeImage_Allocate(width, height, 24, 0x0000FF, 0x00FF00, 0xFF0000);
                    if (dib == IntPtr.Zero)
                    {
                        throw new Exception("FreeImage_Allocate failed for 24-bit bitmap.");
                    }

                    using var rawData = tex.GetRawTextureData<byte>();
                    var stride = width * 3;
                    var rowBuffer = new byte[stride];

                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * stride;
                        var slice = new NativeSlice<byte>(rawData, srcOffset, stride);
                        slice.CopyTo(rowBuffer); // Efficient copy from native to managed buffer

                        var destPtr = FreeImage_GetScanLine(dib, y);
                        Marshal.Copy(rowBuffer, 0, destPtr, stride);
                    }

                    break;
                }

                case TextureFormat.RGBAHalf: // 16-bit float per channel, 64 bpp
                {
                    // We convert half-float data to full-float data and use FIT_RGBAF.
                    // FIT_RGBAF uses 4 x 32-bit float for RGBA (128 bpp).
                    dib = FreeImage_AllocateT(FREE_IMAGE_TYPE.FIT_RGBAF, width, height, 128, 0, 0, 0);
                    if (dib == IntPtr.Zero)
                        throw new Exception("FreeImage_AllocateT failed for 128-bit RGBA float bitmap.");

                    using var rawData = tex.GetRawTextureData<half>();
                    var strideInHalfs = width * 4;
                    var rowBufferFloats = new float[strideInHalfs]; // Buffer to hold the converted float data

                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * strideInHalfs;
                        // Manually convert one row of halfs to floats
                        for (var i = 0; i < strideInHalfs; i++)
                        {
                            rowBufferFloats[i] = rawData[srcOffset + i]; // Implicit conversion
                        }

                        var destPtr = FreeImage_GetScanLine(dib, y);
                        Marshal.Copy(rowBufferFloats, 0, destPtr, strideInHalfs);
                    }

                    // Check for alpha usage by comparing to the half representation of 1.0
                    var oneHalf = new half(1.0f);
                    for (var i = 3; i < rawData.Length; i += 4)
                    {
                        if (rawData[i] != oneHalf)
                        {
                            hasAlpha = true;
                            break;
                        }
                    }
                    break;
                }

                case TextureFormat.RGBAFloat: // 32-bit float per channel, 128 bpp
                {
                    // FIT_RGBAF uses 4 x 32-bit float for RGBA
                    dib = FreeImage_AllocateT(FREE_IMAGE_TYPE.FIT_RGBAF, width, height, 128, 0, 0, 0);
                    if (dib == IntPtr.Zero)
                        throw new Exception("FreeImage_AllocateT failed for 128-bit RGBA float bitmap.");

                    using var rawData = tex.GetRawTextureData<float>();
                    var strideInFloats = width * 4;
                    var rowBuffer = new float[strideInFloats];

                    // FreeImage FIT_RGBAF is stored as RGBA, which matches Unity's RGBAFloat. No swizzling needed.
                    for (var y = 0; y < height; y++)
                    {
                        var srcOffset = y * strideInFloats;
                        var slice = new NativeSlice<float>(rawData, srcOffset, strideInFloats);
                        slice.CopyTo(rowBuffer);

                        var destPtr = FreeImage_GetScanLine(dib, y);
                        Marshal.Copy(rowBuffer, 0, destPtr, strideInFloats);
                    }

                    // Check for alpha usage
                    for (var i = 3; i < rawData.Length; i += 4)
                    {
                        if (rawData[i] != 1.0f)
                        {
                            hasAlpha = true;
                            break;
                        }
                    }
                    break;
                }

                default:
                    throw new NotSupportedException($"Texture format {tex.format} is not supported.");
            }

            return dib;
        }

        /// <summary>
        /// A generic encoder that handles resource creation, processing, saving, and cleanup.
        /// </summary>
        /// <param name="tex">The source texture.</param>
        /// <param name="format">The target FreeImage format.</param>
        /// <param name="flags">Format-specific saving flags.</param>
        /// <param name="processor">A function that takes the source bitmap and alpha flag, and returns the bitmap to save along with any intermediate bitmaps that need cleanup.</param>
        /// <returns>A byte array of the encoded image file.</returns>
        static byte[] EncodeWithProcessor(
            Texture2D tex,
            FREE_IMAGE_FORMAT format,
            int flags = 0,
            Func<IntPtr, bool, (IntPtr dibToSave, IntPtr[] intermediateDibs)> processor = null)
        {
            if (tex == null)
            {
                throw new ArgumentNullException(nameof(tex));
            }

            if (!tex.isReadable)
            {
                throw new InvalidOperationException("Texture is not readable. Enable Read/Write in import settings.");
            }

            var dibSrc = IntPtr.Zero;
            var mem = IntPtr.Zero;
            IntPtr[] intermediateDibs = null;

            try
            {
                dibSrc = CreateDibFromTexture(tex, out var hasAlpha);

                var dibToSave = dibSrc;
                if (processor != null)
                {
                    (dibToSave, intermediateDibs) = processor(dibSrc, hasAlpha);
                }

                mem = FreeImage_OpenMemory();
                if (mem == IntPtr.Zero)
                {
                    throw new Exception("FreeImage_OpenMemory failed.");
                }

                if (!FreeImage_SaveToMemory(format, dibToSave, mem, flags))
                {
                    throw new Exception($"FreeImage_SaveToMemory failed to encode the image to {format}.");
                }

                if (!FreeImage_AcquireMemory(mem, out var dataPtr, out var size))
                {
                    throw new Exception("FreeImage_AcquireMemory failed.");
                }

                var imageData = new byte[size];
                Marshal.Copy(dataPtr, imageData, 0, (int)size);
                return imageData;
            }
            finally
            {
                // Clean up all native resources
                if (intermediateDibs != null)
                {
                    // Unload intermediate DIBs, but skip unloading dibSrc as it's handled separately.
                    foreach (var dib in intermediateDibs.Where(d => d != IntPtr.Zero && d != dibSrc))
                        FreeImage_Unload(dib);
                }

                if (dibSrc != IntPtr.Zero)
                {
                    FreeImage_Unload(dibSrc);
                }

                if (mem != IntPtr.Zero)
                {
                    FreeImage_CloseMemory(mem);
                }
            }
        }

        /// <summary>
        /// Encodes this texture into GIF format.
        /// </summary>
        public static byte[] EncodeToGIF(this Texture2D tex)
        {
            return EncodeWithProcessor(tex, FREE_IMAGE_FORMAT.FIF_GIF, 0, (dibSrc, hasAlpha) =>
            {
                var dib8 = FreeImage_ColorQuantize(dibSrc, FREE_IMAGE_QUANTIZE.FIQ_WUQUANT);
                if (dib8 == IntPtr.Zero)
                {
                    throw new Exception("FreeImage_ColorQuantize failed.");
                }

                if (hasAlpha)
                {
                    var colorsUsed = FreeImage_GetColorsUsed(dib8);
                    var palettePtr = FreeImage_GetPalette(dib8);
                    if (palettePtr != IntPtr.Zero && colorsUsed > 0)
                    {
                        var transparencyTable = new byte[256];
                        var colorStructSize = Marshal.SizeOf<RGBQUAD>();

                        for (var i = 0; i < colorsUsed; i++)
                        {
                            var quad = Marshal.PtrToStructure<RGBQUAD>(new IntPtr(palettePtr.ToInt64() + i * colorStructSize));
                            transparencyTable[i] = quad.rgbReserved < 255 ? (byte)0 : (byte)255;
                        }

                        FreeImage_SetTransparencyTable(dib8, transparencyTable, 256);
                    }
                }

                return (dibToSave: dib8, intermediateDibs: new[] { dib8 });
            });
        }

        public static byte[] EncodeToBMP(this Texture2D tex)
        {
            return EncodeWithProcessor(tex, FREE_IMAGE_FORMAT.FIF_BMP);
        }

        public static byte[] EncodeToTIFF(this Texture2D tex, bool compress = true)
        {
            var flags = compress ? 1 : 0;

            return EncodeWithProcessor(tex, FREE_IMAGE_FORMAT.FIF_TIFF, flags, (dibSrc, hasAlpha) =>
            {
                ManualInvertAllChannels(dibSrc);

                // The function modifies dibSrc in-place, so we save the original bitmap.
                return (dibToSave: dibSrc, intermediateDibs: null);
            });
        }

        public static byte[] EncodeToPSD(this Texture2D tex)
        {
            return EncodeWithProcessor(tex, FREE_IMAGE_FORMAT.FIF_PSD);
        }

        public static byte[] EncodeToHDR(this Texture2D tex)
        {
            return EncodeWithProcessor(tex, FREE_IMAGE_FORMAT.FIF_HDR, 0, (dibSrc, hasAlpha) =>
            {
                // 1. Convert the source LDR bitmap (FIT_BITMAP) to an HDR bitmap (FIT_RGBF).
                //    FIT_RGBF uses 32-bit floats for each of the R, G, and B channels.
                //    This works for both LDR (FIT_BITMAP) and HDR (FIT_RGBAF) source bitmaps.
                var dibFloat = FreeImage_ConvertToRGBF(dibSrc);
                if (dibFloat == IntPtr.Zero)
                {
                    throw new Exception("FreeImage_ConvertToRGBF failed during HDR encoding.");
                }

                // 2. Return the new floating-point bitmap to be saved.
                //    Also, list it as an intermediate bitmap so the 'finally' block cleans it up.
                return (dibToSave: dibFloat, intermediateDibs: new[] { dibFloat });
            });
        }

        /// <summary>
        /// Manually inverts all channels (Red, Green, Blue, and Alpha) of a 32-bit FreeImage bitmap.
        /// This function is robust, using color masks to handle different channel orders.
        /// The bitmap is modified in-place.
        /// </summary>
        /// <param name="dib">A handle to the FreeImage bitmap to modify.</param>
        static void ManualInvertAllChannels(IntPtr dib)
        {
            // Ensure we are only operating on a valid 32-bpp image
            if (dib == IntPtr.Zero || FreeImage_GetBPP(dib) != 32)
            {
                return;
            }

            var width = FreeImage_GetWidth(dib);
            var height = FreeImage_GetHeight(dib);

            // Get the color channel masks to determine the memory layout of the pixel
            var redMask = FreeImage_GetRedMask(dib);
            var greenMask = FreeImage_GetGreenMask(dib);
            var blueMask = FreeImage_GetBlueMask(dib);
            var alphaMask = ~(redMask | greenMask | blueMask);

            // Helper function to calculate how many bits to shift to isolate a channel
            int GetShiftFromMask(uint mask)
            {
                var shift = 0;
                if (mask == 0)
                {
                    return 0;
                }

                while ((mask & 1) == 0)
                {
                    mask >>= 1;
                    shift++;
                }

                return shift;
            }

            var redShift = GetShiftFromMask(redMask);
            var greenShift = GetShiftFromMask(greenMask);
            var blueShift = GetShiftFromMask(blueMask);
            var alphaShift = GetShiftFromMask(alphaMask);

            // Iterate over each row (scanline) of the image
            for (var y = 0; y < height; y++)
            {
                var scanline = FreeImage_GetScanLine(dib, y);

                // Iterate over each pixel in the row
                for (var x = 0; x < width; x++)
                {
                    var pixelOffset = x * 4;

                    // Read the entire 32-bit pixel as an integer
                    var pixel = (uint)Marshal.ReadInt32(scanline, pixelOffset);

                    // 1. Extract the original channels
                    var alpha = (pixel & alphaMask) >> alphaShift;
                    var red = (pixel & redMask) >> redShift;
                    var green = (pixel & greenMask) >> greenShift;
                    var blue = (pixel & blueMask) >> blueShift;

                    // 2. Invert ALL channels
                    alpha = 255 - alpha;
                    red = 255 - red;
                    green = 255 - green;
                    blue = 255 - blue;

                    // 3. Reconstruct the pixel by shifting the new values back into position
                    var newPixel = (alpha << alphaShift) |
                        (red << redShift) |
                        (green << greenShift) |
                        (blue << blueShift);

                    // 4. Write the modified pixel back to memory
                    Marshal.WriteInt32(scanline, pixelOffset, (int)newPixel);
                }
            }
        }
    }
}
